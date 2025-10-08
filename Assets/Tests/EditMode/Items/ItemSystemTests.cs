using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Items;

namespace Dynasty.Tests.Items
{
    public class ItemSystemTests
    {
        private static void Execute(WorldState state, Core.Command.ICommand cmd)
        {
            state.Commands.Enqueue(cmd);
            state.Commands.ExecuteAll(state);
            state.Commands.Clear();
        }

        [Test]
        public void 血包恢复理智_不超过上限()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithSanity(0, 2.5f)
                .WithAP(0, 1)
                .WithItem(0, 2) // 血包进背包 index 0
                .Build();

            Execute(state, new UseItemCommand(0, 0, 0, 0));

            Assert.AreEqual(3f, state.GetCharacterByPlayer(0).Sanity, 0.001f, "2.5+3 应钳到 MaxSanity 3");
        }

        [Test]
        public void 使用血包消耗AP()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithSanity(0, 1f)
                .WithAP(0, 1)
                .WithItem(0, 2)
                .Build();

            Execute(state, new UseItemCommand(0, 0, 0, 0));

            Assert.AreEqual(0, state.GetCharacterByPlayer(0).ActionPoints, "血包 1 AP");
        }

        [Test]
        public void 枪无弹药_射击失败且不扣AP()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithAP(0, 1)
                .WithClearInventory(0)
                .WithEquippedWeapon(0, 3, ammo: 0) // 枪 0 弹
                .WithSanity(1, 3f)
                .Build();

            Execute(state, new FireGunCommand(0, 0, 1));

            Assert.AreEqual(1, state.GetCharacterByPlayer(0).ActionPoints, "无弹不扣 AP");
            Assert.AreEqual(3f, state.GetCharacterByPlayer(1).Sanity, 0.001f, "未造成伤害");
        }

        [Test]
        public void 装弹后射击_扣弹药并造成伤害()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithAP(0, 2)
                .WithClearInventory(0)
                .WithEquippedWeapon(0, 3, ammo: 0)
                .WithSanity(1, 3f)
                .Build();

            Execute(state, new ReloadCommand(0, 0));
            Assert.AreEqual(1, state.GetCharacterByPlayer(0).Inventory.EquippedWeaponAmmo, "装弹后 1 发");

            Execute(state, new FireGunCommand(0, 0, 1));

            Assert.AreEqual(0, state.GetCharacterByPlayer(0).Inventory.EquippedWeaponAmmo, "射击后 0 发");
            Assert.AreEqual(1f, state.GetCharacterByPlayer(1).Sanity, 0.001f, "枪 2 物理伤害：3-2=1");
        }

        [Test]
        public void 陷阱_多个炸弹触发多次伤害()
        {
            var state = ScenarioBuilder.Create(2)
                .WithAllAt(4)
                .WithSanity(0, 3f)
                .WithSanity(1, 3f)
                .WithoutTag(0, GameplayTagConstants.HasShield) // 守护者开局带盾，去掉以免吸收首个炸弹
                .WithTrapAt(4, count: 2) // 地点 4 埋 2 个炸弹
                .Build();

            // 触发：2 个炸弹 = 2×1.5 = 3 效果伤害，地点内所有角色受伤
            Gameplay.Items.TrapSystem.CheckTrigger(state, 4, state.GetCharacterByPlayer(0));

            Assert.AreEqual(0f, state.GetCharacterByPlayer(0).Sanity, 0.001f, "2 个炸弹 = 2×1.5 = 3 伤害");
            Assert.AreEqual(0f, state.GetCharacterByPlayer(1).Sanity, 0.001f);
        }

        [Test]
        public void 拾取武器_进装备位而非背包()
        {
            var state = ScenarioBuilder.Create(2).WithAllAt(4).WithAP(0, 1).WithClearInventory(0).Build();
            var c = state.GetCharacterByPlayer(0);

            Execute(state, new PickupItemCommand(0, 0, 3)); // 枪

            Assert.IsNotNull(c.Inventory.EquippedWeapon, "武器应进装备位");
            Assert.AreEqual(3, c.Inventory.EquippedWeapon.Id);
            Assert.AreEqual(0, c.Inventory.Consumables.Count, "武器不进背包");
        }
    }
}
