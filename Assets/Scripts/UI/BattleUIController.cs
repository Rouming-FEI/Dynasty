using System;
using System.Collections.Generic;
using System.Text;
using Bootstrap;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
using Gameplay.Movement;
using UI.Presenter;
using UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI
{
    /// <summary>
    /// UI Toolkit 宿主（唯一新 MonoBehaviour）。每帧从 Presenter 拉快照渲染五区，
    /// 把按钮点击转回 Presenter 输入意图。不含游戏逻辑；游戏循环由 GameBootstrap 驱动。
    /// 挂载要求：UIDocument（Source Asset = BattleUI.uxml）+ Inspector 引用 GameBootstrap。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BattleUIController : MonoBehaviour
    {
        [SerializeField] private GameBootstrap _bootstrap;

        private BattlePresenter _presenter;          // 选角完成后赋值（OnCharacterSelected）；选角前为 null
        private VisualElement _root;
        private Label _turnLabel, _phaseLabel, _aliveLabel, _judgmentBar;
        private VisualElement _mapNodes, _infoRows, _actionBar;
        private Label _actionHint, _logLabel;
        private VisualElement _charSelectOverlay, _chipOverlay, _resultOverlay, _logOverlay;
        private Label _chipSubtitle, _chipHint, _resultLabel, _logFullLabel;
        private readonly Dictionary<string, Label> _chipCountLabels = new Dictionary<string, Label>();

        // 筹码面板计数（石头/剪刀/布）。0-0-0 起步：玩家需有意识分配满 3 枚才能提交，
        // 提交后同样归零复位，下次判定阶段面板重新打开时是干净状态。
        private int _chipRock, _chipScissors, _chipPaper;
        private bool _logOverlayOpen;
        private bool _bootstrapWarned;               // _bootstrap 未接线只告警一次

        // 行动按钮脏检查签名（玩家|AP|地点|回合|阶段 变化才重建按钮；地点入签名
        // 是为覆盖 0AP 免费行动——免费旅行改位置但 AP/回合/阶段 均不变；数据幽灵
        // actor 另追加 |form{形态}|clone{分身} 维度：Handsonic 切形态(125)/Harmonics
        // 造分身都会让 AP/回合/阶段/地点全不变但攻击按钮语义/范围全变，必须重建）
        private string _actionSignature;

        // 日志尾巴脏检查（条数 + 末条字符串引用都未变则跳过重建）
        private int _lastLogTailCount = -1;
        private string _lastLogTailLastRef;

        private const int ChipsTotal = 3;

        // P7-A 带参技能子选单（混乱模拟道具 / 双子转换武器）
        private VisualElement _submenuOverlay;
        private Label _submenuTitle;
        private VisualElement _submenuList;

        private void Start()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("[BattleUI] UIDocument.rootVisualElement 为空——请在 Inspector 中为 UIDocument 拖入 BattleUI.uxml");
                enabled = false;
                return;
            }
            _turnLabel = _root.Q<Label>("turn-label");
            _phaseLabel = _root.Q<Label>("phase-label");
            _aliveLabel = _root.Q<Label>("alive-label");
            _judgmentBar = _root.Q<Label>("judgment-bar");
            _mapNodes = _root.Q<VisualElement>("map-nodes");
            _infoRows = _root.Q<VisualElement>("info-rows");
            _actionBar = _root.Q<VisualElement>("action-bar");
            _actionHint = _root.Q<Label>("action-hint");
            _logLabel = _root.Q<Label>("log-label");
            _root.Q<Button>("log-button").clicked += ToggleLogOverlay;

            _charSelectOverlay = BuildCharSelectOverlay();
            _chipOverlay = BuildChipOverlay();
            _resultOverlay = BuildResultOverlay();
            _logOverlay = BuildLogOverlay();
            _submenuOverlay = BuildSubmenuOverlay();
            _root.Add(_charSelectOverlay);
            _root.Add(_chipOverlay);
            _root.Add(_resultOverlay);
            _root.Add(_logOverlay);
            _root.Add(_submenuOverlay);   // 最后挂载 = 渲染于最上层
        }

        private void Update()
        {
            if (_bootstrap == null)
            {
                if (_bootstrapWarned) return;
                _bootstrapWarned = true;
                Debug.LogError("[BattleUI] 未指定 GameBootstrap 引用——请在 Inspector 拖入 GameBootstrap 对象");
                enabled = false;
                return;
            }

            // 选角前（_presenter == null）只刷选角层
            if (_presenter == null)
            {
                bool selecting = !_bootstrap.IsGameStarted();
                _charSelectOverlay.style.display =
                    selecting ? DisplayStyle.Flex : DisplayStyle.None;
                // 防御：presenter 为空时除选角层外不应有任何游戏 UI 可见
                // （正常流程 RequestRestart 已收起，此处兜底，幂等）
                _resultOverlay.style.display = DisplayStyle.None;
                _chipOverlay.style.display = DisplayStyle.None;
                return;
            }

            BattleSnapshot s = _presenter.BuildSnapshot();
            RenderTopBar(s);
            RenderInfoRows(s);
            RenderMap(s);
            RenderActionBar(s);
            RenderLogTail(s);
            RenderOverlays(s);
        }

        // ---------------- 五区渲染 ----------------

        private void RenderTopBar(BattleSnapshot s)
        {
            SetLabelIfChanged(_turnLabel, $"回合 {s.CurrentTurn}");
            SetLabelIfChanged(_phaseLabel, $"阶段：{s.PhaseText}");
            SetLabelIfChanged(_aliveLabel, $"存活 {s.AliveCount}/{s.PlayerCount}");

            bool showJudgment = s.JudgmentSummary != null;
            _judgmentBar.style.display = showJudgment ? DisplayStyle.Flex : DisplayStyle.None;
            if (showJudgment)
                _judgmentBar.text = s.JudgmentSummary;
        }

        /// <summary>文本有变化才赋值，避免每帧触发布局脏标记。</summary>
        private static void SetLabelIfChanged(Label label, string text)
        {
            if (label.text != text) label.text = text;
        }

        private void RenderInfoRows(BattleSnapshot s)
        {
            _infoRows.Clear();
            for (int i = 0; i < s.CharacterRows.Count; i++)
            {
                BattleSnapshot.CharacterRow r = s.CharacterRows[i];
                var row = new VisualElement();
                row.AddToClassList("info-row");
                if (r.IsDead) row.AddToClassList("info-row--dead");
                if (r.IsHidden) row.AddToClassList("info-row--hidden");
                if (r.IsCurrentActor && !r.IsDead) row.AddToClassList("info-row--current-actor");

                string weapon = r.WeaponName != null
                    ? $" [{r.WeaponName}{(r.WeaponAmmo > 0 ? r.WeaponAmmo.ToString() : "")}]"
                    : "";
                string tags = r.Tags.Count > 0 ? $" [{string.Join(" ", r.Tags)}]" : "";
                string status = r.IsDead
                    ? "阵亡"
                    : $"{r.Sanity:F1}/{r.MaxSanity:F0}HP {r.ActionPoints}AP";
                var label = new Label($"{r.Name} {status}{weapon}{tags} @{r.LocationName}");
                label.AddToClassList("info-text");
                row.Add(label);
                _infoRows.Add(row);
            }
        }

        private void RenderMap(BattleSnapshot s)
        {
            _mapNodes.Clear();
            for (int i = 0; i < s.MapNodes.Count; i++)
            {
                BattleSnapshot.MapNodeInfo node = s.MapNodes[i];
                var el = new VisualElement();
                el.AddToClassList("map-node");
                if (node.IsCurrentActorHere) el.AddToClassList("map-node--actor-here");

                var title = new Label(node.Name);
                title.AddToClassList("map-node-title");
                el.Add(title);

                for (int p = 0; p < node.PlayerNames.Count; p++)
                {
                    var line = new Label($"· {node.PlayerNames[p]}");
                    line.AddToClassList("map-node-line");
                    el.Add(line);
                }
                for (int b = 0; b < node.ItemBadges.Count; b++)
                {
                    var badge = new Label(node.ItemBadges[b]);
                    badge.AddToClassList("map-badge");
                    el.Add(badge);
                }
                if (node.HasTrap)
                {
                    var trap = new Label("◈ 陷阱");
                    trap.AddToClassList("map-badge");
                    trap.AddToClassList("map-badge--trap");
                    el.Add(trap);
                }
                if (node.HasTurretOccupant)
                {
                    var turret = new Label("▲ 炮台");
                    turret.AddToClassList("map-badge");
                    el.Add(turret);
                }
                _mapNodes.Add(el);
            }
        }

        private void RenderLogTail(BattleSnapshot s)
        {
            // 脏检查：条数与末条引用都未变则跳过（日志队列容量 200，引用稳定）
            string lastRef = s.LogTail.Count > 0 ? s.LogTail[s.LogTail.Count - 1] : null;
            if (s.LogTail.Count == _lastLogTailCount && ReferenceEquals(lastRef, _lastLogTailLastRef))
                return;
            _lastLogTailCount = s.LogTail.Count;
            _lastLogTailLastRef = lastRef;

            var sb = new StringBuilder();
            int start = Math.Max(0, s.LogTail.Count - 4);
            for (int i = start; i < s.LogTail.Count; i++)
                sb.AppendLine(s.LogTail[i]);
            _logLabel.text = sb.ToString();
        }

        // ---------------- 行动区（脏检查 + 按钮构建） ----------------

        private void RenderActionBar(BattleSnapshot s)
        {
            WorldState state = _bootstrap.GetState();
            string signature = BuildActionSignature(s, state);
            if (signature == _actionSignature) return;
            _actionSignature = signature;
            while (_actionBar.childCount > 1)
                _actionBar.RemoveAt(_actionBar.childCount - 1);

            if (s.GameOver)
            {
                _actionHint.text = "对局已结束";
                CloseSubmenu(); // 防御：对局结束收掉可能还开着的带参子选单（幂等）
                return;
            }

            BattleSnapshot.CharacterRow actorRow = null;
            for (int i = 0; i < s.CharacterRows.Count; i++)
            {
                BattleSnapshot.CharacterRow r = s.CharacterRows[i];
                if (r.IsCurrentActor && !r.IsDead) { actorRow = r; break; }
            }
            if (actorRow == null)
            {
                _actionHint.text = s.PhaseText == "判定阶段" ? "等待筹码提交…" : "等待…";
                return;
            }
            if (actorRow.PlayerId != _presenter.HumanPlayerId)
            {
                _actionHint.text = $"{actorRow.Name}（AI）行动中…";
                return;
            }

            _actionHint.text = $"{actorRow.Name} 的回合（{actorRow.ActionPoints} AP）";

            Character c = state.GetCharacterByPlayer(actorRow.PlayerId);
            if (c == null) return;

            // 数据幽灵分身活跃时：行动提示行追加分身理智小字（分身无独立棋盘实体）
            if (state.GhostCloneActive && c.Definition?.Id == SkillSystem.DataGhostCharacterId)
                _actionHint.text += $"　分身 {state.GhostCloneSanity:F1} 理智";

            AddMoveButtons(state, c);
            AddAttackButtons(state, c);
            AddSkillButtons(state, c);
            AddItemButtons(state, c);

            int tick = state.CurrentTick;
            int playerId = c.PlayerId;
            AddButton("浪费 1 AP",
                () => _presenter.SubmitCommand(new WasteAPCommand(tick, playerId)));
        }

        private static string BuildActionSignature(BattleSnapshot s, WorldState state)
        {
            for (int i = 0; i < s.CharacterRows.Count; i++)
            {
                BattleSnapshot.CharacterRow r = s.CharacterRows[i];
                if (r.IsCurrentActor && !r.IsDead)
                {
                    string sig = $"{r.PlayerId}|{r.ActionPoints}|{r.LocationName}|{s.CurrentTurn}|{s.PhaseText}";
                    // 数据幽灵：形态/分身不体现在快照行字段，须从 state 实况补维度——
                    // 切形态(125, 0 AP)后若签名不变，旧形态按钮残留会误导（V1 按钮实际按
                    // V2 结算 / V5 目标列表不更新 / stale 自愈按钮变自伤入口）
                    Character actor = state.GetCharacterByPlayer(r.PlayerId);
                    if (actor?.Definition?.Id == SkillSystem.DataGhostCharacterId)
                        sig += $"|form{DataGhostFormAttack.CurrentForm(actor)}"
                             + $"|clone{(state.GhostCloneActive ? 1 : 0)}";
                    return sig;
                }
            }
            return "none";
        }

        private void AddButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("action-button");
            _actionBar.Add(button);
        }

        private void AddMoveButtons(WorldState state, Character c)
        {
            if (!c.CanMove) return;
            int[] connections = state.Locations.GetConnectedLocations(c.CurrentLocationId);
            if (connections == null) return;

            for (int i = 0; i < connections.Length; i++)
            {
                int locId = connections[i];
                string locName = state.Locations.GetName(locId);
                int tick = state.CurrentTick;
                int playerId = c.PlayerId;
                AddButton($"移动 → {locName} (1 AP)",
                    () => _presenter.SubmitCommand(new MoveCommand(tick, playerId, locId)));
            }
        }

        private void AddAttackButtons(WorldState state, Character c)
        {
            if (!c.CanAct) return;

            // 数据幽灵：普攻 = 形态攻击（数值/拦截语义以 DataGhostFormAttack 为准）。
            // V4 花无攻击，普攻位替换为自愈按钮；V5 无视地点（藏匿不可选）；V1/V2/V3 同地点近战。
            if (c.Definition?.Id == SkillSystem.DataGhostCharacterId)
            {
                int form = DataGhostFormAttack.CurrentForm(c);
                int tick = state.CurrentTick;
                int playerId = c.PlayerId;
                if (form == 4)
                {
                    // V4 花：自愈 0.5（DamageCommand 自目标，命令层按 16 号分派到形态自愈）
                    if (c.Sanity < c.MaxSanity)
                        AddButton("自愈 · V4 花 (1 AP)",
                            () => _presenter.SubmitCommand(new DamageCommand(tick, playerId, playerId, 1f)));
                    return; // 花形态无攻击按钮
                }
                string formName = FormDisplayName(form);
                for (int i = 0; i < state.PlayerCount; i++)
                {
                    Character other = state.Characters[i];
                    if (other == null || other.IsDead || other.PlayerId == c.PlayerId) continue;
                    if (form == 5)
                    {
                        // V5 远程：任意地点；藏匿不可选（规则源 = WeaponSystem.CanSelectRangedTarget）
                        if (!WeaponSystem.CanSelectRangedTarget(c, other)) continue;
                    }
                    else if (other.CurrentLocationId != c.CurrentLocationId)
                    {
                        continue; // V1/V2/V3 近战需同地点
                    }
                    int targetId = other.PlayerId;
                    string targetName = other.Name;
                    AddButton($"攻击 {targetName} [{formName}] (1 AP)",
                        () => _presenter.SubmitCommand(new DamageCommand(tick, playerId, targetId, 1f)));
                }
                return;
            }

            // 原逻辑：同地点敌人 + 武器名
            string weapon = c.Inventory.EquippedWeapon?.Name ?? "空手";
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character other = state.Characters[i];
                if (other == null || other.IsDead || other.PlayerId == c.PlayerId) continue;
                if (other.CurrentLocationId != c.CurrentLocationId) continue;

                int targetId = other.PlayerId;
                string targetName = other.Name;
                int tick = state.CurrentTick;
                int playerId = c.PlayerId;
                AddButton($"攻击 {targetName} [{weapon}] (1 AP)",
                    () => _presenter.SubmitCommand(new DamageCommand(tick, playerId, targetId, 1f)));
            }
        }

        /// <summary>数据幽灵形态显示名（UI 展示用；V4 花无普攻按钮不在此列）。</summary>
        private static string FormDisplayName(int form)
        {
            switch (form)
            {
                case 2: return "V2 长刃";
                case 3: return "V3 三尖戟";
                case 5: return "V5 恶魔猎弩";
                default: return "V1 音刃";
            }
        }

        private void AddSkillButtons(WorldState state, Character c)
        {
            SkillDef[] skills = c.Definition?.Skills;
            if (skills == null) return;

            for (int i = 0; i < skills.Length; i++)
            {
                SkillDef skill = skills[i];

                // 混沌法师「混乱」(120)：打开模拟道具子选单（不走通用目标循环）
                if (skill.Id == SkillSystem.ChaosSkillId
                    && c.Definition?.Id == SkillSystem.ChaosMageCharacterId)
                {
                    if (!SkillSystem.CanActivate(skill, c, i)) continue;
                    int skillTick = state.CurrentTick;
                    int skillPlayer = c.PlayerId;
                    // 费用文案走 SkillCostText（Def 120 Cost=AP1）；扣费由 ChaosSimulateCommand 执行
                    AddButton($"技能 混乱 → 选择模拟道具 ({SkillCostText(skill)})",
                        () => OpenChaosMenu(state, c, skillTick, skillPlayer));
                    continue;
                }

                if (!SkillSystem.CanActivate(skill, c, i)) continue;

                string cost = SkillCostText(skill);
                int skillIndex = i;
                int tick = state.CurrentTick;
                int playerId = c.PlayerId;

                if (skill.Target == SkillTargetType.Self)
                {
                    // 自 Buff 技能：只能对自己
                    AddButton($"技能 {skill.Name} → {c.Name} ({cost})",
                        () => _presenter.SubmitCommand(
                            new UseSkillCommand(tick, playerId, skillIndex, playerId)));
                }
                else if (skill.Target == SkillTargetType.Any)
                {
                    // 守护 (Id=2)：充能未满 → 纯充能（无目标选择）；满充能 → 选择目标给盾
                    int threshold = skill.ChargeThreshold > 0 ? skill.ChargeThreshold : 3;
                    if (skill.Id == SkillSystem.GuardianSkillId
                        && c.ASC.GetGuardianCharge() < threshold)
                    {
                        AddButton($"技能 {skill.Name} (1 AP) [充能 {c.ASC.GetGuardianCharge()}/{threshold}]",
                            () => _presenter.SubmitCommand(
                                new UseSkillCommand(tick, playerId, skillIndex, playerId)));
                    }
                    else
                    {
                        // 任意目标技能：自己 + 所有存活角色
                        AddButton($"技能 {skill.Name} → 自己 [{c.Name}] ({cost})",
                            () => _presenter.SubmitCommand(
                                new UseSkillCommand(tick, playerId, skillIndex, playerId)));
                        for (int j = 0; j < state.PlayerCount; j++)
                        {
                            Character other = state.Characters[j];
                            if (other == null || other.IsDead || other.PlayerId == c.PlayerId) continue;
                            int targetId = other.PlayerId;
                            string targetName = other.Name;
                            AddButton($"技能 {skill.Name} → {targetName} ({cost})",
                                () => _presenter.SubmitCommand(
                                    new UseSkillCommand(tick, playerId, skillIndex, targetId)));
                        }
                    }
                }
                else
                {
                    // 敌方技能：找同地点敌人（技能不受地点限制，同地点优先）
                    int targetId = FindSkillTarget(state, c);
                    if (targetId < 0) continue;
                    string targetName = state.GetCharacterByPlayer(targetId)?.Name;
                    AddButton($"技能 {skill.Name} → {targetName} ({cost})",
                        () => _presenter.SubmitCommand(
                            new UseSkillCommand(tick, playerId, skillIndex, targetId)));
                }
            }

            // 双子灵巧态：转换武器入口（非技能槽，是灵巧状态的衍生行动；TwinConvertCommand）
            if (c.Definition?.Id == SkillSystem.TwinCharacterId
                && c.CanAct
                && c.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile)
                && c.Inventory.EquippedWeapon != null
                && c.ActionPoints >= 1)
            {
                int tick = state.CurrentTick;
                int pid = c.PlayerId;
                // 转换非技能槽（无 SkillDef 可查），费用文案与 TwinConvertCommand.Execute
                // 扣 1 AP 同源——改动扣费须同步此文案
                AddButton("转换武器 (1 AP)", () => OpenConvertMenu(c, tick, pid));
            }
        }

        /// <summary>技能目标：同地点敌人优先，否则任一存活敌人（技能不受地点限制）。</summary>
        private static int FindSkillTarget(WorldState state, Character c)
        {
            int anyEnemy = -1;
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character other = state.Characters[i];
                if (other == null || other.IsDead || other.PlayerId == c.PlayerId) continue;
                if (other.CurrentLocationId == c.CurrentLocationId) return other.PlayerId;
                if (anyEnemy < 0) anyEnemy = other.PlayerId;
            }
            return anyEnemy;
        }

        private void AddItemButtons(WorldState state, Character c)
        {
            if (!c.CanAct) return;

            for (int i = 0; i < c.Inventory.Consumables.Count; i++)
            {
                ItemDef item = c.Inventory.Consumables[i];
                if (item == null || c.ActionPoints < item.UseAPCost) continue;

                int targetId = FindItemTarget(state, c, item.TargetType);
                if (targetId < 0) continue;

                string targetName = state.GetCharacterByPlayer(targetId)?.Name;
                int tick = state.CurrentTick;
                int playerId = c.PlayerId;
                int itemIndex = i;
                AddButton($"道具 {item.Name} → {targetName} ({item.UseAPCost} AP)",
                    () => _presenter.SubmitCommand(
                        new UseItemCommand(tick, playerId, itemIndex, targetId)));
            }
        }

        /// <summary>道具目标：Self 对自己；SameLocation 需同地点敌人；AnyPlayer 任一存活敌人。</summary>
        private static int FindItemTarget(WorldState state, Character c, ItemTargetType type)
        {
            if (type == ItemTargetType.Self) return c.PlayerId;

            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character other = state.Characters[i];
                if (other == null || other.IsDead || other.PlayerId == c.PlayerId) continue;
                if (type == ItemTargetType.SameLocation
                    && other.CurrentLocationId != c.CurrentLocationId) continue;
                return other.PlayerId;
            }
            return -1;
        }

        private static string SkillCostText(SkillDef skill)
        {
            return skill.Cost.Type == SkillCostType.AP
                ? $"{skill.Cost.Amount:F0} AP"
                : $"{skill.Cost.Amount:F0} {skill.Cost.Type}";
        }

        // ---------------- 覆盖层 ----------------

        private VisualElement BuildCharSelectOverlay()
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("overlay");
            overlay.style.display = DisplayStyle.None;

            var box = new VisualElement();
            box.AddToClassList("overlay-box");
            var title = new Label("选择你的角色");
            title.AddToClassList("result-text");
            box.Add(title);

            var grid = new VisualElement();
            grid.AddToClassList("char-grid");
            foreach (int id in CharacterCatalog.GetAllIds())
            {
                int characterId = id;
                CharacterDef def = CharacterCatalog.Create(id);
                string hint = def.Skills != null && def.Skills.Length > 0
                    ? def.Skills[0].Name
                    : "无技能";
                var button = new Button(() => OnCharacterSelected(characterId))
                {
                    text = $"{def.Name}\n({hint})"
                };
                button.AddToClassList("char-button");
                grid.Add(button);
            }
            box.Add(grid);
            overlay.Add(box);
            return overlay;
        }

        private VisualElement BuildChipOverlay()
        {
            ResetChipCounts();

            var overlay = new VisualElement();
            overlay.AddToClassList("overlay");
            overlay.style.display = DisplayStyle.None;

            var box = new VisualElement();
            box.AddToClassList("overlay-box");
            var title = new Label($"分配你的筹码（共 {ChipsTotal} 枚）");
            title.AddToClassList("result-text");
            box.Add(title);

            // 语境副标题：默认「其余玩家已就绪」；平局作废重判时由 RenderOverlays 切换文案
            _chipSubtitle = new Label("其余玩家已就绪，等待你提交…");
            box.Add(_chipSubtitle);

            BuildChipRow(box, "石头", () => _chipRock, v => _chipRock = v);
            BuildChipRow(box, "剪刀", () => _chipScissors, v => _chipScissors = v);
            BuildChipRow(box, "布", () => _chipPaper, v => _chipPaper = v);

            _chipHint = new Label($"剩余 {ChipsTotal}");
            box.Add(_chipHint);

            var submit = new Button(SubmitChips) { text = "提交" };
            submit.AddToClassList("big-button");
            box.Add(submit);

            overlay.Add(box);
            return overlay;
        }

        private void BuildChipRow(VisualElement parent, string name, Func<int> get, Action<int> set)
        {
            var row = new VisualElement();
            row.AddToClassList("chip-row");

            var label = new Label(name);
            label.AddToClassList("chip-name");
            row.Add(label);

            var minus = new Button(() => ChangeChip(name, get, set, -1)) { text = "−" };
            row.Add(minus);

            var count = new Label(get().ToString());
            count.AddToClassList("chip-count");
            _chipCountLabels[name] = count;
            row.Add(count);

            var plus = new Button(() => ChangeChip(name, get, set, 1)) { text = "+" };
            row.Add(plus);

            parent.Add(row);
        }

        private void ChangeChip(string name, Func<int> get, Action<int> set, int delta)
        {
            int newVal = get() + delta;
            if (newVal < 0) return;
            if (delta > 0 && _chipRock + _chipScissors + _chipPaper >= ChipsTotal) return;
            set(newVal);
            _chipCountLabels[name].text = newVal.ToString();
            _chipHint.text = $"剩余 {ChipsTotal - (_chipRock + _chipScissors + _chipPaper)}";
        }

        private void SubmitChips()
        {
            if (_presenter == null) return;
            if (_chipRock + _chipScissors + _chipPaper != ChipsTotal) return;
            _presenter.SubmitChipAllocation(_chipRock, _chipScissors, _chipPaper);
            ResetChipCounts();
        }

        /// <summary>筹码面板复位为 0-0-0 并刷新计数与剩余提示（提交后/构建时调用）。</summary>
        private void ResetChipCounts()
        {
            _chipRock = _chipScissors = _chipPaper = 0;
            if (_chipCountLabels.TryGetValue("石头", out var rock)) rock.text = "0";
            if (_chipCountLabels.TryGetValue("剪刀", out var scissors)) scissors.text = "0";
            if (_chipCountLabels.TryGetValue("布", out var paper)) paper.text = "0";
            if (_chipHint != null) _chipHint.text = $"剩余 {ChipsTotal}";
        }

        private VisualElement BuildResultOverlay()
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("overlay");
            overlay.style.display = DisplayStyle.None;

            var box = new VisualElement();
            box.AddToClassList("overlay-box");
            _resultLabel = new Label("");
            _resultLabel.AddToClassList("result-text");
            box.Add(_resultLabel);

            var restart = new Button(RequestRestart) { text = "再来一局" };
            restart.AddToClassList("big-button");
            box.Add(restart);

            overlay.Add(box);
            return overlay;
        }

        private VisualElement BuildLogOverlay()
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("overlay");
            overlay.style.display = DisplayStyle.None;

            var box = new VisualElement();
            box.AddToClassList("overlay-box");
            _logFullLabel = new Label("");
            _logFullLabel.AddToClassList("info-text");

            // 长日志走 ScrollView 滚动；关闭按钮留在滚动区外
            var scroll = new ScrollView();
            scroll.Add(_logFullLabel);
            box.Add(scroll);

            var close = new Button(CloseLogOverlay) { text = "关闭" };
            close.AddToClassList("big-button");
            box.Add(close);

            overlay.Add(box);
            return overlay;
        }

        // ---------------- P7-A 带参行动子选单（混乱模拟道具 / 转换武器） ----------------

        /// <summary>带参行动子选单：关闭按钮 + 标题 + 选项按钮列表。</summary>
        private VisualElement BuildSubmenuOverlay()
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("overlay");
            overlay.style.display = DisplayStyle.None;

            var box = new VisualElement();
            box.AddToClassList("overlay-box");
            _submenuTitle = new Label("");
            _submenuTitle.AddToClassList("result-text");
            box.Add(_submenuTitle);

            _submenuList = new VisualElement();
            box.Add(_submenuList);

            var close = new Button(CloseSubmenu) { text = "关闭" };
            close.AddToClassList("big-button");
            box.Add(close);

            overlay.Add(box);
            return overlay;
        }

        /// <summary>打开子选单：标题 + (文本, 点击回调) 列表。</summary>
        private void OpenSubmenu(string title, List<(string label, Action onClick)> items)
        {
            _submenuTitle.text = title;
            _submenuList.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                Action onClick = items[i].onClick;
                var b = new Button(onClick) { text = items[i].label };
                b.AddToClassList("action-button");
                _submenuList.Add(b);
            }
            _submenuOverlay.style.display = DisplayStyle.Flex;
        }

        private void CloseSubmenu()
        {
            _submenuOverlay.style.display = DisplayStyle.None;
        }

        /// <summary>混沌「混乱」第一步：选模拟道具。候选池唯一取自
        /// ChaosSimulateCommand.SimPool（与命令层成员判定同源，UI 不另设池表/硬编码上限）。</summary>
        private void OpenChaosMenu(WorldState state, Character c, int tick, int playerId)
        {
            var items = new List<(string label, Action onClick)>();
            for (int i = 0; i < ChaosSimulateCommand.SimPool.Length; i++)
            {
                int itemId = ChaosSimulateCommand.SimPool[i]; // 每轮迭代新实例 → 闭包正确捕获
                ItemDef item = ItemRegistry.Get(itemId);
                if (item == null) continue;
                items.Add((item.Name, () => OpenChaosTarget(state, tick, playerId, itemId)));
            }
            OpenSubmenu($"混乱：模拟道具（{c.Name}）", items);
        }

        /// <summary>混沌第二步：自身类道具直接对自己施放；武器类列出全部存活敌人（豁免地理/持有限制）。</summary>
        private void OpenChaosTarget(WorldState state, int tick, int playerId, int itemId)
        {
            // 自身类道具（血包/荆棘素/炸弹/净化）：无需选目标，直接对自己施放
            if (ChaosSimulateCommand.IsSelfItem(itemId))
            {
                _presenter.SubmitCommand(new ChaosSimulateCommand(tick, playerId, itemId, playerId));
                CloseSubmenu();
                return;
            }
            // 敌人道具：列出全部存活敌人（混乱豁免地理/持有限制）
            var targets = new List<(string label, Action onClick)>();
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character other = state.Characters[i];
                if (other == null || other.IsDead || other.PlayerId == playerId) continue;
                int targetId = other.PlayerId;
                targets.Add(($"{other.Name}（{state.Locations.GetName(other.CurrentLocationId)}）",
                    () =>
                    {
                        _presenter.SubmitCommand(new ChaosSimulateCommand(tick, playerId, itemId, targetId));
                        CloseSubmenu();
                    }));
            }
            if (targets.Count == 0)
            {
                // 无可用目标：清空选项并改标题（保持覆盖层关闭按钮可退回）
                _submenuTitle.text = "混乱：无可用目标（无存活敌人）";
                _submenuList.Clear();
                return;
            }
            string itemName = ItemRegistry.Get(itemId)?.Name ?? itemId.ToString();
            OpenSubmenu($"混乱：选择 {itemName} 的目标", targets);
        }

        /// <summary>双子灵巧「转换武器」：刀/枪/施工锤（与 TwinConvertCommand 白名单同步，不含幻剑/圣盾）。</summary>
        private void OpenConvertMenu(Character c, int tick, int playerId)
        {
            var items = new List<(string label, Action onClick)>();
            int[] pool = { 1, 3, 4 }; // 刀/枪/施工锤（TwinConvertCommand.Execute 仅放行 1/3/4）
            ItemDef current = c.Inventory.EquippedWeapon;
            for (int i = 0; i < pool.Length; i++)
            {
                int weaponId = pool[i];
                ItemDef weapon = ItemRegistry.Get(weaponId);
                if (weapon == null) continue;
                if (current != null && current.Id == weaponId) continue; // 同武器转换无意义
                items.Add((weapon.Name, () =>
                {
                    _presenter.SubmitCommand(new TwinConvertCommand(tick, playerId, weaponId));
                    CloseSubmenu();
                }));
            }
            // 子选单标题费用硬编码：非技能槽，与 TwinConvertCommand.Execute 扣 1 AP 同源（改动须同步）
            OpenSubmenu("灵巧：转换武器（1 AP）", items);
        }

        private void RenderOverlays(BattleSnapshot s)
        {
            // 选角层：仅游戏未开始时显示（presenter==null 的早期分支在 Update 内处理）
            _charSelectOverlay.style.display =
                !_bootstrap.IsGameStarted() ? DisplayStyle.Flex : DisplayStyle.None;

            // 筹码层：判定阶段且本人尚未提交（已提交/死亡/结束都收起）
            bool showChip = !s.GameOver
                         && s.PhaseText == "判定阶段"
                         && !_presenter.HasPendingJudgment
                         && !IsHumanDead(s);
            _chipOverlay.style.display = showChip ? DisplayStyle.Flex : DisplayStyle.None;
            if (showChip)
            {
                _chipHint.text = $"剩余 {ChipsTotal - (_chipRock + _chipScissors + _chipPaper)}";
                // 面板可见时必处判定阶段：最近一次判定为全局平（作废保留的那次）→ 重判提示。
                // 局限（P5 待办）：LastJudgment 跨回合残留，连续平局保底进入行动后的
                // 下一回合判定期会误报；精确检测需在快照中记录判定所属回合。
                _chipSubtitle.text = s.LastJudgmentGlobalFlat
                    ? "全局平局，重新分配"
                    : "其余玩家已就绪，等待你提交…";
            }

            _resultOverlay.style.display = s.GameOver ? DisplayStyle.Flex : DisplayStyle.None;
            if (s.GameOver)
                _resultLabel.text = s.WinnerText;

            if (_logOverlayOpen)
                RefreshFullLog();
        }

        private bool IsHumanDead(BattleSnapshot s)
        {
            for (int i = 0; i < s.CharacterRows.Count; i++)
                if (s.CharacterRows[i].PlayerId == _presenter.HumanPlayerId)
                    return s.CharacterRows[i].IsDead;
            return false;
        }

        private void ToggleLogOverlay()
        {
            _logOverlayOpen = !_logOverlayOpen;
            _logOverlay.style.display = _logOverlayOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (_logOverlayOpen)
                RefreshFullLog();
        }

        private void CloseLogOverlay()
        {
            _logOverlayOpen = false;
            _logOverlay.style.display = DisplayStyle.None;
        }

        private void RefreshFullLog()
        {
            _logFullLabel.text = string.Join("\n",
                _presenter?.GetFullLog() ?? Array.Empty<string>());
        }

        // ---------------- 热座接线（Bootstrap 提供开局/重开，UI 不碰 Simulation 内部） ----------------

        private void OnCharacterSelected(int characterId)
        {
            if (_presenter != null) return;   // 双击/重复回调防重入

            // P0=真人选定角色；AI 从剩余角色不重复抽取（确定性随机）
            WorldState state = _bootstrap.GetState();
            var remaining = new List<int>();
            foreach (int id in CharacterCatalog.GetAllIds())
                if (id != characterId) remaining.Add(id);

            var defs = new CharacterDef[_bootstrap.PlayerCount];
            defs[0] = CharacterCatalog.Create(characterId);
            for (int i = 1; i < defs.Length; i++)
            {
                int pick = state.Random.Next(remaining.Count);
                defs[i] = CharacterCatalog.Create(remaining[pick]);
                remaining.RemoveAt(pick);
            }

            BeginBattleWithDefs(defs);
        }

        /// <summary>以给定角色直接开局（热座选角回调与天梯挑战层局共用入口）。
        /// 前置：bootstrap 已处于待开局态（_gameStarted == false——挑战层局由
        /// RestartHotseat(seed, defs) 先复位）。内部重建 presenter 并 Bind 当前锁步。</summary>
        public void BeginBattleWithDefs(CharacterDef[] defs)
        {
            if (_presenter != null) return;   // 防重入（选角双击同语义）
            _bootstrap.BeginHotseat(defs);
            _presenter = new BattlePresenter(_bootstrap.GetState());
            _presenter.Bind(_bootstrap.GetLockstep(), 0);
            _actionSignature = null;
            _charSelectOverlay.style.display = DisplayStyle.None;
        }

        /// <summary>回到选角态（挑战菜单/终局回菜单用）：复位 bootstrap 与 presenter。</summary>
        public void ResetToCharacterSelect()
        {
            RequestRestart();   // 既有私有方法语义完备（RestartHotseat + presenter 清空 + 各层收起）
        }

        private void RequestRestart()
        {
            _bootstrap.RestartHotseat();
            _presenter = null;
            _actionSignature = null;

            // 重开回到选角：立即收起结果/筹码/日志层并复位筹码计数——
            // 选角层由下一帧 Update 显示，期间不应残留可交互的游戏 UI
            _resultOverlay.style.display = DisplayStyle.None;
            _chipOverlay.style.display = DisplayStyle.None;
            CloseLogOverlay();
            CloseSubmenu();
            ResetChipCounts();
        }
    }
}
