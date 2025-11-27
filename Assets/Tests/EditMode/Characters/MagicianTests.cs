using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.Characters
{
    public class MagicianTests
    {
        private static WorldState BuildWithMagician()
        {
            var state = ScenarioBuilder.Create(4).WithAllAt(4).Build();
            var m = state.GetCharacterByPlayer(3);
            m.Definition = MagicianDef.Create();
            m.Definition.ApplyTo(m);
            return state;
        }

        [Test]
        public void 镜花水月_受伤害时判定_结果两态且确定性()
        {
            var s1 = BuildWithMagician();
            var s2 = BuildWithMagician();
            var m1 = s1.GetCharacterByPlayer(3);
            var m2 = s2.GetCharacterByPlayer(3);
            var src1 = s1.GetCharacterByPlayer(0);
            var src2 = s2.GetCharacterByPlayer(0);

            float d1 = DamagePipeline.ApplyDamage(s1, src1.Id, m1.Id, 2f, DamageType.Physical);
            float d2 = DamagePipeline.ApplyDamage(s2, src2.Id, m2.Id, 2f, DamageType.Physical);

            Assert.AreEqual(d1, d2, 0.001f, "同 seed 确定性");
            // 简化版语义：胜 → 0 伤害；败 → 3（2+1）
            Assert.IsTrue(d1 == 0f || d1 == 3f, $"伤害应为 0 或 3，实际 {d1}");
        }

        [Test]
        public void 镜花水月_非魔术师角色不受影响()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithSanity(1, 3f).Build();
            var src = state.GetCharacterByPlayer(0);
            var t = state.GetCharacterByPlayer(1);

            float d = DamagePipeline.ApplyDamage(state, src.Id, t.Id, 2f, DamageType.Physical);

            Assert.AreEqual(2f, d, 0.001f, "魑魅无镜花水月");
        }
    }
}
