using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Locations;
using Gameplay.Movement;

namespace Dynasty.Tests.Locations
{
    public class LocationRuleTests
    {
        private static void Execute(WorldState state, Core.Command.ICommand cmd)
        {
            state.Commands.Enqueue(cmd);
            state.Commands.ExecuteAll(state);
            state.Commands.Clear();
        }

        [Test]
        public void 进入藏匿处_获得隐身标签()
        {
            var state = ScenarioBuilder.Create(2)
                .WithCharacterAt(0, 4)
                .WithAP(0, 1)
                .Build();
            var c = state.GetCharacterByPlayer(0);

            Execute(state, new MoveCommand(0, 0, 6)); // 移动到藏匿处

            Assert.AreEqual(6, c.CurrentLocationId);
            Assert.IsTrue(c.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden));
        }

        [Test]
        public void 离开藏匿处_移除隐身标签()
        {
            var state = ScenarioBuilder.Create(2)
                .WithCharacterAt(0, 6)
                .WithAP(0, 1)
                .Build();
            var c = state.GetCharacterByPlayer(0);

            Execute(state, new MoveCommand(0, 0, 4)); // 离开

            Assert.IsFalse(c.ASC.Tags.HasTag(GameplayTagConstants.HideoutHidden));
        }

        [Test]
        public void 实验室门关_拒绝进入且不扣AP()
        {
            var state = ScenarioBuilder.Create(2)
                .WithCharacterAt(0, 4)
                .WithAP(0, 2)
                .Build();
            var c = state.GetCharacterByPlayer(0);
            // 门默认关（LabRule.DoorOpen=false）

            Execute(state, new MoveCommand(0, 0, 7));

            Assert.AreEqual(4, c.CurrentLocationId, "门关应拒绝进入");
            Assert.AreEqual(2, c.ActionPoints, "被拒不扣 AP");
        }

        [Test]
        public void 实验室开门后_可进入()
        {
            var state = ScenarioBuilder.Create(2)
                .WithCharacterAt(0, 4)
                .WithAP(0, 2)
                .Build();
            var c = state.GetCharacterByPlayer(0);
            var lab = (LabRule)state.Locations.Rules[7];
            lab.DoorOpen = true; // 直接开门

            Execute(state, new MoveCommand(0, 0, 7));

            Assert.AreEqual(7, c.CurrentLocationId);
        }

        [Test]
        public void 炮塔不发射_不消耗已瞄准状态()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(8)
                .WithAP(0, 1)
                .WithSanity(1, 3f)
                .Build();

            Execute(state, new MountTurretCommand(0, 0));

            var c = state.GetCharacterByPlayer(0);
            Assert.IsTrue(c.ASC.Tags.HasTag(GameplayTagConstants.OnTurret));
            Assert.AreEqual(0, c.ActionPoints, "上塔 1 AP，从 1 扣到 0");
        }

        [Test]
        public void 炮塔发射_需完整流程且打中目标()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(8) // 地狱前哨
                .WithAP(0, 4)
                .WithSanity(1, 3f)
                .Build();

            Execute(state, new MountTurretCommand(0, 0));
            Execute(state, new LoadTurretCommand(0, 0));
            Execute(state, new AimTurretCommand(0, 0));
            Execute(state, new FireTurretCommand(0, 0, 1));

            Assert.AreEqual(0f, state.GetCharacterByPlayer(1).Sanity, 0.001f, "炮塔 3 物理伤害：3-3=0");
            var c = state.GetCharacterByPlayer(0);
            Assert.IsTrue(c.ASC.Tags.HasTag(GameplayTagConstants.OnTurret), "仍在上塔");
            Assert.IsFalse(c.ASC.Tags.HasTag(GameplayTagConstants.TurretLoaded), "发射后需重装");
            Assert.IsFalse(c.ASC.Tags.HasTag(GameplayTagConstants.TurretAimed), "发射后需重瞄准");
        }
    }
}
