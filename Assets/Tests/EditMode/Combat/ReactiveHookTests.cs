using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.Combat
{
    public class ReactiveHookTests
    {
        // 测试用响应技能：OnPreDamage 触发时把伤害翻倍（胜）或归零（败）的语义由特例实现，
        // 此处验证钩子被调用且能改伤害。用 skillId 900 的假 Reactive 技能注册到 ReactiveSystem 测试分派
        private static CharacterDef HookDef()
        {
            var def = GuardianDef.Create();
            def.Skills = new[]
            {
                new SkillDef
                {
                    Id = 900,
                    Name = "测试响应",
                    Activation = SkillActivationType.Reactive,
                    ReactiveTrigger = ReactiveTriggerType.OnPreDamage
                }
            };
            return def;
        }

        [Test]
        public void OnPreDamage钩子_响应技能被分派()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithSanity(1, 3f).Build();
            var target = state.GetCharacterByPlayer(1);
            target.Definition = HookDef(); // 替换定义为带 Reactive 的角色

            // ReactiveSystem 测试分派：Id=900 的语义 = 伤害 +1（简化可测语义）
            var src = state.GetCharacterByPlayer(0);
            float dmg = DamagePipeline.ApplyDamage(state, src.Id, target.Id, 1f, DamageType.Physical);

            Assert.AreEqual(2f, dmg, 0.001f, "响应技能对伤害 +1");
            Assert.AreEqual(1f, target.Sanity, 0.001f, "3 - 2 = 1");
        }

        [Test]
        public void 无响应技能_伤害不变()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithSanity(1, 3f).Build();
            var src = state.GetCharacterByPlayer(0);
            var target = state.GetCharacterByPlayer(1); // 魑魅无 Reactive 技能

            float dmg = DamagePipeline.ApplyDamage(state, src.Id, target.Id, 1f, DamageType.Physical);

            Assert.AreEqual(1f, dmg, 0.001f);
            Assert.AreEqual(2f, target.Sanity, 0.001f);
        }

        // 测试用 OnKill 响应技能：击杀时给击杀者 +1 AttackBonus。
        // 注意：TryOnKill 按「击杀者」的技能表分派（与 Task 6 倾听者设计一致），故挂到击杀者身上。
        private static CharacterDef OnKillHookDef()
        {
            var def = GuardianDef.Create();
            def.Skills = new[]
            {
                new SkillDef
                {
                    Id = 900,
                    Name = "测试响应",
                    Activation = SkillActivationType.Reactive,
                    ReactiveTrigger = ReactiveTriggerType.OnKill
                }
            };
            return def;
        }

        [Test]
        public void OnKill钩子_击杀时触发()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithSanity(1, 0.5f).Build();
            var target = state.GetCharacterByPlayer(1);
            var src = state.GetCharacterByPlayer(0);
            src.Definition = OnKillHookDef(); // 击杀者带 OnKill 响应技能，测试分派：+1 AttackBonus

            DamagePipeline.ApplyDamage(state, src.Id, target.Id, 1f, DamageType.Physical);

            Assert.IsTrue(target.Sanity <= 0f, "目标已死");
            Assert.AreEqual(1f, src.ASC.GetAttributeValue(AttributeConstants.AttackBonus), 0.001f, "击杀钩子生效");
        }
    }
}
