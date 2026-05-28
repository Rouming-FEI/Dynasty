using System;
using Bootstrap;
using Core;
using Gameplay.Characters;
using Gameplay.Ladder;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI
{
    /// <summary>
    /// 天梯挑战真人游玩控制器：挑战主菜单（档案）/ 进行中 HUD / 层间与终局弹层。
    /// 层局承载 = 复用热座链路：GameBootstrap.RestartHotseat(seed, defs) 复位 →
    /// BattleUIController.BeginBattleWithDefs(defs) 开局（热座全交互即真人操作）。
    /// 每帧轮询层局终态（GameOver 或槽 0 死亡）→ ReportHumanLayerEnd 推进会话。
    /// 挂载要求：UIDocument（Source Asset = LadderUI.uxml）+ Inspector 引用 GameBootstrap 与 BattleUIController。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ChallengeUIController : MonoBehaviour
    {
        [SerializeField] private GameBootstrap _bootstrap;
        [SerializeField] private BattleUIController _battleUI;

        private IProfileStore _store;
        private ChallengeRunner _runner;
        private ChallengeRunSession _session;
        private int _runSeed;
        private bool _layerRunning;

        private VisualElement _menuRoot, _hudRoot, _overlayRoot;
        private Label _menuRank, _menuStats, _menuContinueHint;
        private Label _hudLayer, _hudChar, _hudStreak;
        private Label _overlayTitle, _overlayBody;
        private Button _overlayPrimary, _overlaySecondary;
        private bool _warned;

        private void Start()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[LadderUI] UIDocument.rootVisualElement 为空——请在 Inspector 为 UIDocument 拖入 LadderUI.uxml");
                enabled = false;
                return;
            }
            _menuRoot = root.Q<VisualElement>("menu-root");
            _hudRoot = root.Q<VisualElement>("hud-root");
            _overlayRoot = root.Q<VisualElement>("overlay-root");
            _menuRank = root.Q<Label>("menu-rank-label");
            _menuStats = root.Q<Label>("menu-stats-label");
            _menuContinueHint = root.Q<Label>("menu-continue-hint");
            _hudLayer = root.Q<Label>("hud-layer-label");
            _hudChar = root.Q<Label>("hud-char-label");
            _hudStreak = root.Q<Label>("hud-streak-label");
            _overlayTitle = root.Q<Label>("overlay-title");
            _overlayBody = root.Q<Label>("overlay-body");
            _overlayPrimary = root.Q<Button>("overlay-primary-button");
            _overlaySecondary = root.Q<Button>("overlay-secondary-button");

            root.Q<Button>("new-run-button").clicked += OnNewRun;
            root.Q<Button>("continue-button").clicked += OnContinue;
            root.Q<Button>("to-hotseat-button").clicked += OnToHotseat;
            root.Q<Button>("abandon-button").clicked += OnAbandon;

            // 档案存储：Simulation FileProfileStore + Unity persistentDataPath
            _store = new FileProfileStore(Application.persistentDataPath);
            _runner = new ChallengeRunner(new ChallengeConfig(), _store);

            ShowMenu();
        }

        private void Update()
        {
            if (!_layerRunning) return;
            if (_bootstrap == null || _session == null || !_session.IsActive)
            {
                _layerRunning = false;
                return;
            }
            PollLayerEnd();
        }

        /// <summary>轮询热座层局：GameOver 或槽 0 死亡 → 收层。</summary>
        private void PollLayerEnd()
        {
            WorldState state = _bootstrap.GetState();
            if (state == null) return;
            Character human = state.GetCharacterByPlayer(0);
            bool alive0 = human != null && !human.IsDead;
            if (state.Phase != TurnPhase.GameOver && alive0) return;   // 层局未决

            _layerRunning = false;
            var info = new ChallengeRunner.LayerRunInfo
            {
                Phase = state.Phase,
                Slot0Alive = alive0,
                Rounds = 0
            };
            _session = _runner.ReportHumanLayerEnd(info);
            AfterLayerSettled();
        }

        /// <summary>层局结算后：吃鸡 → 层间弹层；终局 → 结算卡（清 ActiveRun）。</summary>
        private void AfterLayerSettled()
        {
            if (_session.IsComplete)
            {
                ClearActiveRun();
                _battleUI.ResetToCharacterSelect();
                ShowOverlay(_session.Result.Won ? "登顶！" : "挑战结束",
                    $"连胜 {_session.Result.Streak} · 得分 {_session.Result.Score}\n"
                    + (_session.Result.Won ? "皇朝之巅，无人能及。" : $"止步于第 {_session.Result.FailedAtLayer} 层。"),
                    "回主菜单", () => { HideOverlay(); ShowMenu(); });
                return;
            }

            // 吃鸡过层：层间弹层
            ShowOverlay($"第 {_session.CurrentLayer - 1} 层 吃鸡！",
                $"即将进入第 {_session.CurrentLayer} 层（{_session.CurrentLayer + 1} 人局）。\n"
                + "每层角色重新抽签——新对手、新组合。",
                "继续下一层", StartNextLayer,
                "暂停挑战（存档点）", PauseRun);
        }

        private void StartNextLayer()
        {
            HideOverlay();
            StartLayer(_session);
        }

        private void StartLayer(ChallengeRunSession session)
        {
            if (_bootstrap == null || _battleUI == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogError("[LadderUI] 未指定 GameBootstrap / BattleUIController 引用——请在 Inspector 拖入");
                }
                return;
            }
            _battleUI.ResetToCharacterSelect();
            _menuRoot.style.display = DisplayStyle.None;
            _hudRoot.style.display = DisplayStyle.Flex;
            _hudLayer.text = $"第 {session.CurrentLayer} 层 / {LadderRules.MaxLayers} · {session.CurrentLayer + 1} 人局";
            _hudChar.text = $"我的角色：{session.CurrentDefs[0]?.Name ?? "?"}";
            // 会话语义：CurrentLayer = 待跑层号，其前各层已吃鸡 → 连胜 = CurrentLayer - 1（含 Resume 场景）
            _hudStreak.text = $"本挑战连胜：{session.CurrentLayer - 1}";

            _bootstrap.RestartHotseat(session.CurrentLayerSeed, session.CurrentDefs);
            _battleUI.BeginBattleWithDefs(session.CurrentDefs);
            _layerRunning = true;
        }

        // ---------------- 菜单动作 ----------------

        private void OnNewRun()
        {
            _runSeed = Environment.TickCount ^ Guid.NewGuid().GetHashCode();
            if (_runSeed == 0) _runSeed = 42;
            _session = _runner.BeginHumanRun(_runSeed);
            ClearActiveRun();                     // 新挑战覆盖旧存档点
            StartLayer(_session);
        }

        private void OnContinue()
        {
            LadderProfile p = _store.Load();
            if (!p.HasActiveRun) return;
            _runSeed = p.ActiveSeed;
            _session = _runner.ResumeHumanRun(p.ActiveSeed, p.ActiveLayer);
            StartLayer(_session);
        }

        private void OnToHotseat()
        {
            // 露出热座选角（BattleUI 常驻层）：隐藏挑战菜单
            _menuRoot.style.display = DisplayStyle.None;
            _hudRoot.style.display = DisplayStyle.None;
        }

        private void OnAbandon()
        {
            if (!_layerRunning) return;
            // 弃局 = 当前层判败（层内中断不存档；结束挑战）
            _layerRunning = false;
            var info = new ChallengeRunner.LayerRunInfo
            {
                Phase = TurnPhase.ActionPhase,   // 局未决
                Slot0Alive = false               // 弃局按槽 0 败计
            };
            _session = _runner.ReportHumanLayerEnd(info);
            AfterLayerSettled();
        }

        private void PauseRun()
        {
            // 层间存档点：吃鸡已完成 → 下次从 session.CurrentLayer 继续
            LadderProfile p = _store.Load();
            p.HasActiveRun = true;
            p.ActiveSeed = _runSeed;
            p.ActiveLayer = _session.CurrentLayer;
            _store.Save(p);
            HideOverlay();
            _battleUI.ResetToCharacterSelect();
            ShowMenu();
        }

        private void ClearActiveRun()
        {
            LadderProfile p = _store.Load();
            if (!p.HasActiveRun) return;
            p.HasActiveRun = false;
            _store.Save(p);
        }

        // ---------------- 屏显 ----------------

        private void ShowMenu()
        {
            _hudRoot.style.display = DisplayStyle.None;
            _overlayRoot.style.display = DisplayStyle.None;
            _menuRoot.style.display = DisplayStyle.Flex;
            LadderProfile p = _store.Load();
            _menuRank.text = $"段位：{p.RankName}";
            _menuStats.text = $"最佳连胜 {p.BestStreak} / {LadderRules.MaxLayers} · 生涯总分 {p.TotalScore} · "
                            + $"挑战 {p.Attempts} 次\n最近一次：连胜 {p.LastStreak} · 得分 {p.LastScore}";
            bool hasRun = p.HasActiveRun;
            _menuContinueHint.text = hasRun
                ? $"进行中的挑战：第 {p.ActiveLayer} 层（继续后从该层开打）"
                : "没有进行中的挑战——新挑战将从第 1 层开始";
        }

        private void ShowOverlay(string title, string body, string primaryText, Action primary,
            string secondaryText = null, Action secondary = null)
        {
            _menuRoot.style.display = DisplayStyle.None;
            _hudRoot.style.display = DisplayStyle.None;
            _overlayTitle.text = title;
            _overlayBody.text = body;
            _overlayPrimary.text = primaryText;
            _overlayPrimary.clicked -= PrimaryProxy;
            _overlayPrimary.clicked += PrimaryProxy;
            _primaryAction = primary;
            _overlaySecondary.style.display = secondary == null ? DisplayStyle.None : DisplayStyle.Flex;
            _overlaySecondary.text = secondaryText ?? "";
            _overlaySecondary.clicked -= SecondaryProxy;
            _overlaySecondary.clicked += SecondaryProxy;
            _secondaryAction = secondary;
            _overlayRoot.style.display = DisplayStyle.Flex;
        }

        private void HideOverlay() => _overlayRoot.style.display = DisplayStyle.None;

        private Action _primaryAction;
        private Action _secondaryAction;
        private void PrimaryProxy() => _primaryAction?.Invoke();
        private void SecondaryProxy() => _secondaryAction?.Invoke();
    }
}
