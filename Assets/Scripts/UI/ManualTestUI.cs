using Bootstrap;
using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
using Gameplay.Movement;
using Network.Local;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// 手动测试面板 — 基于 IMGUI (OnGUI) 的调试 UI，无需搭建 Canvas。
    /// 注册为 LocalLockstep.ManualInputProvider 后接管所有角色的行动输入（热座模式）。
    /// 工作方式：行动阶段 Lockstep 轮询 GetManualInput —— 未点击时返回 null（回合
    /// 暂停、不扣 AP），点击按钮后缓存 Command，下一次轮询被取走并结算。
    /// 仅用于测试；UI 层不受 Hot Path 零 GC 约束。
    /// </summary>
    public class ManualTestUI : MonoBehaviour
    {
        [SerializeField] private GameBootstrap _bootstrap;

        private LocalLockstep _lockstep;
        private WorldState _state;
        private Character _currentChar;
        private ICommand _pendingCommand;
        private bool _waitingForInput;

        private void Start()
        {
            TryRegister();
        }

        private void Update()
        {
            // GameBootstrap 与本组件的 Start 顺序不确定，注册失败则每帧重试
            if (_lockstep == null) TryRegister();
        }

        private void TryRegister()
        {
            if (_bootstrap == null) return;
            _lockstep = _bootstrap.GetLockstep();
            if (_lockstep == null) return;

            _state = _bootstrap.GetState();
            _lockstep.ManualInputProvider = GetManualInput;
            Debug.Log("[ManualTestUI] 已接管手动输入（热座模式）");
        }

        /// <summary>
        /// Lockstep 行动阶段轮询入口。有匹配当前行动者的缓存指令则交出，
        /// 否则记录当前行动者并返回 null 让回合暂停等待点击。
        /// </summary>
        private ICommand GetManualInput(int playerId)
        {
            if (_pendingCommand != null && _pendingCommand.PlayerId == playerId)
            {
                ICommand cmd = _pendingCommand;
                _pendingCommand = null;
                _waitingForInput = false;
                return cmd;
            }

            _pendingCommand = null; // 丢弃行动者不匹配的残留指令（防御性）
            _currentChar = _state.GetCharacterByPlayer(playerId);
            _waitingForInput = true;
            return null;
        }

        public void SubmitCommand(ICommand cmd)
        {
            _pendingCommand = cmd;
        }

        private void OnGUI()
        {
            if (!_waitingForInput || _currentChar == null || _state == null) return;

            Character c = _currentChar;
            if (c.IsDead) return;

            GUILayout.BeginArea(new Rect(10, 10, 340, Screen.height - 20), GUI.skin.box);
            GUILayout.Label($"[Round {_state.CurrentTurn}] {c.Name} 的回合  AP:{c.ActionPoints}  HP:{c.Sanity:F1}/{c.MaxSanity:F0}");

            if (_pendingCommand != null)
            {
                GUILayout.Label("已选择操作，等待结算...");
            }
            else
            {
                DrawMoveButtons(c);
                DrawAttackButtons(c);
                DrawSkillButtons(c);
                DrawItemButtons(c);

                if (GUILayout.Button("跳过（浪费 1 AP）"))
                    SubmitCommand(new WasteAPCommand(_state.CurrentTick, c.PlayerId));
            }

            GUILayout.Space(8);
            DrawAllCharacterStates();
            GUILayout.EndArea();
        }

        private void DrawMoveButtons(Character c)
        {
            if (!c.CanMove) return;
            int[] connections = _state.Locations.GetConnectedLocations(c.CurrentLocationId);
            if (connections == null) return;

            for (int i = 0; i < connections.Length; i++)
            {
                int locId = connections[i];
                if (GUILayout.Button($"移动 → {_state.Locations.GetName(locId)} (1 AP)"))
                    SubmitCommand(new MoveCommand(_state.CurrentTick, c.PlayerId, locId));
            }
        }

        private void DrawAttackButtons(Character c)
        {
            if (!c.CanAct) return;
            string weapon = c.Inventory.EquippedWeapon?.Name ?? "空手";

            for (int i = 0; i < _state.PlayerCount; i++)
            {
                Character other = _state.Characters[i];
                if (other == null || other.IsDead || other.PlayerId == c.PlayerId) continue;
                if (other.CurrentLocationId != c.CurrentLocationId) continue;

                if (GUILayout.Button($"攻击 {other.Name} [{weapon}] (1 AP)"))
                    SubmitCommand(new DamageCommand(_state.CurrentTick, c.PlayerId, other.PlayerId, 1f));
            }
        }

        private void DrawSkillButtons(Character c)
        {
            SkillDef[] skills = c.Definition?.Skills;
            if (skills == null) return;

            for (int i = 0; i < skills.Length; i++)
            {
                SkillDef skill = skills[i];
                if (!SkillSystem.CanActivate(skill, c)) continue;

                string cost = skill.Cost.Type == SkillCostType.AP
                    ? $"{skill.Cost.Amount:F0} AP"
                    : $"{skill.Cost.Amount:F0} {skill.Cost.Type}";

                if (skill.Target == SkillTargetType.Self)
                {
                    // 自 Buff 技能：只能对自己
                    if (GUILayout.Button($"技能 {skill.Name} → {c.Name} ({cost})"))
                        SubmitCommand(new UseSkillCommand(_state.CurrentTick, c.PlayerId, i, c.PlayerId));
                }
                else if (skill.Target == SkillTargetType.Any)
                {
                    // 任意目标技能：显示自己 + 所有存活角色
                    // 守护 (Id=2)：充能未满 → 纯充能（无目标选择）；满充能 → 选择目标给盾
                    if (skill.Id == 2)
                    {
                        int charge = c.ASC.GetGuardianCharge();
                        if (charge >= 3)
                        {
                            GUILayout.Label($"技能 {skill.Name} (免费) [满充能·选择给盾目标] — 给谁：");
                            if (GUILayout.Button($"  └ 自己 [{c.Name}]"))
                                SubmitCommand(new UseSkillCommand(_state.CurrentTick, c.PlayerId, i, c.PlayerId));
                            for (int j = 0; j < _state.PlayerCount; j++)
                            {
                                Character other = _state.Characters[j];
                                if (other == null || other.IsDead || other.PlayerId == c.PlayerId) continue;
                                if (GUILayout.Button($"  └ {other.Name}"))
                                    SubmitCommand(new UseSkillCommand(_state.CurrentTick, c.PlayerId, i, other.PlayerId));
                            }
                        }
                        else
                        {
                            // 充能未满：纯充能，无目标选择
                            if (GUILayout.Button($"技能 {skill.Name} (1 AP) [充能 {charge}/3]"))
                                SubmitCommand(new UseSkillCommand(_state.CurrentTick, c.PlayerId, i, c.PlayerId));
                        }
                    }
                    else
                    {
                        GUILayout.Label($"技能 {skill.Name} ({cost}) — 选择目标：");
                        if (GUILayout.Button($"  └ 自己 [{c.Name}]"))
                            SubmitCommand(new UseSkillCommand(_state.CurrentTick, c.PlayerId, i, c.PlayerId));
                        for (int j = 0; j < _state.PlayerCount; j++)
                        {
                            Character other = _state.Characters[j];
                            if (other == null || other.IsDead || other.PlayerId == c.PlayerId) continue;
                            if (GUILayout.Button($"  └ {other.Name}"))
                                SubmitCommand(new UseSkillCommand(_state.CurrentTick, c.PlayerId, i, other.PlayerId));
                        }
                    }
                }
                else
                {
                    // 敌方技能：找同地点敌人
                    int targetId = FindSkillTarget(c);
                    if (targetId < 0) continue;
                    Character target = _state.GetCharacterByPlayer(targetId);
                    if (GUILayout.Button($"技能 {skill.Name} → {target?.Name} ({cost})"))
                        SubmitCommand(new UseSkillCommand(_state.CurrentTick, c.PlayerId, i, targetId));
                }
            }
        }

        /// <summary>技能目标：同地点敌人优先，否则任一存活敌人（技能不受地点限制）。</summary>
        private int FindSkillTarget(Character c)
        {
            int anyEnemy = -1;
            for (int i = 0; i < _state.PlayerCount; i++)
            {
                Character other = _state.Characters[i];
                if (other == null || other.IsDead || other.PlayerId == c.PlayerId) continue;
                if (other.CurrentLocationId == c.CurrentLocationId) return other.PlayerId;
                if (anyEnemy < 0) anyEnemy = other.PlayerId;
            }
            return anyEnemy;
        }

        private void DrawItemButtons(Character c)
        {
            if (!c.CanAct) return;

            for (int i = 0; i < c.Inventory.Consumables.Count; i++)
            {
                ItemDef item = c.Inventory.Consumables[i];
                if (item == null || c.ActionPoints < item.UseAPCost) continue;

                int targetId = FindItemTarget(c, item.TargetType);
                if (targetId < 0) continue;

                Character target = _state.GetCharacterByPlayer(targetId);
                if (GUILayout.Button($"道具 {item.Name} → {target?.Name} ({item.UseAPCost} AP)"))
                    SubmitCommand(new UseItemCommand(_state.CurrentTick, c.PlayerId, i, targetId));
            }
        }

        /// <summary>道具目标：Self 对自己；SameLocation 需同地点敌人；AnyPlayer 任一存活敌人。</summary>
        private int FindItemTarget(Character c, ItemTargetType type)
        {
            if (type == ItemTargetType.Self) return c.PlayerId;

            for (int i = 0; i < _state.PlayerCount; i++)
            {
                Character other = _state.Characters[i];
                if (other == null || other.IsDead || other.PlayerId == c.PlayerId) continue;
                if (type == ItemTargetType.SameLocation && other.CurrentLocationId != c.CurrentLocationId) continue;
                return other.PlayerId;
            }
            return -1;
        }

        private void DrawAllCharacterStates()
        {
            for (int i = 0; i < _state.PlayerCount; i++)
            {
                Character ch = _state.Characters[i];
                if (ch == null) continue;
                string dead = ch.IsDead ? " [死亡]" : "";
                string shield = ch.ASC.Tags.HasTag(GameplayTagConstants.HasShield) ? " [盾]" : "";
                string mist = ch.ASC.Tags.HasTag(GameplayTagConstants.MistState) ? " [迷离]" : "";
                GUILayout.Label($"{ch.Name}  HP:{ch.Sanity:F1}  AP:{ch.ActionPoints}{shield}{mist}{dead}  @{_state.Locations.GetName(ch.CurrentLocationId)}");
            }
        }
    }
}
