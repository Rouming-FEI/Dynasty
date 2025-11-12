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
                    results.Add(CheckResult.NotImplemented("文档对照-7.12 迷离之境",
                        "攻击判定加成与心雾三层退出尚未实现（当前仅挂 MistState 标签）"));
                    break;
                case 11: // 鬼葬
                    results.AddRange(VerifyGhostBuryMount());
                    results.Add(CheckResult.Pass("文档对照-7.12 鬼葬",
                        "三档人数联动与实现一致（链路层验证）"));
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

        /// <summary>链路层脚本：守护跨回合充能 + 鬼葬三档联动。</summary>
        public static List<TurnScript> GetTurnScripts()
        {
            var scripts = new List<TurnScript>();
            scripts.Add(GuardianCrossTurnCharge());
            scripts.Add(GhostBurySolo());
            scripts.Add(GhostBuryDuo());
            scripts.Add(GhostBuryTrio());
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
                for (int i = 0; i < s.PlayerCount; i++)
                    s.GetCharacterByPlayer(i).CurrentLocationId = 4;
            });

            script.Steps.Add(new ActStep(0, new UseSkillCommand(0, 0, 1, 1)));  // 守护(1AP) → 充能 2
            script.Steps.Add(new AssertStep("第一回合充能", 2f, s => s.GetCharacterByPlayer(0).ASC.GetGuardianCharge()));
            script.Steps.Add(new EndTurnStep());
            script.Steps.Add(new AssertStep("跨回合保留", 2f, s => s.GetCharacterByPlayer(0).ASC.GetGuardianCharge()));
            script.Steps.Add(new ActStep(0, new UseSkillCommand(0, 0, 1, 1)));  // 充能 3 → 同回合免费给盾兜底清零
            script.Steps.Add(new AssertStep("充能清零", 0f, s => s.GetCharacterByPlayer(0).ASC.GetGuardianCharge()));
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
            script.Steps.Add(new AssertStep("自己受05", 2.5f, s => s.GetCharacterByPlayer(1).Sanity));
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
            script.Steps.Add(new AssertStep("守护者受05", 2.5f, s => s.GetCharacterByPlayer(0).Sanity));
            script.Steps.Add(new AssertStep("普通人受05", 2.5f, s => s.GetCharacterByPlayer(2).Sanity));
            return script;
        }
    }
}
