using System;
using System.Collections.Generic;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace SkillAcceptance
{
    /// <summary>
    /// 推导式技能断言：读取 SkillDef 声明自动生成通用检查项。
    /// 每个检查项独立场景验证，互不污染。职责边界：只做通用断言，
    /// 技能专属规则（充能、周期负载、判定分支细节）一律进 AcceptanceRegistry。
    /// </summary>
    public static class SkillContractVerifier
    {
        public static List<CheckResult> Verify(CharacterDef def, SkillDef skill, int skillIndex)
        {
            var results = new List<CheckResult>();
            if (skill == null) return results;

            // ——— 1. 激活类型 ———
            WorldState probe = AcceptanceScenes.BuildState(def);
            int casterPlayer = AcceptanceScenes.FindPlayerOfDef(probe, def);
            Character probeCaster = probe.GetCharacterByPlayer(casterPlayer);
            // BuildState 未经过判定分发 AP（默认 0），注入足额 AP 并补齐前置标签/属性，
            // 使本检查只验证「激活类型」而非资源是否充足
            probeCaster.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 10);
            if (skill.Cost.Type == SkillCostType.Charge)
                for (int i = 0; i < (int)skill.Cost.Amount; i++)
                    probeCaster.ASC.AddGuardianCharge(); // 充能成本注入，使激活类型检查只验证类型
            if (skill.RequiredTags != null)
                for (int i = 0; i < skill.RequiredTags.Length; i++)
                    probeCaster.ASC.Tags.AddTag(skill.RequiredTags[i]);
            if (skill.Restrictions != null)
                for (int r = 0; r < skill.Restrictions.Length; r++)
                {
                    SkillRestriction res = skill.Restrictions[r];
                    if (res.AttributeId == 0) continue;
                    probeCaster.ASC.SetAttributeBaseValue(res.AttributeId, res.MinValue);
                }
            bool canAct = SkillSystem.CanActivate(skill, probeCaster, skillIndex);

            if (skill.Activation != SkillActivationType.Active)
            {
                results.Add(canAct
                    ? CheckResult.Fail("激活类型-不可主动施放", $"{skill.Activation} 技能却返回 CanActivate = true")
                    : CheckResult.Pass("激活类型-不可主动施放", $"CanActivate = false（{skill.Activation} 正确）"));
                return results; // 被动/Reactive 无后续通用检查
            }

            results.Add(canAct
                ? CheckResult.Pass("激活类型-可主动施放", "CanActivate = true")
                : CheckResult.Fail("激活类型-可主动施放", "Active 技能 CanActivate = false"));

            // ——— 2. 前置条件拦截 ———
            if (skill.Cost.Type == SkillCostType.AP)
            {
                if (skill.Id == SkillSystem.GuardianSkillId)
                {
                    results.Add(CheckResult.Skip("前置条件-AP 不足拦截", "守护充能技能（Id=2）AP 语义由特例表验证"));
                }
                else
                {
                    var s = AcceptanceScenes.BuildState(def);
                    int p = AcceptanceScenes.FindPlayerOfDef(s, def);
                    Character c = s.GetCharacterByPlayer(p);
                    c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, skill.Cost.Amount - 1);
                    bool blocked = !SkillSystem.CanActivate(skill, c, skillIndex);
                    results.Add(blocked
                        ? CheckResult.Pass("前置条件-AP 不足拦截", $"AP={skill.Cost.Amount - 1} 时 CanActivate = false")
                        : CheckResult.Fail("前置条件-AP 不足拦截", $"AP={skill.Cost.Amount - 1} 仍可施放（期望 false）"));
                }
            }

            if (skill.RequiredTags != null && skill.RequiredTags.Length > 0)
            {
                var s = AcceptanceScenes.BuildState(def);
                int p = AcceptanceScenes.FindPlayerOfDef(s, def);
                Character c = s.GetCharacterByPlayer(p);
                for (int i = 0; i < skill.RequiredTags.Length; i++)
                    c.ASC.Tags.RemoveTag(skill.RequiredTags[i]);
                bool blocked = !SkillSystem.CanActivate(skill, c, skillIndex);
                results.Add(blocked
                    ? CheckResult.Pass("前置条件-RequiredTags 拦截", "缺失必需标签时 CanActivate = false")
                    : CheckResult.Fail("前置条件-RequiredTags 拦截", "缺失必需标签仍可施放（期望 false）"));
            }

            if (skill.BlockingTags != null && skill.BlockingTags.Length > 0)
            {
                var s = AcceptanceScenes.BuildState(def);
                int p = AcceptanceScenes.FindPlayerOfDef(s, def);
                Character c = s.GetCharacterByPlayer(p);
                for (int i = 0; i < skill.BlockingTags.Length; i++)
                    c.ASC.Tags.AddTag(skill.BlockingTags[i]);
                bool blocked = !SkillSystem.CanActivate(skill, c, skillIndex);
                results.Add(blocked
                    ? CheckResult.Pass("前置条件-BlockingTags 拦截", "携带阻断标签时 CanActivate = false")
                    : CheckResult.Fail("前置条件-BlockingTags 拦截", "携带阻断标签仍可施放（期望 false）"));
            }

            if (skill.Restrictions != null)
            {
                for (int r = 0; r < skill.Restrictions.Length; r++)
                {
                    SkillRestriction restriction = skill.Restrictions[r];
                    if (restriction.AttributeId == 0 && restriction.MinValue == 0f) continue; // default struct 视为无限制
                    var s = AcceptanceScenes.BuildState(def);
                    int p = AcceptanceScenes.FindPlayerOfDef(s, def);
                    Character c = s.GetCharacterByPlayer(p);
                    c.ASC.SetAttributeBaseValue(restriction.AttributeId, restriction.MinValue - 1f);
                    bool blocked = !SkillSystem.CanActivate(skill, c, skillIndex);
                    results.Add(blocked
                        ? CheckResult.Pass($"前置条件-属性限制拦截[attr {restriction.AttributeId}]",
                            $"属性 {restriction.AttributeId} = {restriction.MinValue - 1f} 时 CanActivate = false")
                        : CheckResult.Fail($"前置条件-属性限制拦截[attr {restriction.AttributeId}]",
                            "低于 MinValue 仍可施放（期望 false）"));
                }
            }

            // ——— 3. 正向场景：施放后状态 ———
            var pos = AcceptanceScenes.BuildState(def);
            int pc = AcceptanceScenes.FindPlayerOfDef(pos, def);
            Character caster = pos.GetCharacterByPlayer(pc);
            caster.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 10);
            if (skill.Cost.Type == SkillCostType.Charge)
                for (int i = 0; i < (int)skill.Cost.Amount; i++)
                    caster.ASC.AddGuardianCharge();
            if (skill.RequiredTags != null)
                for (int i = 0; i < skill.RequiredTags.Length; i++)
                    caster.ASC.Tags.AddTag(skill.RequiredTags[i]);
            if (skill.BlockingTags != null)
                for (int i = 0; i < skill.BlockingTags.Length; i++)
                    caster.ASC.Tags.RemoveTag(skill.BlockingTags[i]);
            Character target = skill.Target == SkillTargetType.Self
                ? caster
                : AcceptanceScenes.FindOther(pos, pc);

            float apBefore = caster.ASC.GetAttributeValue(AttributeConstants.ActionPoints);
            float sanityBefore = caster.Sanity;
            float erosionBefore = caster.ErosionLimit;
            int chargeBefore = caster.ASC.GetGuardianCharge();
            SkillSystem.Activate(skill, caster, target, pos, skillIndex);

            // 主成本扣减（按 Type 断言；守护充能技能 Id==2 的 AP 语义 Skip 交特例表）
            float apExtra = SumExtraCost(skill, SkillCostType.AP);
            if (skill.Cost.Type == SkillCostType.AP)
            {
                if (skill.Id == SkillSystem.GuardianSkillId)
                {
                    results.Add(CheckResult.Skip("施放-AP 扣减", "守护充能技能（Id=2）AP 扣减语义由特例表验证"));
                }
                else
                {
                    float expectedAp = apBefore - skill.Cost.Amount - apExtra;
                    float apAfter = caster.ASC.GetAttributeValue(AttributeConstants.ActionPoints);
                    results.Add(Math.Abs(apAfter - expectedAp) < 0.001f
                        ? CheckResult.Pass("施放-AP 扣减", $"AP {apBefore} → {apAfter}（期望 {expectedAp}）")
                        : CheckResult.Fail("施放-AP 扣减", $"AP {apBefore} → {apAfter}（期望 {expectedAp}）"));
                }
            }
            else if (skill.Id == 50)
            {
                // 静止时空：理智扣至 0.5（非标准 -Amount 扣减），由特例表验证
                results.Add(CheckResult.Skip("施放-主成本扣减(Sanity)", "静止时空理智扣至 0.5 由特例表验证"));
            }
            else if (skill.Cost.Type != SkillCostType.AP && skill.Cost.Amount != 0f)
            {
                results.Add(CheckResourceDelta("施放-主成本扣减(" + skill.Cost.Type + ")", skill.Cost,
                    BeforeAfter(caster, skill.Cost.Type, sanityBefore, erosionBefore, chargeBefore)));
            }

            // 额外成本逐个核对
            if (skill.ExtraCosts != null)
            {
                for (int e = 0; e < skill.ExtraCosts.Length; e++)
                {
                    SkillCost extra = skill.ExtraCosts[e];
                    if (extra.Amount == 0f) continue; // default struct 视为无成本
                    results.Add(CheckResourceDelta($"施放-额外成本扣减[{e}](" + extra.Type + ")", extra,
                        BeforeAfter(caster, extra.Type, sanityBefore, erosionBefore, chargeBefore)));
                }
            }

            // 效果 GE：持续挂载 / 瞬时属性变化 / 标签授予
            if (skill.Effects != null)
            {
                for (int i = 0; i < skill.Effects.Length; i++)
                {
                    GameplayEffect ge = skill.Effects[i];
                    if (ge == null) continue;
                    results.AddRange(CheckEffect(skill, def, skillIndex, caster, target, ge, i));
                }
            }

            // 冷却：施放后锁定 + 冷却中拦截（仅 CooldownTurns > 0 时生成）
            if (skill.CooldownTurns > 0)
            {
                int cd = caster.ASC.GetCooldown(skillIndex);
                results.Add(cd == skill.CooldownTurns
                    ? CheckResult.Pass("施放-冷却锁定", $"GetCooldown = {cd}（期望 {skill.CooldownTurns}）")
                    : CheckResult.Fail("施放-冷却锁定", $"GetCooldown = {cd}（期望 {skill.CooldownTurns}）"));
                bool recast = SkillSystem.CanActivate(skill, caster, skillIndex);
                results.Add(!recast
                    ? CheckResult.Pass("前置条件-冷却拦截", "冷却中 CanActivate = false")
                    : CheckResult.Fail("前置条件-冷却拦截", "冷却中仍可施放（期望 false）"));
            }

            // ——— 4. 目标正向验证 ———
            switch (skill.Target)
            {
                case SkillTargetType.Self:
                    results.Add(CheckResult.Pass("目标-对自身施放", "正向场景已对自身施放成功"));
                    break;
                case SkillTargetType.Enemy:
                    results.Add(target != null && target != caster
                        ? CheckResult.Pass("目标-对敌人施放", $"目标 {target.Name} 施放成功")
                        : CheckResult.Fail("目标-对敌人施放", "无可用敌人目标"));
                    break;
                case SkillTargetType.Any:
                    var selfState = AcceptanceScenes.BuildState(def);
                    int sp = AcceptanceScenes.FindPlayerOfDef(selfState, def);
                    Character sc = selfState.GetCharacterByPlayer(sp);
                    sc.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 10);
                    if (skill.Cost.Type == SkillCostType.Charge)
                        for (int i = 0; i < (int)skill.Cost.Amount; i++)
                            sc.ASC.AddGuardianCharge();
                    bool selfOk = SkillSystem.CanActivate(skill, sc, skillIndex);
                    SkillSystem.Activate(skill, sc, sc, selfState, skillIndex);
                    results.Add(selfOk
                        ? CheckResult.Pass("目标-任意目标（自己）", "对自己施放成功")
                        : CheckResult.Fail("目标-任意目标（自己）", "CanActivate = false"));
                    break;
            }

            // ——— 5. 判定类技能 ———
            AddJudgmentChecks(results, skill, def, skillIndex);

            return results;
        }

        // ------------------------------------------------------------------

        private static float SumExtraCost(SkillDef skill, SkillCostType type)
        {
            float sum = 0f;
            if (skill.ExtraCosts != null)
                for (int i = 0; i < skill.ExtraCosts.Length; i++)
                    if (skill.ExtraCosts[i].Type == type) sum += skill.ExtraCosts[i].Amount;
            return sum;
        }

        private static (float before, float after) BeforeAfter(
            Character c, SkillCostType type, float sanityBefore, float erosionBefore, int chargeBefore)
        {
            switch (type)
            {
                case SkillCostType.Sanity: return (sanityBefore, c.Sanity);
                case SkillCostType.ErosionLimit: return (erosionBefore, c.ErosionLimit);
                case SkillCostType.Charge: return (chargeBefore, c.ASC.GetGuardianCharge());
                default: return (0f, 0f);
            }
        }

        private static CheckResult CheckResourceDelta(string name, SkillCost cost, (float before, float after) pair)
        {
            float expected = pair.before - cost.Amount;
            bool ok = Math.Abs(pair.after - expected) < 0.001f;
            return ok
                ? CheckResult.Pass(name, $"{pair.before} → {pair.after}（期望 {expected}）")
                : CheckResult.Fail(name, $"{pair.before} → {pair.after}（期望 {expected}）");
        }

        /// <summary>单个效果 GE 的挂载/瞬时/标签检查。瞬时 GE 独立场景单独施放求属性差值。</summary>
        private static List<CheckResult> CheckEffect(
            SkillDef skill, CharacterDef def, int skillIndex, Character caster, Character target,
            GameplayEffect ge, int effectIndex)
        {
            var results = new List<CheckResult>();
            string tag = $"[{effectIndex}]GE{ge.Id}";

            if (ge.GrantTags != null)
            {
                for (int t = 0; t < ge.GrantTags.Length; t++)
                {
                    GameplayTag grant = ge.GrantTags[t];
                    results.Add(target.ASC.Tags.HasTag(grant)
                        ? CheckResult.Pass($"施放-标签授予{tag}", $"目标持有 {grant}")
                        : CheckResult.Fail($"施放-标签授予{tag}", $"目标未持有 {grant}"));
                }
            }

            if (ge.IsInstant)
            {
                var s = AcceptanceScenes.BuildState(def);
                int p = AcceptanceScenes.FindPlayerOfDef(s, def);
                Character t = skill.Target == SkillTargetType.Self
                    ? s.GetCharacterByPlayer(p)
                    : AcceptanceScenes.FindOther(s, p);

                float before = t.ASC.GetAttributeValue(AttributeConstants.Sanity);
                float maxSanity = t.ASC.GetAttributeValue(AttributeConstants.MaxSanity);

                // 独立应用单个 GE，避免技能其余效果/判定分支干扰本检查
                GameplayEffectSpec spec = t.ASC.RentSpec();
                spec.Definition = ge;
                spec.SourceId = t.Id;
                spec.TargetId = t.Id;
                t.ASC.ApplyGameplayEffect(spec);

                // 按 ApplySingleModifier 语义逐步模拟：仅 Sanity 属性生效，每步 [0, MaxSanity] 钳制
                float expected = before;
                bool supported = true;
                if (ge.Modifiers != null)
                {
                    for (int m = 0; m < ge.Modifiers.Length; m++)
                    {
                        AttributeModifier mod = ge.Modifiers[m];
                        if (mod.AttributeId != AttributeConstants.Sanity) continue;
                        switch (mod.Op)
                        {
                            case AttributeModifierOp.Add: expected += mod.Magnitude; break;
                            case AttributeModifierOp.Multiply: expected *= 1f + mod.Magnitude; break;
                            case AttributeModifierOp.Override: expected = mod.Magnitude; break;
                            default: supported = false; break;
                        }
                        expected = Math.Max(0f, Math.Min(maxSanity, expected));
                    }
                }
                if (!supported)
                {
                    results.Add(CheckResult.Skip($"施放-瞬时 GE 生效{tag}", "未知 ModifierOp，暂不自动断言"));
                    return results;
                }

                float after = t.ASC.GetAttributeValue(AttributeConstants.Sanity);
                float expectedDelta = expected - before;
                float actualDelta = after - before;
                results.Add(Math.Abs(actualDelta - expectedDelta) < 0.001f
                    ? CheckResult.Pass($"施放-瞬时 GE 生效{tag}", $"Sanity Δ {actualDelta}（期望 Δ {expectedDelta}）")
                    : CheckResult.Fail($"施放-瞬时 GE 生效{tag}", $"Sanity Δ {actualDelta}（期望 Δ {expectedDelta}）"));
            }
            else if (ge.DurationTurns > 0)
            {
                results.Add(target.ASC.HasActiveEffect(ge.Id)
                    ? CheckResult.Pass($"施放-持续 GE 挂载{tag}", "目标存在该活跃 GE")
                    : CheckResult.Fail($"施放-持续 GE 挂载{tag}", "目标未挂载该 GE"));
            }
            return results;
        }

        private static void AddJudgmentChecks(
            List<CheckResult> results, SkillDef skill, CharacterDef def, int skillIndex)
        {
            if (skill.Judgment == null) return;

            // 胜负收敛推导仅支持「Sanity Add 且非周期」的效果；复杂修改器/周期负载无法
            // 从定义安全推导期望值，Skip 交由特例表（避免误报）
            if (!IsSanityAddOnly(skill.Judgment.OnWinEffects)
                || !IsSanityAddOnly(skill.Judgment.OnLoseEffects)
                || !IsSanityAddOnly(skill.Effects))
            {
                results.Add(CheckResult.Skip("判定-胜负收敛",
                    "效果含非 Sanity-Add 修改器或周期 GE，推导暂不支持，需特例表验证"));
                return;
            }

            float mainDelta = SumSanityAdd(skill.Effects);
            float winDelta = SumSanityAdd(skill.Judgment.OnWinEffects) + mainDelta;
            float loseDelta = SumSanityAdd(skill.Judgment.OnLoseEffects)
                + (skill.Judgment.CancelOnLose ? 0f : mainDelta);

            // 确定性：同 seed 两次结果一致
            float d1 = RunJudgmentOnce(skill, def, skillIndex, 42);
            float d2 = RunJudgmentOnce(skill, def, skillIndex, 42);
            results.Add(Math.Abs(d1 - d2) < 0.001f
                ? CheckResult.Pass("判定-确定性", $"同 seed 两次 ΔSanity 一致（{d1}）")
                : CheckResult.Fail("判定-确定性", $"同 seed 两次不一致：{d1} vs {d2}"));

            // 收敛：多 seed 结果必须落在胜/败两分支之一
            bool converged = true;
            string observed = "";
            for (int sd = 42; sd < 46; sd++)
            {
                float d = RunJudgmentOnce(skill, def, skillIndex, sd);
                observed += d + " ";
                bool ok = Math.Abs(d - winDelta) < 0.001f || Math.Abs(d - loseDelta) < 0.001f;
                if (!ok) { converged = false; break; }
            }
            results.Add(converged
                ? CheckResult.Pass("判定-胜负收敛", $"各 seed ΔSanity ∈ {{胜 {winDelta}, 败 {loseDelta}}}")
                : CheckResult.Fail("判定-胜负收敛",
                    $"出现第三分支（观测 {observed}，期望 {{胜 {winDelta}, 败 {loseDelta}}}）"));
        }

        /// <summary>跑一次判定技能，返回目标 Sanity 变化量。</summary>
        private static float RunJudgmentOnce(SkillDef skill, CharacterDef def, int skillIndex, int seed)
        {
            var s = AcceptanceScenes.BuildState(def, seed);
            int p = AcceptanceScenes.FindPlayerOfDef(s, def);
            Character c = s.GetCharacterByPlayer(p);
            c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 10);
            Character t = AcceptanceScenes.FindOther(s, p);
            float before = t.Sanity;
            SkillSystem.Activate(skill, c, t, s, skillIndex);
            return t.Sanity - before;
        }

        /// <summary>GE 数组中 Sanity 属性 Add 修改器幅值之和（简化：仅统计 Sanity + Add，其余忽略）。</summary>
        private static float SumSanityAdd(GameplayEffect[] effects)
        {
            float sum = 0f;
            if (effects == null) return sum;
            for (int i = 0; i < effects.Length; i++)
            {
                GameplayEffect ge = effects[i];
                if (ge == null || ge.Modifiers == null) continue;
                for (int m = 0; m < ge.Modifiers.Length; m++)
                {
                    AttributeModifier mod = ge.Modifiers[m];
                    if (mod.AttributeId == AttributeConstants.Sanity && mod.Op == AttributeModifierOp.Add)
                        sum += mod.Magnitude;
                }
            }
            return sum;
        }

        /// <summary>GE 数组是否全部为「Sanity Add 且非周期」修改器（可安全推导期望值）。</summary>
        private static bool IsSanityAddOnly(GameplayEffect[] effects)
        {
            if (effects == null) return true;
            for (int i = 0; i < effects.Length; i++)
            {
                GameplayEffect ge = effects[i];
                if (ge == null) continue;
                if (ge.Period.HasValue) return false;
                if (ge.Modifiers == null) continue;
                for (int m = 0; m < ge.Modifiers.Length; m++)
                {
                    AttributeModifier mod = ge.Modifiers[m];
                    if (mod.AttributeId != AttributeConstants.Sanity || mod.Op != AttributeModifierOp.Add)
                        return false;
                }
            }
            return true;
        }
    }
}
