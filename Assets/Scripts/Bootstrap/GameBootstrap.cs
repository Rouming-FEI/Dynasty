using Core;
using Network.Local;
using AI;
using Gameplay.Characters;
using System.Collections;
using UnityEngine;

namespace Bootstrap
{
    public class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private int _playerCount = 4;
        [SerializeField] private int _randomSeed = 42;
        [SerializeField] private bool _autoStart = true;
        [SerializeField] private float _turnDelay = 0.5f;

        private WorldState _state;
        private LocalLockstep _lockstep;
        private bool _running;
        private WaitForSeconds _wait; // cached to avoid per-turn GC alloc

        private void Start()
        {
            _wait = new WaitForSeconds(_turnDelay);
            if (_autoStart)
                StartCoroutine(RunGameLoop());
        }

        private IEnumerator RunGameLoop()
        {
            Debug.Log($"[Dynasty] Starting game with {_playerCount} players, seed={_randomSeed}");

            // Initialize
            _state = new WorldState(_playerCount, _randomSeed);

            SimpleAI[] ais = new SimpleAI[_playerCount];
            for (int i = 0; i < _playerCount; i++)
                ais[i] = new SimpleAI(i);

            _lockstep = new LocalLockstep(_state, ais);
            _lockstep.StartGame();
            _running = true;

            Debug.Log($"[Dynasty] Game started. Turn 1 begins.");

            // Main game loop
            int turnCount = 0;
            while (_running && _state.Phase != TurnPhase.GameOver)
            {
                bool continuing = _lockstep.RunTurn();
                turnCount++;

                if (!continuing || _state.Phase == TurnPhase.GameOver)
                    break;

                // Log progress
                if (turnCount % 5 == 0)
                {
                    Debug.Log($"[Dynasty] Turn {_state.CurrentTurn}, Alive: {_state.AliveCharacterCount}/{_playerCount}");
                }

                yield return _wait;
            }

            // Game over
            Debug.Log($"[Dynasty] Game Over after {turnCount} turns, {_state.CurrentTurn} rounds");

            // Find winner
            for (int i = 0; i < _playerCount; i++)
            {
                Character c = _state.Characters[i];
                if (c != null && !c.IsDead)
                {
                    Debug.Log($"[Dynasty] Winner: {c.Name} (Player {c.PlayerId})");
                }
            }

            long hash = _state.ComputeStateHash();
            Debug.Log($"[Dynasty] Final state hash: {hash}");

            _running = false;
        }

        public WorldState GetState() => _state;
        public LocalLockstep GetLockstep() => _lockstep;
    }
}
