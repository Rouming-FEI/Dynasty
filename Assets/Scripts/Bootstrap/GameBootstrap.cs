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
        [SerializeField] private bool _hotseatMode = true;   // P4 默认热座

        private WorldState _state;
        private LocalLockstep _lockstep;
        private SimpleAI[] _ais;
        private bool _running;
        private bool _gameStarted;
        private WaitForSeconds _wait;
        private WaitForSeconds _manualWait;

        private void Start()
        {
            _wait = new WaitForSeconds(_turnDelay);
            _manualWait = new WaitForSeconds(_manualPollDelay);

            _state = new WorldState(_playerCount, _randomSeed);
            SubscribeEvents(_state.Events);

            _ais = new SimpleAI[_playerCount];
            for (int i = 0; i < _playerCount; i++)
                _ais[i] = new SimpleAI(i);

            // 热座模式：等 UI 选角后 BeginHotseat；纯 AI 模式直接开。
            // （热座下 _lockstep 延迟到 BeginHotseat 创建，ManualTestUI 已停用）
            if (!_hotseatMode && _autoStart)
            {
                _lockstep = new LocalLockstep(_state, _ais);
                _lockstep.StartGame();
                _gameStarted = true;   // 纯 AI 也是已开局：选角层不弹、BeginHotseat 不重入
                StartCoroutine(RunGameLoop());
            }
        }

        private IEnumerator RunGameLoop()
        {
            // 等待一帧，让 ManualTestUI 等组件有机会注册 ManualInputProvider，
            // 避免首个行动被 AI 抢跑
            yield return null;

            Debug.Log($"========== Dynasty 对战开始 (seed={_randomSeed}) ==========");

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

                // 热座：真人存活用短轮询（随时提交输入）；真人死亡观战切回串场节奏。
                // ManualInputProvider 在热座 Bind 后常驻（不因死亡解绑），故热座下
                // 只看真人存活；非热座保留旧判据（ManualTestUI 全手动接管时轮询）
                bool fastPoll;
                if (_hotseatMode)
                {
                    Character human = _state.GetCharacterByPlayer(0);
                    fastPoll = human != null && !human.IsDead;
                }
                else
                {
                    fastPoll = _lockstep.ManualInputProvider != null;
                }
                yield return fastPoll ? _manualWait : _wait;
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
                if (_state.HasJudgmentResult)
                {
                    var j = _state.LastJudgment;
                    string[] handNames = { "石头", "剪刀", "布" };
                    sb.Append("  胜手: ").Append(j.WinnerHand >= 0 ? handNames[j.WinnerHand] : "全局平筹码").Append(" | ");
                    sb.Append("败手: ").Append(j.LoserHand >= 0 ? handNames[j.LoserHand] : "-").Append(" | ");
                    sb.Append("中立: ").AppendLine(j.NeutralHand >= 0 ? handNames[j.NeutralHand] : "-");
                }
                sb.Append("  筹码: ");
                for (int i = 0; i < _state.PlayerCount; i++)
                {
                    Character c = _state.Characters[i];
                    if (c == null || c.IsDead) continue;
                    var a = _state.LastAllocations[i];
                    sb.Append($"{c.Name}:石{a.Rock}剪{a.Scissors}布{a.Paper}");
                    if (i < _state.PlayerCount - 1) sb.Append(" | ");
                }
                sb.AppendLine();
                sb.Append("  AP: ");
                for (int i = 0; i < _state.PlayerCount; i++)
                {
                    Character c = _state.Characters[i];
                    if (c == null || c.IsDead) continue;
                    string role = "";
                    if (_state.HasJudgmentResult)
                        role = _state.LastJudgment.Roles[i] == Gameplay.Judgment.JudgmentRole.Winner ? "[胜]" :
                               _state.LastJudgment.Roles[i] == Gameplay.Judgment.JudgmentRole.Loser ? "[败]" : "[中]";
                    sb.Append($"{c.Name}:{c.ActionPoints}AP{role}");
                    if (i < _state.PlayerCount - 1) sb.Append(" | ");
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

            events.Subscribe(GameplayEventTypes.NukeLaunched, 0, (ref GameplayEvent e) =>
            {
                var launcher = _state.GetCharacter(e.SourceId);
                Debug.Log(e.IntArg1 == 1
                    ? $"[核弹] {launcher?.Name ?? "?"} 判定获胜，核弹发射！全场 3 点真实伤害（藏匿者免疫）"
                    : $"[核弹] {launcher?.Name ?? "?"} 判定落败，发射失败");
            });

            events.Subscribe(GameplayEventTypes.TrapTriggered, 0, (ref GameplayEvent e) =>
            {
                var trigger = _state.GetCharacter(e.TargetId);
                string cause = trigger != null ? $"被 {trigger.Name} 触发" : "被远程引爆";
                Debug.Log($"[炸弹] {_state.Locations.GetName(e.IntArg1)} 的触发式炸弹{cause}");
            });

            events.Subscribe(GameplayEventTypes.ThornDotTick, 0, (ref GameplayEvent e) =>
            {
                var c = _state.GetCharacter(e.TargetId);
                Debug.Log($"[荆棘素] {c?.Name ?? "?"} 本回合移动过，受荆棘反噬");
            });

            // 死亡豁免：DamagePipeline 仅在豁免生效时向全局总线派发 OnDamageTaken (IntArg1=1)
            events.Subscribe(GameplayEventTypes.OnDamageTaken, 0, (ref GameplayEvent e) =>
            {
                if (e.IntArg1 != 1) return;
                var c = _state.GetCharacter(e.TargetId);
                Debug.Log($"[死亡豁免] {c?.Name ?? "?"} 濒死之际被应急处理装置救回 (0.5 HP)");
            });

            events.Subscribe(GameplayEventTypes.TidePulse, 0, (ref GameplayEvent e) =>
            {
                string[] handNames = { "石头", "剪刀", "布" };
                string indoor = e.IntArg1 >= 0 && e.IntArg1 < 3 ? handNames[e.IntArg1] : "?";
                string outdoor = e.IntArg2 >= 0 && e.IntArg2 < 3 ? handNames[e.IntArg2] : "?";
                string outcome = e.Magnitude == 1f ? "室内一方受创" :
                                 e.Magnitude == 2f ? "室外一方受创" : "平局，无人受伤";
                Debug.Log($"[实验室] 深度潮汐判定: 室内出{indoor} vs 室外出{outdoor} → {outcome}");
            });

            events.Subscribe(GameplayEventTypes.TurretFired, 0, (ref GameplayEvent e) =>
            {
                var src = _state.GetCharacter(e.SourceId);
                var tgt = _state.GetCharacter(e.TargetId);
                Debug.Log($"[炮台] {src?.Name ?? "?"} 操纵地狱前哨炮台轰击 {tgt?.Name ?? "?"}");
            });
        }

        private void LogCharacterStates()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append($"[Round {_state.CurrentTurn}] ");
            for (int i = 0; i < _state.PlayerCount; i++)
            {
                Character c = _state.Characters[i];
                if (c == null) continue;
                string dead = c.IsDead ? " [X]" : "";
                string shield = c.ASC.Tags.HasTag(GameplayTagConstants.HasShield) ? " [盾]" : "";
                string location = _state.Locations.GetName(c.CurrentLocationId);

                // 装备武器显示：枪附带弹药数（[枪1弹]），其他武器只显示名称（[施工锤]）
                string weapon = "";
                var equipped = c.Inventory.EquippedWeapon;
                if (equipped != null)
                    weapon = equipped.Id == 3
                        ? $" [{equipped.Name}{c.Inventory.EquippedWeaponAmmo}弹]"
                        : $" [{equipped.Name}]";

                sb.Append($"{c.Name}:{c.Sanity:F1}HP AP={c.ActionPoints}{shield}{dead} @{location}{weapon}");
                if (i < _state.PlayerCount - 1) sb.Append(" | ");
            }
            Debug.Log(sb.ToString());
        }

        public WorldState GetState() => _state;
        public LocalLockstep GetLockstep() => _lockstep;
        public int PlayerCount => _playerCount;

        /// <summary>是否已开局（热座选角画面显隐依据）。</summary>
        public bool IsGameStarted() => _gameStarted;

        /// <summary>热座开局：用注入角色建局并启动主循环（UI 选角后调用）。</summary>
        public void BeginHotseat(CharacterDef[] defs)
        {
            if (_gameStarted) return;
            _lockstep = new LocalLockstep(_state, _ais);
            _lockstep.StartGame(defs);
            _gameStarted = true;
            StartCoroutine(RunGameLoop());
        }

        /// <summary>
        /// 热座重开：重置世界回到待选角状态。
        /// 同种子重开 = 可复现对局（AI 角色抽取与初始状态一致，走向由真人输入分化）。
        /// </summary>
        public void RestartHotseat()
        {
            StopAllCoroutines();
            _running = false;
            _gameStarted = false;
            _lockstep = null;
            _state = new WorldState(_playerCount, _randomSeed);
            SubscribeEvents(_state.Events);
        }

        /// <summary>热座重开：指定种子 + 注入角色（天梯挑战层局用——层局人数与 AI 流按 defs
        /// 重建，2..8 人层通用；热座默认路径不调本重载，仍走 Start 的 _playerCount）。</summary>
        public void RestartHotseat(int seed, CharacterDef[] defs)
        {
            StopAllCoroutines();
            _running = false;
            _gameStarted = false;
            _lockstep = null;
            _randomSeed = seed;
            // 天梯层局人数 = defs 长度（第 k 层 = k+1 人局，2..8）；AI 流按局重建（>4 人层原数组越界）。
            // 热座默认路径不调本重载，无影响。
            int n = defs != null && defs.Length >= 2 ? defs.Length : _playerCount;
            _state = new WorldState(n, _randomSeed);
            _ais = new SimpleAI[n];
            for (int i = 0; i < n; i++) _ais[i] = new SimpleAI(i);
            SubscribeEvents(_state.Events);
            BeginHotseat(defs);
        }
    }
}
