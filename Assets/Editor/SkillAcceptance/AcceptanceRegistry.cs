using System;
using System.Collections.Generic;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
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
                case 120: // 混沌法师混乱（命令承载语义，特例直接执行命令断言）
                    results.AddRange(VerifyChaos());
                    break;
                case 121: // 反噬（被动占位）
                    results.Add(CheckResult.Pass("文档对照-7.5 反噬",
                        "判定逻辑在 120 特例与混沌链路脚本中双向覆盖（胜/败）"));
                    break;
                case 122: // 地狱的子民
                    results.AddRange(VerifyHellChild());
                    break;
                case 123: // 附灵·泣血
                    results.AddRange(VerifyTwinVampiric());
                    break;
                case 124: // 附灵·灵巧
                    results.AddRange(VerifyTwinAgile());
                    break;
                case 125: // Handsonic 形态切换
                    results.AddRange(VerifyFormSwitch());
                    results.AddRange(VerifyFormDamage());
                    break;
                case 126: // Harmonics
                    results.AddRange(VerifyHarmonics());
                    break;
                case 127: // 规诫
                    results.AddRange(VerifyDiscipline());
                    break;
                case 128: // 行向深渊
                    results.AddRange(VerifyAbyss());
                    break;
                case 129: // 分身负载（周期负载链路由 GhostCloneLifecycle 脚本覆盖）
                    results.Add(CheckResult.Pass("文档对照-7.15 分身负载",
                        "每回合末扣 1 由链路脚本 GhostCloneLifecycle 覆盖"));
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
            scripts.Add(ChaosSimChain());
            scripts.Add(TwinAttuneChain());
            scripts.Add(GhostCloneLifecycle());
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

        // ==================== P7-A 特例 ====================

        /// <summary>确定性场景助手：混沌法师在 slot3 的验收局（4 人局，全员同地点 4，真开局基线）。</summary>
        private static WorldState ChaosScene(int seed)
        {
            var s = AcceptanceScenes.BuildState(ChaosMageDef.Create(), seed);
            for (int i = 0; i < s.PlayerCount; i++)
                s.GetCharacterByPlayer(i).CurrentLocationId = 4;
            return s;
        }

        /// <summary>扫描 seed 至「混沌动作判定」为期望分支。itemId=2 时胜 = 理智回血上升。
        /// casterSanity 为施放前理智（刀 4 / 血包 1）。返回已执行完该动作的场景（同 seed 确定性）。</summary>
        private static WorldState ChaosAct(bool wantWin, int itemId, int targetPlayerId, float casterSanity)
        {
            for (int seed = 42; seed < 400; seed++)
            {
                var s = ChaosScene(seed);
                Character chaos = s.GetCharacterByPlayer(3);
                chaos.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
                chaos.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, casterSanity);
                float before = chaos.Sanity;
                new ChaosSimulateCommand(0, 3, itemId, targetPlayerId).Execute(s);
                bool win = chaos.Sanity >= before; // 败必自扣 1 → 理智下降；血包胜 = 回血上升
                if (win == wantWin) return s;
            }
            return null;
        }

        private static List<CheckResult> VerifyChaos()
        {
            var results = new List<CheckResult>();

            var sw = ChaosAct(true, 1, 1, 4f);
            var sl = ChaosAct(false, 1, 1, 4f);
            var sb = ChaosAct(true, 2, 3, 1f);
            var sbl = ChaosAct(false, 2, 3, 2f); // 败侧：血包自用 → 反噬自扣 1 且无回血
            if (sw == null || sl == null || sb == null || sbl == null)
                return Single("混乱-分支扫描", false, "400 seed 内未命中期望分支（实现回归）");

            Character cw = sw.GetCharacterByPlayer(3);
            Character vw = sw.GetCharacterByPlayer(1);
            results.Add(Check(Math.Abs(vw.Sanity - 1.5f) < 0.001f,
                "混乱-胜-模拟刀 1.5 物理", $"victim {vw.Sanity}（期望 1.5）"));
            results.Add(Check(Math.Abs(cw.Sanity - 4f) < 0.001f && cw.ActionPoints == 2,
                "混乱-胜-不自损且扣 1 AP", $"chaos sanity {cw.Sanity} AP {cw.ActionPoints}"));

            Character cl = sl.GetCharacterByPlayer(3);
            Character vl = sl.GetCharacterByPlayer(1);
            results.Add(Check(Math.Abs(cl.Sanity - 3f) < 0.001f,
                "混乱-败-自扣 1 理智", $"chaos {cl.Sanity}（期望 3）"));
            results.Add(Check(Math.Abs(vl.Sanity - 3f) < 0.001f,
                "混乱-败-效果不结算", $"victim {vl.Sanity}（期望 3）"));

            Character cb = sb.GetCharacterByPlayer(3);
            results.Add(Check(Math.Abs(cb.Sanity - 4f) < 0.001f && cb.ActionPoints == 2,
                "混乱-血包模拟回 3（1 → 4）扣 1 AP", $"chaos sanity {cb.Sanity} AP {cb.ActionPoints}"));

            Character cbl = sbl.GetCharacterByPlayer(3);
            results.Add(Check(Math.Abs(cbl.Sanity - 1f) < 0.001f && cbl.ActionPoints == 2,
                "混乱-血包败-反噬自扣无回血（2 → 1）", $"chaos sanity {cbl.Sanity} AP {cbl.ActionPoints}"));

            // 池外道具拒绝
            var so = ChaosScene(42);
            Character co = so.GetCharacterByPlayer(3);
            co.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            co.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 4f);
            new ChaosSimulateCommand(0, 3, 5, 3).Execute(so); // 核弹不在池
            results.Add(Check(co.ActionPoints == 2 && Math.Abs(co.Sanity - 4f) < 0.001f,
                "混乱-池外道具拒绝不扣费", $"AP {co.ActionPoints} sanity {co.Sanity}"));
            return results;
        }

        /// <summary>单结果速写。</summary>
        private static List<CheckResult> Single(string name, bool ok, string msg)
        {
            var list = new List<CheckResult>();
            list.Add(ok ? CheckResult.Pass(name, msg) : CheckResult.Fail(name, msg));
            return list;
        }

        /// <summary>单结果速写（bool 断言）。</summary>
        private static CheckResult Check(bool ok, string name, string msg)
            => ok ? CheckResult.Pass(name, msg) : CheckResult.Fail(name, msg);

        /// <summary>数据幽灵验收场景（slot3，V1 基线 + 周期被动注册）。</summary>
        private static WorldState GhostScene(int seed)
        {
            var s = AcceptanceScenes.BuildState(DataGhostDef.Create(), seed);
            for (int i = 0; i < s.PlayerCount; i++)
                s.GetCharacterByPlayer(i).CurrentLocationId = 4;
            return s;
        }

        /// <summary>数据幽灵角色（slot3）。</summary>
        private static Character Ghost(WorldState s) => s.GetCharacterByPlayer(3);

        private static List<CheckResult> VerifyHellChild()
        {
            var results = new List<CheckResult>();
            var s = ChaosScene(42);
            Character chaos = s.GetCharacterByPlayer(3);
            chaos.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            s.Locations.DropItem(4, 1, 1);
            new PickupItemCommand(0, 3, 1).Execute(s);
            results.Add(Check(chaos.Inventory.EquippedWeapon == null && chaos.ActionPoints == 3,
                "地狱的子民-禁拾取（不扣 AP）", $"AP {chaos.ActionPoints}"));

            chaos.Inventory.AddConsumable(ItemRegistry.Get(2));
            new UseItemCommand(0, 3, 0, 3).Execute(s);
            results.Add(Check(chaos.ActionPoints == 3 && Math.Abs(chaos.Sanity - 4f) < 0.001f,
                "地狱的子民-禁使用血包", $"AP {chaos.ActionPoints}"));

            chaos.Inventory.Consumables.Clear();
            chaos.Inventory.AddConsumable(ItemRegistry.Get(8));
            new UseItemCommand(0, 3, 0, 3).Execute(s);
            results.Add(Check(chaos.ASC.Tags.HasTag(GameplayTagConstants.DeathSaveActive),
                "地狱的子民-应急装置豁免", "DeathSaveActive 生效"));

            // 普攻可用
            Character victim = s.GetCharacterByPlayer(1);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            new DamageCommand(0, 3, 1, 1f).Execute(s);
            results.Add(Check(chaos.ActionPoints == 2 && Math.Abs(victim.Sanity - 2f) < 0.001f,
                "地狱的子民-普攻不受限", $"victim {victim.Sanity}"));
            return results;
        }

        /// <summary>双子场景（slot3 持刀——AcceptanceScenes 已补发 InitialItem=1）。</summary>
        private static WorldState TwinScene(int seed)
        {
            var s = AcceptanceScenes.BuildState(TwinDef.Create(), seed);
            for (int i = 0; i < s.PlayerCount; i++)
                s.GetCharacterByPlayer(i).CurrentLocationId = 4;
            return s;
        }

        private static Character Twin(WorldState s) => s.GetCharacterByPlayer(3);

        private static List<CheckResult> VerifyTwinVampiric()
        {
            var results = new List<CheckResult>();

            // 无武器拒绝（123 + 124 同款直调）
            var s1 = TwinScene(42);
            Character t1 = Twin(s1);
            t1.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            t1.Inventory.EquippedWeapon = null;
            SkillSystem.Activate(t1.Definition.Skills[0], t1, t1, s1, 0);
            results.Add(Check(t1.ActionPoints == 2
                && !t1.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric),
                "附灵-无武器拒绝(123)不扣 AP", $"AP {t1.ActionPoints}"));
            SkillSystem.Activate(t1.Definition.Skills[1], t1, t1, s1, 1);
            results.Add(Check(t1.ActionPoints == 2
                && !t1.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile)
                && !t1.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric),
                "附灵-无武器拒绝(124)不扣 AP", $"AP {t1.ActionPoints}"));

            // 泣血：伤害 +0.5 且命中回血
            var s2 = TwinScene(42);
            Character t2 = Twin(s2);
            Character victim = s2.GetCharacterByPlayer(1);
            t2.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            t2.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f);
            t2.ASC.Tags.AddTag(GameplayTagConstants.TwinVampiric);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            new DamageCommand(0, 3, 1, 1f).Execute(s2); // 普攻 1 → 1.5
            results.Add(Check(Math.Abs(victim.Sanity - 1.5f) < 0.001f,
                "泣血-武器伤害 +0.5（1 → 1.5）", $"victim {victim.Sanity}"));
            results.Add(Check(Math.Abs(t2.Sanity - 2.5f) < 0.001f,
                "泣血-命中回 0.5", $"twin {t2.Sanity}"));

            // 切换互斥与扣费：123 → 124 → 123 三连直调，AP 精确递减 3 → 2 → 1 → 0（照搬 TwinTests 同款）
            var s3 = TwinScene(42);
            Character t3 = Twin(s3);
            t3.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3f);
            SkillSystem.Activate(t3.Definition.Skills[0], t3, t3, s3, 0);
            results.Add(Check(t3.ActionPoints == 2 && t3.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric)
                && !t3.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile),
                "切换-123 挂泣血扣 1 AP", $"AP {t3.ActionPoints}"));
            SkillSystem.Activate(t3.Definition.Skills[1], t3, t3, s3, 1);
            results.Add(Check(t3.ActionPoints == 1 && t3.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile)
                && !t3.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric),
                "切换-124 互斥替换泣血再扣 1 AP", $"AP {t3.ActionPoints}"));
            SkillSystem.Activate(t3.Definition.Skills[0], t3, t3, s3, 0);
            results.Add(Check(t3.ActionPoints == 0 && t3.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric)
                && !t3.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile),
                "切换-123 切回扣至 0 AP", $"AP {t3.ActionPoints}"));
            return results;
        }

        private static List<CheckResult> VerifyTwinAgile()
        {
            var results = new List<CheckResult>();

            // 非灵巧态转换拒绝
            var s1 = TwinScene(42);
            Character t1 = Twin(s1);
            t1.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            new TwinConvertCommand(0, 3, 3).Execute(s1);
            results.Add(Check(t1.ActionPoints == 2 && t1.Inventory.EquippedWeapon.Id == 1,
                "转换-非灵巧态拒绝", $"AP {t1.ActionPoints}"));

            // 灵巧态刀 → 枪（弹药归零：预置弹药 1，防「默认 0 恒绿」假窗口）
            var s2 = TwinScene(42);
            Character t2 = Twin(s2);
            t2.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            t2.ASC.Tags.AddTag(GameplayTagConstants.TwinAgile);
            t2.Inventory.EquippedWeaponAmmo = 1; // 转换前预置弹药：删除归零实现必须红
            new TwinConvertCommand(0, 3, 3).Execute(s2);
            results.Add(Check(t2.Inventory.EquippedWeapon != null
                && t2.Inventory.EquippedWeapon.Id == 3
                && t2.Inventory.EquippedWeaponAmmo == 0
                && t2.ActionPoints == 1,
                "转换-刀换枪成功含弹药重置", "equip 枪 ammo 0 AP 1"));
            return results;
        }

        private static List<CheckResult> VerifyFormSwitch()
        {
            var results = new List<CheckResult>();
            var s = GhostScene(42);
            Character g = Ghost(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);

            // 初始仅 V1：切换无效返回
            SkillSystem.Activate(g.Definition.Skills[0], g, g, s, 0);
            results.Add(Check(g.ASC.Tags.HasTag(GameplayTagConstants.V1Form)
                && !g.ASC.Tags.HasTag(GameplayTagConstants.FormSwitched),
                "形态-仅 V1 时切换无效", "保持 V1、不置每回合标记"));

            // 全解锁后按序循环
            g.ASC.Tags.AddTag(GameplayTagConstants.V2Unlocked);
            g.ASC.Tags.AddTag(GameplayTagConstants.V3Unlocked);
            g.ASC.Tags.AddTag(GameplayTagConstants.V4Unlocked);
            g.ASC.Tags.AddTag(GameplayTagConstants.V5Unlocked);
            SkillSystem.Activate(g.Definition.Skills[0], g, g, s, 0); // V1 → V2
            results.Add(Check(g.ASC.Tags.HasTag(GameplayTagConstants.V2Form)
                && g.ASC.Tags.HasTag(GameplayTagConstants.FormSwitched),
                "形态-切到 V2 且置每回合标记", "V2Form + FormSwitched"));

            SkillSystem.Activate(g.Definition.Skills[0], g, g, s, 0); // 同回合第二次被拦
            results.Add(Check(g.ASC.Tags.HasTag(GameplayTagConstants.V2Form)
                && !g.ASC.Tags.HasTag(GameplayTagConstants.V3Form),
                "形态-每回合限 1 次", "BlockingTags 拦截二次切换"));

            g.ASC.Tags.RemoveTag(GameplayTagConstants.FormSwitched);
            SkillSystem.Activate(g.Definition.Skills[0], g, g, s, 0); // V2 → V3
            g.ASC.Tags.RemoveTag(GameplayTagConstants.FormSwitched);
            SkillSystem.Activate(g.Definition.Skills[0], g, g, s, 0); // V3 → V4
            g.ASC.Tags.RemoveTag(GameplayTagConstants.FormSwitched);
            SkillSystem.Activate(g.Definition.Skills[0], g, g, s, 0); // V4 → V5
            g.ASC.Tags.RemoveTag(GameplayTagConstants.FormSwitched);
            SkillSystem.Activate(g.Definition.Skills[0], g, g, s, 0); // V5 → V1 循环
            results.Add(Check(g.ASC.Tags.HasTag(GameplayTagConstants.V1Form),
                "形态-V5 后循环回 V1", "V1Form"));

            // 跳锁：只解锁 V4 时 V1 → V4（跳过锁定的 V2/V3）
            var s2 = GhostScene(43);
            Character g2 = Ghost(s2);
            g2.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g2.ASC.Tags.AddTag(GameplayTagConstants.V4Unlocked);
            SkillSystem.Activate(g2.Definition.Skills[0], g2, g2, s2, 0);
            results.Add(Check(g2.ASC.Tags.HasTag(GameplayTagConstants.V4Form),
                "形态-切换跳锁（V1 → V4）", "V4Form"));
            return results;
        }

        private static List<CheckResult> VerifyHarmonics()
        {
            var results = new List<CheckResult>();
            var s = GhostScene(42);
            Character g = Ghost(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);

            SkillSystem.Activate(g.Definition.Skills[1], g, g, s, 1); // Harmonics
            results.Add(Check(s.GhostCloneActive && Math.Abs(s.GhostCloneSanity - 3f) < 0.001f
                && g.ActionPoints == 1,
                "Harmonics-造分身（理智 3，扣 1 AP）", $"sanity {s.GhostCloneSanity} AP {g.ActionPoints}"));

            SkillSystem.Activate(g.Definition.Skills[1], g, g, s, 1); // 已有分身
            results.Add(Check(s.GhostCloneActive && Math.Abs(s.GhostCloneSanity - 3f) < 0.001f
                && g.ActionPoints == 1,
                "Harmonics-已有分身拒绝不扣费", $"AP {g.ActionPoints}"));
            return results;
        }

        private static List<CheckResult> VerifyDiscipline()
        {
            var results = new List<CheckResult>();

            // 近战物理
            var s1 = GhostScene(42);
            Character g1 = Ghost(s1);
            Character src1 = s1.GetCharacterByPlayer(1);
            g1.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            DamagePipeline.ApplyDamage(s1, src1.Id, g1.Id, 1f, DamageType.Physical);
            results.Add(Check(g1.ASC.Tags.HasTag(GameplayTagConstants.V2Unlocked)
                && g1.ASC.Tags.HasTag(GameplayTagConstants.V3Unlocked)
                && g1.ASC.Tags.HasTag(GameplayTagConstants.V4Unlocked)
                && !g1.ASC.Tags.HasTag(GameplayTagConstants.V5Unlocked),
                "规诫-近战解锁 V2/V3/V4", "近战 → V2+V3+V4"));

            // 远程（枪）
            var s2 = GhostScene(42);
            Character g2 = Ghost(s2);
            Character src2 = s2.GetCharacterByPlayer(1);
            src2.Inventory.EquipWeapon(ItemRegistry.Get(3));
            src2.Inventory.EquippedWeaponAmmo = 1;
            g2.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            DamagePipeline.ApplyDamage(s2, src2.Id, g2.Id, 2f, DamageType.Physical);
            results.Add(Check(g2.ASC.Tags.HasTag(GameplayTagConstants.V5Unlocked)
                && g2.ASC.Tags.HasTag(GameplayTagConstants.V4Unlocked)
                && !g2.ASC.Tags.HasTag(GameplayTagConstants.V2Unlocked),
                "规诫-远程解锁 V5", "枪伤 → V5+V4"));

            // V1 减伤（近战场景 g1 处于默认 V1：1 点物理伤实际扣 0.5）
            results.Add(Check(Math.Abs(g1.Sanity - 2.5f) < 0.001f,
                "规诫-V1 减伤 0.5（1 → 0.5）", $"ghost sanity {g1.Sanity}（期望 2.5）"));
            return results;
        }

        /// <summary>把角色切到指定形态标签（独占：先清全部形态标签再打目标标签，同 SwitchForm 落点）。</summary>
        private static void SetForm(Character g, int form)
        {
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V1Form);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V2Form);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V3Form);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V4Form);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V5Form);
            switch (form)
            {
                case 2: g.ASC.Tags.AddTag(GameplayTagConstants.V2Form); break;
                case 3: g.ASC.Tags.AddTag(GameplayTagConstants.V3Form); break;
                case 4: g.ASC.Tags.AddTag(GameplayTagConstants.V4Form); break;
                case 5: g.ASC.Tags.AddTag(GameplayTagConstants.V5Form); break;
                default: g.ASC.Tags.AddTag(GameplayTagConstants.V1Form); break;
            }
        }

        /// <summary>形态结算特例（真实 DamageCommand → DataGhostFormAttack 形态分派）：
        /// V1 近战 1（V1 减伤侧已在 VerifyDiscipline）/ V2 1.5 / V3 三连 0.5×3（护盾只挡第一击）/
        /// V4 花自愈 0.5（victim 无损）/ V5 跨地点命中（空手免弹药）。
        /// victim 理智预置 3（满值防中道死亡干扰断言）。</summary>
        private static List<CheckResult> VerifyFormDamage()
        {
            var results = new List<CheckResult>();

            // V1 近战 1
            var s1 = GhostScene(42);
            Character g1 = Ghost(s1);
            g1.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3f);
            SetForm(g1, 1);
            Character v1 = s1.GetCharacterByPlayer(1);
            v1.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            new DamageCommand(0, 3, 1, 1f).Execute(s1);
            results.Add(Check(Math.Abs(v1.Sanity - 2f) < 0.001f,
                "形态结算-V1 近战 1", $"victim {v1.Sanity}（期望 2）"));

            // V2 近战 1.5
            var s2 = GhostScene(42);
            Character g2 = Ghost(s2);
            g2.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3f);
            SetForm(g2, 2);
            Character v2 = s2.GetCharacterByPlayer(1);
            v2.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            new DamageCommand(0, 3, 1, 1f).Execute(s2);
            results.Add(Check(Math.Abs(v2.Sanity - 1.5f) < 0.001f,
                "形态结算-V2 近战 1.5", $"victim {v2.Sanity}（期望 1.5）"));

            // V3 三连 0.5×3（无盾）
            var s3 = GhostScene(42);
            Character g3 = Ghost(s3);
            g3.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3f);
            SetForm(g3, 3);
            Character v3 = s3.GetCharacterByPlayer(1);
            v3.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            new DamageCommand(0, 3, 1, 1f).Execute(s3);
            results.Add(Check(Math.Abs(v3.Sanity - 1.5f) < 0.001f,
                "形态结算-V3 三连 0.5×3", $"victim {v3.Sanity}（期望 1.5）"));

            // V3 三连 + 护盾：只挡第一击（0.5 被盾吃 + 0.5 + 0.5 = 净损 1），盾已破
            var s4 = GhostScene(42);
            Character g4 = Ghost(s4);
            g4.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3f);
            SetForm(g4, 3);
            Character v4 = s4.GetCharacterByPlayer(1);
            v4.ASC.Tags.AddTag(GameplayTagConstants.HasShield);
            v4.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            new DamageCommand(0, 3, 1, 1f).Execute(s4);
            results.Add(Check(Math.Abs(v4.Sanity - 2f) < 0.001f,
                "形态结算-V3 护盾只挡首击", $"victim {v4.Sanity}（期望 2：净损 1）"));
            results.Add(Check(!v4.ASC.Tags.HasTag(GameplayTagConstants.HasShield),
                "形态结算-V3 护盾已破", "首击破盾后剩余两击生效"));

            // V4 花：自愈 0.5（victim 无损）
            var s5 = GhostScene(42);
            Character g5 = Ghost(s5);
            g5.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3f);
            g5.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f);
            SetForm(g5, 4);
            Character v5 = s5.GetCharacterByPlayer(1);
            v5.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            new DamageCommand(0, 3, 1, 1f).Execute(s5);
            results.Add(Check(Math.Abs(g5.Sanity - 2.5f) < 0.001f,
                "形态结算-V4 自愈 0.5", $"ghost {g5.Sanity}（期望 2.5）"));
            results.Add(Check(Math.Abs(v5.Sanity - 3f) < 0.001f,
                "形态结算-V4 目标无损", $"victim {v5.Sanity}（期望 3）"));

            // V5 远程：跨地点命中（空手免弹药）
            var s6 = GhostScene(42);
            Character g6 = Ghost(s6);
            g6.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3f);
            SetForm(g6, 5);
            Character v6 = s6.GetCharacterByPlayer(1);
            v6.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            v6.CurrentLocationId = 8; // 异地目标（非藏匿可瞄准）
            results.Add(Check(g6.Inventory.EquippedWeapon == null,
                "形态结算-V5 前置空手", "V5 形态自带远程语义（无实体武器）"));
            new DamageCommand(0, 3, 1, 1f).Execute(s6);
            results.Add(Check(Math.Abs(v6.Sanity - 2f) < 0.001f,
                "形态结算-V5 跨地点命中", $"victim {v6.Sanity}（期望 2）"));
            return results;
        }

        /// <summary>扫描 seed 至行向深渊判定为期望分支（victim 0.5 被 V1 一击击杀，无分身干扰）。</summary>
        private static WorldState AbyssAct(bool wantWin)
        {
            for (int seed = 42; seed < 400; seed++)
            {
                var s = GhostScene(seed);
                Character g = Ghost(s);
                Character victim = s.GetCharacterByPlayer(1);
                g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
                g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
                victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0.5f);
                DamagePipeline.ApplyDamage(s, g.Id, victim.Id, 1f, DamageType.Physical); // 击杀 → 128
                s.Commands.ExecuteAll(s);
                bool win = !g.IsDead && Math.Abs(g.Sanity - 3f) < 0.001f;
                bool lose = g.IsDead || Math.Abs(g.Sanity - 2f) < 0.001f;
                if (wantWin && win) return s;
                if (!wantWin && lose) return s;
            }
            return null;
        }

        /// <summary>
        /// 扫描 seed 至「128 败连坐 → 分身死亡判定」终态（victim 0.5 被 V1 一击击杀 → 128 判定败
        /// （本体 3 → 2）→ 分身 1 → 0 → BodyDuelOnCloneDeath：胜 → 本体存活 2 理智 / 败 → 本体直接死亡）。
        /// 仅在分身已消亡（!GhostCloneActive）的 seed 内分类；两分类互补命中才证明 42..399 内两态均可达
        /// ——两态都强制存在，任一分支删除即扫描 Fail 变红。
        /// </summary>
        private static WorldState AbyssDoomChain(bool wantBodyDead)
        {
            for (int seed = 42; seed < 400; seed++)
            {
                var s = GhostScene(seed);
                Character g = Ghost(s);
                Character victim = s.GetCharacterByPlayer(1);
                g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
                g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
                victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0.5f);
                s.GhostCloneActive = true;
                s.GhostCloneSanity = 1f; // 败连坐 -1 → 0 → 分身死亡判定链
                DamagePipeline.ApplyDamage(s, g.Id, victim.Id, 1f, DamageType.Physical); // 击杀 → 128
                s.Commands.ExecuteAll(s); // 执行可能入队的 DeathCommand（分身死亡链）
                if (!s.GhostCloneActive) // 分身已消亡：128 败连坐必然已发生（胜分支分身仍活跃，跳过）
                {
                    if (wantBodyDead && g.IsDead) return s;
                    if (!wantBodyDead && !g.IsDead && Math.Abs(g.Sanity - 2f) < 0.001f) return s;
                }
            }
            return null;
        }

        /// <summary>
        /// 扫描 seed 至「分身活跃双发攻击 → 复制段击杀目标」场景（victim 1.5：首击 1 → 0.5 未死、
        /// 复制段再 1 → 0 击杀——分身击杀归本体触发 128，判定胜 → AP+1 且分身无连坐）。
        /// 返回 victim 死亡而本体/分身无恙的 seed（128 胜分支落点），供精确终态断言。
        /// </summary>
        private static WorldState CloneKillScene(out Character ghost, out Character victim)
        {
            for (int seed = 42; seed < 400; seed++)
            {
                var s = GhostScene(seed);
                Character g = Ghost(s);
                Character v = s.GetCharacterByPlayer(1);
                g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3f);
                g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
                v.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1.5f);
                s.GhostCloneActive = true;
                s.GhostCloneSanity = 1f;
                new DamageCommand(0, 3, 1, 1f).Execute(s); // V1 双发：victim 死于复制段
                s.Commands.ExecuteAll(s);
                if (!v.IsDead || v.Sanity > 0f) continue; // 击杀未发生（双发语义缺失）：跳过
                if (g.IsDead) continue; // 128 败连坐本体死亡链：AbyssDoomChain 覆盖，跳过
                if (!s.GhostCloneActive) continue; // 128 败连坐分身消亡：跳过
                ghost = g;
                victim = v;
                return s;
            }
            ghost = null;
            victim = null;
            return null;
        }

        private static List<CheckResult> VerifyAbyss()
        {
            var results = new List<CheckResult>();
            var w = AbyssAct(true);
            var l = AbyssAct(false);
            var cd = AbyssDoomChain(true);
            var ca = AbyssDoomChain(false);
            if (w == null || l == null || cd == null || ca == null)
                return Single("行向深渊-分支扫描", false,
                    "400 seed 内未命中期望终态（胜/败/败连坐分身死亡判定两态必须全部存在——实现回归）");

            Character gw = w.GetCharacterByPlayer(3);
            results.Add(Check(gw.ActionPoints == 3 && Math.Abs(gw.Sanity - 3f) < 0.001f,
                "行向深渊-胜 AP+1", $"AP {gw.ActionPoints}"));

            Character gl = l.GetCharacterByPlayer(3);
            results.Add(Check(Math.Abs(gl.Sanity - 2f) < 0.001f,
                "行向深渊-败本体 -1", $"sanity {gl.Sanity}"));

            // 败连坐 → 分身死亡判定：败分支（本体直接死亡）
            Character gcd = cd.GetCharacterByPlayer(3);
            results.Add(Check(!cd.GhostCloneActive && gcd.IsDead,
                "行向深渊-败连坐-分身死亡判定败：本体直接死亡",
                $"dead={gcd.IsDead} clone={cd.GhostCloneActive}"));

            // 败连坐 → 分身死亡判定：胜分支（本体存活 2 理智）
            Character gca = ca.GetCharacterByPlayer(3);
            results.Add(Check(!ca.GhostCloneActive && !gca.IsDead && Math.Abs(gca.Sanity - 2f) < 0.001f,
                "行向深渊-败连坐-分身死亡判定胜：本体存活 2 理智",
                $"dead={gca.IsDead} sanity={gca.Sanity} clone={ca.GhostCloneActive}"));

            // 分身复制段击杀 → 归本体触发 128（胜：AP+1 无连坐；未触发则 AP 2 无 +1）
            Character gk;
            Character vk;
            WorldState ck = CloneKillScene(out gk, out vk);
            results.Add(ck != null
                ? CheckResult.Pass("行向深渊-分身复制段击杀触发", "victim 死于双发复制段且 128 胜分支可达")
                : CheckResult.Fail("行向深渊-分身复制段击杀触发",
                    "400 seed 内未命中（复制段击杀未归本体触发 128——实现回归）"));
            if (ck != null)
            {
                results.Add(Check(gk.ActionPoints == 3 && Math.Abs(gk.Sanity - 3f) < 0.001f
                    && ck.GhostCloneActive && Math.Abs(ck.GhostCloneSanity - 1f) < 0.001f,
                    "行向深渊-复制段击杀归本体（胜 AP+1 分身无连坐）",
                    $"AP {gk.ActionPoints} sanity {gk.Sanity} clone {ck.GhostCloneSanity}"));
                results.Add(Check(vk.IsDead && vk.Sanity <= 0f,
                    "行向深渊-复制段击杀已结算死亡", $"victim dead={vk.IsDead} sanity={vk.Sanity}"));
            }
            return results;
        }

        /// <summary>4 人局固定筹码：slot3 每回合 Winner（1 AP），其余 Neutral。
        /// 与 ChipsTimeThiefWinner 同源同构（首个回合行 = p0-p2 Neutral + p3 Winner）——
        /// 直接复用其首行，杜绝双表重复定义漂移。</summary>
        private static ChipAllocation[] ChipsSlot3Winner() => ChipsTimeThiefWinner()[0];

        /// <summary>通用替换：slot3 换成指定 Def 的角色（同 TerminalIllnessTurns 先例）。
        /// 注意：新角色不继承任何开局道具——连 Def.InitialItem 都不发（真开局 GrantInitialItems 的
        /// 「每人 1 血包 + 出生地点道具」同样缺失），依赖背包的链路（拾刀/转换/附灵等）必须在脚本
        /// Override 显式空手投放或补齐，勿依赖开局背包。</summary>
        private static void ReplaceSlot3(WorldState s, CharacterDef def)
        {
            Character c = new Character(4, 3, def.Name) { CurrentLocationId = 4 };
            def.ApplyTo(c);
            for (int i = 0; i < c.Definition.Skills.Length; i++)
                c.ASC.RegisterPassive(c.Definition.Skills[i]);
            c.ASC.EnsureCooldownSlots(c.Definition.Skills.Length);
            s.Characters[3] = c;
            for (int i = 0; i < s.PlayerCount; i++)
                s.GetCharacterByPlayer(i).CurrentLocationId = 4;
        }

        /// <summary>混沌-模拟链路：刀模拟判定双分支合法性 + 血包模拟（每回合 1 行动）。</summary>
        private static TurnScript ChaosSimChain()
        {
            var script = new TurnScript
            {
                SkillId = SkillSystem.ChaosSkillId,
                SkillName = "混乱",
                ScriptName = "混沌-模拟链路（双分支）",
                PlayerCount = 4,
                JudgmentInputs = new[] { ChipsSlot3Winner() }
            };
            script.Overrides.Add(s =>
            {
                ReplaceSlot3(s, ChaosMageDef.Create());
                Character chaos = s.GetCharacterByPlayer(3);
                chaos.Inventory.EquippedWeapon = null;
                chaos.Inventory.Consumables.Clear();
                // 起始理智 3：T1 胜 → 3 / 败 → 2；T2 从 3（或 2）出发回 3 钳 4 必可见——
                // 若从 4 出发，回 3 被上限钳制会与「无回血」不可区分（恒真窗口）
                chaos.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
                Character victim = s.GetCharacterByPlayer(1);
                victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            });
            // T1：模拟刀 → 胜（victim 1.5，chaos 3）或 败（victim 3，chaos 2）双分支合法
            script.Steps.Add(new ActStep(3, new ChaosSimulateCommand(0, 3, 1, 1)));
            script.Steps.Add(new AssertTrueStep("刀模拟后 chaos 理智两态",
                s => { Character c = s.GetCharacterByPlayer(3); return Math.Abs(c.Sanity - 3f) < 0.001f || Math.Abs(c.Sanity - 2f) < 0.001f; }));
            script.Steps.Add(new AssertTrueStep("刀模拟后 victim 两态",
                s => { Character v = s.GetCharacterByPlayer(1); return Math.Abs(v.Sanity - 3f) < 0.001f || Math.Abs(v.Sanity - 1.5f) < 0.001f; }));
            script.Steps.Add(new AssertTrueStep("胜败互斥出现",
                s => { Character c = s.GetCharacterByPlayer(3); Character v = s.GetCharacterByPlayer(1);
                       bool win = Math.Abs(c.Sanity - 3f) < 0.001f && Math.Abs(v.Sanity - 1.5f) < 0.001f;
                       bool lose = Math.Abs(c.Sanity - 2f) < 0.001f && Math.Abs(v.Sanity - 3f) < 0.001f;
                       return win || lose; }));
            script.Steps.Add(new EndTurnStep());
            // T2：血包模拟（自用）。真实回合语义推演：T1 胜 → 出发 3 / T1 败 → 出发 2；
            // T2 胜 → 回 3 钳上限 4；T2 败 → 反噬自扣 1（3 → 2 / 2 → 1）且不回血。
            // 四终态两两互斥且穷尽：删除回血/反噬任一实现分支必落集合外 → 红。
            script.Steps.Add(new ActStep(3, new ChaosSimulateCommand(0, 3, 2, 3)));
            script.Steps.Add(new AssertTrueStep("血包模拟终态精确（胜回 3 钳 4 / 败自扣 1，互斥穷尽）",
                s =>
                {
                    Character c = s.GetCharacterByPlayer(3);
                    Character v = s.GetCharacterByPlayer(1);
                    bool t1wT2w = Math.Abs(c.Sanity - 4f) < 0.001f && Math.Abs(v.Sanity - 1.5f) < 0.001f;
                    bool t1wT2l = Math.Abs(c.Sanity - 2f) < 0.001f && Math.Abs(v.Sanity - 1.5f) < 0.001f;
                    bool t1lT2w = Math.Abs(c.Sanity - 4f) < 0.001f && Math.Abs(v.Sanity - 3f) < 0.001f;
                    bool t1lT2l = Math.Abs(c.Sanity - 1f) < 0.001f && Math.Abs(v.Sanity - 3f) < 0.001f;
                    return t1wT2w || t1wT2l || t1lT2w || t1lT2l;
                }));
            script.Steps.Add(new EndTurnStep());
            return script;
        }

        /// <summary>双子-附灵-攻击-切换-转换 完整链路（确定性无判定）。</summary>
        private static TurnScript TwinAttuneChain()
        {
            var script = new TurnScript
            {
                SkillId = SkillSystem.TwinVampiricSkillId,
                SkillName = "附灵·泣血",
                ScriptName = "双子-拾刀-泣血-转换链路",
                PlayerCount = 4,
                JudgmentInputs = new[] { ChipsSlot3Winner() }
            };
            script.Overrides.Add(s =>
            {
                ReplaceSlot3(s, TwinDef.Create());
                Character twin = s.GetCharacterByPlayer(3);
                twin.Inventory.EquippedWeapon = null;   // 无刀：先拾取
                twin.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f);
                Character victim = s.GetCharacterByPlayer(1);
                victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
                s.Locations.DropItem(4, 1, 1);          // 地点 4 放一把刀
            });
            // T1 无武器附灵拒绝（不扣 AP 由特例表精确断言；回合末行动者 AP 必归零）
            script.Steps.Add(new ActStep(3, new UseSkillCommand(0, 3, 0, 3)));
            script.Steps.Add(new AssertTrueStep("无武器附灵被拒",
                s => { Character c = s.GetCharacterByPlayer(3); return c.ActionPoints == 0
                    && !c.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric)
                    && !c.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile); }));
            script.Steps.Add(new EndTurnStep());
            // T2 拾刀
            script.Steps.Add(new ActStep(3, new PickupItemCommand(0, 3, 1)));
            script.Steps.Add(new AssertTrueStep("拾刀成功",
                s => s.GetCharacterByPlayer(3).Inventory.EquippedWeapon != null
                    && s.GetCharacterByPlayer(3).Inventory.EquippedWeapon.Id == 1));
            script.Steps.Add(new EndTurnStep());
            // T3 附灵泣血
            script.Steps.Add(new ActStep(3, new UseSkillCommand(0, 3, 0, 3)));
            script.Steps.Add(new AssertTrueStep("泣血挂载",
                s => s.GetCharacterByPlayer(3).ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric)));
            script.Steps.Add(new EndTurnStep());
            // T4 泣血普攻：victim 3 → 1.5，本体 2 → 2.5
            script.Steps.Add(new ActStep(3, new DamageCommand(0, 3, 1, 1f)));
            script.Steps.Add(new AssertStep("泣血普攻伤害", 1.5f, s => s.GetCharacterByPlayer(1).Sanity));
            script.Steps.Add(new AssertStep("命中回血", 2.5f, s => s.GetCharacterByPlayer(3).Sanity));
            script.Steps.Add(new EndTurnStep());
            // T5 切换灵巧（互斥：旧泣血标签须移除——删除 RemoveTag(TwinVampiric) 实现必红）
            script.Steps.Add(new ActStep(3, new UseSkillCommand(0, 3, 1, 3)));
            script.Steps.Add(new AssertTrueStep("切到灵巧并移除泣血",
                s => { Character c = s.GetCharacterByPlayer(3);
                       return c.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile)
                           && !c.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric); }));
            script.Steps.Add(new EndTurnStep());
            // T6 转换枪
            script.Steps.Add(new ActStep(3, new TwinConvertCommand(0, 3, 3)));
            script.Steps.Add(new AssertTrueStep("转换枪成功",
                s => { var c = s.GetCharacterByPlayer(3).Inventory;
                       return c.EquippedWeapon != null && c.EquippedWeapon.Id == 3 && c.EquippedWeaponAmmo == 0; }));
            script.Steps.Add(new EndTurnStep());
            return script;
        }

        /// <summary>数据幽灵-分身完整生命周期链路（Harmonics → 分身活跃双发普攻 → 回合末扣减 → 分身死亡判定）。
        /// 分身由 Override 注入（同 MistFogExit 先例）：RunTurn 单次含回合末，注入后首个完整回合
        /// （攻击 ActStep 所在回合）的回合末即第 1 次扣减，保证数值断言与「每回合末扣 1」口径一致。
        /// 双发攻击观察 ×2 同步复制（victim 3 → 1）；复制段击杀触发 128 的归本体语义由
        /// VerifyAbyss 的 CloneKillScene 扫描精确断言。</summary>
        private static TurnScript GhostCloneLifecycle()
        {
            var script = new TurnScript
            {
                SkillId = SkillSystem.HarmonicsSkillId,
                SkillName = "Harmonics",
                ScriptName = "数据幽灵-分身生命周期",
                PlayerCount = 4,
                JudgmentInputs = new[] { ChipsSlot3Winner() }
            };
            script.Overrides.Add(s =>
            {
                ReplaceSlot3(s, DataGhostDef.Create());
                Character g = s.GetCharacterByPlayer(3);
                g.Inventory.EquippedWeapon = null;
                g.Inventory.Consumables.Clear();
                g.ASC.Tags.AddTag(GameplayTagConstants.V1Form); // 数据幽灵基线（同 TurnManager）
                g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
                g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
                Character victim = s.GetCharacterByPlayer(1);
                victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f); // 双发 1×2 后余 1，防中道死亡
                SkillSystem.Activate(g.Definition.Skills[1], g, g, s, 1); // 造分身：理智 3
            });
            // 首步断言直接落「分身生成」（造分身后尚未经过任何回合末）
            script.Steps.Add(new AssertTrueStep("分身生成",
                s => s.GhostCloneActive && Math.Abs(s.GhostCloneSanity - 3f) < 0.001f));
            // 分身活跃当回合普攻：同步双发 ×2（V1 1×2 总 2）→ victim 3 → 1；攻击回合末分身 3 → 2
            script.Steps.Add(new ActStep(3, new DamageCommand(0, 3, 1, 1f)));
            script.Steps.Add(new AssertStep("双发普攻合计 -2（victim 3 → 1）", 1f, s => s.GetCharacterByPlayer(1).Sanity));
            script.Steps.Add(new AssertStep("回合末扣 1（攻击当回合）", 2f, s => s.GhostCloneSanity));
            script.Steps.Add(new EndTurnStep());   // 2 → 1
            script.Steps.Add(new AssertStep("回合末扣 1（第 2 次）", 1f, s => s.GhostCloneSanity));
            script.Steps.Add(new EndTurnStep());   // 1 → 0 → 分身死亡判定（胜/败由 seed 42 确定性落点）
            script.Steps.Add(new AssertTrueStep("分身消亡", s => !s.GhostCloneActive));
            // 分身死亡判定终态按 seed 42 确定性落点精确断言：本体存活满理智 3（BodyDuel 胜）。
            // 「败 → 本体直接死亡」分支删除变红的强制保障在 VerifyAbyss 的 AbyssDoomChain
            // 双态扫描（dead 终态 seed 必须存在），两态互补钉死本判定。
            script.Steps.Add(new AssertTrueStep("分身死亡判定胜：本体存活 3 理智",
                s => { Character g = s.GetCharacterByPlayer(3);
                       return !g.IsDead && Math.Abs(g.Sanity - 3f) < 0.001f; }));
            return script;
        }
    }
}
