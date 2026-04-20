using Core;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
using NUnit.Framework;

namespace Dynasty.Tests.Characters
{
    public class ChaosMageTests
    {
        private const int ChaosPlayer = 3;

        /// <summary>建 4 人局并把 slot3 换成混沌法师；全员移同地点 4。</summary>
        private static WorldState BuildWithChaos(int seed)
        {
            var state = ScenarioBuilder.Create(4, seed).WithAllAt(4).Build();
            var old = state.GetCharacterByPlayer(ChaosPlayer);
            var def = ChaosMageDef.Create();
            var c = new Character(old.Id, old.PlayerId, def.Name) { CurrentLocationId = 4 };
            def.ApplyTo(c);
            c.Inventory.EquippedWeapon = null;
            c.Inventory.Consumables.Clear();
            state.Characters[ChaosPlayer] = c;
            return state;
        }

        /// <summary>
        /// 扫描 seed 使反噬判定结果为期望分支（确定性：同 seed 同结果，扫描固定，不会抖动）。
        /// 判定在混沌命令内消费 state.Random；每次扫描新建场景并以同一命令作为首个动作执行，
        /// 判定前随机流相同 → 结果可复现。wantWin = 施放者未被反噬（自损 1 即败）。
        /// </summary>
        private static WorldState ChaosSceneWithOutcome(bool wantWin, int itemId, int targetPlayerId,
            float casterSanity, out Character chaos, out Character victim)
        {
            chaos = null;
            victim = null;
            for (int seed = 42; seed < 300; seed++)
            {
                var s = BuildWithChaos(seed);
                chaos = s.GetCharacterByPlayer(ChaosPlayer);
                victim = s.GetCharacterByPlayer(1);
                chaos.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
                chaos.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, casterSanity);
                victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
                float before = chaos.Sanity;
                new ChaosSimulateCommand(0, ChaosPlayer, itemId, targetPlayerId).Execute(s);
                // 分支判定：败 = 自扣 1（理智下降）；胜 = 理智不降（刀：不变；血包：回血上升）
                bool win = chaos.Sanity >= before;
                if (win == wantWin) return s;
            }
            Assert.Fail("300 个 seed 内未命中期望判定分支");
            return null;
        }

        [Test]
        public void 混乱_判定胜_模拟刀对目标造成15物理伤害()
        {
            var s = ChaosSceneWithOutcome(true, 1, 1, 4f, out Character chaos, out Character victim);
            Assert.AreEqual(4f, chaos.Sanity, 0.001f, "胜：施放者不自损");
            Assert.AreEqual(1.5f, victim.Sanity, 0.001f, "模拟刀 = 近战 1.5 物理");
            Assert.AreEqual(2, chaos.ActionPoints, "施放扣 1 AP");
        }

        [Test]
        public void 混乱_判定败_效果不结算且自扣1理智()
        {
            var s = ChaosSceneWithOutcome(false, 1, 1, 4f, out Character chaos, out Character victim);
            Assert.AreEqual(3f, chaos.Sanity, 0.001f, "败：自扣 1 理智");
            Assert.AreEqual(3f, victim.Sanity, 0.001f, "败：效果不结算");
            Assert.AreEqual(2, chaos.ActionPoints, "AP 照扣（施放行为已发生）");
        }

        [Test]
        public void 混乱_判定胜_模拟血包回复3理智()
        {
            // 低理智扫描：胜 = 回 3（1 → 4）；败 = 自扣 1（1 → 0），两分支区分明确
            var s = ChaosSceneWithOutcome(true, 2, ChaosPlayer, 1f, out Character chaos, out Character victim);
            Assert.AreEqual(4f, chaos.Sanity, 0.001f, "血包回 3（钳 MaxSanity=4）");
            Assert.AreEqual(2, chaos.ActionPoints, "施放扣 1 AP");
        }

        [Test]
        public void 混乱_模拟池外的道具被拒且不扣AP()
        {
            var s = BuildWithChaos(42);
            var chaos = s.GetCharacterByPlayer(ChaosPlayer);
            chaos.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            chaos.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            new ChaosSimulateCommand(0, ChaosPlayer, 5, ChaosPlayer).Execute(s);  // 核弹不在池
            new ChaosSimulateCommand(0, ChaosPlayer, 8, ChaosPlayer).Execute(s);  // 应急装置不在池
            new ChaosSimulateCommand(0, ChaosPlayer, 12, ChaosPlayer).Execute(s); // 海幻归鞘刀不在池
            new ChaosSimulateCommand(0, ChaosPlayer, 13, ChaosPlayer).Execute(s); // 圣盾不在池
            Assert.AreEqual(2, chaos.ActionPoints, "非法 itemId 不扣 AP");
            Assert.AreEqual(3f, chaos.Sanity, 0.001f, "无任何效果/自损");
        }

        [Test]
        public void 混乱_无效目标_不扣AP不判定()
        {
            var s = BuildWithChaos(42);
            var chaos = s.GetCharacterByPlayer(ChaosPlayer);
            chaos.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            chaos.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            Character victim = s.GetCharacterByPlayer(1);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0.001f);
            victim.ASC.Tags.AddTag(GameplayTagConstants.Dead); // 目标死亡
            new ChaosSimulateCommand(0, ChaosPlayer, 1, 1).Execute(s);
            Assert.AreEqual(2, chaos.ActionPoints, "无效目标不扣 AP");
            Assert.AreEqual(3f, chaos.Sanity, 0.001f, "不发生判定");
        }

        [Test]
        public void 地狱的子民_禁拾取与使用道具_应急装置豁免_普攻可用()
        {
            var s = BuildWithChaos(42);
            var chaos = s.GetCharacterByPlayer(ChaosPlayer);
            chaos.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            // 血包不可用断言基于非满血（3/4）：若拦截缺失，回 3 会升到 4 暴露实现错误
            chaos.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);

            // 拾取武器被拒
            s.Locations.DropItem(4, 1, 1);
            new PickupItemCommand(0, ChaosPlayer, 1).Execute(s);
            Assert.IsNull(chaos.Inventory.EquippedWeapon, "禁拾取武器");
            Assert.AreEqual(3, chaos.ActionPoints, "拦截发生在扣 AP 前");

            // 使用血包被拒
            chaos.Inventory.AddConsumable(ItemRegistry.Get(2));
            new UseItemCommand(0, ChaosPlayer, 0, ChaosPlayer).Execute(s);
            Assert.AreEqual(3f, chaos.Sanity, 0.001f, "血包不可用");
            Assert.AreEqual(3, chaos.ActionPoints, "拦截不扣 AP");

            // 应急装置豁免
            chaos.Inventory.Consumables.Clear();
            chaos.Inventory.AddConsumable(ItemRegistry.Get(8));
            new UseItemCommand(0, ChaosPlayer, 0, ChaosPlayer).Execute(s);
            Assert.IsTrue(chaos.ASC.Tags.HasTag(GameplayTagConstants.DeathSaveActive), "豁免路径生效");

            // 普攻可用（徒手）：Amount=1 物理；混沌无攻击加成、目标防御 0 → 实际 1 伤害
            chaos.Inventory.Consumables.Clear();
            Character victim = s.GetCharacterByPlayer(1);
            victim.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            new DamageCommand(0, ChaosPlayer, 1, 1f).Execute(s);
            Assert.AreEqual(2f, victim.Sanity, 0.001f, "普攻造成 1 伤害");
            Assert.AreEqual(2, chaos.ActionPoints, "普攻扣 1 AP（道具拦截不影响普攻）");
        }

        [Test]
        public void 混乱_判定败_理智归零进入死亡结算()
        {
            // 1 理智施放混乱命中败局：反噬自损 1 → 理智 0，必须入队 DeathCommand 走统一
            // 死亡结算（引擎非伤害直扣先例均钳 0.5 防致死），禁止「0 理智仍存活」僵尸态
            var s = ChaosSceneWithOutcome(false, 1, 1, 1f, out Character chaos, out Character victim);
            Assert.AreEqual(0f, chaos.Sanity, 0.001f, "败：1 理智被反噬扣至 0");
            Assert.IsFalse(chaos.IsDead, "死亡结算前不直接判死（等队列执行）");
            s.Commands.ExecuteAll(s); // LocalLockstep 语义：命令中入队的 DeathCommand 本趟 ExecuteAll 结算
            Assert.IsTrue(chaos.IsDead, "反噬致死进入死亡结算");
            Assert.AreEqual(3f, victim.Sanity, 0.001f, "败：效果不结算");
        }

        [Test]
        public void 混乱_无存活他人_血包免判定直结()
        {
            // 无他人分支：rival == null 视为胜——不判定直接结算血包 +3，绝不因反噬自损
            var s = BuildWithChaos(42);
            var chaos = s.GetCharacterByPlayer(ChaosPlayer);
            chaos.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
            chaos.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1f);
            for (int i = 0; i < s.Characters.Length; i++)
            {
                Character ch = s.Characters[i];
                if (ch != null && ch.PlayerId != ChaosPlayer)
                {
                    ch.ASC.Tags.AddTag(GameplayTagConstants.Dead);
                    ch.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0f);
                }
            }
            new ChaosSimulateCommand(0, ChaosPlayer, 2, ChaosPlayer).Execute(s);
            Assert.AreEqual(4f, chaos.Sanity, 0.001f, "免判定直结：回 3 理智（1→4 钳 MaxSanity）");
            Assert.AreEqual(2, chaos.ActionPoints, "施放扣 1 AP");
            Assert.IsFalse(chaos.IsDead, "无他人视为胜：不自损");
            Assert.AreEqual(0, s.Commands.Count, "胜分支不产生入队命令");
        }

        [Test]
        public void 混乱_无敌人目标_对手取数组序第一存活他人判定()
        {
            // Self 类道具无敌人目标 → 反噬对手 = 数组序第一存活他人（player0 死 → player1）。
            // 若对手选取失效（误判无他人 → 恒胜直结），血包永不出现败局；
            // 能扫到败局即证明判定对手路径生效（对手唯一候选 = player1）。
            WorldState lost = null;
            Character chaos = null;
            for (int seed = 42; seed < 400; seed++)
            {
                var sc = BuildWithChaos(seed);
                Character p0 = sc.GetCharacterByPlayer(0);
                p0.ASC.Tags.AddTag(GameplayTagConstants.Dead);
                p0.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0f);
                Character p2 = sc.GetCharacterByPlayer(2);
                p2.ASC.Tags.AddTag(GameplayTagConstants.Dead);
                p2.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0f);
                var ch = sc.GetCharacterByPlayer(ChaosPlayer);
                ch.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
                ch.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
                float before = ch.Sanity;
                new ChaosSimulateCommand(0, ChaosPlayer, 2, ChaosPlayer).Execute(sc);
                if (ch.Sanity < before) { lost = sc; chaos = ch; break; } // 败局命中
            }
            Assert.IsNotNull(lost, "400 seed 内未命中败局：存活他人判定路径未生效（应恒胜直结）");
            Assert.AreEqual(2f, chaos.Sanity, 0.001f, "败于第一存活他人：自扣 1、血包未结算");
            Assert.AreEqual(2, chaos.ActionPoints, "AP 照扣（判定败也扣——S2）");
            Assert.AreEqual(0, lost.Commands.Count, "败而未死：无入队命令");
            Assert.AreEqual(3f, lost.GetCharacterByPlayer(1).Sanity, 0.001f, "败：他人不受任何影响");
        }

        [Test]
        public void 混乱_未结算尸体不参与反噬判定_无存活他人视为胜()
        {
            // M1：同回合刚被杀（DeathCommand 未结算）的角色 Sanity=0 且无 Dead 标记（!IsDead），
            // 混沌无目标分支的「数组序第一存活他人」内联扫描曾只查 IsDead → 4AP 混沌同回合
            // 击杀后再模拟血包等自用道具会与尸体判定（败 → 反噬自扣甚至入队死亡）。
            // 修复语义与 FindFirstOtherAlive 对齐：Sanity ≤ 0 视为死（DamagePipeline 死亡判据
            // 同源），尸体不得参与——全场只剩尸体的场景 rival 必须为 null → 免判定直结
            // （1→4 满、无入队命令、不自损）。多 seed 断言行为恒等：尸体若被选中即引入
            // 随机判定，落入败局（自扣至 0 + DeathCommand 入队）即红，检出旧内联实现。
            for (int seed = 42; seed < 50; seed++)
            {
                var s = BuildWithChaos(seed);
                var chaos = s.GetCharacterByPlayer(ChaosPlayer);
                chaos.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 3);
                chaos.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1f);
                for (int i = 0; i < s.Characters.Length; i++)
                {
                    Character ch = s.Characters[i];
                    if (ch != null && ch.PlayerId != ChaosPlayer)
                        ch.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 0f); // 未结算尸体：不打 Dead 标记
                }
                new ChaosSimulateCommand(0, ChaosPlayer, 2, ChaosPlayer).Execute(s);
                Assert.AreEqual(4f, chaos.Sanity, 0.001f,
                    $"seed={seed}：尸体不得参与判定，血包应直结回 3（1→4 钳 MaxSanity）");
                Assert.AreEqual(2, chaos.ActionPoints, $"seed={seed}：施放扣 1 AP");
                Assert.IsFalse(chaos.IsDead, $"seed={seed}：无他人视为胜，不自损");
                Assert.AreEqual(0, s.Commands.Count, $"seed={seed}：胜分支不产生入队命令");
            }
        }
    }
}
