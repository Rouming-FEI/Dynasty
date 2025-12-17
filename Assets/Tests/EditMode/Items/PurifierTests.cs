using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Items;

namespace Dynasty.Tests.Items
{
    public class PurifierTests
    {
        private static void Execute(WorldState state, Core.Command.ICommand cmd)
        {
            state.Commands.Enqueue(cmd);
            state.Commands.ExecuteAll(state);
            state.Commands.Clear();
        }

        [Test]
        public void 净化药剂_清除负面状态并挂增益()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithAP(0, 1)
                .WithClearInventory(0) // 清掉开局默认血包，让净化药剂落在 index 0
                .WithItem(0, 9).Build();
            var c = state.GetCharacterByPlayer(0);
            c.ASC.Tags.AddTag(GameplayTagConstants.TimeStop);    // 负面（静止时空）
            c.ASC.Tags.AddTag(GameplayTagConstants.MistState);   // 负面（迷离）
            c.ASC.Tags.AddTag(GameplayTagConstants.HasShield);   // 正面，不应清除

            Execute(state, new UseItemCommand(0, 0, 0, 0)); // 使用背包 index 0

            Assert.IsFalse(c.ASC.Tags.HasTag(GameplayTagConstants.TimeStop), "负面状态清除");
            Assert.IsFalse(c.ASC.Tags.HasTag(GameplayTagConstants.MistState), "负面状态清除");
            Assert.IsTrue(c.ASC.Tags.HasTag(GameplayTagConstants.HasShield), "正面状态保留");
            Assert.AreEqual(0.5f, c.ASC.GetAttributeValue(AttributeConstants.PhysicalDefense), 0.001f, "防御 +0.5");
            Assert.AreEqual(0.5f, c.ASC.GetAttributeValue(AttributeConstants.AttackBonus), 0.001f, "攻击 +0.5");
            Assert.AreEqual(0, c.ActionPoints, "消耗 1 AP");
        }

        [Test]
        public void 净化药剂_实验室中使用0AP()
        {
            var state = ScenarioBuilder.Create(2)
                .WithCharacterAt(0, 7) // 实验室
                .WithAP(0, 1)
                .WithClearInventory(0) // 清掉开局默认血包，让净化药剂落在 index 0
                .WithItem(0, 9)
                .Build();
            var c = state.GetCharacterByPlayer(0);
            var lab = (Gameplay.Locations.LabRule)state.Locations.Rules[7];
            lab.DoorOpen = true;

            Execute(state, new UseItemCommand(0, 0, 0, 0));

            Assert.AreEqual(1, c.ActionPoints, "实验室中 0 AP");
        }
    }
}
