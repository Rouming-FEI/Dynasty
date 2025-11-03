using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.Characters
{
    public class SkillSystemTests
    {
        [Test]
        public void 被动技能不可主动施放()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).Build();
            var c = state.GetCharacterByPlayer(0);
            SkillDef passive = c.Definition.Skills[0]; // 重装 (Passive)

            Assert.IsFalse(SkillSystem.CanActivate(passive, c));
        }

        [Test]
        public void AP不足时不可施放()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithAP(0, 0)
                .Build();
            var c = state.GetCharacterByPlayer(0);
            SkillDef guard = c.Definition.Skills[1]; // 守护 (1 AP)

            Assert.IsFalse(SkillSystem.CanActivate(guard, c));
        }

        [Test]
        public void AP足够时守护可施放_扣AP并加充能()
        {
            // 守护真实配置：1AP 攒 1 充能（GuardianDef.Cost.Amount = 1，非计划假设的 2AP）
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithAP(0, 1)
                .Build();
            var c = state.GetCharacterByPlayer(0);
            var target = state.GetCharacterByPlayer(1);
            SkillDef guard = c.Definition.Skills[1];

            Assert.IsTrue(SkillSystem.CanActivate(guard, c));
            SkillSystem.Activate(guard, c, target, state);

            Assert.AreEqual(0, c.ActionPoints, "守护消耗 1 AP，从 1 扣到 0");
            Assert.AreEqual(1, c.ASC.GetGuardianCharge(), "每次守护 +1 充能");
        }

        [Test]
        public void 守护满3充能_再施放不扣AP并给目标上盾()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithAP(0, 1)
                .WithGuardianCharge(0, 3)
                .Build();
            var c = state.GetCharacterByPlayer(0);
            var target = state.GetCharacterByPlayer(1);
            SkillDef guard = c.Definition.Skills[1];

            SkillSystem.Activate(guard, c, target, state);

            Assert.AreEqual(1, c.ActionPoints, "满充能施放不扣 AP");
            Assert.AreEqual(0, c.ASC.GetGuardianCharge(), "给盾后充能清零");
            Assert.IsTrue(target.ASC.Tags.HasTag(GameplayTagConstants.HasShield), "目标获得护盾");
        }

        [Test]
        public void 迷离之境_自身上迷离标签()
        {
            // 魑魅是 playerId=1（守护者 0 / 魑魅 1 / 普通人 2 起的顺序）
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithAP(1, 1)
                .Build();
            var c = state.GetCharacterByPlayer(1);
            SkillDef mist = c.Definition.Skills[0]; // 迷离之境

            SkillSystem.Activate(mist, c, c, state);

            Assert.IsTrue(c.ASC.Tags.HasTag(GameplayTagConstants.MistState));
            Assert.AreEqual(0, c.ActionPoints, "消耗 1 AP");
        }

        [Test]
        public void 鬼葬_挂载3回合周期GE()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithAP(1, 1)
                .Build();
            var c = state.GetCharacterByPlayer(1);
            SkillDef ghostBury = c.Definition.Skills[1]; // 鬼葬

            SkillSystem.Activate(ghostBury, c, c, state);

            Assert.AreEqual(1, c.ASC.ActiveEffectCount, "鬼葬 GE 挂载");
            Assert.AreEqual(0, c.ActionPoints);
        }
    }
}
