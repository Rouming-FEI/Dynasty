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

        [Test]
        public void UseSkillCommand转发技能索引_冷却在生产路径生效()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithAP(0, 3).Build();
            var c = state.GetCharacterByPlayer(0);

            // 给角色挂一个带冷却技能的自定义定义（index 0 = 冷却技能），走生产路径 UseSkillCommand
            var def = new CharacterDef
            {
                Id = 999,
                Name = "测试冷却角色",
                Skills = new[] { CooldownSkill() }
            };
            c.Definition = def;
            c.ASC.EnsureCooldownSlots(def.Skills.Length);

            var cmd = new UseSkillCommand(state.CurrentTick, 0, 0, 0);
            cmd.Execute(state);

            Assert.IsTrue(c.ASC.GetCooldown(0) > 0, "UseSkillCommand 转发 SkillIndex 后冷却应生效");
        }
    }
}
