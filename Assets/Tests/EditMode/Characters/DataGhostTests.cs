using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
using NUnit.Framework;

namespace Dynasty.Tests.Characters
{
    public class DataGhostTests
    {
        private const int GhostPlayer = 3;

        /// <summary>4 人局 slot3 换成数据幽灵（无武器，V1 初始形态）。</summary>
        private static WorldState BuildWithGhost(int seed = 42)
        {
            var state = ScenarioBuilder.Create(4, seed).WithAllAt(4).Build();
            var old = state.GetCharacterByPlayer(GhostPlayer);
            var def = DataGhostDef.Create();
            var c = new Character(old.Id, old.PlayerId, def.Name) { CurrentLocationId = 4 };
            def.ApplyTo(c);
            c.Inventory.EquippedWeapon = null;
            c.Inventory.Consumables.Clear();
            c.ASC.Tags.AddTag(GameplayTagConstants.V1Form); // 生产路径由 TurnManager.StartGame 补；测试自建需手动
            state.Characters[GhostPlayer] = c;
            return state;
        }

        private static Character Ghost(WorldState s) => s.GetCharacterByPlayer(GhostPlayer);
        private static Character Other(WorldState s) => s.GetCharacterByPlayer(1);

        private static void UseFormSkill(WorldState s, int skillIndex)
        {
            Character g = Ghost(s);
            SkillSystem.Activate(g.Definition.Skills[skillIndex], g, g, s, skillIndex);
        }

        private static void AddAllUnlocks(WorldState s)
        {
            Character g = Ghost(s);
            g.ASC.Tags.AddTag(GameplayTagConstants.V2Unlocked);
            g.ASC.Tags.AddTag(GameplayTagConstants.V3Unlocked);
            g.ASC.Tags.AddTag(GameplayTagConstants.V4Unlocked);
            g.ASC.Tags.AddTag(GameplayTagConstants.V5Unlocked);
        }

        [Test]
        public void 初始形态V1_未解锁时切换无效()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            UseFormSkill(s, 0); // 125 形态切换
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V1Form), "仍为 V1");
            Assert.IsFalse(g.ASC.Tags.HasTag(GameplayTagConstants.FormSwitched), "无效切换不消耗每回合次数");
        }

        [Test]
        public void 形态切换_解锁后按序循环且每回合仅一次()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            AddAllUnlocks(s);

            UseFormSkill(s, 0); // V1 → V2
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V2Form), "切到 V2");
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.FormSwitched), "已切换标记");

            UseFormSkill(s, 0); // 同回合第二次：BlockingTags 拦截
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V2Form), "同回合不能再切");

            g.ASC.Tags.RemoveTag(GameplayTagConstants.FormSwitched); // 模拟回合末重置
            UseFormSkill(s, 0); // V2 → V3
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V3Form), "切到 V3");
            g.ASC.Tags.RemoveTag(GameplayTagConstants.FormSwitched);
            UseFormSkill(s, 0); // V3 → V4
            g.ASC.Tags.RemoveTag(GameplayTagConstants.FormSwitched);
            UseFormSkill(s, 0); // V4 → V5
            g.ASC.Tags.RemoveTag(GameplayTagConstants.FormSwitched);
            UseFormSkill(s, 0); // V5 → V1（循环）
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V1Form), "V5 后回到 V1");
        }

        [Test]
        public void 规诫_受物理近战伤害_解锁V2V3V4且V1减伤生效()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            var src = Other(s);
            src.Inventory.EquipWeapon(ItemRegistry.Get(1)); // 刀 = 近战
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);

            float dealt = DamagePipeline.ApplyDamage(s, src.Id, g.Id, 1f, DamageType.Physical);

            Assert.AreEqual(0.5f, dealt, 0.001f, "V1 音刃物理防御 +0.5：1 → 0.5");
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V2Unlocked), "近战解锁 V2");
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V3Unlocked), "近战解锁 V3");
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V4Unlocked), "任意伤害解锁 V4");
            Assert.IsFalse(g.ASC.Tags.HasTag(GameplayTagConstants.V5Unlocked), "近战不解锁 V5");
        }

        [Test]
        public void 规诫_受枪远程伤害_解锁V5()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            var src = Other(s);
            src.Inventory.EquipWeapon(ItemRegistry.Get(3)); // 枪 = 远程
            src.Inventory.EquippedWeaponAmmo = 1;
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);

            DamagePipeline.ApplyDamage(s, src.Id, g.Id, 2f, DamageType.Physical);

            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V5Unlocked), "远程解锁 V5");
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V4Unlocked), "任意伤害解锁 V4");
            Assert.IsFalse(g.ASC.Tags.HasTag(GameplayTagConstants.V2Unlocked), "远程不解锁 V2");
            Assert.IsFalse(g.ASC.Tags.HasTag(GameplayTagConstants.V3Unlocked), "远程不解锁 V3");
        }

        [Test]
        public void 规诫_魔法伤害_只解锁V4()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            var src = Other(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);

            DamagePipeline.ApplyDamage(s, src.Id, g.Id, 1f, DamageType.Magic);

            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V4Unlocked), "魔法伤害解锁 V4");
            Assert.IsFalse(g.ASC.Tags.HasTag(GameplayTagConstants.V2Unlocked));
            Assert.IsFalse(g.ASC.Tags.HasTag(GameplayTagConstants.V3Unlocked));
            Assert.IsFalse(g.ASC.Tags.HasTag(GameplayTagConstants.V5Unlocked));
        }

        [Test]
        public void 形态结算_V1近战1伤害_切出V1失去减伤()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            var victim = Other(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);

            new DamageCommand(0, GhostPlayer, 1, 1f).Execute(s);
            Assert.AreEqual(2f, victim.Sanity, 0.001f, "V1 近战 1 伤害");
            Assert.AreEqual(1, g.ActionPoints, "形态攻击扣 1 AP");

            // 切 V2 后失去 V1 减伤
            var s2 = BuildWithGhost();
            var g2 = Ghost(s2);
            var src2 = Other(s2);
            src2.Inventory.EquipWeapon(ItemRegistry.Get(1));
            g2.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            g2.ASC.Tags.RemoveTag(GameplayTagConstants.V1Form);
            g2.ASC.Tags.AddTag(GameplayTagConstants.V2Form);
            float dealt2 = DamagePipeline.ApplyDamage(s2, src2.Id, g2.Id, 1f, DamageType.Physical);
            Assert.AreEqual(1f, dealt2, 0.001f, "V2 形态无减伤");
        }

        [Test]
        public void 形态结算_V2近战15伤害()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            var victim = Other(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V1Form);
            g.ASC.Tags.AddTag(GameplayTagConstants.V2Form);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);

            new DamageCommand(0, GhostPlayer, 1, 1f).Execute(s);
            Assert.AreEqual(1.5f, victim.Sanity, 0.001f, "V2 长刃 1.5 物理");
        }

        [Test]
        public void 形态结算_V3三连05乘3()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            var victim = Other(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V1Form);
            g.ASC.Tags.AddTag(GameplayTagConstants.V3Form);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);

            new DamageCommand(0, GhostPlayer, 1, 1f).Execute(s); // 一次普攻行动 = 3 连

            Assert.AreEqual(1.5f, victim.Sanity, 0.001f, "0.5 × 3 = 1.5 总伤");
            Assert.AreEqual(1, g.ActionPoints, "三连整体只扣 1 AP");
        }

        [Test]
        public void 形态结算_V3目标中途死亡_剩余击空()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            var victim = Other(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V1Form);
            g.ASC.Tags.AddTag(GameplayTagConstants.V3Form);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1f); // 第一击 0.5、第二击 0.5 即死

            new DamageCommand(0, GhostPlayer, 1, 1f).Execute(s);
            Assert.IsTrue(victim.IsDead || victim.Sanity <= 0.5f, "第二击后死亡");
            Assert.AreEqual(1, g.ActionPoints);
        }

        [Test]
        public void 形态结算_V4花_普攻替换为自愈()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V1Form);
            g.ASC.Tags.AddTag(GameplayTagConstants.V4Form);

            new DamageCommand(0, GhostPlayer, GhostPlayer, 1f).Execute(s); // 目标 = 自己

            Assert.AreEqual(2.5f, g.Sanity, 0.001f, "V4 花自愈 0.5");
            Assert.AreEqual(1, g.ActionPoints, "花回复行动消耗 1 AP（普攻行动价）");
        }

        [Test]
        public void 形态结算_V5远程无视地点且不需弹药()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            var victim = Other(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V1Form);
            g.ASC.Tags.AddTag(GameplayTagConstants.V5Form);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            victim.CurrentLocationId = 0;   // 异地
            g.CurrentLocationId = 6;

            new DamageCommand(0, GhostPlayer, 1, 1f).Execute(s);
            Assert.AreEqual(2f, victim.Sanity, 0.001f, "V5 远程 1 伤害（任意地点）");
            Assert.AreEqual(1, g.ActionPoints, "无需弹药，扣 1 AP");
        }

        [Test]
        public void 形态结算_V5藏匿目标不可选中_不扣AP()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            var victim = Other(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V1Form);
            g.ASC.Tags.AddTag(GameplayTagConstants.V5Form);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            victim.ASC.Tags.AddTag(GameplayTagConstants.HideoutHidden);

            new DamageCommand(0, GhostPlayer, 1, 1f).Execute(s);
            Assert.AreEqual(3f, victim.Sanity, 0.001f, "藏匿不可命中");
            Assert.AreEqual(2, g.ActionPoints, "无效目标不扣 AP");
        }
    }
}
