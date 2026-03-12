using System;
using System.Collections.Generic;
using Core;
using Core.Replay;
using Core.Sync;
using Gameplay.Judgment;
using LiteNetLib.Utils;
using Network.Protocol;
using Network.Session;

namespace Network.Sync
{
    /// <summary>
    /// 断线重连端到端：客户端 ResyncRequest → 主机 ResyncReply（快照+会话日志）→ 重建+重放+校验。
    /// 主机侧：会话日志（已广播输入批次，随广播同步记录）+ 快照管理（开局锚点 + 最近 2 份回合末）；
    /// HandleResyncRequest 返回 ResyncReply 载荷字节（[snapshotTick][snapshotLen][snapshotBytes][logLen][logBytes]），
    /// 消息组装（写 Header + 发送）由 NetworkGameLoop 负责（最小耦合）。
    /// 客户端侧：HandleResyncReply 反序列化快照（哈希自校验）→ GameLoop 重建世界与锁步（纯重放模式）→
    /// Poll 按当前阶段逐条把日志条目喂入现有批次队列，由 GameLoop 既有消费路径执行（复用轮次/行动者校验）；
    /// 日志耗尽 → IsResyncing=false（追平），恢复订阅实时广播。
    /// 传输事件（OnPeerDisconnected）与重连发起（重新 Connect + SendResyncRequest）由驱动层接线到
    /// GameLoop 公开 API（NotifyDisconnected/SendResyncRequest），本 Handler 不依赖具体传输实现。
    /// </summary>
    public sealed class ReconnectHandler
    {
        private readonly NetworkGameLoop _loop;
        private readonly List<(int Tick, byte[] Bytes)> _snapshots = new List<(int Tick, byte[] Bytes)>();

        // 客户端重放状态
        private List<SessionLog.Entry> _pendingLog = new List<SessionLog.Entry>();
        private int _logIndex;
        private bool _isResyncing;
        private int _resyncTargetTick;

        /// <summary>保留的回合末快照份数（不含开局锚点；超出即挤掉最旧回合末快照）。</summary>
        private const int MaxRecentSnapshots = 2;

        /// <summary>主机会话日志（广播批次序列，重连数据源）。</summary>
        public SessionLog Log { get; } = new SessionLog();

        /// <summary>最近快照 tick（-1 = 尚无快照）。</summary>
        public int LastSnapshotTick => _snapshots.Count > 0 ? _snapshots[_snapshots.Count - 1].Tick : -1;

        /// <summary>已保留快照的 tick 序列（[0] 恒为开局锚点；调试/测试用）。</summary>
        public int[] SnapshotTicks
        {
            get
            {
                var arr = new int[_snapshots.Count];
                for (int i = 0; i < arr.Length; i++) arr[i] = _snapshots[i].Tick;
                return arr;
            }
        }

        /// <summary>客户端：是否处于重连追平中（期间忽略实时广播）。</summary>
        public bool IsResyncing => _isResyncing;

        /// <summary>客户端：重放目标 tick（追平进度展示；= 日志最后条目 tick，空日志 = 快照 tick）。</summary>
        public int ResyncTargetTick => _resyncTargetTick;

        public ReconnectHandler(NetworkGameLoop loop)
        {
            _loop = loop ?? throw new ArgumentNullException(nameof(loop));
        }

        // ---- 主机侧 ----

        /// <summary>主机：记录一次判定批次广播（OnJudgmentCommitted 钩子接入；tick = 广播时引擎 tick）。</summary>
        public void LogChipBatch(int tick, ChipAllocation[] chips)
        {
            Log.AddChipBatch(tick, chips);
        }

        /// <summary>主机：记录一次行动批次广播（isPass=true → 超时标记 Source=255 + Kind==WasteAP 约定）。</summary>
        public void LogActionBatch(ReplayActionRecord rec, bool isPass)
        {
            ReplayActionRecord copy = rec;
            if (isPass) copy.Source = 255;   // 日志内超时标记（广播载荷 isPass 不入日志，重放端按普通 WasteAP 注入）
            Log.AddAction(copy.Tick, copy);
        }

        /// <summary>主机：捕获当前状态快照（开局 + 每回合末 TurnEndBatch 后调用）。
        /// 保留开局锚点（tick=0，全量重放兜底）+ 最近 2 份回合末快照；同 tick 幂等。</summary>
        public void CaptureSnapshot()
        {
            WorldState state = _loop.State;
            if (state == null) return;
            if (_snapshots.Count > 0 && _snapshots[_snapshots.Count - 1].Tick == state.CurrentTick)
                return;   // 同 tick 重复调用（防御）
            _snapshots.Add((state.CurrentTick, WorldStateSerializer.Serialize(state)));
            if (_snapshots.Count > MaxRecentSnapshots + 1)
                _snapshots.RemoveAt(1);   // 挤掉最旧回合末快照，保留 [0] 开局锚点
        }

        /// <summary>主机：处理 ResyncRequest → 返回 ResyncReply 载荷字节；null = 无可回复数据（未开局/无快照）。
        /// 快照取 tick ≤ lastTick 的最近一份（无则回退开局锚点 tick=0——lastTick ≥ 0 恒命中锚点）；
        /// 日志从快照 tick 起（含）截取。lastTurn 保留供上层诊断，快照选择以 tick 为准。</summary>
        public byte[] HandleResyncRequest(int lastTick, int lastTurn)
        {
            if (_snapshots.Count == 0) return null;
            int idx = 0;   // 开局锚点兜底
            for (int i = 0; i < _snapshots.Count; i++)
                if (_snapshots[i].Tick <= lastTick) idx = i;
            (int snapTick, byte[] snapBytes) = _snapshots[idx];
            byte[] logBytes = Log.SerializeFrom(snapTick);
            var w = new NetDataWriter();
            NetProtocol.WriteResyncReply(w, snapTick, snapBytes, snapBytes.Length, logBytes, logBytes.Length);
            var payload = new byte[w.Length];
            Array.Copy(w.Data, payload, w.Length);
            return payload;
        }

        // ---- 客户端侧 ----

        /// <summary>客户端：处理 ResyncReply 载荷 → 反序列化快照（哈希自校验）→ GameLoop 重建世界与锁步
        /// （纯重放模式）→ 挂起日志条目 → 进入追平。返回 false = 载荷损坏/重建失败。</summary>
        public bool HandleResyncReply(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return false;
            try
            {
                var reader = new NetDataReader(payload);
                NetProtocol.ReadResyncReply(reader, out int snapTick, out byte[] snapBytes, out int logLen, out byte[] logBytes);
                WorldState restored = WorldStateSerializer.Deserialize(snapBytes);   // 头部哈希自校验，坏数据抛异常
                if (!_loop.RebuildClientState(restored))
                    return false;
                _pendingLog = SessionLog.Deserialize(logBytes);
                _logIndex = 0;
                _resyncTargetTick = snapTick;
                for (int i = 0; i < _pendingLog.Count; i++)
                    if (_pendingLog[i].Tick > _resyncTargetTick)
                        _resyncTargetTick = _pendingLog[i].Tick;
                _isResyncing = true;
                return true;
            }
            catch (Exception)
            {
                _isResyncing = false;
                return false;
            }
        }

        /// <summary>客户端：每帧推进重放——按当前阶段喂入下一条日志条目（判定阶段喂 Chip、
        /// 行动阶段喂 Action）到现有批次队列，由 GameLoop 正常消费路径执行（同一 Tick 内消费）；
        /// 日志耗尽 → IsResyncing=false（追平，恢复订阅实时广播）。</summary>
        public void Poll()
        {
            if (!_isResyncing) return;
            if (_logIndex >= _pendingLog.Count)
            {
                _isResyncing = false;   // 追平
                return;
            }
            if (_loop.State == null) return;
            SessionLog.Entry e = _pendingLog[_logIndex];
            bool expectChip = _loop.State.Phase == TurnPhase.JudgmentPhase;
            if (e.IsChipBatch != expectChip)
            {
                // 顺序校验：快照为回合边界（判定阶段开头），日志条目与其后状态阶段一一对应
                _loop.ReportReplayError($"重放日志与状态阶段不匹配（期望 {(expectChip ? "判定" : "行动")}，条目为 {(e.IsChipBatch ? "判定" : "行动")}）");
                _isResyncing = false;
                return;
            }
            if (e.IsChipBatch)
                _loop.EnqueueReplayChipBatch(_loop.State.CurrentTurn, e.Chips);   // 轮次以重放端自身为准
            else
                _loop.EnqueueReplayAction(e.Action);
            _logIndex++;
        }
    }
}
