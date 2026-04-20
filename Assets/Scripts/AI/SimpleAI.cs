using System;
using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
using Gameplay.Judgment;
using Gameplay.Movement;

namespace AI
{
    public class SimpleAI
    {
        private readonly DeterministicRandom _random;

        /// <summary>注入随机流（网络模式：AI 决策不得消耗世界随机流，见 P6 设计）。
        /// 默认 seed = playerId * 1000 + 7：+7 保证非零（xorshift 约束），乘法拉开不同玩家的序列；
        /// 固定 seed 意味着 AI 序列与对局 seed 无关、每局可复现（确定性调试特性）。</summary>
        public SimpleAI(int playerId) : this(playerId, new DeterministicRandom(playerId * 1000 + 7)) { }

        /// <summary>注入随机流（网络模式：AI 决策不得消耗世界随机流，见 P6 设计）。</summary>
        public SimpleAI(int playerId, DeterministicRandom aiRandom)
        {
            _random = aiRandom;
        }

        public ChipAllocation GetJudgmentInput(WorldState state, int playerId)
        {
            ChipAllocation allocation = new ChipAllocation(playerId);
            allocation.SetRandom(_random);
            return allocation;
        }

        public ICommand GetActionInput(WorldState state, int playerId)
        {
            Character c = state.GetCharacterByPlayer(playerId);
            if (c == null || c.IsDead) return null;
            bool hasFreeAction = c.ASC.GetGuardianCharge() >= 3;
            if (c.ActionPoints <= 0 && !hasFreeAction) return null;

            // 理智垂危 → 最高优先级使用应急处理装置自救（0 AP，不占决策骰）
            ICommand emergency = TryUseEmergencyDevice(state, c, playerId);
            if (emergency != null) return emergency;

            // P7-A 角色特例：混沌/数据幽灵全接管（绝不落回通用技能流——否则混沌 120 会被
            // TryUseSkill 当普通技能空放烧 AP）；双子只拦截附灵/转换/泣血攻击（其余落通用流）
            int defId = c.Definition?.Id ?? 0;
            if (defId == SkillSystem.ChaosMageCharacterId)
                return TryChaosAction(state, c, playerId)
                    ?? TryMove(state, c, playerId)
                    ?? new WasteAPCommand(state.CurrentTick, playerId);
            if (defId == SkillSystem.DataGhostCharacterId)
                return TryGhostAction(state, c, playerId)
                    ?? TryMove(state, c, playerId)
                    ?? new WasteAPCommand(state.CurrentTick, playerId);
            if (defId == SkillSystem.TwinCharacterId)
            {
                ICommand twinCmd = TryTwinAction(state, c, playerId);
                if (twinCmd != null) return twinCmd;
            }

            int roll = _random.Next(100);

            // Find enemy at same location (for combat) and any alive enemy (for movement)
            int sameLocEnemy = -1;
            int anyEnemy = -1;
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character other = state.Characters[i];
                if (other == null || other.IsDead || other.PlayerId == playerId) continue;
                anyEnemy = other.PlayerId; // just grab first alive enemy
                if (other.CurrentLocationId == c.CurrentLocationId)
                    sameLocEnemy = other.PlayerId;
            }

            ICommand cmd;
            if (sameLocEnemy >= 0)
            {
                // Enemy at same location — exclusive ranges:
                //   [ 0, 35)  35%  use skill on enemy
                //   [35, 70)  35%  attack with weapon (枪→远程/换弹, 锤→重击, 其他→普攻)
                //   [70, 85)  15%  heal if hurt
                //   [85,100)  15%  move away
                if (roll < 35)
                    cmd = TryUseSkill(state, c, playerId, sameLocEnemy);
                else if (roll < 70)
                    cmd = TryAttack(state, c, playerId, sameLocEnemy);
                else if (roll < 85)
                    cmd = TryHeal(state, c, playerId);
                else
                    cmd = TryMove(state, c, playerId);
            }
            else
            {
                // No enemy here — exclusive ranges:
                //   [ 0, 10)  10%  独处时安置触发式炸弹（背包有 Id=7 才生效）
                //   [10, 65)  55%  有枪 → 远程攻击/换弹；否则向敌人移动（无敌人 → 自 Buff）
                //   [65, 80)  15%  heal if hurt
                //   [80,100)  20%  self-buff skill (守护充能/迷离之境 独处时也该用)
                if (roll < 10)
                    cmd = TryPlantTrap(state, c, playerId);
                else if (roll < 65)
                {
                    cmd = TryUseRangedWeapon(state, c, playerId);
                    if (cmd == null)
                        cmd = anyEnemy >= 0 ? TryMove(state, c, playerId) : TryUseSelfSkill(state, c, playerId);
                }
                else if (roll < 80)
                    cmd = TryHeal(state, c, playerId);
                else
                    cmd = TryUseSelfSkill(state, c, playerId);
            }

            // Any range whose action is unavailable falls back to moving
            // (testing: never idle-waste AP unless movement is also impossible).
            return cmd ?? TryMove(state, c, playerId) ?? new WasteAPCommand(state.CurrentTick, playerId);
        }

        private ICommand TryUseSkill(WorldState state, Character c, int playerId, int targetId)
        {
            if (c.Definition?.Skills == null || c.Definition.Skills.Length == 0) return null;

            // Single pass over CanActivate: collect valid indices, then pick one (zero GC)
            Span<int> validSkills = stackalloc int[c.Definition.Skills.Length];
            int vc = 0;
            for (int i = 0; i < c.Definition.Skills.Length; i++)
            {
                SkillDef cand = c.Definition.Skills[i];
                if (SkillSystem.CanActivate(cand, c) && !IsTwinFormRecast(c, cand))
                    validSkills[vc++] = i;
            }
            if (vc == 0) return null;

            int skillIdx = validSkills[_random.Next(vc)];

            // 自 Buff（Self）/ 任意目标（Any）技能：AI 对自己施放
            // 守护充能攒在自己身上，满 3 充能自动兑换护盾给目标（target 即自己）
            // Any 类型技能在 AI 测试中统一对自己施放
            SkillDef skill = c.Definition.Skills[skillIdx];
            int target = (skill.Target == SkillTargetType.Self || skill.Target == SkillTargetType.Any)
                ? playerId : targetId;
            return new UseSkillCommand(state.CurrentTick, playerId, skillIdx, target);
        }

        /// <summary>独处时的自 Buff/任意目标技能：挑 Target == Self 或 Any 的可激活技能，对自己施放。</summary>
        private ICommand TryUseSelfSkill(WorldState state, Character c, int playerId)
        {
            if (c.Definition?.Skills == null || c.Definition.Skills.Length == 0) return null;

            Span<int> validSkills = stackalloc int[c.Definition.Skills.Length];
            int vc = 0;
            for (int i = 0; i < c.Definition.Skills.Length; i++)
            {
                SkillDef skill = c.Definition.Skills[i];
                if ((skill.Target == SkillTargetType.Self || skill.Target == SkillTargetType.Any)
                    && SkillSystem.CanActivate(skill, c) && !IsTwinFormRecast(c, skill))
                    validSkills[vc++] = i;
            }
            if (vc == 0) return null;

            int skillIdx = validSkills[_random.Next(vc)];
            return new UseSkillCommand(state.CurrentTick, playerId, skillIdx, playerId);
        }

        /// <summary>
        /// 武器攻击路由：枪 → 远程射击/换弹（不限地点），施工锤 → 近战重击 1.5，
        /// 其他武器 → 普攻 1.0。空手不攻击。
        /// </summary>
        private static ICommand TryAttack(WorldState state, Character c, int playerId, int targetId)
        {
            ItemDef weapon = c.Inventory.EquippedWeapon;
            if (weapon == null) return null;

            // 枪 (Id=3)：有弹 → 远程射击最近敌人；无弹 → 换弹
            if (weapon.Id == 3)
            {
                ICommand ranged = TryUseRangedWeapon(state, c, playerId);
                if (ranged != null) return ranged;
            }

            // 施工锤 (Id=4)：近战重击同地点敌人
            if (weapon.Id == 4)
                return new DamageCommand(state.CurrentTick, playerId, targetId, 1.5f);

            // 其他武器（刀等）：普通攻击
            return new DamageCommand(state.CurrentTick, playerId, targetId, 1f);
        }

        /// <summary>
        /// 远程武器行动：装备枪时，有弹 → 射击最近敌人（同地点优先，否则任意存活敌人），
        /// 无弹 → 换弹。未装备枪返回 null。
        /// </summary>
        private static ICommand TryUseRangedWeapon(WorldState state, Character c, int playerId)
        {
            ItemDef weapon = c.Inventory.EquippedWeapon;
            if (weapon == null || weapon.Id != 3) return null;

            if (c.Inventory.EquippedWeaponAmmo <= 0)
                return new ReloadCommand(state.CurrentTick, playerId);

            int target = FindNearestEnemy(state, c, playerId);
            if (target < 0) return null;
            return new FireGunCommand(state.CurrentTick, playerId, target);
        }

        /// <summary>
        /// 最近敌人：同地点优先，否则按索引取首个存活敌人（确定性）。
        /// 跳过无法瞄准的藏匿目标（目标隐身且自己未隐身），避免必然落空的射击。
        /// </summary>
        private static int FindNearestEnemy(WorldState state, Character c, int playerId)
        {
            bool selfHidden = c.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden);
            int any = -1;
            for (int i = 0; i < state.PlayerCount; i++)
            {
                Character other = state.Characters[i];
                if (other == null || other.IsDead || other.PlayerId == playerId) continue;
                if (other.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden) && !selfHidden) continue;
                if (other.CurrentLocationId == c.CurrentLocationId) return other.PlayerId;
                if (any < 0) any = other.PlayerId;
            }
            return any;
        }

        /// <summary>独处时安置触发式炸弹（Id=7）。背包无弹返回 null（外层回退到移动）。</summary>
        private static ICommand TryPlantTrap(WorldState state, Character c, int playerId)
        {
            for (int i = 0; i < c.Inventory.Consumables.Count; i++)
            {
                ItemDef item = c.Inventory.Consumables[i];
                if (item != null && item.Id == 7)
                    return new UseItemCommand(state.CurrentTick, playerId, i, playerId);
            }
            return null;
        }

        /// <summary>
        /// 理智 &lt; 2 且背包有应急处理装置（Id=8）时使用。
        /// DeathSave 已激活则不重复使用（避免浪费第二件装置）。
        /// </summary>
        private static ICommand TryUseEmergencyDevice(WorldState state, Character c, int playerId)
        {
            if (c.Sanity >= 2f) return null;
            if (c.ASC.Tags.HasTag(GameplayTagConstants.DeathSaveActive)) return null;

            for (int i = 0; i < c.Inventory.Consumables.Count; i++)
            {
                ItemDef item = c.Inventory.Consumables[i];
                if (item != null && item.Id == 8)
                    return new UseItemCommand(state.CurrentTick, playerId, i, playerId);
            }
            return null;
        }

        /// <summary>按技能 Id 找技能槽索引（无 → -1）。</summary>
        private static int FindSkillIndex(Character c, int skillId)
        {
            if (c?.Definition?.Skills == null) return -1;
            for (int i = 0; i < c.Definition.Skills.Length; i++)
                if (c.Definition.Skills[i].Id == skillId) return i;
            return -1;
        }

        /// <summary>
        /// 双子（角色 15）已附灵（泣血/灵巧）时排除附灵技能槽 123/124：通用流随机再选附灵技能，
        /// 要么重附空耗 1 AP、要么随机翻转泣血⇄灵巧——推翻 TryTwinAction 按理智/武器选定的形态策略
        /// （Task 6 评审 I2-Minor）。docs 允许 1 AP 主动切换，但随机翻转非策略；附灵/切换决策
        /// 只由 TryTwinAction 专属路径发出，通用流落攻击/移动。
        /// </summary>
        private static bool IsTwinFormRecast(Character c, SkillDef skill)
        {
            if (c.Definition?.Id != SkillSystem.TwinCharacterId) return false;
            int id = skill == null ? 0 : skill.Id;
            if (id != SkillSystem.TwinVampiricSkillId && id != SkillSystem.TwinAgileSkillId) return false;
            return c.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric)
                || c.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile);
        }

        /// <summary>混沌法师决策：低理智模拟血包；有敌人时 60% 混乱模拟（刀/枪掷选）或普攻；否则交给移动。</summary>
        private ICommand TryChaosAction(WorldState state, Character c, int playerId)
        {
            if (c.Sanity <= 1.5f && c.Sanity < c.MaxSanity)
                return new ChaosSimulateCommand(state.CurrentTick, playerId, 2, playerId); // 模拟血包

            int enemy = FindNearestEnemy(state, c, playerId);
            if (enemy < 0) return null;

            if (_random.Next(100) < 60)
            {
                int itemId = _random.Next(2) == 0 ? 1 : 3; // 刀（近战 1.5）/ 枪（远程 2，免弹药地理）
                return new ChaosSimulateCommand(state.CurrentTick, playerId, itemId, enemy);
            }
            if (c.Inventory.EquippedWeapon == null) return null; // 空手：无普攻
            return TryAttack(state, c, playerId, enemy);
        }

        /// <summary>数据幽灵决策：无分身且充裕时概率造分身；低理智切 V4 花；远程优势切 V5；
        /// V1 常态概率升 V2；按当前形态攻击（V4 自愈）。
        /// 形态切换（125）按预测落点匹配目标才发（见下方 NextUnlockedForm 门控）。</summary>
        private ICommand TryGhostAction(WorldState state, Character c, int playerId)
        {
            int harmIdx = FindSkillIndex(c, SkillSystem.HarmonicsSkillId);
            int switchIdx = FindSkillIndex(c, SkillSystem.HandsonicSkillId);

            if (!state.GhostCloneActive && c.Sanity >= 2.5f && c.ActionPoints >= 1
                && harmIdx >= 0 && _random.Next(100) < 30)
                return new UseSkillCommand(state.CurrentTick, playerId, harmIdx, playerId);

            int enemy = FindNearestEnemy(state, c, playerId);
            bool sameLoc = enemy >= 0
                && state.GetCharacterByPlayer(enemy)?.CurrentLocationId == c.CurrentLocationId;
            int form = DataGhostFormAttack.CurrentForm(c);

            if (switchIdx >= 0 && !c.ASC.Tags.HasTag(GameplayTagConstants.FormSwitched))
            {
                // 技能 125 每次只单步落到「下一已解锁形态」（SwitchForm 语义）：决策按只读预测
                // NextUnlockedForm 的落点匹配目标形态才发指令，否则「想切 V4 实际落 V2」要空烧
                // 每回合一次的切换额度爬多回合、V5→V4 还会绕环。落点不匹配即放弃本次切换
                // （走攻击/移动），保持简单确定性策略（Task 6 评审 I1）。
                int nextForm = DataGhostFormAttack.NextUnlockedForm(c, form);
                if (c.Sanity < 2f && c.Sanity < c.MaxSanity
                    && DataGhostFormAttack.IsUnlocked(c, 4) && nextForm == 4)
                    return new UseSkillCommand(state.CurrentTick, playerId, switchIdx, playerId); // → V4 花
                if (enemy >= 0 && !sameLoc
                    && DataGhostFormAttack.IsUnlocked(c, 5) && nextForm == 5)
                    return new UseSkillCommand(state.CurrentTick, playerId, switchIdx, playerId); // → V5 远程
                if (sameLoc && nextForm == 2 && _random.Next(100) < 35)
                    return new UseSkillCommand(state.CurrentTick, playerId, switchIdx, playerId); // → V2 长刃
            }

            if (form == 4)
                return c.Sanity < c.MaxSanity
                    ? (ICommand)new DamageCommand(state.CurrentTick, playerId, playerId, 1f) // V4 自愈
                    : null;
            if (sameLoc)
                return new DamageCommand(state.CurrentTick, playerId, enemy, 1f); // V1/V2/V3 近战
            if (enemy >= 0 && form == 5)
                return new DamageCommand(state.CurrentTick, playerId, enemy, 1f); // V5 远程
            return null;
        }

        /// <summary>双子决策：无附灵且持武器时附灵（受伤 → 泣血回血；健康 → 灵巧换枪路线）；
        /// 泣血态恢复健康后主动切灵巧（docs 7.6 设计内 1 AP 切换）；灵巧态周期换枪；
        /// 泣血态受伤打近战吸血。附灵形态只在本分派内变更（通用技能流排除附灵槽，
        /// 见 IsTwinFormRecast——避免随机重附/翻转推翻本策略）。其余交给通用流。</summary>
        private ICommand TryTwinAction(WorldState state, Character c, int playerId)
        {
            if (c.Inventory.EquippedWeapon == null) return null;
            if (c.ActionPoints < 1) return null;

            bool vamp = c.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric);
            bool agile = c.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile);

            if (!vamp && !agile)
            {
                // 确定性策略（非掷币）：受伤（需回血）→ 泣血；健康 → 灵巧（换枪路线）。
                // AI 随机流按玩家固定，掷币结果跨对局恒定——无意义的随机源。
                bool wantVamp = c.Sanity < c.MaxSanity - 0.9f;
                int idx = wantVamp
                    ? FindSkillIndex(c, SkillSystem.TwinVampiricSkillId)
                    : FindSkillIndex(c, SkillSystem.TwinAgileSkillId);
                if (idx >= 0)
                    return new UseSkillCommand(state.CurrentTick, playerId, idx, playerId);
            }

            if (agile && c.Inventory.EquippedWeapon.Id != 3 && _random.Next(100) < 18)
                return new TwinConvertCommand(state.CurrentTick, playerId, 3); // 周期换枪（简化）

            if (vamp && c.Sanity < c.MaxSanity - 0.9f)
            {
                int enemy = FindNearestEnemy(state, c, playerId);
                if (enemy >= 0
                    && state.GetCharacterByPlayer(enemy)?.CurrentLocationId == c.CurrentLocationId)
                    return TryAttack(state, c, playerId, enemy); // 泣血攻击吸血
            }

            if (vamp && c.Sanity >= c.MaxSanity - 0.9f
                && c.Inventory.EquippedWeapon.Id != 3)
            {
                // 泣血仅在受伤时有回血价值：健康后主动切换灵巧续上换枪路线
                // （随机翻转被通用流排除，这是形态变更的唯一健康路径）
                int agileIdx = FindSkillIndex(c, SkillSystem.TwinAgileSkillId);
                if (agileIdx >= 0)
                    return new UseSkillCommand(state.CurrentTick, playerId, agileIdx, playerId);
            }
            return null;
        }

        private static ICommand TryHeal(WorldState state, Character c, int playerId)
        {
            if (c.Sanity >= c.MaxSanity) return null;
            for (int i = 0; i < c.Inventory.Consumables.Count; i++)
            {
                ItemDef item = c.Inventory.Consumables[i];
                // 只认血包（Id=2）。荆棘素/触发式炸弹/应急装置的 TargetType 也是 Self，
                // 按 TargetType 判断会把功能道具当治疗品误耗；应急装置由
                // TryUseEmergencyDevice 在理智垂危时专门处理，不在这里消耗。
                if (item != null && item.Id == 2)
                    return new UseItemCommand(state.CurrentTick, playerId, i, playerId);
            }
            return null;
        }

        private ICommand TryMove(WorldState state, Character c, int playerId)
        {
            // Simple movement: step to a random connected location (map hub topology
            // makes this converge toward enemies over a few turns)
            int[] connections = state.Locations.GetConnectedLocations(c.CurrentLocationId);
            if (connections == null || connections.Length == 0) return null;
            int target = connections[_random.Next(connections.Length)];
            return new MoveCommand(state.CurrentTick, playerId, target);
        }
    }
}
