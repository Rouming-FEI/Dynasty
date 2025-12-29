using System;
using Core;
using Core.GAS;
using Gameplay.Characters;
using NUnit.Framework;

namespace Dynasty.Tests
{
    /// <summary>StartGame(defs) 注入重载：默认回退、自定义注入、长度校验。</summary>
    public class TurnManagerInjectionTests
    {
        private static WorldState NewState()
        {
            var state = new WorldState(4, 511);
            state.TurnManager = new TurnManager();
            return state;
        }

        [Test]
        public void StartGame_WithNullDefs_FallsBackToDefaultRotation()
        {
            WorldState state = NewState();

            state.TurnManager.StartGame(state, null);

            // 默认轮换：守护者/魑魅/普通人/守护者
            Assert.AreEqual(1, state.Characters[0].Definition.Id);
            Assert.AreEqual(2, state.Characters[1].Definition.Id);
            Assert.AreEqual(3, state.Characters[2].Definition.Id);
            Assert.AreEqual(1, state.Characters[3].Definition.Id);
        }

        [Test]
        public void StartGame_WithCustomDefs_AppliesEachDef_AndPaladinDropsShield()
        {
            WorldState state = NewState();
            CharacterDef[] defs =
            {
                SwordGhostDef.Create(), // id 4
                PaladinDef.Create(),    // id 12
                AgentDef.Create(),      // id 13
                ListenerDef.Create()    // id 8
            };

            state.TurnManager.StartGame(state, defs);

            Assert.AreEqual(4, state.Characters[0].Definition.Id);
            Assert.AreEqual(12, state.Characters[1].Definition.Id);
            Assert.AreEqual(13, state.Characters[2].Definition.Id);
            Assert.AreEqual(8, state.Characters[3].Definition.Id);

            // 圣骑士(P1) 出生地点 1，开局掉落一件盾(id 13)；该地点初始为空，剩余数恰为 1
            Assert.AreEqual(1, state.Locations.GetRemainingItemCount(1));

            // 代行者(P2, id 13) 被动：NoStatus 常驻
            Assert.IsTrue(state.Characters[2].ASC.Tags.HasTag(GameplayTagConstants.NoStatus));
        }

        [Test]
        public void StartGame_WithWrongLengthDefs_Throws()
        {
            WorldState state = NewState();
            CharacterDef[] defs = { GuardianDef.Create() };

            Assert.Throws<ArgumentException>(
                () => state.TurnManager.StartGame(state, defs));
        }
    }
}
