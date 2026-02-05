using Core.Command;
using Core.GAS;
using Gameplay.Judgment;
using Network.Local;

namespace Core.Replay
{
    /// <summary>
    /// 旁观录制器：挂 LocalLockstep 旁观钩子 + EventBus 旁路 + OnTickAdvanced，
    /// 零干预采集判定筹码、输入命令、事件时间线与每 Tick 状态哈希。
    /// 回放侧只注入真人输入（AI 辸入不注入、由 AI 重决策，保证随机流消耗对齐）。
    /// 注意：回合末批量命令共用一个 Tick（1 回合末 = 1 Tick），事件/命令均以
    /// state.CurrentTick（递增后）为锚，勿假设 1 命令 = 1 Tick。
    /// 录制期间若抛异常须 finally 调 Finish 卸载钩子。
    /// </summary>
    public sealed class ReplayRecorder
    {
        private readonly LocalLockstep _lockstep;
        private readonly WorldState _state;
        private readonly ReplayLog _log;
        private bool _finished;

        public ReplayLog Log => _log;

        private ReplayRecorder(LocalLockstep lockstep, int seed, int[] characterDefIds, int maxTurns)
        {
            _lockstep = lockstep;
            _state = lockstep.State;
            _log = new ReplayLog
            {
                PlayerCount = _state.PlayerCount,
                Seed = seed,
                CharacterDefIds = characterDefIds,
                MaxTurns = maxTurns,
                HumanPlayersMask = new bool[_state.PlayerCount]
            };
            if (lockstep.HumanPlayers != null)
                System.Array.Copy(lockstep.HumanPlayers, _log.HumanPlayersMask,
                    System.Math.Min(lockstep.HumanPlayers.Length, _log.HumanPlayersMask.Length));
            Attach();
        }

        /// <summary>创建录制器并挂接全部旁观钩子。必须在 StartGame 之后调用。</summary>
        public static ReplayRecorder Attach(LocalLockstep lockstep, int seed, int[] characterDefIds,
            int maxTurns = GameConfig.MaxTurns)
        {
            return new ReplayRecorder(lockstep, seed, characterDefIds, maxTurns);
        }

        private void Attach()
        {
            // OnJudgmentCollected 不挂：槽位级收集无消费者——整轮筹码经
            // OnJudgmentCommitted 的完整 allocations 数组记录，逐槽快照属 dead store。
            _lockstep.OnJudgmentCommitted = OnJudgmentCommitted;
            _lockstep.OnActionCommand = OnActionCommand;
            _state.Events.RecorderHook = OnEvent;
            _state.OnTickAdvanced = OnTickAdvanced;
        }

        private void OnJudgmentCommitted(ChipAllocation[] allocations)
        {
            var copy = new ChipAllocation[allocations.Length];
            System.Array.Copy(allocations, copy, allocations.Length);
            _log.Chips.Add(new ReplayChipRound { Allocations = copy });
        }

        private void OnActionCommand(int playerId, ICommand cmd, bool isHuman)
        {
            ReplayActionRecord r = ReplayActionFactory.ToRecord(cmd);
            if (r.Kind == ReplayActionKind.Unknown) return; // 未知类型跳过（引擎内部命令不经此钩子，正常不触发）
            r.Tick = _state.CurrentTick;
            r.PlayerId = playerId;
            r.Source = isHuman ? (byte)1 : (byte)0;
            _log.Actions.Add(r);
        }

        private void OnEvent(int eventType, ref GameplayEvent e)
        {
            _log.Events.Add(new ReplayEventRecord
            {
                Tick = _state.CurrentTick,
                Turn = _state.CurrentTurn,
                EventType = eventType,
                SourceId = e.SourceId,
                TargetId = e.TargetId,
                Magnitude = e.Magnitude,
                Arg1 = e.Arg1,
                Arg2 = e.Arg2,
                IntArg1 = e.IntArg1,
                IntArg2 = e.IntArg2
            });
        }

        private void OnTickAdvanced(int tick)
        {
            if (_log.TickHashes == null || _log.TickHashes.Length < tick)
            {
                // 懒初始化/倍增扩容（正常路径每次 +1，防御跳号；倍增避免 O(n²) 拷贝）
                int newSize = System.Math.Max(tick, _log.TickHashes != null
                    ? _log.TickHashes.Length * 2 : tick);
                var grown = new long[newSize];
                if (_log.TickHashes != null)
                    System.Array.Copy(_log.TickHashes, grown, _log.TickHashes.Length);
                _log.TickHashes = grown;
            }
            // 检查点语义：Tick N 的全部状态变更完成后快照（引擎回调在变更后触发）
            _log.TickHashes[tick - 1] = _state.ComputeStateHash();
        }

        /// <summary>收尾：卸载录制器挂接的钩子，补全终态哈希。返回录制日志。</summary>
        public ReplayLog Finish()
        {
            if (_finished) return _log;
            _finished = true;
            // 只卸自己挂过的 4 个钩子；OnJudgmentCollected 从未挂接（见 Attach），
            // 不在此清空以免误清外部使用者
            _lockstep.OnJudgmentCommitted = null;
            _lockstep.OnActionCommand = null;
            _state.Events.RecorderHook = null;
            _state.OnTickAdvanced = null;

            // 防御：若最后 Tick 递增后无回调（理论不发生），补记终态哈希
            if (_log.TickHashes == null || _log.TickHashes.Length < _state.CurrentTick)
                OnTickAdvanced(_state.CurrentTick);
            // 倍增扩容可能超出实际 Tick 数，截齐到 ReplayLog 契约长度（Length == TotalTicks）
            if (_log.TickHashes != null && _log.TickHashes.Length > _state.CurrentTick)
            {
                var exact = new long[_state.CurrentTick];
                System.Array.Copy(_log.TickHashes, exact, _state.CurrentTick);
                _log.TickHashes = exact;
            }
            _log.TotalTicks = _state.CurrentTick;
            return _log;
        }
    }
}
