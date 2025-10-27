using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.Characters
{
    public class SkillCooldownTests
    {
        private static SkillDef CooldownSkill()
            => new SkillDef
            {
                Id = 970,
                Cost = new SkillCost(SkillCostType.AP, 1),
                Activation = SkillActivationType.Active,
                Target = SkillTargetType.Self,
                CooldownTurns = 2
            };

        // 冷却按技能索引（CharacterDef.Skills 下标）存储，测试用 slot 0 验证机制。
        // 角色 ASC 的冷却槽由 TurnManager.StartGame 的 EnsureCooldownSlots 分配，
        // 测试角色（守护者）技能数 >= 1，slot 0 始终可用。

        [Test]
        public void 施放后进入冷却_冷却中不可再施放()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithAP(0, 3).Build();
            var c = state.GetCharacterByPlayer(0);
            SkillDef s = CooldownSkill();

            Assert.IsTrue(SkillSystem.CanActivate(s, c, 0));
            SkillSystem.Activate(s, c, c, state, 0);
            Assert.IsFalse(SkillSystem.CanActivate(s, c, 0), "冷却中不可施放");
        }

        [Test]
        public void 冷却随回合递减_到期后可再施放()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithAP(0, 3).Build();
            var c = state.GetCharacterByPlayer(0);
            SkillDef s = CooldownSkill();

            SkillSystem.Activate(s, c, c, state, 0);
            c.ASC.TickEffects(state); // 冷却 2 → 1
            Assert.IsFalse(SkillSystem.CanActivate(s, c, 0));
            c.ASC.TickEffects(state); // 冷却 1 → 0
            Assert.IsTrue(SkillSystem.CanActivate(s, c, 0), "冷却结束可再施放");
        }
    }
}
