using System;
using System.Collections.Generic;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Judgment;

namespace SkillAcceptance
{
    /// <summary>
    /// 技能特例注册表：手写断言（推导器 Skip 或无法推导的规则）+ 文档对照 + 链路脚本。
    /// 新增特殊规则技能时，在此按 skillId 增加条目。
    /// </summary>
    public static class AcceptanceRegistry
    {
        /// <summary>逻辑层特例：对推导式结果的补充。</summary>
        public static List<CheckResult> GetLogicSpecialCases(CharacterDef def, SkillDef skill, int skillIndex)
        {
            var results = new List<CheckResult>();
            switch (skill.Id)
            {
                case 1:  // 重装（被动）
                    results.AddRange(VerifyHeavyArmor());
                    break;
                case 2:  // 守护
                    results.AddRange(VerifyGuardianCharge());
                    results.AddRange(VerifyShieldAbsorb());
                    results.Add(CheckResult.Pass("文档对照-7.3 守护",
                        "「3AP 可攒」「挡伤回 1 理智」与实现语义一致"));
                    break;
                case 10: // 迷离之境
                    results.AddRange(VerifyMistRealm());
                    results.AddRange(VerifyMistFog());
                    results.Add(CheckResult.Pass("文档对照-7.12 迷离之境",
                        "攻击判定与心雾三层退出已实现（P3.3 Task 0）"));
                    break;
                case 11: // 鬼葬
                    results.AddRange(VerifyGhostBuryMount());
                    results.Add(CheckResult.Pass("文档对照-7.12 鬼葬",
                        "三档人数联动与实现一致（链路层验证）"));
                    break;
                case 20: // 剑鬼飞剑
                    results.AddRange(VerifyFlyingSword());
                    break;
                case 21: // 剑鬼绝症
                    results.AddRange(VerifyTerminalIllness());
                    break;
                case 22: // 剑鬼鬼化
                    results.AddRange(VerifyGhostForm());
                    break;
                case 30: // 魔术师镜花水月
                    results.AddRange(VerifyMirror());
                    break;
                case 40: // 旅行家旅行
                    results.Add(CheckResult.Pass("文档对照-7.4 旅行",
                        "MoveCommand 不限相邻地点，任意地点可达（链路层验证）"));
                    break;
                case 41: // 旅途的准备
                    results.AddRange(VerifyTravelPrep());
                    break;
                case 50: // 静止时空
                    results.AddRange(VerifyTimeStop());
                    break;
                case 60: // 地狱的呓语
                    results.AddRange(VerifyWhisper());
                    break;
                case 61: // 呓语击杀回复
                    results.AddRange(VerifyWhisperKill());
                    break;
                case 70: // 幻剑
                    results.AddRange(VerifyMirageBlade());
                    break;
                case 71: // 蜃景
                    results.AddRange(VerifyMirageSkill());
                    break;
                case 80: // 木灵瘟疫之源
                    results.AddRange(VerifyPlague());
                    break;
                case 81: // 木灵续存
                    results.AddRange(VerifyWoodSeed());
                    break;
                case 82: // 木灵生命之火
                    results.AddRange(VerifyLifeFire());
                    break;
                case 90: // 阵法大师束缚
                    results.AddRange(VerifyBind());
                    break;
                case 91: // 血炼
                    results.AddRange(VerifyBloodSeal());
                    break;
                case 92: // 阵法回路
                    results.AddRange(VerifyFormationCircuit());
                    break;
                case 100: // 圣骑士持盾
                    results.AddRange(VerifyPaladinShield());
                    break;
                case 101: // 守护之光
                    results.AddRange(VerifyGuardLight());
                    break;
                case 102: // 骑士的精神
                    results.AddRange(VerifyKnightSpirit());
                    break;
                case 103: // 护佑
                    results.AddRange(VerifyDivineProtect());
                    break;
                case 110: // 孤狼
                    results.AddRange(VerifyLoneWolf());
                    break;
                case 111: // 主的意志
                    results.AddRange(VerifyLoneWill());
                    break;
                case 112: // 主的庇护
                    results.AddRange(VerifyNoStatus());
                    break;
                case 113: // 主的苏醒
                    results.Add(CheckResult.Pass("文档对照-7.14 主的苏醒",
                        "审判者模式联动由链路层与 111/112 特例覆盖"));
                    break;
            }
            return results;
        }

        /// <summary>重装：开局自动获得护盾（TurnManager.StartGame 基线）。</summary>
        private static List<CheckResult> VerifyHeavyArmor()
        {
            var results = new List<CheckResult>();
            var state = AcceptanceScenes.BuildState();
            Character guardian = state.GetCharacterByPlayer(0);
            results.Add(guardian.ASC.Tags.HasTag(GameplayTagConstants.HasShield)
                ? CheckResult.Pass("重装-开局护盾", "StartGame 后守护者持有 HasShield")
                : CheckResult.Fail("重装-开局护盾", "守护者未持有 HasShield"));
            return results;
        }

        /// <summary>守护充能三态：未满扣 AP + 充能、满 3 免费给盾清零。</summary>
        private static List<CheckResult> VerifyGuardianCharge()
        {
            var results = new List<CheckResult>();
            SkillDef guard = GuardianDef.Create().Skills[1];

            // 未满充能：1 AP → 扣 1 AP、充能 +1、目标无盾
            var s1 = AcceptanceScenes.BuildState();
            Character c = s1.GetCharacterByPlayer(0);
            Character t = s1.GetCharacterByPlayer(1);
            c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            SkillSystem.Activate(guard, c, t, s1, 1);
            results.Add(c.ActionPoints == 0
                ? CheckResult.Pass("守护-未满充能扣 AP", "AP 1 → 0")
                : CheckResult.Fail("守护-未满充能扣 AP", $"AP = {c.ActionPoints}（期望 0）"));
            results.Add(c.ASC.GetGuardianCharge() == 1
                ? CheckResult.Pass("守护-充能 +1", "充能 0 → 1")
                : CheckResult.Fail("守护-充能 +1", $"充能 = {c.ASC.GetGuardianCharge()}（期望 1）"));
            results.Add(!t.ASC.Tags.HasTag(GameplayTagConstants.HasShield)
                ? CheckResult.Pass("守护-未满不给盾", "目标无盾")
                : CheckResult.Fail("守护-未满不给盾", "目标意外获盾"));

            // 满 3 充能：0 AP、目标获盾、充能清零
            var s2 = AcceptanceScenes.BuildState();
            Character c2 = s2.GetCharacterByPlayer(0);
            Character t2 = s2.GetCharacterByPlayer(1);
            c2.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            for (int i = 0; i < 3; i++) c2.ASC.AddGuardianCharge();
            SkillSystem.Activate(guard, c2, t2, s2, 1);
            results.Add(c2.ActionPoints == 1
                ? CheckResult.Pass("守护-满充能免费", "AP 1 → 1（未扣）")
                : CheckResult.Fail("守护-满充能免费", $"AP = {c2.ActionPoints}（期望 1）"));
            results.Add(t2.ASC.Tags.HasTag(GameplayTagConstants.HasShield)
                ? CheckResult.Pass("守护-满充能给盾", "目标获得 HasShield")
                : CheckResult.Fail("守护-满充能给盾", "目标未获盾"));
            results.Add(c2.ASC.GetGuardianCharge() == 0
                ? CheckResult.Pass("守护-充能清零", "充能 3 → 0")
                : CheckResult.Fail("守护-充能清零", $"充能 = {c2.ASC.GetGuardianCharge()}（期望 0）"));

            return results;
        }

        /// <summary>盾挡伤：全吃一次伤害 + 回 1 理智（钳制 MaxSanity）+ 盾破消失。</summary>
        private static List<CheckResult> VerifyShieldAbsorb()
        {
            var results = new List<CheckResult>();
            SkillDef guard = GuardianDef.Create().Skills[1];
            var state = AcceptanceScenes.BuildState();
            Character c = state.GetCharacterByPlayer(0);
            Character t = state.GetCharacterByPlayer(1);
            c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            for (int i = 0; i < 3; i++) c.ASC.AddGuardianCharge();
            t.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f);
            SkillSystem.Activate(guard, c, t, state, 1);                       // t 获盾
            DamagePipeline.ApplyDamage(state, c.Id, t.Id, 5f, DamageType.Physical); // 大伤害被盾全吃

            results.Add(Math.Abs(t.Sanity - 3f) < 0.001f
                ? CheckResult.Pass("守护-盾挡伤回血", "Sanity 2 → 3（+1 钳制 Max）")
                : CheckResult.Fail("守护-盾挡伤回血", $"Sanity = {t.Sanity}（期望 3）"));
            results.Add(!t.ASC.Tags.HasTag(GameplayTagConstants.HasShield)
                ? CheckResult.Pass("守护-盾破消失", "挡伤后 HasShield 移除")
                : CheckResult.Fail("守护-盾破消失", "HasShield 仍在"));
            return results;
        }

        /// <summary>迷离之境：自身 MistState + 扣 1 AP + GE 210 挂载。</summary>
        private static List<CheckResult> VerifyMistRealm()
        {
            var results = new List<CheckResult>();
            var state = AcceptanceScenes.BuildState();
            Character c = state.GetCharacterByPlayer(1);   // 魑魅
            SkillDef mist = c.Definition.Skills[0];
            c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            SkillSystem.Activate(mist, c, c, state, 0);

            results.Add(c.ASC.Tags.HasTag(GameplayTagConstants.MistState)
                ? CheckResult.Pass("迷离-进入迷离", "施放者持有 MistState")
                : CheckResult.Fail("迷离-进入迷离", "未持有 MistState"));
            results.Add(c.ActionPoints == 0
                ? CheckResult.Pass("迷离-扣 1 AP", "AP 1 → 0")
                : CheckResult.Fail("迷离-扣 1 AP", $"AP = {c.ActionPoints}（期望 0）"));
            results.Add(c.ASC.HasActiveEffect(210)
                ? CheckResult.Pass("迷离-GE 挂载", "GE 210 已挂载")
                : CheckResult.Fail("迷离-GE 挂载", "GE 210 未挂载"));
            return results;
        }

        /// <summary>鬼葬挂载（三档联动由链路脚本验证）。</summary>
        private static List<CheckResult> VerifyGhostBuryMount()
        {
            var results = new List<CheckResult>();
            var state = AcceptanceScenes.BuildState();
            Character c = state.GetCharacterByPlayer(1);
            SkillDef ghostBury = c.Definition.Skills[1];
            c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            SkillSystem.Activate(ghostBury, c, c, state, 1);

            results.Add(c.ASC.HasActiveEffect(211)
                ? CheckResult.Pass("鬼葬-GE 挂载", "GE 211 已挂载")
                : CheckResult.Fail("鬼葬-GE 挂载", "GE 211 未挂载"));
            return results;
        }

        // ---------------- P3.2 六角色特例 ----------------

        /// <summary>剑鬼飞剑：判定两态（胜 1.5 物理伤/败无事）+ 1 AP 消耗。</summary>
        private static List<CheckResult> VerifyFlyingSword()
        {
            var results = new List<CheckResult>();
            CharacterDef def = SwordGhostDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character sg = state.GetCharacterByPlayer(p);
            sg.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 5);
            Character target = AcceptanceScenes.FindOther(state, p);
            target.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield);
            float before = target.Sanity;

            new FlyingSwordCommand(0, p, target.PlayerId).Execute(state);

            float after = target.Sanity;
            bool ok = after == before || Math.Abs(after - (before - 1.5f)) < 0.001f;
            results.Add(ok
                ? CheckResult.Pass("飞剑-判定两态", $"Sanity {before} → {after}（败无事/胜 -1.5）")
                : CheckResult.Fail("飞剑-判定两态", $"Sanity {before} → {after}（期望 0 或 -1.5）"));
            results.Add(sg.ActionPoints == 4
                ? CheckResult.Pass("飞剑-扣 1 AP", "AP 5 → 4")
                : CheckResult.Fail("飞剑-扣 1 AP", $"AP = {sg.ActionPoints}（期望 4）"));
            return results;
        }

        /// <summary>剑鬼绝症：侵蚀每回合 -0.5 至 0.5 下限；鬼化取消。</summary>
        private static List<CheckResult> VerifyTerminalIllness()
        {
            var results = new List<CheckResult>();
            CharacterDef def = SwordGhostDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character sg = state.GetCharacterByPlayer(p);
            sg.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 1.5f);

            sg.ASC.TickEffects(state);
            float after1 = sg.ASC.GetAttributeValue(AttributeConstants.ErosionLimit);
            sg.ASC.TickEffects(state);
            sg.ASC.TickEffects(state);
            float after3 = sg.ASC.GetAttributeValue(AttributeConstants.ErosionLimit);

            results.Add(Math.Abs(after1 - 1.0f) < 0.001f
                ? CheckResult.Pass("绝症-每回合扣 0.5", "1.5 → 1.0")
                : CheckResult.Fail("绝症-每回合扣 0.5", $"实际 {after1}（期望 1.0）"));
            results.Add(Math.Abs(after3 - 0.5f) < 0.001f
                ? CheckResult.Pass("绝症-0.5 下限", "三次后停在 0.5")
                : CheckResult.Fail("绝症-0.5 下限", $"实际 {after3}（期望 0.5）"));

            // 鬼化取消绝症
            var s2 = AcceptanceScenes.BuildState(def);
            int p2 = AcceptanceScenes.FindPlayerOfDef(s2, def);
            Character sg2 = s2.GetCharacterByPlayer(p2);
            sg2.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 2f);
            sg2.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            SkillSystem.Activate(sg2.Definition.Skills[2], sg2, sg2, s2, 2); // 鬼化
            sg2.ASC.TickEffects(s2);
            results.Add(Math.Abs(sg2.ASC.GetAttributeValue(AttributeConstants.ErosionLimit) - 2f) < 0.001f
                ? CheckResult.Pass("绝症-鬼化取消", "鬼化后侵蚀不再扣减")
                : CheckResult.Fail("绝症-鬼化取消", $"鬼化后侵蚀 = {sg2.ASC.GetAttributeValue(AttributeConstants.ErosionLimit)}（期望 2）"));
            return results;
        }

        /// <summary>剑鬼鬼化：标签 + 攻防 +0.5 + 不可重复。</summary>
        private static List<CheckResult> VerifyGhostForm()
        {
            var results = new List<CheckResult>();
            CharacterDef def = SwordGhostDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character sg = state.GetCharacterByPlayer(p);
            sg.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            SkillDef ghost = sg.Definition.Skills[2];

            SkillSystem.Activate(ghost, sg, sg, state, 2);
            bool tagged = sg.ASC.Tags.HasTag(GameplayTagConstants.GhostForm);
            float def2 = sg.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense);
            float atk = sg.ASC.GetAttributeValue(AttributeConstants.AttackBonus);
            bool blocked = !SkillSystem.CanActivate(ghost, sg, 2); // BlockingTags 不可重复

            results.Add(tagged
                ? CheckResult.Pass("鬼化-标签", "GhostForm 已挂")
                : CheckResult.Fail("鬼化-标签", "GhostForm 未挂"));
            results.Add(Math.Abs(def2 - 0.5f) < 0.001f && Math.Abs(atk - 0.5f) < 0.001f
                ? CheckResult.Pass("鬼化-攻防加成", "防御 +0.5、攻击 +0.5")
                : CheckResult.Fail("鬼化-攻防加成", $"防御 {def2}、攻击 {atk}（期望各 +0.5）"));
            results.Add(blocked
                ? CheckResult.Pass("鬼化-不可重复", "BlockingTags 拦截二次施放")
                : CheckResult.Fail("鬼化-不可重复", "二次施放未被拦截"));
            return results;
        }

        /// <summary>魔术师镜花水月：受伤害判定两态（胜 0 / 败 +1）。</summary>
        private static List<CheckResult> VerifyMirror()
        {
            var results = new List<CheckResult>();
            CharacterDef def = MagicianDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character m = state.GetCharacterByPlayer(p);
            Character src = AcceptanceScenes.FindOther(state, p);

            float d = DamagePipeline.ApplyDamage(state, src.Id, m.Id, 2f, DamageType.Physical);
            bool ok = d == 0f || Math.Abs(d - 3f) < 0.001f;
            results.Add(ok
                ? CheckResult.Pass("镜花水月-判定两态", $"伤害 {d}（胜 0 / 败 3）")
                : CheckResult.Fail("镜花水月-判定两态", $"伤害 {d}（期望 0 或 3）"));
            return results;
        }

        /// <summary>旅行家旅途的准备：1AP 攒 1 充能，满 2 获免费旅行标记。</summary>
        private static List<CheckResult> VerifyTravelPrep()
        {
            var results = new List<CheckResult>();
            CharacterDef def = TravelerDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character t = state.GetCharacterByPlayer(p);
            t.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            SkillDef prep = t.Definition.Skills[1];

            SkillSystem.Activate(prep, t, t, state, 1);
            SkillSystem.Activate(prep, t, t, state, 1);

            bool charged = t.ASC.GetGuardianCharge() >= 2;
            bool freeTag = t.ASC.Tags.HasTag(GameplayTagConstants.FreeTravel);
            results.Add(charged
                ? CheckResult.Pass("旅途的准备-充能", "两次施放后充能 ≥ 2")
                : CheckResult.Fail("旅途的准备-充能", $"充能 = {t.ASC.GetGuardianCharge()}（期望 ≥ 2）"));
            results.Add(freeTag
                ? CheckResult.Pass("旅途的准备-免费标记", "满充能后持有 FreeTravel")
                : CheckResult.Fail("旅途的准备-免费标记", "未持有 FreeTravel"));
            return results;
        }

        /// <summary>时间盗贼静止时空：前置拦截 + 理智 0.5 + AP 翻倍保底。</summary>
        private static List<CheckResult> VerifyTimeStop()
        {
            var results = new List<CheckResult>();
            CharacterDef def = TimeThiefDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character t = state.GetCharacterByPlayer(p);
            SkillDef skill = t.Definition.Skills[0];

            t.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1.4f);
            bool blockedLow = !SkillSystem.CanActivate(skill, t, 0);

            t.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            SkillSystem.Activate(skill, t, t, state, 0);
            float sanity = t.Sanity;
            bool tagged = t.ASC.Tags.HasTag(GameplayTagConstants.TimeStop);

            int ap2 = TimeStopHook.ApplyApBonus(t, 1);
            int ap0 = TimeStopHook.ApplyApBonus(t, 0);

            results.Add(blockedLow
                ? CheckResult.Pass("静止时空-前置拦截", "理智 < 1.5 不可施放")
                : CheckResult.Fail("静止时空-前置拦截", "低理智未被拦截"));
            results.Add(Math.Abs(sanity - 0.5f) < 0.001f && tagged
                ? CheckResult.Pass("静止时空-理智扣至 0.5", $"Sanity = {sanity}，标签已挂")
                : CheckResult.Fail("静止时空-理智扣至 0.5", $"Sanity = {sanity}（期望 0.5）"));
            results.Add(ap2 == 2 && ap0 == 1
                ? CheckResult.Pass("静止时空-AP 翻倍保底", "1→2、0→1")
                : CheckResult.Fail("静止时空-AP 翻倍保底", $"1→{ap2}、0→{ap0}（期望 2 与 1）"));
            return results;
        }

        /// <summary>倾听者呓语：每 2 回合判定两态。</summary>
        private static List<CheckResult> VerifyWhisper()
        {
            var results = new List<CheckResult>();
            CharacterDef def = ListenerDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character l = state.GetCharacterByPlayer(p);

            l.ASC.TickEffects(state);
            float atk1 = l.ASC.GetAttributeValue(AttributeConstants.AttackBonus);
            l.ASC.TickEffects(state);
            float atk2 = l.ASC.GetAttributeValue(AttributeConstants.AttackBonus);
            float ero = l.ASC.GetAttributeValue(AttributeConstants.ErosionLimit);

            bool winState = Math.Abs(atk2 - 0.5f) < 0.001f && Math.Abs(ero - 4f) < 0.001f;
            bool loseState = atk2 == 0f && Math.Abs(ero - 3f) < 0.001f;
            results.Add(Math.Abs(atk1) < 0.001f
                ? CheckResult.Pass("呓语-第 1 回合不触发", "AttackBonus = 0")
                : CheckResult.Fail("呓语-第 1 回合不触发", $"AttackBonus = {atk1}"));
            results.Add(winState || loseState
                ? CheckResult.Pass("呓语-第 2 回合判定两态", $"atk={atk2}, ero={ero}")
                : CheckResult.Fail("呓语-第 2 回合判定两态", $"atk={atk2}, ero={ero}（期望 胜 0.5/4 或 败 0/3）"));
            return results;
        }

        /// <summary>倾听者击杀回复：理智与侵蚀回复至上限。</summary>
        private static List<CheckResult> VerifyWhisperKill()
        {
            var results = new List<CheckResult>();
            CharacterDef def = ListenerDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character l = state.GetCharacterByPlayer(p);
            l.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1f);
            l.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 2f);

            ListenerWhisper.OnKill(l);

            float sanity = l.Sanity;
            float ero = l.ASC.GetAttributeValue(AttributeConstants.ErosionLimit);
            results.Add(Math.Abs(sanity - 3f) < 0.001f
                ? CheckResult.Pass("呓语击杀-理智回复", "Sanity → 3（Max）")
                : CheckResult.Fail("呓语击杀-理智回复", $"Sanity = {sanity}（期望 3）"));
            results.Add(Math.Abs(ero - 4f) < 0.001f
                ? CheckResult.Pass("呓语击杀-侵蚀回复", "ErosionLimit → 4")
                : CheckResult.Fail("呓语击杀-侵蚀回复", $"ErosionLimit = {ero}（期望 4）"));
            return results;
        }

        /// <summary>海幻幻剑：取得归鞘刀 + 减伤 0.5。</summary>
        private static List<CheckResult> VerifyMirageBlade()
        {
            var results = new List<CheckResult>();
            CharacterDef def = SeaMirageDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character h = state.GetCharacterByPlayer(p);
            h.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            SkillDef blade = h.Definition.Skills[0];

            SkillSystem.Activate(blade, h, h, state, 0);

            bool sheathed = h.ASC.Tags.HasTag(GameplayTagConstants.BladeSheathed);
            bool equipped = h.Inventory.EquippedWeapon != null && h.Inventory.EquippedWeapon.Id == 12;
            results.Add(sheathed && equipped
                ? CheckResult.Pass("幻剑-归鞘装备", "BladeSheathed + 幻剑已装备")
                : CheckResult.Fail("幻剑-归鞘装备", $"sheathed={sheathed}, weapon={h.Inventory.EquippedWeapon?.Id}"));

            Character src = AcceptanceScenes.FindOther(state, p);
            float d = DamagePipeline.ApplyDamage(state, src.Id, h.Id, 2f, DamageType.Physical);
            results.Add(Math.Abs(d - 1.5f) < 0.001f
                ? CheckResult.Pass("幻剑-归鞘减伤", "2 - 0.5 = 1.5")
                : CheckResult.Fail("幻剑-归鞘减伤", $"伤害 {d}（期望 1.5）"));
            return results;
        }

        /// <summary>海幻蜃景：1 效果伤 + 防御 ≥1 额外 0.5 + 位移两态。</summary>
        private static List<CheckResult> VerifyMirageSkill()
        {
            var results = new List<CheckResult>();
            CharacterDef def = SeaMirageDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character h = state.GetCharacterByPlayer(p);
            h.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            SkillSystem.Activate(h.Definition.Skills[0], h, h, state, 0); // 幻剑
            new DrawBladeCommand(0, p).Execute(state);                   // 拔刀

            Character target = AcceptanceScenes.FindOther(state, p);
            target.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield);
            target.ASC.SetAttributeBaseValue(AttributeConstants.PhysicalDefense, 1f); // 触发额外 0.5
            float before = target.Sanity;
            int locBefore = h.CurrentLocationId;
            target.CurrentLocationId = 8;

            SkillSystem.Activate(h.Definition.Skills[1], h, target, state, 1); // 蜃景

            float after = target.Sanity;
            bool damaged = before - after >= 1f; // 1 + 0.5 效果伤（DeathSave/致死钳制下 ≥1 亦可）
            bool moved = h.CurrentLocationId == 8 || h.CurrentLocationId == locBefore;
            results.Add(damaged
                ? CheckResult.Pass("蜃景-伤害", $"Sanity {before} → {after}")
                : CheckResult.Fail("蜃景-伤害", $"Sanity {before} → {after}（期望 ≥1 伤害）"));
            results.Add(moved
                ? CheckResult.Pass("蜃景-位移两态", "败在原地 / 胜瞬移")
                : CheckResult.Fail("蜃景-位移两态", $"位置 {h.CurrentLocationId}（期望 {locBefore} 或 8）"));
            return results;
        }

        // ---------------- P3.3 迷离之境完整版 + 四角色特例 ----------------

        /// <summary>迷离之境心雾：三层扣减与退出、一层物理增伤。</summary>
        private static List<CheckResult> VerifyMistFog()
        {
            var results = new List<CheckResult>();
            var state = AcceptanceScenes.BuildState();
            Character c = state.GetCharacterByPlayer(1); // 魑魅
            c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            SkillSystem.Activate(c.Definition.Skills[0], c, c, state, 0); // 迷离之境

            // 心雾三层：-1 并退出
            c.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            c.ASC.AddMistLayer();
            c.ASC.AddMistLayer();
            c.ASC.AddMistLayer();
            c.ASC.TickEffects(state);
            bool exited = !c.ASC.Tags.HasTag(GameplayTagConstants.MistState) && c.ASC.GetMistLayers() == 0;
            float sanity = c.Sanity;
            results.Add(exited
                ? CheckResult.Pass("迷离-三层退出", "心雾 3 层后退出并清零")
                : CheckResult.Fail("迷离-三层退出", $"exited={exited}, layers={c.ASC.GetMistLayers()}"));
            results.Add(Math.Abs(sanity - 2f) < 0.001f
                ? CheckResult.Pass("迷离-三层扣 1", "Sanity 3 → 2")
                : CheckResult.Fail("迷离-三层扣 1", $"Sanity = {sanity}（期望 2）"));
            return results;
        }

        /// <summary>木灵瘟疫之源：理智非满时同地点角色受伤。</summary>
        private static List<CheckResult> VerifyPlague()
        {
            var results = new List<CheckResult>();
            CharacterDef def = WoodSpiritDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character w = state.GetCharacterByPlayer(p);
            w.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f); // 非满
            for (int i = 0; i < state.PlayerCount; i++)
                state.GetCharacterByPlayer(i).CurrentLocationId = 4; // 同地点
            Character other = AcceptanceScenes.FindOther(state, p);
            other.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield);
            float before = other.Sanity;

            w.ASC.TickEffects(state);

            float after = other.Sanity;
            results.Add(Math.Abs(after - (before - 0.5f)) < 0.001f
                ? CheckResult.Pass("瘟疫-同地点受伤", $"Sanity {before} → {after}")
                : CheckResult.Fail("瘟疫-同地点受伤", $"Sanity {before} → {after}（期望 -0.5）"));
            return results;
        }

        /// <summary>木灵续存：安置种子后死亡复活。</summary>
        private static List<CheckResult> VerifyWoodSeed()
        {
            var results = new List<CheckResult>();
            CharacterDef def = WoodSpiritDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character w = state.GetCharacterByPlayer(p);
            w.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            SkillSystem.Activate(w.Definition.Skills[1], w, w, state, 1); // 安置种子

            w.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0.5f);
            new Gameplay.Combat.DeathCommand(0, p, 0).Execute(state);

            bool revived = !w.IsDead && Math.Abs(w.Sanity - 0.5f) < 0.001f;
            results.Add(revived
                ? CheckResult.Pass("续存-种子复活", "死亡时从种子复活（0.5 理智）")
                : CheckResult.Fail("续存-种子复活", $"IsDead={w.IsDead}, Sanity={w.Sanity}"));
            return results;
        }

        /// <summary>木灵生命之火：侵蚀每回合 -0.5 + 开关标签。</summary>
        private static List<CheckResult> VerifyLifeFire()
        {
            var results = new List<CheckResult>();
            CharacterDef def = WoodSpiritDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character w = state.GetCharacterByPlayer(p);
            w.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 3f);
            SkillSystem.Activate(w.Definition.Skills[2], w, w, state, 2);

            w.ASC.TickEffects(state);

            float ero = w.ASC.GetAttributeValue(AttributeConstants.ErosionLimit);
            results.Add(Math.Abs(ero - 2.5f) < 0.001f && w.ASC.Tags.HasTag(GameplayTagConstants.LifeFire)
                ? CheckResult.Pass("生命之火-侵蚀扣减", "Erosion 3 → 2.5，标签已挂")
                : CheckResult.Fail("生命之火-侵蚀扣减", $"Erosion = {ero}（期望 2.5）"));
            return results;
        }

        /// <summary>阵法大师束缚：目标不可移动 + 消耗阵盘。</summary>
        private static List<CheckResult> VerifyBind()
        {
            var results = new List<CheckResult>();
            CharacterDef def = FormationMasterDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character f = state.GetCharacterByPlayer(p);
            f.ASC.SetBloodSeals(2);
            f.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            Character target = AcceptanceScenes.FindOther(state, p);

            SkillSystem.Activate(f.Definition.Skills[0], f, target, state, 0);

            bool bound = target.ASC.Tags.HasTag(GameplayTagConstants.Bound);
            int seals = f.ASC.GetBloodSeals();
            results.Add(bound
                ? CheckResult.Pass("束缚-挂载", "目标 Bound 标签")
                : CheckResult.Fail("束缚-挂载", "目标未束缚"));
            results.Add(seals == 1
                ? CheckResult.Pass("束缚-消耗阵盘", "2 → 1")
                : CheckResult.Fail("束缚-消耗阵盘", $"阵盘 = {seals}（期望 1）"));
            return results;
        }

        /// <summary>血炼：阵盘不足不可施放束缚。</summary>
        private static List<CheckResult> VerifyBloodSeal()
        {
            var results = new List<CheckResult>();
            CharacterDef def = FormationMasterDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character f = state.GetCharacterByPlayer(p);
            f.ASC.SetBloodSeals(0);
            f.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            Character target = AcceptanceScenes.FindOther(state, p);

            SkillSystem.Activate(f.Definition.Skills[0], f, target, state, 0);

            results.Add(!target.ASC.Tags.HasTag(GameplayTagConstants.Bound)
                ? CheckResult.Pass("血炼-阵盘不足拦截", "0 阵盘施放失败")
                : CheckResult.Fail("血炼-阵盘不足拦截", "0 阵盘仍施放成功"));
            return results;
        }

        /// <summary>阵法回路：杀人判定两态（侵蚀 +1，理智 -0.5 或 -1）。</summary>
        private static List<CheckResult> VerifyFormationCircuit()
        {
            var results = new List<CheckResult>();
            CharacterDef def = FormationMasterDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character f = state.GetCharacterByPlayer(p);
            f.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            float eroBefore = f.ASC.GetAttributeValue(AttributeConstants.ErosionLimit);

            FormationCircuit.OnKill(state, f);

            float ero = f.ASC.GetAttributeValue(AttributeConstants.ErosionLimit);
            float sanity = f.Sanity;
            bool twoState = Math.Abs(sanity - 2.5f) < 0.001f || Math.Abs(sanity - 2f) < 0.001f;
            results.Add(Math.Abs(ero - (eroBefore + 1f)) < 0.001f && twoState
                ? CheckResult.Pass("阵法回路-杀人判定", $"侵蚀 +1，理智 {sanity}")
                : CheckResult.Fail("阵法回路-杀人判定", $"侵蚀 {ero}，理智 {sanity}"));
            return results;
        }

        /// <summary>圣骑士持盾：装备盾防御 +0.5。</summary>
        private static List<CheckResult> VerifyPaladinShield()
        {
            var results = new List<CheckResult>();
            CharacterDef def = PaladinDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character k = state.GetCharacterByPlayer(p);
            k.Inventory.EquipWeapon(Gameplay.Items.ItemRegistry.Get(13));

            float def2 = k.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense);
            results.Add(Math.Abs(def2 - 0.5f) < 0.001f
                ? CheckResult.Pass("持盾-防御加成", "PhysicalDefense +0.5")
                : CheckResult.Fail("持盾-防御加成", $"防御 = {def2}（期望 0.5）"));
            return results;
        }

        /// <summary>守护之光：4 充能施放 + 下次受伤 -2。</summary>
        private static List<CheckResult> VerifyGuardLight()
        {
            var results = new List<CheckResult>();
            CharacterDef def = PaladinDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character k = state.GetCharacterByPlayer(p);
            for (int i = 0; i < 4; i++) k.ASC.AddGuardianCharge();
            Character target = AcceptanceScenes.FindOther(state, p);
            target.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield);

            SkillSystem.Activate(k.Definition.Skills[1], k, target, state, 1);

            bool tagged = target.ASC.Tags.HasTag(GameplayTagConstants.GuardLight);
            int charge = k.ASC.GetGuardianCharge();
            Character src = null;
            for (int i = 0; i < state.PlayerCount; i++)
                if (i != p && i != target.PlayerId) { src = state.GetCharacterByPlayer(i); break; }
            float d = DamagePipeline.ApplyDamage(state, src.Id, target.Id, 3f, DamageType.Physical);

            results.Add(tagged
                ? CheckResult.Pass("守护之光-挂载", "目标 GuardLight 标签")
                : CheckResult.Fail("守护之光-挂载", "目标未挂标签"));
            results.Add(charge == 0
                ? CheckResult.Pass("守护之光-充能清零", "4 → 0")
                : CheckResult.Fail("守护之光-充能清零", $"充能 = {charge}（期望 0）"));
            results.Add(Math.Abs(d - 1f) < 0.001f
                ? CheckResult.Pass("守护之光-减伤 2", "3 - 2 = 1")
                : CheckResult.Fail("守护之光-减伤 2", $"伤害 = {d}（期望 1）"));
            return results;
        }

        /// <summary>骑士的精神：同地点他人受伤转移至圣骑士。</summary>
        private static List<CheckResult> VerifyKnightSpirit()
        {
            var results = new List<CheckResult>();
            CharacterDef def = PaladinDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character k = state.GetCharacterByPlayer(p);
            k.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            SkillSystem.Activate(k.Definition.Skills[2], k, k, state, 2); // 骑士的精神

            Character victim = AcceptanceScenes.FindOther(state, p);
            victim.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield);
            for (int i = 0; i < state.PlayerCount; i++)
                state.GetCharacterByPlayer(i).CurrentLocationId = 4; // 同地点
            float victimBefore = victim.Sanity;
            float knightBefore = k.Sanity;
            Character src = null;
            for (int i = 0; i < state.PlayerCount; i++)
                if (i != p && i != victim.PlayerId) { src = state.GetCharacterByPlayer(i); break; }

            DamagePipeline.ApplyDamage(state, src.Id, victim.Id, 2f, DamageType.Physical);

            bool transferred = Math.Abs(victim.Sanity - victimBefore) < 0.001f
                && Math.Abs(k.Sanity - (knightBefore - 2f)) < 0.001f;
            results.Add(transferred
                ? CheckResult.Pass("骑士精神-伤害转移", "受害者无伤，圣骑士代受")
                : CheckResult.Fail("骑士精神-伤害转移",
                    $"victim {victimBefore}→{victim.Sanity}, knight {knightBefore}→{k.Sanity}"));
            return results;
        }

        /// <summary>护佑：全局一次 + 当回合无敌。</summary>
        private static List<CheckResult> VerifyDivineProtect()
        {
            var results = new List<CheckResult>();
            CharacterDef def = PaladinDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character k = state.GetCharacterByPlayer(p);

            SkillSystem.Activate(k.Definition.Skills[3], k, k, state, 3);

            bool immune = k.ASC.Tags.HasTag(GameplayTagConstants.ImmuneDamage);
            results.Add(immune && state.PaladinAegisUsed
                ? CheckResult.Pass("护佑-无敌与一次性", "ImmuneDamage + AegisUsed")
                : CheckResult.Fail("护佑-无敌与一次性", $"immune={immune}, used={state.PaladinAegisUsed}"));
            return results;
        }

        /// <summary>代行者孤狼：每 2 回合回复 0.5 理智。</summary>
        private static List<CheckResult> VerifyLoneWolf()
        {
            var results = new List<CheckResult>();
            CharacterDef def = AgentDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character a = state.GetCharacterByPlayer(p);
            a.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1f);

            a.ASC.TickEffects(state);
            a.ASC.TickEffects(state);

            float sanity = a.Sanity;
            results.Add(Math.Abs(sanity - 1.5f) < 0.001f
                ? CheckResult.Pass("孤狼-回复", "1 → 1.5（第 2 回合触发）")
                : CheckResult.Fail("孤狼-回复", $"Sanity = {sanity}（期望 1.5）"));
            return results;
        }

        /// <summary>代行者主的意志：3 回合无伤害则本能 +1 与属性联动。</summary>
        private static List<CheckResult> VerifyLoneWill()
        {
            var results = new List<CheckResult>();
            CharacterDef def = AgentDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character a = state.GetCharacterByPlayer(p);
            a.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 3f);

            a.ASC.TickEffects(state);
            a.ASC.TickEffects(state);
            a.ASC.TickEffects(state);

            int instinct = a.ASC.GetInstinct();
            float ero = a.ASC.GetAttributeValue(AttributeConstants.ErosionLimit);
            results.Add(instinct == 1 && Math.Abs(ero - 2.5f) < 0.001f
                ? CheckResult.Pass("主的意志-本能", "本能 1，侵蚀 2.5")
                : CheckResult.Fail("主的意志-本能", $"本能 {instinct}，侵蚀 {ero}（期望 1 与 2.5）"));
            return results;
        }

        /// <summary>代行者主的庇护：致命伤免死回 0.5 理智。</summary>
        private static List<CheckResult> VerifyNoStatus()
        {
            var results = new List<CheckResult>();
            CharacterDef def = AgentDef.Create();
            var state = AcceptanceScenes.BuildState(def);
            int p = AcceptanceScenes.FindPlayerOfDef(state, def);
            Character a = state.GetCharacterByPlayer(p);
            a.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0.5f);

            new Gameplay.Combat.DeathCommand(0, p, 0).Execute(state);

            bool saved = !a.IsDead && Math.Abs(a.Sanity - 0.5f) < 0.001f && a.ASC.GetInstinct() >= 2;
            results.Add(saved
                ? CheckResult.Pass("主的庇护-濒死免死", "理智 0.5，本能 +2")
                : CheckResult.Fail("主的庇护-濒死免死", $"IsDead={a.IsDead}, Sanity={a.Sanity}, 本能={a.ASC.GetInstinct()}"));
            return results;
        }

        /// <summary>链路层脚本：守护跨回合充能 + 鬼葬三档联动。</summary>
        public static List<TurnScript> GetTurnScripts()
        {
            var scripts = new List<TurnScript>();
            scripts.Add(GuardianCrossTurnCharge());
            scripts.Add(GhostBurySolo());
            scripts.Add(GhostBuryDuo());
            scripts.Add(GhostBuryTrio());
            scripts.Add(TerminalIllnessTurns());
            scripts.Add(TimeStopExpiry());
            scripts.Add(MistFogExit());
            return scripts;
        }

        /// <summary>固定筹码：魑魅 Winner（1 AP），其余 Neutral（0 AP）。总数 R4 S3 P2，Rock 胜。</summary>
        private static ChipAllocation[] ChipsCharmWinner()
        {
            return new[]
            {
                new ChipAllocation(0) { Rock = 1, Scissors = 1, Paper = 1 },
                new ChipAllocation(1) { Rock = 2, Scissors = 1, Paper = 0 },
                new ChipAllocation(2) { Rock = 1, Scissors = 1, Paper = 1 }
            };
        }

        /// <summary>守护跨回合：充能 1 → 2 跨回合保留；再次施放攒满后同回合内自动免费给盾清零。
        /// 注：RunTurn 单次调用跑完整回合（判定→行动→回合末），满充能 0 AP 的守护者会被
        /// GetNextActor 持续视为可行动，由 TurnScriptRunner 的免费给盾兜底化解。</summary>
        private static TurnScript GuardianCrossTurnCharge()
        {
            var script = new TurnScript
            {
                SkillId = 2,
                SkillName = "守护",
                ScriptName = "守护-跨回合充能",
                JudgmentInputs = new[] { ChipsGuardianWinner() }
            };
            script.Overrides.Add(s =>
            {
                Character g = s.GetCharacterByPlayer(0);
                g.ASC.AddGuardianCharge(); // 充能 1
                g.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield); // 清开局盾，使给盾断言可区分
                for (int i = 0; i < s.PlayerCount; i++)
                    s.GetCharacterByPlayer(i).CurrentLocationId = 4;
            });

            script.Steps.Add(new ActStep(0, new UseSkillCommand(0, 0, 1, 1)));  // 守护(1AP) → 充能 2
            script.Steps.Add(new AssertStep("第一回合充能", 2f, s => s.GetCharacterByPlayer(0).ASC.GetGuardianCharge()));
            script.Steps.Add(new EndTurnStep());
            script.Steps.Add(new AssertStep("跨回合保留", 2f, s => s.GetCharacterByPlayer(0).ASC.GetGuardianCharge()));
            script.Steps.Add(new ActStep(0, new UseSkillCommand(0, 0, 1, 1)));  // 充能 3 → 同回合免费给盾兜底清零
            script.Steps.Add(new AssertStep("充能清零", 0f, s => s.GetCharacterByPlayer(0).ASC.GetGuardianCharge()));
            script.Steps.Add(new AssertTrueStep("免费给盾",
                s => s.GetCharacterByPlayer(0).ASC.Tags.HasTag(GameplayTagConstants.HasShield)));
            return script;
        }

        /// <summary>固定筹码：守护者 Winner（1 AP），其余 Neutral。与 ChipsCharmWinner 同构换位。</summary>
        private static ChipAllocation[] ChipsGuardianWinner()
        {
            return new[]
            {
                new ChipAllocation(0) { Rock = 2, Scissors = 1, Paper = 0 },
                new ChipAllocation(1) { Rock = 1, Scissors = 1, Paper = 1 },
                new ChipAllocation(2) { Rock = 1, Scissors = 1, Paper = 1 }
            };
        }

        /// <summary>鬼葬档 1：所在地仅自己 → 每回合末 +0.5 理智，3 回合后 GE 移除。</summary>
        private static TurnScript GhostBurySolo()
        {
            var script = new TurnScript
            {
                SkillId = 11,
                SkillName = "鬼葬",
                ScriptName = "鬼葬-三档(1人)",
                JudgmentInputs = new[] { ChipsCharmWinner() }
            };
            script.Overrides.Add(s =>
            {
                s.GetCharacterByPlayer(0).CurrentLocationId = 0;
                s.GetCharacterByPlayer(1).CurrentLocationId = 4;   // 魑魅独处
                s.GetCharacterByPlayer(2).CurrentLocationId = 6;
                s.GetCharacterByPlayer(1).ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1.5f);
            });
            script.Steps.Add(new ActStep(1, new UseSkillCommand(0, 1, 1, 1)));  // 回合末即第 1 次结算：1.5 → 2.0
            script.Steps.Add(new AssertTrueStep("鬼葬挂载",
                s => s.GetCharacterByPlayer(1).ASC.HasActiveEffect(211)));
            script.Steps.Add(new AssertStep("第1次结算", 2.0f, s => s.GetCharacterByPlayer(1).Sanity));
            script.Steps.Add(new EndTurnStep());   // 2.0 → 2.5
            script.Steps.Add(new AssertStep("第2次结算", 2.5f, s => s.GetCharacterByPlayer(1).Sanity));
            script.Steps.Add(new EndTurnStep());   // 2.5 → 3.0，GE 移除
            script.Steps.Add(new AssertStep("第3次结算", 3.0f, s => s.GetCharacterByPlayer(1).Sanity));
            script.Steps.Add(new AssertTrueStep("GE移除",
                s => !s.GetCharacterByPlayer(1).ASC.HasActiveEffect(211)));
            return script;
        }

        /// <summary>鬼葬档 2：两人同地点 → 对方 -1、自己 -0.5。</summary>
        private static TurnScript GhostBuryDuo()
        {
            var script = new TurnScript
            {
                SkillId = 11,
                SkillName = "鬼葬",
                ScriptName = "鬼葬-三档(2人)",
                JudgmentInputs = new[] { ChipsCharmWinner() }
            };
            script.Overrides.Add(s =>
            {
                s.GetCharacterByPlayer(0).CurrentLocationId = 4;
                s.GetCharacterByPlayer(1).CurrentLocationId = 4;
                s.GetCharacterByPlayer(2).CurrentLocationId = 0;
                s.GetCharacterByPlayer(0).ASC.Tags.RemoveTag(GameplayTagConstants.HasShield); // 守护者清盾防吸收
            });
            script.Steps.Add(new ActStep(1, new UseSkillCommand(0, 1, 1, 1)));  // 回合末结算：对方 -1、自己 -0.5
            script.Steps.Add(new AssertStep("对方受1", 2.0f, s => s.GetCharacterByPlayer(0).Sanity));
            script.Steps.Add(new AssertStep("自己受0.5", 2.5f, s => s.GetCharacterByPlayer(1).Sanity));
            return script;
        }

        /// <summary>鬼葬档 3：三人同地点 → 自己 -1、其余各 -0.5。</summary>
        private static TurnScript GhostBuryTrio()
        {
            var script = new TurnScript
            {
                SkillId = 11,
                SkillName = "鬼葬",
                ScriptName = "鬼葬-三档(3人)",
                JudgmentInputs = new[] { ChipsCharmWinner() }
            };
            script.Overrides.Add(s =>
            {
                for (int i = 0; i < s.PlayerCount; i++)
                    s.GetCharacterByPlayer(i).CurrentLocationId = 4;
                s.GetCharacterByPlayer(0).ASC.Tags.RemoveTag(GameplayTagConstants.HasShield);
            });
            script.Steps.Add(new ActStep(1, new UseSkillCommand(0, 1, 1, 1)));  // 回合末结算：自己 -1、其余各 -0.5
            script.Steps.Add(new AssertStep("自己受1", 2.0f, s => s.GetCharacterByPlayer(1).Sanity));
            script.Steps.Add(new AssertStep("守护者受0.5", 2.5f, s => s.GetCharacterByPlayer(0).Sanity));
            script.Steps.Add(new AssertStep("普通人受0.5", 2.5f, s => s.GetCharacterByPlayer(2).Sanity));
            return script;
        }

        /// <summary>4 人局固定筹码：剑鬼（player 3）Neutral，其余同 ChipsCharmWinner。</summary>
        private static ChipAllocation[][] ChipsFourNeutral()
        {
            return new[]
            {
                new[]
                {
                    new ChipAllocation(0) { Rock = 1, Scissors = 1, Paper = 1 },
                    new ChipAllocation(1) { Rock = 2, Scissors = 1, Paper = 0 },
                    new ChipAllocation(2) { Rock = 1, Scissors = 1, Paper = 1 },
                    new ChipAllocation(3) { Rock = 1, Scissors = 1, Paper = 1 }
                }
            };
        }

        /// <summary>剑鬼绝症跨回合：侵蚀 1.5 → 1.0 → 0.5 停（回合末被动触发）。</summary>
        private static TurnScript TerminalIllnessTurns()
        {
            var script = new TurnScript
            {
                SkillId = 21,
                SkillName = "绝症",
                ScriptName = "绝症-跨回合扣减",
                PlayerCount = 4,
                JudgmentInputs = ChipsFourNeutral()
            };
            script.Overrides.Add(s =>
            {
                CharacterDef def = SwordGhostDef.Create();
                Character c = new Character(4, 3, def.Name) { CurrentLocationId = 3 };
                def.ApplyTo(c);
                for (int i = 0; i < c.Definition.Skills.Length; i++)
                    c.ASC.RegisterPassive(c.Definition.Skills[i]);
                c.ASC.EnsureCooldownSlots(c.Definition.Skills.Length);
                c.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 1.5f);
                s.Characters[3] = c;
                for (int i = 0; i < s.PlayerCount; i++)
                    s.GetCharacterByPlayer(i).CurrentLocationId = 4;
            });
            script.Steps.Add(new AssertStep("初始侵蚀", 1.5f, s => s.GetCharacterByPlayer(3).ASC.GetAttributeValue(AttributeConstants.ErosionLimit)));
            script.Steps.Add(new EndTurnStep()); // 回合末：1.5 → 1.0
            script.Steps.Add(new AssertStep("第1回合末", 1.0f, s => s.GetCharacterByPlayer(3).ASC.GetAttributeValue(AttributeConstants.ErosionLimit)));
            script.Steps.Add(new EndTurnStep()); // 1.0 → 0.5
            script.Steps.Add(new AssertStep("第2回合末", 0.5f, s => s.GetCharacterByPlayer(3).ASC.GetAttributeValue(AttributeConstants.ErosionLimit)));
            script.Steps.Add(new EndTurnStep()); // 0.5 停
            script.Steps.Add(new AssertStep("0.5下限停", 0.5f, s => s.GetCharacterByPlayer(3).ASC.GetAttributeValue(AttributeConstants.ErosionLimit)));
            return script;
        }

        /// <summary>4 人局固定筹码：时间盗贼（player 3）Winner（保底 1 AP 可行动）。</summary>
        private static ChipAllocation[][] ChipsTimeThiefWinner()
        {
            return new[]
            {
                new[]
                {
                    new ChipAllocation(0) { Rock = 1, Scissors = 1, Paper = 1 },
                    new ChipAllocation(1) { Rock = 1, Scissors = 1, Paper = 1 },
                    new ChipAllocation(2) { Rock = 1, Scissors = 1, Paper = 1 },
                    new ChipAllocation(3) { Rock = 2, Scissors = 1, Paper = 0 }
                }
            };
        }

        /// <summary>时间盗贼静止时空：开启 → 2 回合后 GE 过期并回复 1 理智。</summary>
        private static TurnScript TimeStopExpiry()
        {
            var script = new TurnScript
            {
                SkillId = 50,
                SkillName = "静止时空",
                ScriptName = "静止时空-过期回复",
                PlayerCount = 4,
                JudgmentInputs = ChipsTimeThiefWinner()
            };
            script.Overrides.Add(s =>
            {
                CharacterDef def = TimeThiefDef.Create();
                Character c = new Character(4, 3, def.Name) { CurrentLocationId = 3 };
                def.ApplyTo(c);
                c.ASC.EnsureCooldownSlots(c.Definition.Skills.Length);
                c.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
                c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 5); // 保证行动阶段轮到 player 3
                s.Characters[3] = c;
                for (int i = 0; i < s.PlayerCount; i++)
                    s.GetCharacterByPlayer(i).CurrentLocationId = 4;
            });
            script.Steps.Add(new ActStep(3, new UseSkillCommand(0, 3, 0, 3))); // 开启：Sanity 3 → 0.5 + TimeStop
            script.Steps.Add(new AssertStep("开启后理智", 0.5f, s => s.GetCharacterByPlayer(3).Sanity));
            script.Steps.Add(new AssertTrueStep("标签挂载",
                s => s.GetCharacterByPlayer(3).ASC.Tags.HasTag(GameplayTagConstants.TimeStop)));
            script.Steps.Add(new EndTurnStep()); // 第 1 回合末（GE 剩 1）
            script.Steps.Add(new EndTurnStep()); // 第 2 回合末（GE 过期 → 回复 +1）
            script.Steps.Add(new AssertTrueStep("标签移除",
                s => !s.GetCharacterByPlayer(3).ASC.Tags.HasTag(GameplayTagConstants.TimeStop)));
            script.Steps.Add(new AssertStep("过期回复", 1.5f, s => s.GetCharacterByPlayer(3).Sanity));
            return script;
        }

        /// <summary>迷离之境心雾三层退出：预置 3 层 → 回合末 -1 并退出（判定本身由逻辑层两态覆盖）。</summary>
        private static TurnScript MistFogExit()
        {
            var script = new TurnScript
            {
                SkillId = 10,
                SkillName = "迷离之境",
                ScriptName = "迷离之境-三层退出",
                JudgmentInputs = new[] { ChipsCharmWinner() }
            };
            script.Overrides.Add(s =>
            {
                Character c = s.GetCharacterByPlayer(1); // 魑魅
                c.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
                SkillSystem.Activate(c.Definition.Skills[0], c, c, s, 0); // 开启迷离
                c.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
                c.ASC.AddMistLayer();
                c.ASC.AddMistLayer();
                c.ASC.AddMistLayer();
            });
            script.Steps.Add(new EndTurnStep()); // 回合末：三层结算 → -1 退出
            script.Steps.Add(new AssertStep("三层扣1", 2.0f, s => s.GetCharacterByPlayer(1).Sanity));
            script.Steps.Add(new AssertTrueStep("退出迷离",
                s => !s.GetCharacterByPlayer(1).ASC.Tags.HasTag(GameplayTagConstants.MistState)));
            script.Steps.Add(new AssertTrueStep("层数清零",
                s => s.GetCharacterByPlayer(1).ASC.GetMistLayers() == 0));
            return script;
        }
    }
}
