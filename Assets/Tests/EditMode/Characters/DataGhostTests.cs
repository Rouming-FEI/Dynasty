using System;
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
        public void 规诫_伤害被盾挡_仍解锁V2V3V4()
        {
            // 挂点语义：OnPreDamage（第 2.5 段）先于护盾段（第 3 段）→ 盾挡伤害同样触发解锁
            var s = BuildWithGhost();
            var g = Ghost(s);
            var src = Other(s);
            src.Inventory.EquipWeapon(ItemRegistry.Get(1)); // 刀 = 近战
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            g.ASC.Tags.AddTag(GameplayTagConstants.HasShield);

            float dealt = DamagePipeline.ApplyDamage(s, src.Id, g.Id, 1f, DamageType.Physical);

            Assert.AreEqual(0f, dealt, 0.001f, "伤害被护盾吸收");
            Assert.IsFalse(g.ASC.Tags.HasTag(GameplayTagConstants.HasShield), "盾破");
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V2Unlocked), "盾挡也解锁 V2");
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V3Unlocked), "盾挡也解锁 V3");
            Assert.IsTrue(g.ASC.Tags.HasTag(GameplayTagConstants.V4Unlocked), "盾挡也解锁 V4");
            Assert.IsFalse(g.ASC.Tags.HasTag(GameplayTagConstants.V5Unlocked), "近战不解锁 V5");
        }

        [Test]
        public void 规诫_伤害被免疫_不解锁任何形态()
        {
            // 免疫检查（第 1 段）先于 OnPreDamage（第 2.5 段）早退 → 被免疫的伤害不触发规诫
            var s = BuildWithGhost();
            var g = Ghost(s);
            var src = Other(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            g.ASC.Tags.AddTag(GameplayTagConstants.ImmunePhysical);

            float dealt = DamagePipeline.ApplyDamage(s, src.Id, g.Id, 1f, DamageType.Physical);

            Assert.AreEqual(0f, dealt, 0.001f, "物理伤害被免疫");
            Assert.AreEqual(3f, g.Sanity, 0.001f, "理智不受损");
            Assert.IsFalse(g.ASC.Tags.HasTag(GameplayTagConstants.V2Unlocked));
            Assert.IsFalse(g.ASC.Tags.HasTag(GameplayTagConstants.V3Unlocked));
            Assert.IsFalse(g.ASC.Tags.HasTag(GameplayTagConstants.V4Unlocked));
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
        public void 形态结算_V3目标中途死亡_只结算两击()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            var victim = Other(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V1Form);
            g.ASC.Tags.AddTag(GameplayTagConstants.V3Form);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1f); // 第 2 击（0.5×2）即死

            // 死亡由 DamagePipeline 入队的 DeathCommand 延迟结算（IsDead 滞后），循环停判据须
            // 与管线 155 行同源（Sanity <= 0）：每完整结算一击恰好派发一次 DamageDealt，
            // 计数 = 结算次数（若旧缺陷复发会结算满 3 击并重复入队死亡命令 → 计数 3 必红）
            int hits = 0;
            s.Events.RecorderHook = delegate (int type, ref GameplayEvent e)
            {
                if (type == GameplayEventTypes.DamageDealt) hits++;
            };
            new DamageCommand(0, GhostPlayer, 1, 1f).Execute(s);
            s.Events.RecorderHook = null;

            Assert.AreEqual(2, hits, "第 2 击杀后第 3 击不得结算");
            Assert.AreEqual(0f, victim.Sanity, 0.001f, "两击合计 1.0 伤害，理智归零");
            // Task 5 起击杀会触发行向深渊（128）：本体扣 1 AP 后，判定胜 +1 / 败不动 → AP 只可能 1 或 2
            Assert.IsTrue(g.ActionPoints == 1 || g.ActionPoints == 2,
                $"三连行动整体只扣 1 AP（± 行向深渊判定胜 +1），实际 AP={g.ActionPoints}");
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

        [Test]
        public void 数据幽灵_拾取武器被拒不扣AP()
        {
            // 形态自带武器语义：实体武器无行动路由 → 显式拦截拾取（与鬼化同款裁决）
            var s = BuildWithGhost();
            var g = Ghost(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);

            new PickupItemCommand(0, GhostPlayer, 1).Execute(s); // 刀（Weapon）

            Assert.IsNull(g.Inventory.EquippedWeapon, "武器未被装备");
            Assert.AreEqual(0, g.Inventory.Consumables.Count, "背包无武器");
            Assert.AreEqual(2, g.ActionPoints, "拦截拾取不扣 AP");
        }

        [Test]
        public void 数据幽灵_拾取消耗品成功扣AP()
        {
            var s = BuildWithGhost();
            var g = Ghost(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);

            new PickupItemCommand(0, GhostPlayer, 2).Execute(s); // 血包（Consumable）

            Assert.AreEqual(1, g.Inventory.Consumables.Count, "血包入背包");
            Assert.AreEqual(2, g.Inventory.Consumables[0].Id, "背包中为血包 Id=2");
            Assert.AreEqual(1, g.ActionPoints, "成功拾取扣 1 AP");
        }

        // ---------------- Task 5 Part B：分身（Harmonics）+ 同步双发 + 行向深渊 + 生命周期 ----------------

        /// <summary>带周期被动注册的数据幽灵（供回合末/分身负载测试）。</summary>
        private static WorldState BuildWithGhostPassive(int seed)
        {
            var state = BuildWithGhost(seed);
            Character g = Ghost(state);
            for (int i = 0; i < g.Definition.Skills.Length; i++)
                g.ASC.RegisterPassive(g.Definition.Skills[i]);
            g.ASC.EnsureCooldownSlots(g.Definition.Skills.Length);
            return state;
        }

        private static void CastHarmonics(WorldState s)
        {
            Character g = Ghost(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            SkillSystem.Activate(g.Definition.Skills[1], g, g, s, 1); // 126 Harmonics
        }

        [Test]
        public void Harmonics_制造分身扣1AP_已有分身拒绝不扣费()
        {
            var s = BuildWithGhost();
            CastHarmonics(s);
            Character g = Ghost(s);
            Assert.IsTrue(s.GhostCloneActive, "分身激活");
            Assert.AreEqual(3f, s.GhostCloneSanity, 0.001f, "分身理智初始 3");
            Assert.AreEqual(1, g.ActionPoints, "扣 1 AP");

            // 已有分身：拒绝且不扣 AP（S4）
            SkillSystem.Activate(g.Definition.Skills[1], g, g, s, 1);
            Assert.AreEqual(1, g.ActionPoints, "重复施放不扣 AP");
            Assert.AreEqual(3f, s.GhostCloneSanity, 0.001f, "不刷新分身");
        }

        [Test]
        public void 同步双发_V1普攻后分身复制一次同伤害()
        {
            var s = BuildWithGhostPassive(42);
            CastHarmonics(s);
            Character g = Ghost(s);
            Character victim = Other(s);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);

            new DamageCommand(0, GhostPlayer, 1, 1f).Execute(s); // V1 普攻 1

            Assert.AreEqual(1f, victim.Sanity, 0.001f, "本体 1 + 分身复制 1 = 2 总伤");
            Assert.AreEqual(0, g.ActionPoints, "复制 0 AP：施放后剩 1，普攻扣 1 → 0");
        }

        [Test]
        public void 同步双发_V3三连整体视为一次行动_分身复制完整三连()
        {
            var s = BuildWithGhostPassive(42);
            CastHarmonics(s);
            Character g = Ghost(s);
            Character victim = Other(s);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V1Form);
            g.ASC.Tags.AddTag(GameplayTagConstants.V3Form);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);

            new DamageCommand(0, GhostPlayer, 1, 1f).Execute(s); // V3 三连 + 分身复制三连

            Assert.AreEqual(0f, victim.Sanity, 0.001f, "本体 1.5 + 分身 1.5 = 3 总伤");
            Assert.AreEqual(1, g.ActionPoints, "复制 0 AP：2 - 1 = 1");
        }

        [Test]
        public void V4花回复_不参与分身复制()
        {
            var s = BuildWithGhostPassive(42);
            CastHarmonics(s);
            Character g = Ghost(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f);
            g.ASC.Tags.RemoveTag(GameplayTagConstants.V1Form);
            g.ASC.Tags.AddTag(GameplayTagConstants.V4Form);

            new DamageCommand(0, GhostPlayer, GhostPlayer, 1f).Execute(s); // 自愈

            Assert.AreEqual(2.5f, g.Sanity, 0.001f, "本体自愈 0.5");
            Assert.AreEqual(3f, s.GhostCloneSanity, 0.001f, "花回复不复制（分身理智不动）");
        }

        [Test]
        public void 分身每回合末扣1理智_扣至0触发死亡判定()
        {
            var s = BuildWithGhostPassive(42);
            CastHarmonics(s);
            Character g = Ghost(s);

            g.ASC.TickEffects(s); // 回合末 1：3 → 2
            Assert.AreEqual(2f, s.GhostCloneSanity, 0.001f);
            Assert.IsTrue(s.GhostCloneActive);

            g.ASC.TickEffects(s); // 回合末 2：2 → 1
            Assert.AreEqual(1f, s.GhostCloneSanity, 0.001f);

            g.ASC.TickEffects(s); // 回合末 3：1 → 0 → 分身死亡判定（胜/败两态，确定性随机）
            Assert.IsFalse(s.GhostCloneActive, "分身已消亡");
            s.Commands.ExecuteAll(s); // 模拟 EndTurn 收尾执行入队命令

            bool bodyDead = g.IsDead;
            bool bodySurvived = !g.IsDead && g.Sanity > 0f;
            Assert.IsTrue(bodyDead || bodySurvived, "分身死亡判定后本体必为两态之一");
            if (bodySurvived)
                Assert.AreEqual(3f, g.Sanity, 0.001f, "判定胜：本体无事（满血 3）");
        }

        [Test]
        public void 无分身时_回合末周期负载零开销()
        {
            var s = BuildWithGhostPassive(42);
            Character g = Ghost(s);
            g.ASC.TickEffects(s);
            Assert.IsFalse(s.GhostCloneActive);
            Assert.AreEqual(3f, g.Sanity, 0.001f, "本体不受分身负载影响");
        }

        /// <summary>扫描 seed 使行向深渊判定为期望分支（本体胜/败）。victim 0.5 理智被 V1 一击击杀。</summary>
        private static WorldState AbyssScene(bool wantWin, bool withClone, out Character g, out Character victim)
        {
            for (int seed = 42; seed < 400; seed++)
            {
                var s = BuildWithGhostPassive(seed);
                g = Ghost(s);
                victim = Other(s);
                g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
                g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
                victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0.5f);
                if (withClone)
                {
                    s.GhostCloneActive = true;
                    s.GhostCloneSanity = 1f; // 败连坐 -1 → 0 → 分身死亡判定链
                }
                DamagePipeline.ApplyDamage(s, g.Id, victim.Id, 1f, DamageType.Physical); // 击杀 → 触发 128
                s.Commands.ExecuteAll(s); // 执行可能入队的 DeathCommand（分身死亡链）
                // 分支判定：胜 = 本体不死且理智仍 3；败 = 本体死亡或理智 2（本体 -1）
                bool win = !g.IsDead && Math.Abs(g.Sanity - 3f) < 0.001f;
                bool lose = g.IsDead || (Math.Abs(g.Sanity - 2f) < 0.001f);
                if (wantWin && win) return s;
                if (!wantWin && lose) return s;
            }
            g = null;
            victim = null;
            Assert.Fail("400 个 seed 内未命中期望分支");
            return null;
        }

        [Test]
        public void 行向深渊_判定胜_本体AP加1()
        {
            var s = AbyssScene(true, false, out Character g, out Character victim);
            Assert.AreEqual(3f, g.Sanity, 0.001f, "胜：本体无损");
            Assert.AreEqual(3, g.ActionPoints, "杀敌 + 行向深渊胜：AP 2 → 3");
        }

        [Test]
        public void 行向深渊_判定败_本体扣1理智()
        {
            var s = AbyssScene(false, false, out Character g, out Character victim);
            Assert.IsTrue(g.IsDead || Math.Abs(g.Sanity - 2f) < 0.001f, "败：本体理智 3 → 2");
        }

        [Test]
        public void 行向深渊_败连坐_分身扣至0进入分身死亡判定()
        {
            var s = AbyssScene(false, true, out Character g, out Character victim);
            // 败：本体 -1；分身 1 → 0 → 分身死亡判定（胜：无事 / 败：本体死亡）——两态合法即过
            Assert.IsFalse(s.GhostCloneActive, "分身已消亡");
            Assert.IsTrue(g.IsDead || Math.Abs(g.Sanity - 2f) < 0.001f,
                $"本体终态合法：死亡或 2 理智（实际 sanity={g.Sanity}, dead={g.IsDead}）");
        }

        [Test]
        public void 本体死亡_分身随本体消亡()
        {
            var s = BuildWithGhostPassive(42);
            CastHarmonics(s);
            Character g = Ghost(s);
            Character src = Other(s);
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1f);

            DamagePipeline.ApplyDamage(s, src.Id, g.Id, 3f, DamageType.Physical); // 致死
            s.Commands.ExecuteAll(s);

            Assert.IsTrue(g.IsDead, "本体死亡");
            Assert.IsFalse(s.GhostCloneActive, "分身清理");
            Assert.AreEqual(0f, s.GhostCloneSanity, 0.001f);
        }
    }
}
