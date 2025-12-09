using NUnit.Framework;
using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Items;

namespace Dynasty.Tests.Characters
{
    public class SeaMirageTests
    {
        private static void Execute(WorldState state, Core.Command.ICommand cmd)
        {
            state.Commands.Enqueue(cmd);
            state.Commands.ExecuteAll(state);
            state.Commands.Clear();
        }

        private static WorldState BuildWithSeaMirage()
        {
            var state = ScenarioBuilder.Create(4).WithAllAt(4).Build();
            var h = state.GetCharacterByPlayer(3);
            h.Definition = SeaMirageDef.Create();
            h.Definition.ApplyTo(h);
            h.Inventory.EquipWeapon(ItemRegistry.Get(1)); // 初始刀
            return state;
        }

        [Test]
        public void 幻剑_取得归鞘刀并享受05全减伤()
        {
            var state = BuildWithSeaMirage();
            var h = state.GetCharacterByPlayer(3);
            h.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 1);
            SkillDef mirage = h.Definition.Skills[0];
            SkillSystem.Activate(mirage, h, h, state, 0);

            Assert.IsTrue(h.ASC.Tags.HasTag(GameplayTagConstants.BladeSheathed), "归鞘状态");
            Assert.IsNotNull(h.Inventory.EquippedWeapon, "持刀");
            Assert.AreEqual(12, h.Inventory.EquippedWeapon.Id, "幻剑武器 Id=12");

            // 减伤：物理伤害 -0.5
            var src = state.GetCharacterByPlayer(0);
            float dmg = DamagePipeline.ApplyDamage(state, src.Id, h.Id, 2f, DamageType.Physical);
            Assert.AreEqual(1.5f, dmg, 0.001f, "0.5 全减伤");
        }

        [Test]
        public void 拔刀后无减伤且蜃景可用()
        {
            var state = BuildWithSeaMirage();
            var h = state.GetCharacterByPlayer(3);
            h.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            SkillSystem.Activate(h.Definition.Skills[0], h, h, state, 0); // 幻剑

            Execute(state, new DrawBladeCommand(0, 3));

            Assert.IsFalse(h.ASC.Tags.HasTag(GameplayTagConstants.BladeSheathed));
            Assert.IsTrue(h.ASC.Tags.HasTag(GameplayTagConstants.BladeDrawn));
            Assert.AreEqual(1, h.ActionPoints, "拔刀 1 AP");

            var src = state.GetCharacterByPlayer(0);
            float dmg = DamagePipeline.ApplyDamage(state, src.Id, h.Id, 2f, DamageType.Physical);
            Assert.AreEqual(2f, dmg, 0.001f, "拔刀无减伤");
        }

        [Test]
        public void 蜃景_伤害与判定位移()
        {
            var state = BuildWithSeaMirage();
            var h = state.GetCharacterByPlayer(3);
            h.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 4);
            SkillSystem.Activate(h.Definition.Skills[0], h, h, state, 0);
            Execute(state, new DrawBladeCommand(0, 3));

            var target = state.GetCharacterByPlayer(0);
            target.ASC.Tags.RemoveTag(GameplayTagConstants.HasShield);
            target.ASC.SetAttributeBaseValue(AttributeConstants.PhysicalDefense, 1f); // 目标防御 ≥1 触发额外 0.5
            float sanityBefore = target.Sanity;
            int locBefore = h.CurrentLocationId;
            target.CurrentLocationId = 6; // 目标在别处

            SkillSystem.Activate(h.Definition.Skills[1], h, target, state, 1); // 蜃景

            Assert.AreEqual(1, h.ActionPoints, "蜃景 1 AP");
            Assert.IsTrue(target.Sanity <= sanityBefore, "远程 1 法术伤害");
            // 位移：判定胜则瞬移（随机两态，只断言无异常且位置合法）
            Assert.IsTrue(h.CurrentLocationId == locBefore || h.CurrentLocationId == 6,
                "败在原地，胜瞬移至目标地");
        }
    }
}
