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
            _turnManager.StartGame(_state);
        }

        /// <summary>Run one full turn (judgment + action phase). Returns false if game over.</summary>
        public bool RunTurn()
        {
            if (_state.Phase == TurnPhase.GameOver) return false;

            // Judgment phase: collect chips from all AIs
            if (_state.Phase == TurnPhase.JudgmentPhase)
            {
                Span<ChipAllocation> allocations = stackalloc ChipAllocation[_state.PlayerCount];
                for (int i = 0; i < _state.PlayerCount; i++)
                {
                    Character c = _state.GetCharacterByPlayer(i);
                    if (c != null && !c.IsDead)
                        allocations[i] = _ais[i].GetJudgmentInput(_state, i);
                    else
                        allocations[i] = new ChipAllocation(i) { };
                    // Ensure dead players don't affect judgment — set to 0-0-0
                    if (c == null || c.IsDead)
                        allocations[i].Set(0, 0, 0);
                }

                if (!_turnManager.Advance(_state, allocations))
                    return false;
            }

            // Action phase: each actor takes their turn
            while (_state.Phase == TurnPhase.ActionPhase)
            {
                // Find next actor with AP > 0
                int actorPlayerId = GetNextActor();
                if (actorPlayerId < 0) break; // no one can act

                ICommand cmd;
                if (ManualInputProvider != null)
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
                    _state.Commands.Enqueue(cmd);
                    _state.Commands.ExecuteAll(_state);
                    _state.Commands.Clear();
                }

                // Check for phase transitions (death might end game)
                if (_state.Phase == TurnPhase.GameOver) return false;
                if (_state.Phase == TurnPhase.TurnEnd) return true;

                _turnManager.IncrementActorIndex();
            }

            // Final advance to check end conditions
            return _turnManager.Advance(_state, Span<ChipAllocation>.Empty);
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
                if (c != null && !c.IsDead && c.ActionPoints > 0)
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
