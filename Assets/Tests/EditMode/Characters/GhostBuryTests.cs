using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;

namespace Dynasty.Tests.Characters
{
    public class GhostBuryTests
    {
        private static void CastGhostBury(WorldState state, int playerId)
        {
            var c = state.GetCharacterByPlayer(playerId);
            SkillSystem.Activate(c.Definition.Skills[1], c, c, state); // 魑魅 [1]=鬼葬
        }

        [Test]
        public void 鬼葬_所在地仅自己_每回合末回复05理智()
        {
            var state = ScenarioBuilder.Create(2)
                .WithCharacterAt(1, 4)
                .WithCharacterAt(0, 6) // 自己单独在藏匿处
                .WithAP(1, 1)
                .WithSanity(1, 2f)
                .Build();
            CastGhostBury(state, 1);

            state.GetCharacterByPlayer(1).ASC.TickEffects(state); // 第 1 回合末

            Assert.AreEqual(2.5f, state.GetCharacterByPlayer(1).Sanity, 0.001f, "1 人：+0.5 回复");
        }

        [Test]
        public void 鬼葬_两人同地点_对方受1法术效果伤_自己受05()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithAP(1, 1)
                .WithSanity(0, 3f)
                .WithSanity(1, 3f)
                .WithoutTag(0, GameplayTagConstants.HasShield) // 守护者开局护盾会吸收效果伤
                .Build();
            CastGhostBury(state, 1);

            state.GetCharacterByPlayer(1).ASC.TickEffects(state); // 对方与自己的 Tick 独立，效果由魑魅的 GE 结算

            Assert.AreEqual(2f, state.GetCharacterByPlayer(0).Sanity, 0.001f, "对方受 1 法术效果伤");
            Assert.AreEqual(2.5f, state.GetCharacterByPlayer(1).Sanity, 0.001f, "自己受 0.5");
        }

        [Test]
        public void 鬼葬_三人同地点_自己受1_其余各受05()
        {
            var state = ScenarioBuilder.Create(3)
                .WithAllAt(4)
                .WithAP(1, 1)
                .WithSanity(0, 3f)
                .WithSanity(1, 3f)
                .WithSanity(2, 3f)
                .WithoutTag(0, GameplayTagConstants.HasShield)
                .Build();
            CastGhostBury(state, 1);

            state.GetCharacterByPlayer(1).ASC.TickEffects(state);

            Assert.AreEqual(2.5f, state.GetCharacterByPlayer(0).Sanity, 0.001f, "其余人受 0.5");
            Assert.AreEqual(2f, state.GetCharacterByPlayer(1).Sanity, 0.001f, "自己受 1");
            Assert.AreEqual(2.5f, state.GetCharacterByPlayer(2).Sanity, 0.001f, "其余人受 0.5");
        }
    }
}
