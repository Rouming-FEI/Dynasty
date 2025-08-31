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
        [SerializeField] private float _manualPollDelay = 0.05f;

        private WorldState _state;
        private LocalLockstep _lockstep;
        private bool _running;
        private WaitForSeconds _wait;
        private WaitForSeconds _manualWait;

        private void Start()
        {
            _wait = new WaitForSeconds(_turnDelay);
            _manualWait = new WaitForSeconds(_manualPollDelay);

            // 在 Start 中同步创建世界与 Lockstep，保证其他组件（如 ManualTestUI）
            // 在自己的 Start/Update 里能通过 GetLockstep() 拿到实例并注册输入
            _state = new WorldState(_playerCount, _randomSeed);

            // Subscribe to all global events for battle log
            SubscribeEvents(_state.Events);

            SimpleAI[] ais = new SimpleAI[_playerCount];
            for (int i = 0; i < _playerCount; i++)
                ais[i] = new SimpleAI(i);

            _lockstep = new LocalLockstep(_state, ais);

            if (_autoStart)
                StartCoroutine(RunGameLoop());
        }

        private IEnumerator RunGameLoop()
        {
            // 等待一帧，让 ManualTestUI 等组件有机会注册 ManualInputProvider，
            // 避免首个行动被 AI 抢跑
            yield return null;

            Debug.Log($"========== Dynasty 对战开始 (seed={_randomSeed}) ==========");

            _lockstep.StartGame();
            _running = true;

            // Log initial state
            LogCharacterStates();

            int lastLoggedTurn = _state.CurrentTurn;
            while (_running && _state.Phase != TurnPhase.GameOver)
            {
                bool continuing = _lockstep.RunTurn();

                if (!continuing || _state.Phase == TurnPhase.GameOver)
                    break;

                // 手动模式下 RunTurn 以轮询频率被反复调用（等待玩家点击时
                // 返回 true 但回合未推进），仅在回合数变化时打印，避免刷屏
                if (_state.CurrentTurn != lastLoggedTurn)
                {
                    LogCharacterStates();
                    lastLoggedTurn = _state.CurrentTurn;
                }

                // 手动模式用短间隔轮询输入；AI 模式保持原节奏
                yield return _lockstep.ManualInputProvider != null ? _manualWait : _wait;
            }

            Debug.Log($"========== 游戏结束: {_state.CurrentTurn} rounds ==========");
            LogCharacterStates();

            long hash = _state.ComputeStateHash();
            Debug.Log($"State hash: {hash}");

            _running = false;
        }

        private void SubscribeEvents(EventBus events)
        {
            events.Subscribe(GameplayEventTypes.JudgmentResolved, 0, (ref GameplayEvent e) =>
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine($"[判定结算] Round {_state.CurrentTurn}");
                // Show last judgment detail if available
                if (_state.LastJudgment != null)
                {
                    var j = _state.LastJudgment;
                    string[] handNames = { "石头", "剪刀", "布" };
                    sb.Append("  胜手: ").Append(j.WinnerHand >= 0 ? handNames[j.WinnerHand] : "全局平筹码").Append(" | ");
                    sb.Append("败手: ").Append(j.LoserHand >= 0 ? handNames[j.LoserHand] : "-").Append(" | ");
                    sb.Append("中立: ").AppendLine(j.NeutralHand >= 0 ? handNames[j.NeutralHand] : "-");
                }
                sb.Append("  AP: ");
                for (int i = 0; i < _playerCount; i++)
                {
                    Character c = _state.Characters[i];
                    if (c == null || c.IsDead) continue;
                    string role = "";
                    if (_state.LastJudgment != null)
                        role = _state.LastJudgment.Roles[i] == Gameplay.Judgment.JudgmentRole.Winner ? "[胜]" :
                               _state.LastJudgment.Roles[i] == Gameplay.Judgment.JudgmentRole.Loser ? "[败]" : "[中]";
                    sb.Append($"{c.Name}:{c.ActionPoints}AP{role}");
                    if (i < _playerCount - 1) sb.Append(" | ");
                }
                Debug.Log(sb.ToString());
            });

            events.Subscribe(GameplayEventTypes.EntityMoved, 0, (ref GameplayEvent e) =>
            {
                var c = _state.GetCharacterByPlayer(e.SourceId);
                Debug.Log($"[移动] {c?.Name ?? "?"} : {_state.Locations.GetName(e.IntArg1)} → {_state.Locations.GetName(e.IntArg2)}");
            });

            events.Subscribe(GameplayEventTypes.PlayerDied, 0, (ref GameplayEvent e) =>
            {
                var c = _state.GetCharacter(e.TargetId);
                var killer = _state.GetCharacterByPlayer(e.IntArg1);
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
