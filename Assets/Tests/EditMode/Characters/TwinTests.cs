using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
using NUnit.Framework;

namespace Dynasty.Tests.Characters
{
    public class TwinTests
    {
        private const int TwinPlayer = 3;

        /// <summary>4 人局 slot3 换成双子（真实 StartGame 发放初始道具），并装备刀。</summary>
        private static WorldState BuildWithTwin(int seed = 42)
        {
            var state = ScenarioBuilder.Create(4, seed).WithAllAt(4).Build();
            var old = state.GetCharacterByPlayer(TwinPlayer);
            var def = TwinDef.Create();
            var c = new Character(old.Id, old.PlayerId, def.Name) { CurrentLocationId = 4 };
            def.ApplyTo(c);
            c.Inventory.Consumables.Clear();
            c.Inventory.EquipWeapon(ItemRegistry.Get(1)); // 刀
            c.Inventory.EquippedWeaponAmmo = 0;
            state.Characters[TwinPlayer] = c;
            return state;
        }

        private static SkillDef Skill(Character c, int skillId)
        {
            for (int i = 0; i < c.Definition.Skills.Length; i++)
                if (c.Definition.Skills[i].Id == skillId) return c.Definition.Skills[i];
            return null;
        }

        private static int IndexOf(Character c, int skillId)
        {
            for (int i = 0; i < c.Definition.Skills.Length; i++)
                if (c.Definition.Skills[i].Id == skillId) return i;
            return -1;
        }

        [Test]
        public void 无武器_两附灵技能均被拒且不扣AP()
        {
            var s = BuildWithTwin();
            var twin = s.GetCharacterByPlayer(TwinPlayer);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            twin.Inventory.EquippedWeapon = null;

            Character target = twin;
            SkillSystem.Activate(Skill(twin, SkillSystem.TwinVampiricSkillId), twin, target, s, 0);
            Assert.AreEqual(2, twin.ActionPoints, "无武器施放 123 不扣 AP");
            SkillSystem.Activate(Skill(twin, SkillSystem.TwinAgileSkillId), twin, target, s, 1);
            Assert.AreEqual(2, twin.ActionPoints, "无武器施放 124 不扣 AP");
            Assert.IsFalse(twin.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric));
            Assert.IsFalse(twin.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile));
        }

        [Test]
        public void 附灵泣血_施放后挂标签扣1AP()
        {
            var s = BuildWithTwin();
            var twin = s.GetCharacterByPlayer(TwinPlayer);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            SkillSystem.Activate(Skill(twin, SkillSystem.TwinVampiricSkillId), twin, twin, s, 0);
            Assert.IsTrue(twin.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric), "泣血标签挂载");
            Assert.AreEqual(1, twin.ActionPoints, "消耗 1 AP");
        }

        [Test]
        public void 泣血态_武器攻击伤害加05且命中回血()
        {
            var s = BuildWithTwin();
            var twin = s.GetCharacterByPlayer(TwinPlayer);
            var victim = s.GetCharacterByPlayer(1);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f); // 先受伤，回血空间 1
            twin.ASC.Tags.AddTag(GameplayTagConstants.TwinVampiric);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);

            new DamageCommand(0, TwinPlayer, 1, 1f).Execute(s); // 普攻 1（UI/AI 普攻量）

            Assert.AreEqual(1.5f, victim.Sanity, 0.001f, "泣血武器伤害 +0.5：1 → 1.5");
            Assert.AreEqual(2.5f, twin.Sanity, 0.001f, "每次攻击命中回 0.5（2 → 2.5）");
            Assert.AreEqual(2, twin.ActionPoints, "普攻扣 1 AP");
        }

        [Test]
        public void 无泣血标签_攻击无加成()
        {
            var s = BuildWithTwin();
            var twin = s.GetCharacterByPlayer(TwinPlayer);
            var victim = s.GetCharacterByPlayer(1);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f); // 不满血，回血不被 MaxSanity 钳制掩盖
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);

            new DamageCommand(0, TwinPlayer, 1, 1f).Execute(s);
            Assert.AreEqual(2f, victim.Sanity, 0.001f, "无加成普攻 1");
            Assert.AreEqual(2f, twin.Sanity, 0.001f, "无附灵不回血");
        }

        [Test]
        public void 泣血态_任意武器伤害量均加05()
        {
            var s = BuildWithTwin();
            var twin = s.GetCharacterByPlayer(TwinPlayer);
            var victim = s.GetCharacterByPlayer(1);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f);
            twin.ASC.Tags.AddTag(GameplayTagConstants.TwinVampiric);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);

            new DamageCommand(0, TwinPlayer, 1, 1.5f).Execute(s); // 刀量级伤害，攻防 0

            Assert.AreEqual(1f, victim.Sanity, 0.001f, "1.5 + 0.5 泣血加成 = 2.0 伤害");
            Assert.AreEqual(2.5f, twin.Sanity, 0.001f, "攻击回 0.5");
        }

        [Test]
        public void 泣血态_换武器标签保留效果仍生效()
        {
            var s = BuildWithTwin();
            var twin = s.GetCharacterByPlayer(TwinPlayer);
            var victim = s.GetCharacterByPlayer(1);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f);
            twin.ASC.Tags.AddTag(GameplayTagConstants.TwinVampiric);

            twin.Inventory.EquipWeapon(ItemRegistry.Get(3)); // 刀 → 枪（S3：附灵作用于角色，换武器保留）
            Assert.IsTrue(twin.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric), "换武器后泣血标签保留");

            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            new DamageCommand(0, TwinPlayer, 1, 1f).Execute(s);
            Assert.AreEqual(1.5f, victim.Sanity, 0.001f, "换枪后泣血 +0.5 仍生效");
        }

        [Test]
        public void 灵巧态_转换武器_刀换枪成功弹药归零()
        {
            var s = BuildWithTwin();
            var twin = s.GetCharacterByPlayer(TwinPlayer);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            twin.ASC.Tags.AddTag(GameplayTagConstants.TwinAgile);
            twin.Inventory.EquippedWeaponAmmo = 1; // 预置弹药：删除转换归零实现必须红

            new TwinConvertCommand(0, TwinPlayer, 3).Execute(s); // 刀 → 枪

            Assert.IsNotNull(twin.Inventory.EquippedWeapon);
            Assert.AreEqual(3, twin.Inventory.EquippedWeapon.Id, "装备枪");
            Assert.AreEqual(0, twin.Inventory.EquippedWeaponAmmo, "转换弹药强制归零（需装填）");
            Assert.AreEqual(2, twin.ActionPoints, "转换扣 1 AP");
        }

        [Test]
        public void 灵巧态_持圣盾转刀_卸下清理EquipEffect321()
        {
            var s = BuildWithTwin();
            var twin = s.GetCharacterByPlayer(TwinPlayer);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            twin.ASC.Tags.AddTag(GameplayTagConstants.TwinAgile);

            new PickupItemCommand(0, TwinPlayer, 13).Execute(s); // 圣盾 13（拾取无角色限制）
            Assert.AreEqual(13, twin.Inventory.EquippedWeapon.Id, "拾取后装备圣盾");
            Assert.IsTrue(twin.ASC.HasActiveEffect(321), "圣盾 EquipEffect 321 挂载");

            new TwinConvertCommand(0, TwinPlayer, 1).Execute(s); // 圣盾 → 刀

            Assert.AreEqual(1, twin.Inventory.EquippedWeapon.Id, "转换到刀");
            Assert.IsFalse(twin.ASC.HasActiveEffect(321), "转换卸下圣盾时 EquipEffect 321 被清理");
        }

        [Test]
        public void 非灵巧态_转换被拒不扣AP()
        {
            var s = BuildWithTwin();
            var twin = s.GetCharacterByPlayer(TwinPlayer);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            new TwinConvertCommand(0, TwinPlayer, 3).Execute(s);
            Assert.AreEqual(1, twin.Inventory.EquippedWeapon.Id, "武器未变");
            Assert.AreEqual(2, twin.ActionPoints, "非灵巧态转换不扣 AP");
        }

        [Test]
        public void 切换_123到124到123_各扣1AP效果互换()
        {
            var s = BuildWithTwin();
            var twin = s.GetCharacterByPlayer(TwinPlayer);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);

            SkillSystem.Activate(Skill(twin, SkillSystem.TwinVampiricSkillId), twin, twin, s, 0);
            Assert.AreEqual(2, twin.ActionPoints);
            Assert.IsTrue(twin.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric));

            SkillSystem.Activate(Skill(twin, SkillSystem.TwinAgileSkillId), twin, twin, s, 1);
            Assert.AreEqual(1, twin.ActionPoints);
            Assert.IsTrue(twin.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile), "切到灵巧");
            Assert.IsFalse(twin.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric), "泣血被替换");

            SkillSystem.Activate(Skill(twin, SkillSystem.TwinVampiricSkillId), twin, twin, s, 0);
            Assert.AreEqual(0, twin.ActionPoints);
            Assert.IsTrue(twin.ASC.Tags.HasTag(GameplayTagConstants.TwinVampiric), "切回泣血");
            Assert.IsFalse(twin.ASC.Tags.HasTag(GameplayTagConstants.TwinAgile), "灵巧被替换");
        }

        [Test]
        public void 空手转换被拒_池外武器被拒()
        {
            var s = BuildWithTwin();
            var twin = s.GetCharacterByPlayer(TwinPlayer);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            twin.ASC.Tags.AddTag(GameplayTagConstants.TwinAgile);

            twin.Inventory.EquippedWeapon = null;
            new TwinConvertCommand(0, TwinPlayer, 3).Execute(s);
            Assert.AreEqual(2, twin.ActionPoints, "空手转换不扣 AP");

            twin.Inventory.EquipWeapon(ItemRegistry.Get(1));
            new TwinConvertCommand(0, TwinPlayer, 13).Execute(s); // 圣盾不在池
            Assert.AreEqual(1, twin.Inventory.EquippedWeapon.Id, "武器未变");
            Assert.AreEqual(2, twin.ActionPoints, "池外武器不扣 AP");
        }
    }
}
