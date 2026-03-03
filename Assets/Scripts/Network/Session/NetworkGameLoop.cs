using System;
using System.Collections.Generic;
using AI;
using Core;
using Core.Replay;
using Gameplay.Characters;
using Gameplay.Judgment;
using LiteNetLib.Utils;
using Network.Local;
using Network.Protocol;
using Network.Transport;

namespace Network.Session
{
    /// <summary>
    /// 网络对战驱动：主机 = 唯一决策中心（收集真人提交 + AI 本地决策 + 超时自动 1-1-1，
    /// 广播全员 ChipBatch）；客户端 = 纯重放（HumanPlayers=null，JudgmentInputProvider 从批次队列取值，不跑 AI）。
    /// 判定结算（BuildActionOrder 平局裁决）两端执行 → 世界随机流同步；AI 决策用独立随机流（Task 3）。
    /// 本任务完成判定窗口；行动窗口（ActionBatch/TurnEndBatch）Task 8 补充。
    /// 窗口时序：ClockProvider（可注入，测试快进时钟）；关窗条件 = 全部真人槽提交 或 超时。
    /// </summary>
    public sealed class NetworkGameLoop
    {
        private readonly INetworkTransport _transport;
        private readonly NetworkSession _session;
        private readonly SessionConfig _config;
        private LocalLockstep _lockstep;
        private WorldState _state;
        private SimpleAI[] _ais;              // 仅主机使用（AI 决策独立随机流）

        // 判定窗口状态（主机）
        private ChipAllocation[] _pendingChips;   // 真人槽提交缓存（AI 槽值在 LocalLockstep 缓存，不在此）
        private bool[] _chipFilled;               // 真人槽已提交标记
        private long _windowStartMs;
        private bool _windowOpen;

        // 批次队列（客户端：待执行的输入批次；行动批次 Task 8 使用）
        private readonly Queue<ChipAllocation[]> _chipBatchQueue = new Queue<ChipAllocation[]>();
        private readonly Queue<ReplayActionRecord> _actionBatchQueue = new Queue<ReplayActionRecord>();
        private ChipAllocation[] _clientChipBatch;   // 客户端：当前待执行判定批次（JudgmentInputProvider 读取）
        private ChipAllocation[] _committedBatch;    // 主机：最近判定结算的全员筹码（OnJudgmentCommitted 捕获 → 广播）

        public WorldState State => _state;
        public bool IsHost => _session.IsHost;
        public bool GameOver => _state == null || _state.Phase == TurnPhase.GameOver;
        public string LastError { get; private set; }
        public event Action<string> OnError;

        /// <summary>可注入时钟（返回毫秒）；测试用快进时钟控制窗口超时。</summary>
        public Func<long> ClockProvider = () => Environment.TickCount64;

        /// <summary>主机真人槽提交回调（内部解析 ChipSubmit 后触发；默认路由 SubmitChip，外部可接管/观察）。</summary>
        public Action<int, ChipAllocation> OnHostChipReceived;

        /// <summary>主机真人槽行动提交回调（Task 8 行动窗口）。</summary>
        public Action<int, ReplayActionRecord> OnHostActionReceived;

        /// <summary>重连请求回调（Task 11 接入）。</summary>
        public Action<byte[]> OnHostResyncRequest;

        /// <summary>重连快照回复（Task 11 接入）。</summary>
        public Func<int, int, byte[]> OnHostResyncReply;

        public NetworkGameLoop(INetworkTransport transport, NetworkSession session, SessionConfig config)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _transport.OnDataReceived += HandleMessage;
        }

        /// <summary>开始对局（主机开战后调用；客户端收到 GameStart 后调用）。幂等。</summary>
        public void StartGame()
        {
            if (_state != null) return;
            int[] defIds = _session.PlayerDefIds;
            if (defIds == null || defIds.Length != _config.PlayerCount)
            {
                ReportError("角色表缺失或长度不符，无法开局");
                return;
            }
            _state = new WorldState(_config.PlayerCount, _session.Seed);
            _ais = new SimpleAI[_config.PlayerCount];
            for (int i = 0; i < _config.PlayerCount; i++)
                _ais[i] = new SimpleAI(i);
            _lockstep = new LocalLockstep(_state, _ais);

            var defs = new CharacterDef[defIds.Length];
            for (int i = 0; i < defIds.Length; i++)
            {
                defs[i] = CharacterDefCatalog.Get(defIds[i]);
                if (defs[i] == null)
                {
                    ReportError($"未知角色 DefId: {defIds[i]}");
                    return;
                }
            }
            _lockstep.StartGame(defs);

            // Task 7：行动窗口未实现，判定结算后冻结行动阶段（Task 8 移除本行并接入行动窗口）
            _lockstep.PauseActions = true;

            if (IsHost)
            {
                // 主机：真人槽掩码（槽 0=主机本地 + 已连接对端槽）；未提交真人槽返回 null → LocalLockstep 暂停
                _lockstep.HumanPlayers = BuildHumanMask();
                _lockstep.HumanJudgmentInput = QueryHumanJudgment;
                _lockstep.OnJudgmentCommitted = batch => _committedBatch = batch;
            }
            else
            {
                // 客户端：纯重放——全槽位筹码来自广播批次（不跑 AI）
                _lockstep.HumanPlayers = null;
                _lockstep.JudgmentInputProvider = i =>
                {
                    if (_clientChipBatch != null && i < _clientChipBatch.Length)
                        return _clientChipBatch[i];
                    ReportError("收到非法判定批次");
                    return new ChipAllocation(i);
                };
            }

            // 主机开局：广播 GameStart（客户端收到后以同 seed/同角色表建世界开局）
            if (IsHost)
            {
                var w = new NetDataWriter();
                NetProtocol.WriteHeader(w, NetMsgType.GameStart);
                NetProtocol.WriteGameStart(w, _state.CurrentTurn);
                _transport.Broadcast(w);
            }
        }

        /// <summary>主机：真人玩家提交判定筹码（窗口内接收；窗口外/重复提交忽略）。
        /// 客户端：编码 ChipSubmit 转发给主机（主机 peerId 约定为 0）。</summary>
        public void SubmitChip(int playerId, ChipAllocation alloc)
        {
            if (IsHost)
            {
                if (_state == null || !_windowOpen || _state.Phase != TurnPhase.JudgmentPhase) return;
                if (playerId < 0 || playerId >= _chipFilled.Length) { ReportError($"判定提交越界 playerId={playerId}"); return; }
                if (!IsHumanSlot(playerId)) { ReportError($"非真人槽提交判定: playerId={playerId}"); return; }
                if (_chipFilled[playerId]) return;   // 窗口内一次提交
                alloc.PlayerId = playerId;           // 以槽号为准（广播批次按槽索引消费）
                _pendingChips[playerId] = alloc;
                _chipFilled[playerId] = true;
                return;
            }
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ChipSubmit);
            NetProtocol.WriteChipSubmit(w, playerId, alloc.Rock, alloc.Scissors, alloc.Paper);
            _transport.SendReliable(0, w);
        }

        /// <summary>主机：真人玩家提交行动命令（Task 8 行动窗口；判定阶段占位）。</summary>
        public void SubmitAction(int playerId, ReplayActionRecord record)
        {
            // Task 8：行动窗口
        }

        /// <summary>每帧调用：PollEvents + 推进窗口状态机 + 驱动 LocalLockstep。</summary>
        public void Tick()
        {
            _transport.PollEvents();   // 触发 OnDataReceived → HandleMessage
            if (IsHost) TickHost();
            else TickClient();
        }

        // ---- 主机 ----

        private void TickHost()
        {
            if (_state == null || _state.Phase == TurnPhase.GameOver) return;
            if (_state.Phase == TurnPhase.JudgmentPhase)
            {
                TickHostJudgment();
                return;
            }
            // ActionPhase / TurnEnd：Task 8 行动窗口
        }

        private void TickHostJudgment()
        {
            if (!_windowOpen)
                OpenWindow();
            // 驱动一次收集：AI 槽首次取值缓存（独立随机流），真人槽未提交 → RunTurn 暂停
            _lockstep.RunTurn();
            // 关窗条件 2：超时——未提交真人槽自动 1-1-1 后强制结算
            if (!AllHumanFilled() && ClockProvider() - _windowStartMs >= _config.JudgmentWindowMs)
            {
                for (int i = 0; i < _chipFilled.Length; i++)
                {
                    if (!_chipFilled[i])
                    {
                        _pendingChips[i].SetDefault();   // 1-1-1
                        _chipFilled[i] = true;
                    }
                }
                _lockstep.RunTurn();   // 全填 → 判定结算（OnJudgmentCommitted 捕获全员数组）
            }
            // 结算完成（含全局平作废重判轮）：广播 ChipBatch → 关窗
            // 平局重判后 Phase 仍 JudgmentPhase → 下个 Tick 重新开窗（新一轮 RoundStart）
            if (_committedBatch != null)
            {
                _windowOpen = false;
                BroadcastChipBatch(_committedBatch);
                _committedBatch = null;
            }
        }

        private void OpenWindow()
        {
            _windowOpen = true;
            _windowStartMs = ClockProvider();
            _pendingChips = new ChipAllocation[_state.PlayerCount];
            _chipFilled = new bool[_state.PlayerCount];
            for (int i = 0; i < _state.PlayerCount; i++)
                _pendingChips[i] = new ChipAllocation(i);
            _committedBatch = null;

            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.RoundStart);
            NetProtocol.WriteRoundStart(w, _state.CurrentTurn, _config.JudgmentWindowMs);
            _transport.Broadcast(w);
        }

        private void BroadcastChipBatch(ChipAllocation[] batch)
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ChipBatch);
            NetProtocol.WriteChipBatch(w, batch);
            _transport.Broadcast(w);
        }

        // ---- 客户端 ----

        private void TickClient()
        {
            if (_state == null || _state.Phase == TurnPhase.GameOver) return;
            if (_state.Phase == TurnPhase.JudgmentPhase)
            {
                // 有批次 → 弹出注入重放；无批次 → 暂停等待（不推进）
                if (_chipBatchQueue.Count > 0)
                {
                    _clientChipBatch = _chipBatchQueue.Dequeue();
                    _lockstep.RunTurn();
                    _clientChipBatch = null;
                }
                return;
            }
            // ActionPhase / TurnEnd：Task 8 行动窗口
        }

        // ---- 消息路由 ----

        private void HandleMessage(int peerId, NetDataReader reader)
        {
            NetMsgType type;
            try { type = NetProtocol.ReadType(reader); }
            catch (Exception e) { ReportError($"消息解析失败: {e.Message}"); return; }
            switch (type)
            {
                case NetMsgType.GameStart:
                    // 客户端：收到开局广播 → 建世界开局（幂等：已开局则忽略）
                    if (!IsHost)
                    {
                        try { NetProtocol.ReadGameStart(reader); }
                        catch (Exception e) { ReportError($"GameStart 解析失败: {e.Message}"); }
                        StartGame();
                    }
                    break;
                case NetMsgType.RoundStart:
                    // 客户端纯重放不依赖 RoundStart（等 ChipBatch），仅消费载荷
                    if (!IsHost) { try { NetProtocol.ReadRoundStart(reader, out int t, out int wms); }
                        catch (Exception e) { ReportError($"RoundStart 解析失败: {e.Message}"); } }
                    break;
                case NetMsgType.ChipSubmit:
                    if (IsHost)
                    {
                        try
                        {
                            NetProtocol.ReadChipSubmit(reader, out int pid, out int rock, out int scissors, out int paper);
                            var alloc = new ChipAllocation(pid);
                            alloc.Set(rock, scissors, paper);
                            if (OnHostChipReceived != null) OnHostChipReceived(pid, alloc);
                            else SubmitChip(pid, alloc);
                        }
                        catch (Exception e) { ReportError($"ChipSubmit 解析失败: {e.Message}"); }
                    }
                    break;
                case NetMsgType.ChipBatch:
                    if (!IsHost)
                    {
                        try
                        {
                            NetProtocol.ReadChipBatch(reader, out ChipAllocation[] allocs);
                            _chipBatchQueue.Enqueue(allocs);
                        }
                        catch (Exception e) { ReportError($"ChipBatch 解析失败: {e.Message}"); }
                    }
                    break;
                case NetMsgType.ActionTurn:
                case NetMsgType.ActionBatch:
                case NetMsgType.TurnEndBatch:
                case NetMsgType.GameOver:
                    // Task 8 行动窗口 + 终局接入
                    break;
                default:
                    break;
            }
        }

        // ---- 主机真人槽判定辅助 ----

        /// <summary>真人槽判定掩码：槽 0（主机本地）恒真 + 会话已连接的对端槽。</summary>
        private bool[] BuildHumanMask()
        {
            var mask = new bool[_config.PlayerCount];
            for (int i = 0; i < mask.Length; i++)
                mask[i] = IsHumanSlot(i);
            return mask;
        }

        private bool IsHumanSlot(int i)
        {
            if (i == 0) return true;   // 主机本地玩家恒真人（判定筹码由驱动层 SubmitChip 提交）
            int[] peerIds = _session.PlayerPeerIds;
            return peerIds != null && i < peerIds.Length && peerIds[i] != -1;
        }

        /// <summary>主机真人判定查询：已提交返回筹码，未提交返回 null（LocalLockstep 暂停等待）。</summary>
        private ChipAllocation? QueryHumanJudgment(int i)
        {
            if (_chipFilled == null || i < 0 || i >= _chipFilled.Length || !_chipFilled[i]) return null;
            if (_pendingChips == null || i >= _pendingChips.Length) return null;
            return _pendingChips[i];
        }

        private bool AllHumanFilled()
        {
            for (int i = 0; i < _chipFilled.Length; i++)
                if (IsHumanSlot(i) && !_chipFilled[i]) return false;
            return true;
        }

        private void ReportError(string msg)
        {
            LastError = msg;
            OnError?.Invoke(msg);
        }
    }
}
