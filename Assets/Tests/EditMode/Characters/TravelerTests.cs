using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Movement;

namespace Dynasty.Tests.Characters
{
    public class TravelerTests
    {
        private static void Execute(WorldState state, Core.Command.ICommand cmd)
        {
            state.Commands.Enqueue(cmd);
            state.Commands.ExecuteAll(state);
            state.Commands.Clear();
        }

        private static WorldState BuildWithTraveler()
        {
            var state = ScenarioBuilder.Create(4).WithAllAt(4).Build();
            var t = state.GetCharacterByPlayer(3);
            t.Definition = TravelerDef.Create();
            t.Definition.ApplyTo(t);
            return state;
        }

        [Test]
        public void 旅行_1AP前往任意地点()
        {
            var state = BuildWithTraveler();
            var t = state.GetCharacterByPlayer(3);
            t.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);

            Execute(state, new MoveCommand(0, 3, 8)); // 任意地点：地狱前哨

            Assert.AreEqual(8, t.CurrentLocationId, "任意地点可达");
            Assert.AreEqual(0, t.ActionPoints);
        }

        [Test]
        public void 旅途的准备_攒2充能后免费旅行()
        {
            var state = BuildWithTraveler();
            var t = state.GetCharacterByPlayer(3);
            t.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            SkillDef prep = t.Definition.Skills[1];

            SkillSystem.Activate(prep, t, t, state, 1); // 1AP 攒 1 充能
            Assert.AreEqual(1, t.ASC.GetGuardianCharge(), "充能 1");
            SkillSystem.Activate(prep, t, t, state, 1); // 1AP 攒 2 充能
            Assert.AreEqual(2, t.ASC.GetGuardianCharge(), "充能 2");

            // 满 2 充能：0AP 免费旅行（目标地点由命令层指定——此处用 MoveCommand 到 9）
            Execute(state, new MoveCommand(0, 3, 9));
            Assert.AreEqual(9, t.CurrentLocationId);
            Assert.AreEqual(1, t.ActionPoints, "攒充能花 2 AP，免费旅行不扣");
        }

        [Test]
        public void 免费旅行消费后充能清零()
        {
            var state = BuildWithTraveler();
            var t = state.GetCharacterByPlayer(3);
            t.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            SkillDef prep = t.Definition.Skills[1];

            SkillSystem.Activate(prep, t, t, state, 1); // 攒 1
            SkillSystem.Activate(prep, t, t, state, 1); // 攒 2 → 满，授予免费旅行标记
            Assert.AreEqual(2, t.ASC.GetGuardianCharge(), "满充能");

            Execute(state, new MoveCommand(0, 3, 9));
            Assert.AreEqual(0, t.ASC.GetGuardianCharge(), "免费旅行消费后充能清零");
        }
    }
}
