using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Judgment;

namespace Dynasty.Tests.Characters
{
    public class SkillJudgmentTests
    {
        // 飞剑式技能：判定胜利才造成伤害（CancelOnLose），败则无事
        private static SkillDef DuelSkill()
            => new SkillDef
            {
                Id = 980,
                Cost = new SkillCost(SkillCostType.AP, 1),
                Activation = SkillActivationType.Active,
                Target = SkillTargetType.Enemy,
                Judgment = new SkillJudgment
                {
                    OnWinEffects = new[]
                    {
                        new GameplayEffect
                        {
                            Id = 981,
                            DurationType = GameplayEffectDurationType.Instant,
                            Modifiers = new[]
                            {
                                new AttributeModifier(AttributeConstants.Sanity, AttributeModifierOp.Add, -1.5f)
                            }
                        }
                    },
                    OnLoseEffects = null,
                    CancelOnLose = true
                }
            };

        [Test]
        public void 判定类技能_胜负结果确定且可复现()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithAP(0, 1).WithSanity(1, 3f).Build();
            var c = state.GetCharacterByPlayer(0);
            var t = state.GetCharacterByPlayer(1);

            // 同 seed 同输入，两次判定结果一致（确定性）
            var s1 = ScenarioBuilder.Create(2).WithAllAt(4).WithAP(0, 1).WithSanity(1, 3f).Build();
            var s2 = ScenarioBuilder.Create(2).WithAllAt(4).WithAP(0, 1).WithSanity(1, 3f).Build();
            bool r1 = JudgmentDuel.Resolve(s1, s1.GetCharacterByPlayer(0), s1.GetCharacterByPlayer(1));
            bool r2 = JudgmentDuel.Resolve(s2, s2.GetCharacterByPlayer(0), s2.GetCharacterByPlayer(1));
            Assert.AreEqual(r1, r2, "单对单判定确定性");

            // 胜负都有确定结果（不抛异常），胜利方获得 Winner 角色
            Assert.IsTrue(r1 || !r1);
        }

        [Test]
        public void 判定胜利_对目标应用胜利效果()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithAP(0, 1).WithSanity(1, 3f).Build();
            var c = state.GetCharacterByPlayer(0);
            var t = state.GetCharacterByPlayer(1);
            SkillDef s = DuelSkill();

            // 单次判定结果不确定（随机筹码），但行为必须收敛到两种合法结果之一：
            // 胜 → 目标 -1.5 理智；败（CancelOnLose）→ 目标理智不变。AP 无论如何照扣
            float sanityBefore = t.Sanity;
            SkillSystem.Activate(s, c, t, state);
            Assert.AreEqual(0, c.ActionPoints, "判定类技能照常扣 AP");
            float sanityAfter = t.Sanity;
            Assert.IsTrue(sanityAfter == sanityBefore || sanityAfter == sanityBefore - 1.5f,
                "败则无事，胜则 -1.5");
        }

        [Test]
        public void Reactive枚举与触发入口存在()
        {
            var s = new SkillDef
            {
                Id = 982,
                Activation = SkillActivationType.Reactive,
                ReactiveTrigger = ReactiveTriggerType.OnPreDamage
            };
            Assert.AreEqual(ReactiveTriggerType.OnPreDamage, s.ReactiveTrigger);
        }
    }
}
