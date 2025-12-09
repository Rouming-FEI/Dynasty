using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.Characters
{
    public class ListenerTests
    {
        private static WorldState BuildWithListener()
        {
            var state = ScenarioBuilder.Create(4).WithAllAt(4).Build();
            var l = state.GetCharacterByPlayer(3);
            l.Definition = ListenerDef.Create();
            l.Definition.ApplyTo(l);
            // ApplyTo 不含被动注册（生产路径注册在 TurnManager.StartGame），测试需手动注册
            for (int s = 0; s < l.Definition.Skills.Length; s++)
                l.ASC.RegisterPassive(l.Definition.Skills[s]);
            return state;
        }

        [Test]
        public void 呓语_每2回合末判定_两态确定性()
        {
            var s1 = BuildWithListener();
            var s2 = BuildWithListener();
            var l1 = s1.GetCharacterByPlayer(3);
            var l2 = s2.GetCharacterByPlayer(3);

            l1.ASC.TickEffects(s1); // 第 1 回合（未到期）
            l2.ASC.TickEffects(s2);
            float atk1a = l1.ASC.GetAttributeValue(AttributeConstants.AttackBonus);
            float atk2a = l2.ASC.GetAttributeValue(AttributeConstants.AttackBonus);
            Assert.AreEqual(0f, atk1a, 0.001f, "第 1 回合不触发");
            Assert.AreEqual(atk1a, atk2a, 0.001f);

            l1.ASC.TickEffects(s1); // 第 2 回合（到期）
            l2.ASC.TickEffects(s2);
            float atk1b = l1.ASC.GetAttributeValue(AttributeConstants.AttackBonus);
            float atk2b = l2.ASC.GetAttributeValue(AttributeConstants.AttackBonus);
            float ero1b = l1.ASC.GetAttributeValue(AttributeConstants.ErosionLimit);
            float ero2b = l2.ASC.GetAttributeValue(AttributeConstants.ErosionLimit);
            Assert.AreEqual(atk1b, atk2b, 0.001f, "确定性");
            // 两态：胜 → AttackBonus +0.5；败 → ErosionLimit -1
            bool winState = atk1b > 0f && ero1b == 4f;
            bool loseState = atk1b == 0f && ero1b == 3f;
            Assert.IsTrue(winState || loseState, $"atk={atk1b}, ero={ero1b}");
        }

        [Test]
        public void 呓语_击杀后理智与侵蚀回复至4()
        {
            var state = BuildWithListener();
            var l = state.GetCharacterByPlayer(3);
            l.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1f);
            l.ASC.SetAttributeBaseValue(AttributeConstants.ErosionLimit, 2f);

            ListenerWhisper.OnKill(l);

            Assert.AreEqual(3f, l.Sanity, 0.001f, "理智回复（钳 MaxSanity 3）");
            Assert.AreEqual(4f, l.ASC.GetAttributeValue(AttributeConstants.ErosionLimit), 0.001f, "侵蚀回复至 4");
        }
    }
}
