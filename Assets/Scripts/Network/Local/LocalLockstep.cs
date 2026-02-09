using Core;
using Core.Command;
using Gameplay.Characters;
using Gameplay.Judgment;
using AI;
using System;

namespace Network.Local
{
    public class LocalLockstep
    {
        private WorldState _state;
        private SimpleAI[] _ais;
        private TurnManager _turnManager;

        public WorldState State => _state;
        public TurnManager TurnManager => _turnManager;

        /// <summary>
        /// Optional manual input hook. When set, it replaces AI action input for all actors.
        /// Return null to indicate input is not ready yet — the turn pauses without
        /// consuming AP and RunTurn returns, waiting to be called again.
        /// </summary>
        public Func<int, ICommand> ManualInputProvider;

        /// <summary>
        /// Optional judgment chip input hook. When set, replaces AI chip allocation
        /// for all players, making the judgment phase deterministic in tests.
        /// </summary>
        public Func<int, ChipAllocation> JudgmentInputProvider;

        /// <summary>真人玩家掩码（热座 = 仅 1 位）。null/空 = 全 AI，走既有路径。</summary>
        public bool[] HumanPlayers;

        /// <summary>
        /// 真人判定筹码钩子。返回 null = 未提交，RunTurn 暂停返回（与
        /// ManualInputProvider 行动暂停语义一致）。仅对 HumanPlayers[i]=true 生效；
        /// 若玩家在掩码中但本钩子为 null，回退 JudgmentInputProvider/AI 取值。
        /// </summary>
        public Func<int, ChipAllocation?> HumanJudgmentInput;

        /// <summary>判定旁观钩子：每个玩家槽筹码收集完成时调用（含死亡零填充槽）。isHuman=真人槽。
        /// 回调约束：禁止修改状态/抛异常——违反会破坏帧同步一致性。</summary>
        public Action<int, ChipAllocation, bool> OnJudgmentCollected;
        /// <summary>判定旁观钩子：整轮判定提交结算后调用（含全局平作废重判轮）。
        /// 回调约束：禁止修改状态/抛异常——违反会破坏帧同步一致性。</summary>
        public Action<ChipAllocation[]> OnJudgmentCommitted;
        /// <summary>行动旁观钩子：输入命令入队后调用。isHuman=真人槽。
        /// 回调约束：禁止修改状态/抛异常——违反会破坏帧同步一致性。
        /// 注意：HumanPlayers == null 旧全手动模式下命令实际来自 ManualInputProvider，
        /// 但 isHuman 报 false（与行动段分流条件「isHuman || HumanPlayers == null」
        /// 不一致）——录制器按 isHuman 区分人机输入时需注意此特例。</summary>
        public Action<int, ICommand, bool> OnActionCommand;

        // 判定收集状态（跨 RunTurn 调用保留：暂停期间 AI 分配不重算，避免随机流漂移）
        private ChipAllocation[] _pendingAllocations;
        private bool[] _humanSlotFilled;
        private bool _judgmentCollected;

        /// <summary>Player id of the next actor eligible to act in the action phase, or -1 if none.</summary>
        public int CurrentActorPlayerId => GetNextActor();

        public LocalLockstep(WorldState state, SimpleAI[] ais)
        {
            _state = state;
            _ais = ais;
            _turnManager = new TurnManager();
            state.TurnManager = _turnManager;
        }

        public void StartGame()
        {
            // 弃局重开：清掉判定暂停的中间状态，否则 Resume 分支会跳过 AI
            // 重收集、复用上一局缓存的筹码（随机流漂移、判定用旧值）。
            // 也覆盖 Advance 返回 false（GameOver）退出时未清理的路径。
            ResetJudgmentPauseState();
            _turnManager.StartGame(_state);
        }

        /// <summary>带注入角色开局（热座/选角用）。无参版保持原行为。</summary>
        public void StartGame(CharacterDef[] defs)
        {
            ResetJudgmentPauseState();
            _turnManager.StartGame(_state, defs);
        }

        /// <summary>判定收集中间态复位（两个 StartGame 入口共用，语义见无参版注释）。</summary>
        private void ResetJudgmentPauseState()
        {
            _pendingAllocations = null;
            _humanSlotFilled = null;
            _judgmentCollected = false;
        }

        /// <summary>Run one full turn (judgment + action phase). Returns false if game over.</summary>
        public bool RunTurn()
        {
            if (_state.Phase == TurnPhase.GameOver) return false;

            // Judgment phase: collect chips (AI 即时一次并缓存；真人待提交，未提交则暂停)
            if (_state.Phase == TurnPhase.JudgmentPhase)
            {
                if (!_judgmentCollected)
                {
                    // 首次收集：AI 全部取值（消耗随机流，仅此一次）；真人槽有提交即填
                    _pendingAllocations = new ChipAllocation[_state.PlayerCount];
                    _humanSlotFilled = new bool[_state.PlayerCount];
                    bool anyPending = false;
                    for (int i = 0; i < _state.PlayerCount; i++)
                    {
                        Character c = _state.GetCharacterByPlayer(i);
                        bool isHuman = HumanPlayers != null
                            && i < HumanPlayers.Length && HumanPlayers[i];
                        if (c == null || c.IsDead)
                        {
                            ZeroFill(i, isHuman);
                            continue;
                        }
                        if (isHuman && HumanJudgmentInput != null)
                        {
                            ChipAllocation? submitted = HumanJudgmentInput(i);
                            if (submitted == null) { anyPending = true; continue; }
                            _pendingAllocations[i] = submitted.Value;
                        }
                        else if (JudgmentInputProvider != null)
                        {
                            _pendingAllocations[i] = JudgmentInputProvider(i);
                        }
                        else
                        {
                            _pendingAllocations[i] = _ais[i].GetJudgmentInput(_state, i);
                        }
                        OnJudgmentCollected?.Invoke(i, _pendingAllocations[i], isHuman);
                        _humanSlotFilled[i] = true;
                    }
                    _judgmentCollected = true;
                    if (anyPending)
                        return true;   // 真人未就绪：暂停；AI 值留在 _pendingAllocations 缓存
                }
                else
                {
                    // 已收集过：只补查未就绪的真人槽，AI 不再消耗随机流
                    bool anyPending = false;
                    for (int i = 0; i < _state.PlayerCount; i++)
                    {
                        if (_humanSlotFilled[i]) continue;
                        Character c = _state.GetCharacterByPlayer(i);
                        bool isHuman = HumanPlayers != null
                            && i < HumanPlayers.Length && HumanPlayers[i];
                        if (!isHuman || HumanJudgmentInput == null || c == null || c.IsDead)
                        {
                            // 防御分支：非真人槽不该处于未填状态；真人中途死亡补零
                            ZeroFill(i, isHuman);
                            continue;
                        }
                        ChipAllocation? submitted = HumanJudgmentInput(i);
                        if (submitted == null) { anyPending = true; continue; }
                        _pendingAllocations[i] = submitted.Value;
                        OnJudgmentCollected?.Invoke(i, _pendingAllocations[i], isHuman);
                        _humanSlotFilled[i] = true;
                    }
                    if (anyPending) return true;
                }

                if (!_turnManager.Advance(_state, _pendingAllocations))
                    return false;
                OnJudgmentCommitted?.Invoke(_pendingAllocations);

                // 本轮判定完成（或全局平作废重判）：清理，下轮重新收集
                _judgmentCollected = false;
                _pendingAllocations = null;
                _humanSlotFilled = null;

                // 全局平筹码作废重来：Phase 仍 JudgmentPhase，下轮 RunTurn 重新收集
                if (_state.Phase == TurnPhase.JudgmentPhase)
                    return true;
            }

            // Action phase: each actor takes their turn
            while (_state.Phase == TurnPhase.ActionPhase)
            {
                // Find next actor with AP > 0
                int actorPlayerId = GetNextActor();
                if (actorPlayerId < 0) break; // no one can act

                ICommand cmd;
                // 掩码分流：真人槽走 ManualInputProvider（null 暂停）；AI 槽走 SimpleAI
                // （绝不暂停）。旧模式（HumanPlayers == null，如 ManualTestUI 全手动）
                // 下 ManualInputProvider 全接管。掩码标记真人但钩子为 null 时回退 AI
                // （与判定段 HumanJudgmentInput 缺省回退语义一致）。
                bool isHuman = HumanPlayers != null
                    && actorPlayerId < HumanPlayers.Length && HumanPlayers[actorPlayerId];
                if (ManualInputProvider != null && (isHuman || HumanPlayers == null))
                {
                    cmd = ManualInputProvider(actorPlayerId);
                    // Input not ready yet — pause here without consuming AP or
                    // advancing the actor index; caller re-invokes RunTurn later.
                    if (cmd == null) return true;
                }
                else
                {
                    cmd = _ais[actorPlayerId].GetActionInput(_state, actorPlayerId);
                }

                if (cmd != null)
                {
                    // 每条行动命令执行前：推进逻辑 Tick；OnTickAdvanced 在命令
                    // 执行完成后触发（录制检查点 = 每 Tick 完成后状态）
                    _state.CurrentTick++;
                    _state.Commands.Enqueue(cmd);
                    OnActionCommand?.Invoke(actorPlayerId, cmd, isHuman);
                    _state.Commands.ExecuteAll(_state);
                    _state.Commands.Clear();
                    _state.OnTickAdvanced?.Invoke(_state.CurrentTick);
                }

                // Check for phase transitions (death might end game)
                if (_state.Phase == TurnPhase.GameOver) return false;
                if (_state.Phase == TurnPhase.TurnEnd) return true;

                _turnManager.IncrementActorIndex();
            }

            // Final advance to check end conditions
            return _turnManager.Advance(_state, Span<ChipAllocation>.Empty);
        }

        /// <summary>填零分配（ChipAllocation 构造即全零）并标记槽位已填。</summary>
        private void ZeroFill(int i, bool isHuman)
        {
            _pendingAllocations[i] = new ChipAllocation(i);
            _humanSlotFilled[i] = true;
            OnJudgmentCollected?.Invoke(i, _pendingAllocations[i], isHuman);
        }

        private int GetNextActor()
        {
            int[] order = _turnManager.ActionOrder;
            int startIdx = _turnManager.CurrentActorIndex;

            for (int offset = 0; offset < order.Length; offset++)
            {
                int idx = (startIdx + offset) % order.Length;
                int playerId = order[idx];
                Character c = _state.GetCharacterByPlayer(playerId);
                if (c != null && !c.IsDead && (c.ActionPoints > 0 || c.ASC.GetGuardianCharge() >= 3))
                    return playerId;
            }
            return -1;
        }

        public int RunGame(int maxTurns = GameConfig.MaxTurns)
        {
            StartGame();
            int turnCount = 0;
            while (turnCount < maxTurns && _state.Phase != TurnPhase.GameOver)
            {
                if (!RunTurn()) break;
                turnCount++;
            }
            return turnCount;
        }
    }
}
