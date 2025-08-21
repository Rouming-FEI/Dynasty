using Core;
using Core.GAS;
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
        private WaitForSeconds _wait;

        private void Start()
        {
            _wait = new WaitForSeconds(_turnDelay);
            if (_autoStart)
                StartCoroutine(RunGameLoop());
        }

        private IEnumerator RunGameLoop()
        {
            Debug.Log($"========== Dynasty 对战开始 (seed={_randomSeed}) ==========");

            _state = new WorldState(_playerCount, _randomSeed);

            // Subscribe to all global events for battle log
            SubscribeEvents(_state.Events);

            SimpleAI[] ais = new SimpleAI[_playerCount];
            for (int i = 0; i < _playerCount; i++)
                ais[i] = new SimpleAI(i);

            _lockstep = new LocalLockstep(_state, ais);
            _lockstep.StartGame();
            _running = true;

            // Log initial state
            LogCharacterStates();

            int turnCount = 0;
            while (_running && _state.Phase != TurnPhase.GameOver)
            {
                bool continuing = _lockstep.RunTurn();
                turnCount++;

                if (!continuing || _state.Phase == TurnPhase.GameOver)
                    break;

                // Log character states every turn
                LogCharacterStates();

                yield return _wait;
            }

            Debug.Log($"========== 游戏结束: {turnCount} turns ==========");
            LogCharacterStates();

            long hash = _state.ComputeStateHash();
            Debug.Log($"State hash: {hash}");

            _running = false;
        }

        private void SubscribeEvents(EventBus events)
        {
            events.Subscribe(GameplayEventTypes.JudgmentResolved, 0, (ref GameplayEvent e) =>
            {
                Debug.Log($"[判定结算] AP 分配完成");
            });

            events.Subscribe(GameplayEventTypes.EntityMoved, 0, (ref GameplayEvent e) =>
            {
                var c = _state.GetCharacterByPlayer(e.SourceId);
                Debug.Log($"[移动] {c?.Name ?? "?"} : {_state.Locations.GetName(e.IntArg1)} → {_state.Locations.GetName(e.IntArg2)}");
            });

            events.Subscribe(GameplayEventTypes.PlayerDied, 0, (ref GameplayEvent e) =>
            {
                var c = _state.GetCharacter(e.TargetId);
                var killer = _state.GetCharacter(e.IntArg1);
                Debug.Log($"[死亡] {c?.Name ?? "?"} 被 {killer?.Name ?? "?"} 击杀");
            });

            events.Subscribe(GameplayEventTypes.OnShieldBroken, 0, (ref GameplayEvent e) =>
            {
                var c = _state.GetCharacter(e.TargetId);
                Debug.Log($"[护盾] {c?.Name ?? "?"} 的护盾破碎");
            });

            events.Subscribe(GameplayEventTypes.OnShieldGranted, 0, (ref GameplayEvent e) =>
            {
                var c = _state.GetCharacter(e.TargetId);
                Debug.Log($"[护盾] {c?.Name ?? "?"} 获得护盾");
            });

            events.Subscribe(GameplayEventTypes.DamageDealt, 0, (ref GameplayEvent e) =>
            {
                var src = _state.GetCharacter(e.SourceId);
                var tgt = _state.GetCharacter(e.TargetId);
                string type = ((DamageType)e.IntArg1).ToString();
                Debug.Log($"[伤害] {src?.Name ?? "?"} → {tgt?.Name ?? "?"} : {e.Magnitude:F1} {type}");
            });
        }

        private void LogCharacterStates()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append($"[Round {_state.CurrentTurn}] ");
            for (int i = 0; i < _playerCount; i++)
            {
                Character c = _state.Characters[i];
                if (c == null) continue;
                string dead = c.IsDead ? " [X]" : "";
                string shield = c.ASC.Tags.HasTag(GameplayTagConstants.HasShield) ? " [盾]" : "";
                string location = _state.Locations.GetName(c.CurrentLocationId);
                sb.Append($"{c.Name}:{c.Sanity:F1}HP AP={c.ActionPoints}{shield}{dead} @{location}");
                if (i < _playerCount - 1) sb.Append(" | ");
            }
            Debug.Log(sb.ToString());
        }

        public WorldState GetState() => _state;
        public LocalLockstep GetLockstep() => _lockstep;
    }
}
