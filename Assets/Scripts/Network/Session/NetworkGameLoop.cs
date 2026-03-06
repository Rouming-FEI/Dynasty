using System;
using System.Collections.Generic;
using AI;
using Core;
using Core.Command;
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
    /// 广播全员 ChipBatch；行动阶段逐行动者开窗广播 ActionBatch）；客户端 = 纯重放
    /// （HumanPlayers=null，JudgmentInputProvider/ManualInputProvider 从批次队列取值，不跑 AI）。
    /// 判定结算（BuildActionOrder 平局裁决）两端执行 → 世界随机流同步；AI 决策用独立随机流（Task 3）。
    /// 行动阶段：主机按 ActionOrder 逐行动者开窗（ActionTurn）→ 真人槽收 ActionSubmit /
    /// AI 槽本地决策 / 超时 WasteAP 跳过（isPass=1）→ 广播 ActionBatch → 两端执行；
    /// 全员 AP 耗尽 → TurnEndBatch + 回合末结算 → 下一轮判定。
    /// 窗口时序：ClockProvider（可注入，测试快进时钟）；关窗条件 = 提交/决策完成 或 超时。
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

        // 行动窗口状态（主机）
        private ReplayActionRecord _pendingAction;   // 当前窗口已定记录（真人提交/超时 WasteAP）
        private bool _actionPass;                    // 当前记录是否超时跳过（ActionBatch isPass）
        private bool _actionWindowOpen;
        private long _actionWindowStartMs;
        private bool _injectArmed;                   // 待注入标记：RunTurn 前武装，首次查询注入并解除（单次注入）

        // 批次队列（客户端：待执行的输入批次；判定批次携带 turn 供入队/消费双校验，
        // 行动批次按 FIFO + 行动者校验逐条消费）
        private readonly Queue<(int Turn, ChipAllocation[] Allocs)> _chipBatchQueue = new Queue<(int Turn, ChipAllocation[] Allocs)>();
        private readonly Queue<ReplayActionRecord> _actionBatchQueue = new Queue<ReplayActionRecord>();
        private ChipAllocation[] _clientChipBatch;   // 客户端：当前待执行判定批次（JudgmentInputProvider 读取）
        private bool _clientFaulted;                 // 客户端协议校验失败进入错误状态：停止重放，LastError 记录原因（I3/I6）
        private ChipAllocation[] _committedBatch;    // 主机：最近判定结算的全员筹码（OnJudgmentCommitted 捕获 → 广播）
        private ReplayActionRecord? _clientManualAction;  // 客户端：当前待注入行动记录（ManualInputProvider 读取，单次）

        public WorldState State => _state;
        public bool IsHost => _session.IsHost;
        public bool GameOver => _state == null || _state.Phase == TurnPhase.GameOver;
        public string LastError { get; private set; }
        public event Action<string> OnError;

        /// <summary>可注入时钟（返回毫秒）；测试用快进时钟控制窗口超时。
        /// 默认 Environment.TickCount（int 隐式转 long）：.NET Framework 4.x 无 64 位毫秒
        /// 时钟 API（Unity 编译失败，verify net48 目标兜底）；24.9 天回绕由差值比较的
        /// int 截断处理（见 TickHostJudgment 超时判断）。</summary>
        public Func<long> ClockProvider = () => Environment.TickCount;

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

        /// <summary>开始对局（主机开战后调用；客户端收到 GameStart 后调用）。幂等；校验失败可重试（_state 保持 null）。</summary>
        public void StartGame()
        {
            if (_state != null) return;
            int[] defIds = _session.PlayerDefIds;
            if (defIds == null || defIds.Length != _config.PlayerCount)
            {
                ReportError("角色表缺失或长度不符，无法开局");
                return;
            }
            // I4：全部校验（含角色表解析）通过后再赋值 _state/_lockstep——任一步失败都保持
            // _state == null，调用方可修复后重试；避免半初始化坏状态（_state 非空但 _lockstep
            // 未就绪时 Tick 会 NullReference，且幂等检查拒绝重试）。
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
            _state = new WorldState(_config.PlayerCount, _session.Seed);
            _ais = new SimpleAI[_config.PlayerCount];
            for (int i = 0; i < _config.PlayerCount; i++)
                _ais[i] = new SimpleAI(i);
            _lockstep = new LocalLockstep(_state, _ais);
            _lockstep.StartGame(defs);

            if (IsHost)
            {
                // 主机：真人槽掩码（槽 0=主机本地 + 已连接对端槽）；未提交真人槽返回 null → LocalLockstep 暂停
                _lockstep.HumanPlayers = BuildHumanMask();
                _lockstep.HumanJudgmentInput = QueryHumanJudgment;
                // 主机行动注入：只被真人槽查询（AI 槽走 LocalLockstep 内 _ais 分流）。
                // 单次注入语义：_injectArmed 在窗口关窗后武装，首次查询返回记录命令并解除，
                // 防止同行动者多命令在同一 RunTurn 内被重复注入（每个命令 = 一个独立窗口批次）。
                _lockstep.ManualInputProvider = actor =>
                {
                    if (_injectArmed && _pendingAction.PlayerId == actor)
                    {
                        _injectArmed = false;
                        return ReplayActionFactory.Create(_pendingAction, _pendingAction.Tick);
                    }
                    return null;
                };
                // AI 槽行动广播：RunTurn 内 AI 路径执行后经 OnActionCommand 捕获转记录广播
                // （不能预决策：AI 决策消耗独立随机流，预决策后再 RunTurn 会二次决策导致漂移）
                _lockstep.OnActionCommand += OnActionCaptured;
                // I5：+= 追加而非直接赋值——与 ReplayRecorder.Attach 的 = 赋值可共存。
                // 挂载顺序约定：先 ReplayRecorder.Attach（= 覆盖语义）后本处 += 追加；
                // 若反过来，Attach 的 = 会覆盖本端处理器（ReplayPlayer 等纯回放场景不受影响）。
                _lockstep.OnJudgmentCommitted += batch => _committedBatch = batch;
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
                // 客户端行动注入：HumanPlayers=null 下全槽位走 ManualInputProvider（批次驱动）。
                // _clientManualAction 由 TickClientAction 弹出批次后置入，RunTurn 首次查询注入并解除。
                _lockstep.ManualInputProvider = actor =>
                {
                    if (_clientManualAction != null && _clientManualAction.Value.PlayerId == actor)
                    {
                        ReplayActionRecord rec = _clientManualAction.Value;
                        _clientManualAction = null;
                        return ReplayActionFactory.Create(rec, rec.Tick);
                    }
                    return null;
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

        /// <summary>主机：真人玩家提交行动命令（行动窗口内接收；非法提交拒绝不广播）。
        /// 客户端：编码 ActionSubmit 转发给主机（主机 peerId 约定为 0）。</summary>
        public void SubmitAction(int playerId, ReplayActionRecord record)
        {
            if (IsHost)
            {
                if (_state == null || _state.Phase != TurnPhase.ActionPhase || !_actionWindowOpen) return;
                if (playerId < 0 || playerId >= _config.PlayerCount) { ReportError($"行动提交越界 playerId={playerId}"); return; }
                if (!IsHumanSlot(playerId)) { ReportError($"非真人槽提交行动: playerId={playerId}"); return; }
                if (record.PlayerId != playerId) { ReportError($"行动记录 PlayerId 与提交者不一致: {record.PlayerId} != {playerId}"); return; }
                if (playerId != _lockstep.CurrentActorPlayerId)
                {
                    ReportError($"行动者不匹配: 提交 {playerId} != 当前行动者 {_lockstep.CurrentActorPlayerId}，拒绝提交");
                    return;
                }
                if (_injectArmed) return;   // 窗口内一次提交（含超时已定）
                if (ReplayActionFactory.Create(record, 0) == null)
                {
                    ReportError($"未知行动命令类型: {record.Kind}，拒绝提交");
                    return;
                }
                _pendingAction = record;
                // I7：广播 Tick 以主机为准——执行前的下一个 tick（客户端执行时校验对齐）
                _pendingAction.Tick = _state.CurrentTick + 1;
                _pendingAction.Source = 1;   // 真人输入
                _actionPass = false;
                _injectArmed = true;
                return;
            }
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ActionSubmit);
            NetProtocol.WriteActionSubmit(w, record);
            _transport.SendReliable(0, w);
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
            // I2：真人掩码每 Tick 统一重建（判定/行动分支共用）——掩码是 StartGame/开窗时的
            // 快照，对端断线释放槽位后 LocalLockstep 仍按旧掩码把断线槽当真人 → 判定
            // HumanJudgmentInput / 行动 ManualInputProvider 返回 null → 永久暂停死锁；
            // 重建后断线槽回落 AI 路径，与 TickHostJudgment/AllHumanFilled/TickHostAction
            // 的实时 IsHumanSlot 分流口径一致。判定与行动轮共用此唯一重建点。
            _lockstep.HumanPlayers = BuildHumanMask();
            if (_state.Phase == TurnPhase.JudgmentPhase)
            {
                // 判定轮：结算后停在行动边界（行动窗口由 TickHostAction 驱动，避免判定轮
                // RunTurn 越界执行 AI 行动导致 ChipBatch 轮次错位/时序混乱）
                _lockstep.PauseActions = true;
                TickHostJudgment();
                return;
            }
            _lockstep.PauseActions = false;   // 行动轮：由行动窗口驱动执行
            TickHostAction();
        }

        private void TickHostJudgment()
        {
            if (!_windowOpen)
                OpenWindow();
            // 驱动一次收集：AI 槽首次取值缓存（独立随机流），真人槽未提交 → RunTurn 暂停
            _lockstep.RunTurn();
            // 关窗条件 2：超时——未提交真人槽自动 1-1-1 后强制结算。
            // 回绕安全：ClockProvider 默认 Environment.TickCount（int 值域，24.9 天回绕），
            // 差值统一用 (int)(now - start) 截断——真实耗时 < 2^31 ms 时结果恒等于实际毫秒数，
            // 自动处理 int 回绕（unchecked 截断语义）。
            if (!AllHumanFilled() && (int)(ClockProvider() - _windowStartMs) >= _config.JudgmentWindowMs)
            {
                for (int i = 0; i < _chipFilled.Length; i++)
                {
                    // M7：只填真人槽——AI 槽值由 LocalLockstep 首次收集缓存（_pendingAllocations），
                    // _pendingChips 仅为真人槽提交缓存，写 AI 槽是无效操作
                    if (!_chipFilled[i] && IsHumanSlot(i))
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

        /// <summary>主机行动阶段驱动：按 ActionOrder 逐行动者开窗。
        /// 真人槽：等 ActionSubmit 或超时 → WasteAP 跳过；AI 槽：RunTurn 内 AI 执行经钩子广播。
        /// 每个行动命令 = 一次窗口 = 一个 ActionBatch（客户端逐条消费重放）。</summary>
        private void TickHostAction()
        {
            if (_state.Phase == TurnPhase.TurnEnd)
            {
                // 防御兜底：正常路径 RunTurn 在消费最后命令时已自动跨过回合末
                _lockstep.RunTurn();
                return;
            }
            int actorId = _lockstep.CurrentActorPlayerId;
            if (actorId < 0)
            {
                // 全员 AP 耗尽：广播 TurnEndBatch → RunTurn（内部 Advance → 回合末结算 → 下一轮判定）。
                // 回合末结算推进 CurrentTurn/CurrentTick，两端各自执行同一批次序列后状态一致。
                BroadcastTurnEndBatch();
                _lockstep.RunTurn();
                return;
            }
            int turnBefore = _state.CurrentTurn;
            if (!_actionWindowOpen)
                OpenActionWindow(actorId);

            if (IsHumanSlot(actorId))
            {
                if (!_injectArmed)
                {
                    // 未提交：超时 → WasteAP 跳过；否则 RunTurn 暂停等待真人提交
                    if ((int)(ClockProvider() - _actionWindowStartMs) >= _config.ActionWindowMs)
                    {
                        _pendingAction = MakeWasteApRecord(actorId);
                        _actionPass = true;
                        _injectArmed = true;
                    }
                    else
                    {
                        _lockstep.RunTurn();
                        return;
                    }
                }
                // 真人提交（_injectArmed=true）或超时：广播 ActionBatch → 注入执行（单次）
                BroadcastActionBatch(_pendingAction, (byte)(_actionPass ? 1 : 0));
                _actionWindowOpen = false;
                _lockstep.RunTurn();
                _injectArmed = false;   // 防御：正常首次查询已解除
                if (_state.CurrentTurn != turnBefore) BroadcastTurnEndBatch();
                return;
            }

            // AI 槽：不预决策（避免二次消耗 AI 随机流）——RunTurn 内 AI 路径执行，
            // OnActionCaptured 钩子捕获命令并广播 ActionBatch；窗口即时关闭
            _lockstep.RunTurn();
            _actionWindowOpen = false;
            if (_state.CurrentTurn != turnBefore) BroadcastTurnEndBatch();
        }

        private void OpenActionWindow(int actorId)
        {
            _actionWindowOpen = true;
            _actionWindowStartMs = ClockProvider();
            _pendingAction = default(ReplayActionRecord);
            _actionPass = false;
            _injectArmed = false;

            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ActionTurn);
            NetProtocol.WriteActionTurn(w, actorId, _config.ActionWindowMs);
            _transport.Broadcast(w);
        }

        private void BroadcastActionBatch(ReplayActionRecord rec, byte isPass)
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.ActionBatch);
            NetProtocol.WriteActionBatch(w, rec, isPass);
            _transport.Broadcast(w);
        }

        private void BroadcastTurnEndBatch()
        {
            var w = new NetDataWriter();
            NetProtocol.WriteHeader(w, NetMsgType.TurnEndBatch);
            NetProtocol.WriteTurnEndBatch(w);
            _transport.Broadcast(w);
        }

        /// <summary>超时跳过记录：WasteAP（每命令 -1 AP；AP 未归零则下一 Tick 重新开窗再次跳过，直至归零）。</summary>
        private ReplayActionRecord MakeWasteApRecord(int actorId)
        {
            return new ReplayActionRecord
            {
                Tick = _state.CurrentTick + 1,
                PlayerId = actorId,
                Kind = ReplayActionKind.WasteAP,
                Source = 1   // 真人槽超时：按真人输入对待（记录序列中的真实一步）
            };
        }

        /// <summary>行动旁观钩子（仅主机）：AI 槽行动命令执行前捕获转记录广播。
        /// 真人槽命令已在 TickHostAction 广播（含 isPass），此处跳过防重复广播。</summary>
        private void OnActionCaptured(int actorId, ICommand cmd, bool isHuman)
        {
            if (!IsHost) return;   // 客户端纯重放，不广播
            if (isHuman) return;   // 真人槽已在关窗时广播
            if (cmd == null) return;
            ReplayActionRecord rec = ReplayActionFactory.ToRecord(cmd);
            if (rec.Kind == ReplayActionKind.Unknown)
            {
                ReportError($"AI 行动命令无法转记录（Kind=Unknown），两端将不同步");
                return;
            }
            rec.Tick = _state.CurrentTick;   // 钩子在 CurrentTick++ 后触发 → 执行 tick
            rec.Source = 0;                  // AI 输入
            BroadcastActionBatch(rec, 0);
        }

        private void OpenWindow()
        {
            _windowOpen = true;
            _windowStartMs = ClockProvider();
            // 真人掩码由 TickHost 开头统一重建（I2），开窗不再单独快照——保证判定轮
            // 与行动轮共用同一重建点，断线槽始终按实时 IsHumanSlot 回落 AI 路径。
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
            // I3：载荷携带判定轮次（客户端入队/消费双校验，防过期/乱序/重放批次）
            NetProtocol.WriteChipBatch(w, _state.CurrentTurn, batch);
            _transport.Broadcast(w);
        }

        // ---- 客户端 ----

        private void TickClient()
        {
            if (_state == null || _state.Phase == TurnPhase.GameOver || _clientFaulted) return;
            if (_state.Phase == TurnPhase.JudgmentPhase)
            {
                // 有批次 → 弹出注入重放；无批次 → 暂停等待（不推进）
                if (_chipBatchQueue.Count > 0)
                {
                    var (turn, allocs) = _chipBatchQueue.Dequeue();
                    // I3：消费时复校验轮次（入队已校验；队列严格 FIFO，此处防御乱序/重复消费）
                    if (turn != _state.CurrentTurn)
                    {
                        _clientFaulted = true;
                        ReportError($"消费时判定批次轮次不匹配: 载荷 {turn} != 本地 {_state.CurrentTurn}，进入错误状态");
                        return;
                    }
                    _clientChipBatch = allocs;
                    _lockstep.RunTurn();
                    _clientChipBatch = null;
                }
                return;
            }
            TickClientAction();
        }

        /// <summary>客户端行动阶段驱动：从 _actionBatchQueue 逐条弹出注入重放（空 → ManualInputProvider
        /// null → 暂停）。回合末由 RunTurn 内部 Advance 自动跨过（消费最后批次时一并结算），
        /// TurnEndBatch 仅作协议同步点消费载荷。</summary>
        private void TickClientAction()
        {
            if (_state.Phase == TurnPhase.TurnEnd)
            {
                // 防御兜底：正常路径 RunTurn 在消费最后命令时已自动跨过回合末
                _lockstep.RunTurn();
                return;
            }
            if (_actionBatchQueue.Count > 0)
            {
                ReplayActionRecord rec = _actionBatchQueue.Dequeue();
                int actor = _lockstep.CurrentActorPlayerId;
                if (actor < 0)
                {
                    _clientFaulted = true;
                    ReportError($"行动批次到达但本地无人可动（Phase={_state.Phase}），进入错误状态");
                    return;
                }
                // I8：消费时严格校验行动者与本地当前行动者一致（跨行动者按 ActionTurn 顺序，
                // 队列 FIFO + 逐命令推进保证一一对应；乱序/重放/状态分叉即拦截）
                if (rec.PlayerId != actor)
                {
                    _clientFaulted = true;
                    ReportError($"行动批次行动者不匹配: 载荷 {rec.PlayerId} != 本地 {actor}，进入错误状态");
                    return;
                }
                // I7：Tick 对齐校验——已确认 cmd.Tick 不参与任何逻辑判定（仅记录/回放锚点），
                // 不匹配降级为警告级嗅探，不中断重放（帧同步保护由 PlayerId 校验承担）
                if (rec.Tick != _state.CurrentTick + 1)
                {
                    ReportError($"行动批次 Tick 不匹配: 载荷 {rec.Tick} != 本地 {_state.CurrentTick + 1}（警告级）");
                }
                if (ReplayActionFactory.Create(rec, 0) == null)
                {
                    _clientFaulted = true;
                    ReportError($"行动批次未知命令类型: {rec.Kind}，进入错误状态");
                    return;
                }
                _clientManualAction = rec;
                _lockstep.RunTurn();
                _clientManualAction = null;
                return;
            }
            // 无批次 → 暂停等待（ManualInputProvider null）；全员 AP 耗尽时 RunTurn 自动跨过回合末
            _lockstep.RunTurn();
        }

        // ---- 消息路由 ----

        private void HandleMessage(int peerId, NetDataReader reader)
        {
            // M5：客户端已进入错误状态（faulted）→ 丢弃后续所有消息，不再入队/重放——
            // 协议校验失败的批次是损坏输入，继续入队只会用错误数据推进状态机。
            if (_clientFaulted) return;
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
                            // I1：槽位持有者校验——发送方 peerId 必须持有该槽位（PlayerPeerIds
                            // 槽→peerId 语义：槽 0 恒 -1=主机本地，1..N-1=对端）。伪造他人槽提交
                            // （冒名）→ ReportError + 忽略（不填充 _pendingChips，不触发回调）；
                            // 槽 0 恒 -1 天然拒绝网络提交（主机本地提交不经 HandleMessage）。
                            int[] peerIds = _session.PlayerPeerIds;
                            if (pid < 0 || pid >= peerIds.Length || peerIds[pid] != peerId)
                            {
                                ReportError($"判定提交槽位持有者校验失败: playerId={pid} 非 peer {peerId} 持有，忽略");
                                break;
                            }
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
                            NetProtocol.ReadChipBatch(reader, out int turn, out ChipAllocation[] allocs);
                            if (_state == null)
                            {
                                ReportError("未开局收到判定批次，丢弃");
                                break;
                            }
                            // I6：非法批次（长度≠PlayerCount）不执行，直接丢弃并进入错误状态
                            //（否则 JudgmentInputProvider 越界槽返回空筹码 → 判定结果与主机分叉 → 永久不同步）
                            if (allocs.Length != _state.PlayerCount)
                            {
                                _clientFaulted = true;
                                ReportError($"非法判定批次长度 {allocs.Length} != {_state.PlayerCount}，进入错误状态");
                                break;
                            }
                            // I3：入队前校验轮次——载荷 turn 必须等于本地 CurrentTurn
                            if (turn != _state.CurrentTurn)
                            {
                                _clientFaulted = true;
                                ReportError($"判定批次轮次不匹配: 载荷 {turn} != 本地 {_state.CurrentTurn}，进入错误状态");
                                break;
                            }
                            _chipBatchQueue.Enqueue((turn, allocs));
                        }
                        catch (Exception e) { ReportError($"ChipBatch 解析失败: {e.Message}"); }
                    }
                    break;
                case NetMsgType.ActionTurn:
                    // 客户端纯重放不依赖 ActionTurn（等 ActionBatch），仅消费载荷
                    if (!IsHost)
                    {
                        try { NetProtocol.ReadActionTurn(reader, out int atPid, out int atMs); }
                        catch (Exception e) { ReportError($"ActionTurn 解析失败: {e.Message}"); }
                    }
                    break;
                case NetMsgType.ActionSubmit:
                    if (IsHost)
                    {
                        try
                        {
                            ReplayActionRecord rec = NetProtocol.ReadActionSubmit(reader);
                            // I1：槽位持有者校验——发送方 peerId 必须持有 rec.PlayerId 槽位
                            // （冒名他人槽提交行动）→ ReportError + 忽略（不广播 ActionBatch，
                            // 不触发回调）；槽 0 恒 -1 天然拒绝网络提交。
                            int[] peerIds = _session.PlayerPeerIds;
                            if (rec.PlayerId < 0 || rec.PlayerId >= peerIds.Length || peerIds[rec.PlayerId] != peerId)
                            {
                                ReportError($"行动提交槽位持有者校验失败: playerId={rec.PlayerId} 非 peer {peerId} 持有，忽略");
                                break;
                            }
                            if (OnHostActionReceived != null) OnHostActionReceived(rec.PlayerId, rec);
                            else SubmitAction(rec.PlayerId, rec);
                        }
                        catch (Exception e) { ReportError($"ActionSubmit 解析失败: {e.Message}"); }
                    }
                    break;
                case NetMsgType.ActionBatch:
                    if (!IsHost)
                    {
                        try
                        {
                            NetProtocol.ReadActionBatch(reader, out ReplayActionRecord rec, out byte isPass);
                            if (_state == null)
                            {
                                ReportError("未开局收到行动批次，丢弃");
                                break;
                            }
                            // I8：入队前结构校验（PlayerId 越界/未知命令类型拦截）；行动者与
                            // 轮次严格校验在消费时进行（批次可先于本地状态推进到达，如判定阶段
                            // 收到行动批次属正常先行到达——消费时按 FIFO + 当前行动者校验兜底）
                            if (rec.PlayerId < 0 || rec.PlayerId >= _state.PlayerCount)
                            {
                                _clientFaulted = true;
                                ReportError($"行动批次 PlayerId 越界: {rec.PlayerId}，进入错误状态");
                                break;
                            }
                            if (ReplayActionFactory.Create(rec, 0) == null)
                            {
                                _clientFaulted = true;
                                ReportError($"行动批次未知命令类型: {rec.Kind}，进入错误状态");
                                break;
                            }
                            _actionBatchQueue.Enqueue(rec);
                        }
                        catch (Exception e) { ReportError($"ActionBatch 解析失败: {e.Message}"); }
                    }
                    break;
                case NetMsgType.TurnEndBatch:
                    // 客户端由 RunTurn 内部 Advance 自动跨过回合末（消费最后批次时一并结算，
                    // 不会停留在 TurnEnd 阶段等待），TurnEndBatch 仅作协议同步点消费载荷
                    if (!IsHost)
                    {
                        try { NetProtocol.ReadTurnEndBatch(reader); }
                        catch (Exception e) { ReportError($"TurnEndBatch 解析失败: {e.Message}"); }
                    }
                    break;
                case NetMsgType.GameOver:
                    // 终局消息：双方经相同批次序列自行检测 GameOver；载荷消费防粘包
                    if (!IsHost)
                    {
                        try { int gw = NetProtocol.ReadGameOver(reader); }
                        catch (Exception e) { ReportError($"GameOver 解析失败: {e.Message}"); }
                    }
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
