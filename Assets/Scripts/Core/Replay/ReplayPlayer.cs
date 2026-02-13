using System;
using System.Collections.Generic;
using AI;
using Core.Command;
using Gameplay.Characters;
using Gameplay.Judgment;
using Network.Local;

namespace Core.Replay
{
    /// <summary>
    /// 回放驱动器：用 ReplayLog 重建全新 WorldState + LocalLockstep。
    /// 真人输入注入（HumanPlayers 掩码 + HumanJudgmentInput + ManualInputProvider），
    /// AI 输入不注入（AI 重决策，随机流消耗与录制侧自然对齐）。
    /// 每 Tick 状态变更完成后（OnTickAdvanced，与录制端对称的检查点时机）
    /// 校验 ComputeStateHash 与录制检查点，首个不一致即定位偏差 Tick。
    /// 限制：录制侧若设置 JudgmentInputProvider 全员接管判定，AI 槽不消耗随机流，
    /// 回放侧 AI 重决策会错位——此类日志不可回放（当前录制路径不设
    /// JudgmentInputProvider，仅真人局/纯 AI 局可回放）。
    /// 同理，HumanPlayers == null 旧全手动模式（ManualInputProvider 全接管）下
    /// 输入来源标记失真，此类日志同样不可回放。
    /// </summary>
    public sealed class ReplayPlayer
    {
        private readonly ReplayLog _log;
        private readonly WorldState _state;
        private readonly LocalLockstep _lockstep;
        private readonly Queue<ReplayActionRecord>[] _humanActions;
        private int _chipRound;
        private int _actionsConsumed;
        private int _deviationTick = -1;

        public WorldState State => _state;
        public LocalLockstep Lockstep => _lockstep;
        public int DeviationTick => _deviationTick;
        public bool HasDeviation => _deviationTick >= 0;

        public bool Finished =>
            _state.Phase == TurnPhase.GameOver || _state.CurrentTick >= _log.TotalTicks;

        /// <summary>当前 Tick 的实时状态哈希。</summary>
        public long CurrentHash => _state.ComputeStateHash();

        /// <summary>当前 Tick 的录制期望哈希（Tick 0 时返回 0）。</summary>
        public long ExpectedHash =>
            _state.CurrentTick >= 1 && _state.CurrentTick <= _log.TickHashes.Length
                ? _log.TickHashes[_state.CurrentTick - 1]
                : 0;

        public ReplayPlayer(ReplayLog log)
        {
            _log = log;
            _state = new WorldState(log.PlayerCount, log.Seed);
            var ais = new SimpleAI[log.PlayerCount];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            _lockstep = new LocalLockstep(_state, ais);
            StartGameWith(log.CharacterDefIds);

            _humanActions = new Queue<ReplayActionRecord>[log.PlayerCount];
            for (int i = 0; i < log.PlayerCount; i++)
                _humanActions[i] = new Queue<ReplayActionRecord>();
            for (int i = 0; i < log.Actions.Count; i++)
            {
                ReplayActionRecord a = log.Actions[i];
                if (a.Source == 1 && a.PlayerId >= 0 && a.PlayerId < log.PlayerCount)
                    _humanActions[a.PlayerId].Enqueue(a);
            }

            bool[] mask = log.HumanPlayersMask ?? new bool[log.PlayerCount];
            _lockstep.HumanPlayers = mask;
            _lockstep.HumanJudgmentInput = TakeChips;
            _lockstep.ManualInputProvider = TakeHumanAction;
            _lockstep.OnJudgmentCommitted = allocs => _chipRound++;
            _state.OnTickAdvanced = VerifyTick;
        }

        private void StartGameWith(int[] defIds)
        {
            if (defIds == null || defIds.Length == 0 || AllZero(defIds))
            {
                _lockstep.StartGame(); // 默认轮换
                return;
            }
            for (int i = 0; i < defIds.Length; i++)
                if (defIds[i] <= 0)
                    throw new InvalidOperationException(
                        $"CharacterDefIds[{i}] = {defIds[i]} 非法（0 仅允许全零数组 = 默认轮换）");
            var defs = new CharacterDef[_state.PlayerCount];
            for (int i = 0; i < defs.Length; i++)
            {
                defs[i] = CreateDef(defIds[i % defIds.Length]);
                if (defs[i] == null)
                    throw new InvalidOperationException($"未知角色 Def Id: {defIds[i % defIds.Length]}");
            }
            _lockstep.StartGame(defs);
        }

        private static bool AllZero(int[] ids)
        {
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] != 0) return false;
            return true;
        }

        /// <summary>按角色 Def Id 重建定义（1-13 全表；0 由 AllZero 分支处理）。</summary>
        private static CharacterDef CreateDef(int id)
        {
            switch (id)
            {
                case 1: return GuardianDef.Create();
                case 2: return CharmSpiritDef.Create();
                case 3: return NormalPersonDef.Create();
                case 4: return SwordGhostDef.Create();
                case 5: return MagicianDef.Create();
                case 6: return TravelerDef.Create();
                case 7: return TimeThiefDef.Create();
                case 8: return ListenerDef.Create();
                case 9: return SeaMirageDef.Create();
                case 10: return WoodSpiritDef.Create();
                case 11: return FormationMasterDef.Create();
                case 12: return PaladinDef.Create();
                case 13: return AgentDef.Create();
                default: return null;
            }
        }

        private ChipAllocation? TakeChips(int playerId)
        {
            // 越界/缺轮防御：返回全零筹码（Total=0）——正常路径不会发生，
            // 若发生由后续哈希偏差暴露
            if (_chipRound >= _log.Chips.Count)
                return new ChipAllocation(playerId);
            ChipAllocation[] round = _log.Chips[_chipRound].Allocations;
            if (playerId < 0 || playerId >= round.Length)
                return new ChipAllocation(playerId); // 同上：越界兜底
            return round[playerId];
        }

        private ICommand TakeHumanAction(int playerId)
        {
            Queue<ReplayActionRecord> q = _humanActions[playerId];
            if (q.Count == 0) return null; // 防御：队列耗尽，RunTurn 暂停由步进 guard 兜底
            ReplayActionRecord r = q.Dequeue();
            _actionsConsumed++;
            return ReplayActionFactory.Create(r, _state.CurrentTick);
        }

        private void VerifyTick(int tick)
        {
            if (_deviationTick >= 0) return;
            if (tick >= 1 && tick <= _log.TickHashes.Length)
            {
                long expected = _log.TickHashes[tick - 1];
                if (_state.ComputeStateHash() != expected)
                    _deviationTick = tick;
            }
        }

        /// <summary>推进到 CurrentTick 增加（至少一个 Tick）。返回 false 表示已到终点或已偏差。</summary>
        public bool StepTick()
        {
            if (Finished || HasDeviation) return false;
            int target = _state.CurrentTick + 1;
            return AdvanceTo(() => _state.CurrentTick >= target);
        }

        /// <summary>推进一个完整回合（判定→行动→回合末）。</summary>
        public bool StepTurn()
        {
            if (Finished || HasDeviation) return false;
            int start = _state.CurrentTurn;
            return AdvanceTo(() => _state.CurrentTurn != start || _state.Phase == TurnPhase.GameOver);
        }

        /// <summary>推进到下一条真人行动命令被消费。</summary>
        public bool StepCommand()
        {
            if (Finished || HasDeviation) return false;
            int consumed = _actionsConsumed;
            return AdvanceTo(() => _actionsConsumed != consumed);
        }

        private bool AdvanceTo(Func<bool> done)
        {
            int guard = 0;
            while (!done() && guard++ < 10000)
            {
                int tickBefore = _state.CurrentTick;
                int turnBefore = _state.CurrentTurn;
                if (!_lockstep.RunTurn()) break; // GameOver
                // 停顿检测：行动阶段零推进 = 真人命令队列耗尽等卡死（判定作废重判轮
                // Phase 为 JudgmentPhase，属正常暂停，不误报）
                if (_state.CurrentTick == tickBefore && _state.CurrentTurn == turnBefore
                    && !done() && _state.Phase == TurnPhase.ActionPhase)
                    return false;
            }
            return !Finished && !HasDeviation;
        }
    }
}
