using Core;
using Core.GAS;
using Gameplay.Judgment;

namespace Gameplay.Characters
{
    /// <summary>
    /// 响应式技能分派：DamagePipeline 埋点调用，按 skillId 分派特例。
    /// 目标无 Reactive 技能时零开销跳过（只读遍历技能数组，无堆分配）。
    /// </summary>
    public static class ReactiveSystem
    {
        /// <summary>附灵·泣血加成量（技能效果量，非武器基础伤害数值源，豁免 GetSanityDamage 规则）。</summary>
        private const float VampiricBonus = 0.5f;

        /// <summary>伤害应用前：响应技能可修改 finalDamage。返回修改后的伤害。</summary>
        public static void TryOnPreDamage(WorldState state, Character source, Character target,
            DamageType damageType, ref float finalDamage)
        {
            if (target == null || target.Definition == null || target.Definition.Skills == null) return;

            // 圣骑士受伤充能：目标侧，骑士精神开启时 +1（上限 4）
            if (target.Definition.Id == 12
                && target.ASC.Tags.HasTag(GameplayTagConstants.KnightSpirit)
                && target.ASC.GetGuardianCharge() < 4)
                target.ASC.AddGuardianCharge();

            SkillDef[] skills = target.Definition.Skills;
            for (int i = 0; i < skills.Length; i++)
            {
                SkillDef s = skills[i];
                if (s == null || s.Activation != SkillActivationType.Reactive) continue;
                if (s.ReactiveTrigger != ReactiveTriggerType.OnPreDamage) continue;
                DispatchOnPreDamage(state, source, target, s.Id, damageType, ref finalDamage);
            }
        }

        /// <summary>
        /// 来源侧攻击判定（伤害应用前修改 finalDamage）：魑魅带 MistState 攻击时判定，
        /// 胜 → 本次伤害 + 1/2；败 → 心雾 +1。与 TryOnPreDamage（目标侧分派）互补。
        /// 直接改 finalDamage 而非追加伤害，避免追加 Effect 伤害再次触发本钩子导致递归。
        /// </summary>
        public static void TryOnAttack(WorldState state, Character source, Character target, ref float finalDamage)
        {
            if (source == null || target == null) return;

            // 圣骑士攻击充能：来源侧，常态 +1；骑士精神开启时 +2（上限 4）
            if (source.Definition != null && source.Definition.Id == 12 && source.ASC.GetGuardianCharge() < 4)
            {
                source.ASC.AddGuardianCharge();
                if (source.ASC.Tags.HasTag(GameplayTagConstants.KnightSpirit) && source.ASC.GetGuardianCharge() < 4)
                    source.ASC.AddGuardianCharge();
            }

            // 代行者伤害追踪：记录最近造成伤害的回合（主的意志判定用）
            if (source.Definition != null && source.Definition.Id == 13)
                source.ASC.MarkDamageDealt(state.CurrentTurn);

            // 双子附灵·泣血（Id=123 状态）：武器攻击伤害 +0.5、每次攻击自身回 0.5
            // （本钩子 = 来源侧每个伤害实例结算点；双子无 MistState，须位于早退前）
            if (source.Definition != null && source.Definition.Id == SkillSystem.TwinCharacterId
                && source.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric))
            {
                finalDamage += VampiricBonus;
                source.ASC.ModifyAttributeCurrentValue(AttributeConstants.Sanity, VampiricBonus);
            }

            if (!source.ASC.Tags.HasTag(GameplayTagConstants.MistState)) return;
            bool win = JudgmentDuel.Resolve(state, source, target);
            if (win)
                finalDamage += finalDamage * 0.5f;
            else
                source.ASC.AddMistLayer();
        }

        public static void TryOnKill(WorldState state, Character killer, Character victim)
        {
            if (killer == null || killer.Definition == null || killer.Definition.Skills == null) return;

            // 代行者审判者模式：杀人后本能 -1（不退出审判者，退出由 ApplyLoneWill 按本能 <4 判定）
            if (killer.Definition.Id == 13 && killer.ASC.Tags.HasTag(GameplayTagConstants.JudgeMode))
                killer.ASC.ModifyInstinct(-1);

            SkillDef[] skills = killer.Definition.Skills;
            for (int i = 0; i < skills.Length; i++)
            {
                SkillDef s = skills[i];
                if (s == null || s.Activation != SkillActivationType.Reactive) continue;
                if (s.ReactiveTrigger != ReactiveTriggerType.OnKill) continue;
                DispatchOnKill(state, killer, victim, s.Id);
            }
        }

        // ---- 测试分派与正式分派按 skillId 分支 ----

        private static void DispatchOnPreDamage(WorldState state, Character source, Character target,
            int skillId, DamageType damageType, ref float finalDamage)
        {
            switch (skillId)
            {
                case 900: // 测试语义：伤害 +1
                    finalDamage += 1f;
                    break;
                case 30:  // 魔术师镜花水月（Task 3 实现）
                    MagicianMirror.Resolve(state, source, target, ref finalDamage);
                    break;
                case 127: // 数据幽灵规诫：受击解锁形态（近战/远程近似规则见 GhostDiscipline 注释）
                    GhostDiscipline.Unlock(target, source, damageType);
                    break;
            }
        }

        private static void DispatchOnKill(WorldState state, Character killer, Character victim, int skillId)
        {
            switch (skillId)
            {
                case 900: // 测试语义：击杀者 +1 AttackBonus
                    killer.ASC.ModifyAttributeCurrentValue(AttributeConstants.AttackBonus, 1f);
                    break;
                case 60:  // 倾听者地狱的呓语（Task 6 实现）
                    ListenerWhisper.OnKill(killer);
                    break;
                case 92:  // 阵法大师阵法回路（Task 3 实现）
                    FormationCircuit.OnKill(state, killer);
                    break;
                case SkillSystem.AbyssSkillId: // 128 数据幽灵行向深渊（Task 5 实现）
                    GhostAbyss.OnKill(state, killer);
                    break;
            }
        }
    }

    /// <summary>魔术师镜花水月（简化版）：受伤害时判定，胜 → 伤害抵消；败 → 伤害 +1。</summary>
    public static class MagicianMirror
    {
        public static void Resolve(WorldState state, Character source, Character target, ref float finalDamage)
        {
            bool win = JudgmentDuel.Resolve(state, target, source); // 受伤害方 vs 来源
            if (win)
                finalDamage = 0f;
            else
                finalDamage += 1f;
        }
    }

    /// <summary>倾听者击杀回复：理智与侵蚀上限回复至 4（理智钳 MaxSanity）。</summary>
    public static class ListenerWhisper
    {
        public static void OnKill(Character killer)
        {
            killer.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 4f);
            killer.ASC.SetAttributeBaseValue(AttributeConstants.Sanity,
                killer.ASC.GetAttributeValue(AttributeConstants.MaxSanity));
        }
    }

    /// <summary>阵法大师阵法回路：杀人时侵蚀 +1，并与一名存活角色判定，胜理智 -0.5 / 败 -1。</summary>
    public static class FormationCircuit
    {
        public static void OnKill(WorldState state, Character killer)
        {
            Character rival = null;
            for (int i = 0; i < state.Characters.Length; i++)
            {
                Character ch = state.Characters[i];
                if (ch != null && !ch.IsDead && ch.Id != killer.Id) { rival = ch; break; }
            }
            bool win = rival == null || JudgmentDuel.Resolve(state, killer, rival);
            killer.ASC.ModifyAttributeCurrentValue(AttributeConstants.ErosionLimit, 1f); // 侵蚀 +1
            killer.ASC.ModifyAttributeCurrentValue(AttributeConstants.Sanity, win ? -0.5f : -1f);
        }
    }

    /// <summary>
    /// 数据幽灵规诫（技能 Id=127）：受击解锁形态，永久。
    /// 任意伤害解锁 V4；物理伤害按来源判定近战/远程（spec 批准近似）：
    /// 来源装备枪（Id=3）或自带 V5Form → 远程 → 解锁 V5；否则近战 → 解锁 V2/V3。
    /// Effect/Magic 伤害只解锁 V4。此近似规则已同步进验收注释。
    /// 挂点语义（钉死）：OnPreDamage 位于管线第 2.5 段，先于护盾（第 3 段）与死亡豁免
    /// （第 4.5 段）——被盾挡/被豁免的伤害同样触发解锁（spec §5.3 挂点裁决，
    /// 字面宽于 7.15 的"受击"表述）；唯免疫段（第 1 段）早退的伤害不触发。
    /// </summary>
    public static class GhostDiscipline
    {
        public static void Unlock(Character ghost, Character source, DamageType damageType)
        {
            if (ghost == null || ghost.Definition == null
                || ghost.Definition.Id != SkillSystem.DataGhostCharacterId) return;

            ghost.ASC.Tags.AddTag(GameplayTagConstants.V4Unlocked);
            if (damageType != DamageType.Physical) return;

            bool ranged = source != null && source.Definition != null
                && ((source.Inventory.EquippedWeapon != null && source.Inventory.EquippedWeapon.Id == 3)
                    || source.ASC.Tags.HasTag(GameplayTagConstants.V5Form));
            if (ranged)
                ghost.ASC.Tags.AddTag(GameplayTagConstants.V5Unlocked);
            else
            {
                ghost.ASC.Tags.AddTag(GameplayTagConstants.V2Unlocked);
                ghost.ASC.Tags.AddTag(GameplayTagConstants.V3Unlocked);
            }
        }
    }

    /// <summary>
    /// 数据幽灵行向深渊（技能 Id=128）：击杀（本体或分身任意来源——复制伤害归本体，来源即本体）后
    /// 与数组序第一存活他人判定（S8；存活他人按 Sanity > 0 裁决，刚被杀者的尸体不参与判定——
    /// 2 人残局终杀视为「无他人」直接胜）。胜 → 本体 AP+1；败 → 本体扣 1、分身扣 1（至 0 触发
    /// 分身死亡判定：本体 vs 数组序第一存活他人，败 → 本体直接死亡 DeathCommand）。
    /// 扣理智致死一律走死亡结算入队（先例：混沌反噬 T2 修复），不做直扣后存活。
    /// </summary>
    public static class GhostAbyss
    {
        /// <summary>行向深渊 OnKill 入口（本体击杀目标后；对手 = 数组序第一存活他人，无他人视为胜）。</summary>
        public static void OnKill(WorldState state, Character killer)
        {
            if (killer == null || killer.Definition == null
                || killer.Definition.Id != SkillSystem.DataGhostCharacterId) return;

            Character rival = FindFirstOtherAlive(state, killer);
            bool win = rival == null || JudgmentDuel.Resolve(state, killer, rival);
            if (win)
            {
                killer.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, 1f);
                return;
            }

            // 败：本体 -1 理智；致死（≤0）入队 DeathCommand（对照 DamagePipeline 死亡出口，避免直扣存活）
            killer.ASC.ModifyAttributeCurrentValue(AttributeConstants.Sanity, -1f);
            if (killer.Sanity <= 0f)
            {
                state.Commands.Enqueue(new Gameplay.Combat.DeathCommand(state.CurrentTick, killer.PlayerId, -1));
                return; // 本体死亡 → 分身随本体消亡（DeathCommand 清理），不再连坐扣减
            }

            DrainClone(state, killer);
        }

        /// <summary>分身扣 1 理智；归 0 → 分身死亡（S5），进入分身死亡判定链。</summary>
        public static void DrainClone(WorldState state, Character body)
        {
            if (state == null || body == null || !state.GhostCloneActive) return;
            state.GhostCloneSanity -= 1f;
            if (state.GhostCloneSanity > 0f) return;
            state.GhostCloneSanity = 0f;
            state.GhostCloneActive = false;
            BodyDuelOnCloneDeath(state, body);
        }

        /// <summary>分身死亡（理智归 0）判定：本体 vs 数组序第一存活他人（S8）；败 → 本体直接死亡。</summary>
        public static void BodyDuelOnCloneDeath(WorldState state, Character body)
        {
            if (body == null || body.IsDead) return;
            Character rival = FindFirstOtherAlive(state, body);
            bool win = rival == null || JudgmentDuel.Resolve(state, body, rival);
            if (win) return;
            state.Commands.Enqueue(new Gameplay.Combat.DeathCommand(state.CurrentTick, body.PlayerId, -1));
        }

        /// <summary>
        /// 数组序第一存活他人（不含 self；无 → null，调用方按「无对手 → 判定胜」处理）。
        /// 语义裁决：Sanity ≤ 0 视为死（与 DamagePipeline 死亡判据同源）——TryOnKill/分身死亡判定
        /// 在 DeathCommand/Dead 标记落定前派发，刚被杀者此刻仍 !IsDead，必须以理智归零排除尸体
        /// （2 人残局终杀否则会把尸体判为 rival，违背 S8「无他人视为胜」）。
        /// 阵法回路/混沌/地狱呓语的内联副本为各自场景自洽语义，不随本 helper 改动。
        /// </summary>
        public static Character FindFirstOtherAlive(WorldState state, Character self)
        {
            for (int i = 0; i < state.Characters.Length; i++)
            {
                Character ch = state.Characters[i];
                if (ch != null && !ch.IsDead && ch.Sanity > 0f && ch.Id != self.Id) return ch;
            }
            return null;
        }
    }
}
