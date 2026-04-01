using System;
using System.Collections.Generic;
using AI;
using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Items;
using Network.Local;
using NUnit.Framework;

namespace Dynasty.Tests.Characters
{
    /// <summary>
    /// P7 三角色 AI：决策接线（低理智确定性路径）+ 全 AI 对局确定性冒烟。
    /// 全 AI 局覆盖：混沌模拟/双子附灵转换/数据幽灵造分身-双发-行向深渊随机链路无崩溃且确定性一致。
    /// </summary>
    public class P7AiSmokeTests
    {
        private static CharacterDef[] P7Defs()
        {
            return new[]
            {
                ChaosMageDef.Create(),
                TwinDef.Create(),
                DataGhostDef.Create(),
                GuardianDef.Create()
            };
        }

        /// <summary>全 AI 跑一局（maxTurns 上限），返回各回合边界哈希与终局哈希。
        /// observer 可选：挂在 LocalLockstep.OnActionCommand 上旁观每条行动指令（不修改状态）。</summary>
        private static (List<long> turnHashes, long finalHash, int turns) RunAiGame(
            int seed, Action<WorldState, int, ICommand, bool> observer = null)
        {
            var state = new WorldState(4, seed);
            var ais = new SimpleAI[4];
            for (int i = 0; i < 4; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            if (observer != null)
                lockstep.OnActionCommand = (pid, cmd, isHuman) => observer(state, pid, cmd, isHuman);
            lockstep.StartGame(P7Defs());

            var hashes = new List<long>();
            int turns = 0;
            while (state.Phase != TurnPhase.GameOver && turns < 80)
            {
                hashes.Add(state.ComputeStateHash());
                lockstep.RunTurn();
                turns++;
            }
            hashes.Add(state.ComputeStateHash());
            return (hashes, state.ComputeStateHash(), turns);
        }

        [Test]
        public void 全AI对局_含三角色_确定性一致且能推进()
        {
            var (h1, f1, t1) = RunAiGame(42);
            var (h2, f2, t2) = RunAiGame(42);

            Assert.AreEqual(t1, t2, "两局推进回合数一致");
            Assert.AreEqual(f1, f2, "终局哈希一致（AI 决策确定性）");
            Assert.AreEqual(h1.Count, h2.Count, "回合边界哈希数一致");
            for (int i = 0; i < h1.Count; i++)
                Assert.AreEqual(h1[i], h2[i], $"回合边界 {i} 哈希一致");
            Assert.GreaterOrEqual(t1, 3, "对局应至少推进 3 回合（无死锁）");
        }

        // ---- 专属命令真实使用（OnActionCommand 统计，防「概率门控分支零断言」假绿）----

        /// <summary>统计 [混沌模拟, 双子转换, 数据幽灵 Harmonics(126)] 指令次数。</summary>
        private static int[] CountDedicatedCommands(int seed)
        {
            int[] counts = new int[3];
            RunAiGame(seed, (s, playerId, cmd, isHuman) =>
            {
                if (cmd is ChaosSimulateCommand) counts[0]++;
                else if (cmd is TwinConvertCommand) counts[1]++;
                else if (cmd is UseSkillCommand usc)
                {
                    Character c = s.GetCharacterByPlayer(playerId);
                    if (c?.Definition?.Skills != null && usc.SkillIndex >= 0
                        && usc.SkillIndex < c.Definition.Skills.Length
                        && c.Definition.Skills[usc.SkillIndex].Id == SkillSystem.HarmonicsSkillId)
                        counts[2]++;
                }
            });
            return counts;
        }

        [Test]
        public void 全AI对局_三角色专属命令均真实使用()
        {
            // 局内概率门控（混沌 60% / 双子灵巧转换 18% / 幽灵 Harmonics 30%）必须各命中
            // 至少一次：把「专属命令被真实使用」从概率陈述变成断言。
            // 双子附灵为策略驱动（健康→灵巧），转换链在多数局可达成；seed=1 已两局实测固化
            int seed = 1;
            int[] c1 = CountDedicatedCommands(seed);
            int[] c2 = CountDedicatedCommands(seed);

            CollectionAssert.AreEqual(c1, c2, "两局专属命令计数一致（命令级确定性）");
            Assert.GreaterOrEqual(c1[0], 1, "混沌模拟指令至少 1 次（模拟池成员真实使用）");
            Assert.GreaterOrEqual(c1[1], 1, "双子灵巧转换指令至少 1 次");
            Assert.GreaterOrEqual(c1[2], 1, "数据幽灵 Harmonics(126) 至少 1 次");
        }

        // ---- 决策接线（确定性优先路径）----

        private static WorldState SceneFor(int seed, CharacterDef def, int slot)
        {
            var state = ScenarioBuilder.Create(4, seed).WithAllAt(4).Build();
            var old = state.GetCharacterByPlayer(slot);
            var c = new Character(old.Id, old.PlayerId, def.Name) { CurrentLocationId = 4 };
            def.ApplyTo(c);
            c.Inventory.Consumables.Clear();
            if (def.InitialItem > 0)
                c.Inventory.EquipWeapon(ItemRegistry.Get(def.InitialItem));
            c.ASC.Tags.AddTag(GameplayTagConstants.V1Form); // 数据幽灵形态兜底
            state.Characters[slot] = c;
            return state;
        }

        [Test]
        public void 混沌AI_低理智优先模拟血包()
        {
            var s = SceneFor(42, ChaosMageDef.Create(), 3);
            var chaos = s.GetCharacterByPlayer(3);
            chaos.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            chaos.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1.5f);
            // 隔离：敌人全部留在原地点（4），仅把他人移到 -2（决策查询用哨兵值），
            // 保留施放者位置 4——FindNearestEnemy 只比较 CurrentLocationId 相等性
            for (int i = 0; i < s.PlayerCount; i++)
            {
                if (i == 3) continue;
                s.GetCharacterByPlayer(i).CurrentLocationId = -2;
            }

            var ai = new SimpleAI(3);
            var cmd = ai.GetActionInput(s, 3) as ChaosSimulateCommand;

            Assert.IsNotNull(cmd, "低理智应发混乱命令");
            Assert.AreEqual(2, cmd.ItemId, "模拟血包（Self）");
            Assert.AreEqual(3, cmd.TargetPlayerId);
        }

        [Test]
        public void 双子AI_无附灵时优先附灵泣血()
        {
            var s = SceneFor(42, TwinDef.Create(), 3);
            var twin = s.GetCharacterByPlayer(3);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2f); // 低理智 → 泣血

            var ai = new SimpleAI(3);
            var cmd = ai.GetActionInput(s, 3) as UseSkillCommand;

            Assert.IsNotNull(cmd, "应返回附灵技能行动");
            int idx = cmd.SkillIndex;
            Assert.AreEqual(SkillSystem.TwinVampiricSkillId, twin.Definition.Skills[idx].Id, "低理智选泣血");
        }

        [Test]
        public void 数据幽灵AI_低理智优先切换花形态()
        {
            var s = SceneFor(42, DataGhostDef.Create(), 3);
            var g = s.GetCharacterByPlayer(3);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1f);
            g.ASC.Tags.AddTag(GameplayTagConstants.V4Unlocked);
            for (int i = 0; i < s.PlayerCount; i++)
            {
                if (i == 3) continue;
                s.GetCharacterByPlayer(i).CurrentLocationId = -2;
            }

            var ai = new SimpleAI(3);
            var cmd = ai.GetActionInput(s, 3) as UseSkillCommand;

            Assert.IsNotNull(cmd, "低理智应切形态");
            int idx = cmd.SkillIndex;
            Assert.AreEqual(SkillSystem.HandsonicSkillId, g.Definition.Skills[idx].Id, "切形态技能 125");
            Assert.AreEqual(3, cmd.TargetPlayerId);
        }

        // ---- 受控随机流决策用例（注入 DeterministicRandom seed 使概率门控必命中/必落空，
        // 见 SimpleAI(int, DeterministicRandom)；first Next(100) 值已按 xorshift 实测固化）----

        /// <summary>把指定槽位之外的角色移到 -2（隔离敌人，FindNearestEnemy 只看地点相等）。</summary>
        private static void IsolateOthers(WorldState s, int slot)
        {
            for (int i = 0; i < s.PlayerCount; i++)
            {
                if (i == slot) continue;
                s.GetCharacterByPlayer(i).CurrentLocationId = -2;
            }
        }

        [Test]
        public void 混沌AI_对敌时发混乱模拟_受控随机()
        {
            var s = SceneFor(42, ChaosMageDef.Create(), 3);
            var chaos = s.GetCharacterByPlayer(3);
            chaos.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            IsolateOthers(s, 3); // 敌人全在远处：命中「有敌 → 60% 混乱模拟」分支

            // seed=4：首个 Next(100)=44 ∈ [0,60) → 必进混乱模拟
            var ai = new SimpleAI(3, new DeterministicRandom(4));
            var cmd = ai.GetActionInput(s, 3) as ChaosSimulateCommand;

            Assert.IsNotNull(cmd, "对敌应发混沌模拟指令");
            Assert.IsTrue(cmd.ItemId == 1 || cmd.ItemId == 3, "模拟池武器成员（刀 1 / 枪 3）");
            Assert.AreNotEqual(3, cmd.TargetPlayerId, "目标为敌人而非自身");
        }

        [Test]
        public void 双子AI_灵巧态持刀发转换枪指令_受控随机()
        {
            var s = SceneFor(42, TwinDef.Create(), 3);
            var twin = s.GetCharacterByPlayer(3);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            twin.ASC.Tags.AddTag(GameplayTagConstants.TwinAgile); // 已附灵灵巧

            // 刀（InitialItem=1）非枪 → 命中「灵巧态换枪 18%」；seed=5：首个 Next(100)=5 < 18
            var ai = new SimpleAI(3, new DeterministicRandom(5));
            var cmd = ai.GetActionInput(s, 3) as TwinConvertCommand;

            Assert.IsNotNull(cmd, "灵巧态持非枪武器应发转换指令");
            Assert.AreEqual(3, cmd.NewItemId, "转换成枪（Id=3）");
            Assert.AreEqual(3, cmd.PlayerId);
        }

        [Test]
        public void 双子AI_无武器不空放附灵技能()
        {
            var s = SceneFor(42, TwinDef.Create(), 3);
            var twin = s.GetCharacterByPlayer(3);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            twin.Inventory.EquippedWeapon = null; // 敌人同地点 + 通用流技能区（roll<35）本可选中 123/124

            // seed=2：首个 Next(100)=22 ∈ [0,35) → 落 TryUseSkill；无武器被 CanActivate 拒绝 → 不空放
            var ai = new SimpleAI(3, new DeterministicRandom(2));
            var cmd = ai.GetActionInput(s, 3);

            Assert.IsNotNull(cmd, "仍有行动（回退移动）");
            Assert.IsFalse(cmd is UseSkillCommand, "无武器时 123/124 不进可施放集（spec 4.2 落点 CanActivate）");
        }

        [Test]
        public void 双子AI_已附灵落通用流不重附不翻转()
        {
            var s = SceneFor(42, TwinDef.Create(), 3);
            var twin = s.GetCharacterByPlayer(3);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            twin.ASC.Tags.AddTag(GameplayTagConstants.TwinAgile); // 已附灵灵巧
            twin.Inventory.EquipWeapon(ItemRegistry.Get(3));       // 枪：灵巧转换分支不适用 → 落通用流

            // 敌人同地点 + 通用流技能区（roll<35）：TryUseSkill 收集时附灵槽 123/124 被排除
            // → 不重附、不随机翻转；seed=2：首个 Next(100)=22 ∈ [0,35) 命中技能区
            var ai = new SimpleAI(3, new DeterministicRandom(2));
            var cmd = ai.GetActionInput(s, 3);

            Assert.IsNotNull(cmd, "仍有行动（回退移动）");
            Assert.IsFalse(cmd is UseSkillCommand, "已附灵双子通用流不得重附/随机翻转泣血⇄灵巧");
        }

        [Test]
        public void 双子AI_泣血态恢复健康主动切灵巧()
        {
            var s = SceneFor(42, TwinDef.Create(), 3);
            var twin = s.GetCharacterByPlayer(3);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            twin.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f); // 健康（≥MaxSanity-0.9）
            twin.ASC.Tags.AddTag(GameplayTagConstants.TwinVampiric);       // 曾受伤选的泣血已无回血价值

            // 确定性（无随机）：泣血态健康且非枪 → 主动切灵巧续换枪路线（docs 7.6：1 AP 切换）
            var ai = new SimpleAI(3);
            var cmd = ai.GetActionInput(s, 3) as UseSkillCommand;

            Assert.IsNotNull(cmd, "泣血态健康应主动切灵巧");
            int idx = cmd.SkillIndex;
            Assert.AreEqual(SkillSystem.TwinAgileSkillId, twin.Definition.Skills[idx].Id, "附灵·灵巧 124");
        }

        [Test]
        public void 数据幽灵AI_无分身理智充裕发Harmonics_受控随机()
        {
            var s = SceneFor(42, DataGhostDef.Create(), 3);
            var g = s.GetCharacterByPlayer(3);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            IsolateOthers(s, 3);

            // 无分身 + 理智 ≥2.5 → 命中「30% 造分身」；seed=10：首个 Next(100)=10 < 30
            var ai = new SimpleAI(3, new DeterministicRandom(10));
            var cmd = ai.GetActionInput(s, 3) as UseSkillCommand;

            Assert.IsNotNull(cmd, "应发造分身技能指令");
            int idx = cmd.SkillIndex;
            Assert.AreEqual(SkillSystem.HarmonicsSkillId, g.Definition.Skills[idx].Id, "Harmonics 126");
            Assert.AreEqual(3, cmd.TargetPlayerId);
        }

        [Test]
        public void 数据幽灵AI_同地点V1V2解锁_升V2形态()
        {
            var s = SceneFor(42, DataGhostDef.Create(), 3);
            var g = s.GetCharacterByPlayer(3);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 2.4f); // <2.5 跳过 Harmonics（不烧随机流）
            g.ASC.Tags.AddTag(GameplayTagConstants.V2Unlocked); // V1+V2 解锁：NextUnlockedForm(V1)=V2

            // 敌人同地点 → 命中「升 V2 35%」；seed=12：首个 Next(100)=32 < 35
            var ai = new SimpleAI(3, new DeterministicRandom(12));
            var cmd = ai.GetActionInput(s, 3) as UseSkillCommand;

            Assert.IsNotNull(cmd, "同地点应升 V2 长刃");
            int idx = cmd.SkillIndex;
            Assert.AreEqual(SkillSystem.HandsonicSkillId, g.Definition.Skills[idx].Id, "切换技能 125");
            Assert.AreEqual(3, cmd.TargetPlayerId);
        }

        [Test]
        public void 数据幽灵AI_低理智想切V4但下一落点非V4_不发切换()
        {
            var s = SceneFor(42, DataGhostDef.Create(), 3);
            var g = s.GetCharacterByPlayer(3);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 1f); // 低理智想切 V4 花自救
            g.ASC.Tags.AddTag(GameplayTagConstants.V2Unlocked);
            g.ASC.Tags.AddTag(GameplayTagConstants.V4Unlocked);
            IsolateOthers(s, 3); // 无同地点敌人（不触发升 V2 分支）

            // V1→NextUnlockedForm=V2 ≠ V4：125 单步语义下该指令落不到 V4，不发（不空烧切换额度）
            var ai = new SimpleAI(3, new DeterministicRandom(1));
            var cmd = ai.GetActionInput(s, 3);

            Assert.IsFalse(cmd is UseSkillCommand, "预测落点非 V4 时不得发切换指令（要爬 V2 会空烧）");
        }

        [Test]
        public void 数据幽灵AI_想切V5但下一落点非V5_不发切换()
        {
            var s = SceneFor(42, DataGhostDef.Create(), 3);
            var g = s.GetCharacterByPlayer(3);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            g.ASC.Tags.AddTag(GameplayTagConstants.V2Unlocked);
            g.ASC.Tags.AddTag(GameplayTagConstants.V5Unlocked);
            IsolateOthers(s, 3); // 敌人远处 → 想切 V5 远程

            // 理智 3 ≥2.5 先烧 Harmonics 骰：seed=1 首个 Next(100)=61 ∈ [30,100) 必落空，
            // 到达切换块；V1→NextUnlockedForm=V2 ≠ V5 → 不发切换
            var ai = new SimpleAI(3, new DeterministicRandom(1));
            var cmd = ai.GetActionInput(s, 3);

            Assert.IsFalse(cmd is UseSkillCommand, "预测落点非 V5 时不得发切换指令（V5→V4 绕环同理）");
        }

        [Test]
        public void 数据幽灵AI_远程优势且V5即下一落点_直达V5()
        {
            var s = SceneFor(42, DataGhostDef.Create(), 3);
            var g = s.GetCharacterByPlayer(3);
            g.ASC.SetAttributeBaseValue(AttributeConstants.ActionPoints, 2);
            g.ASC.SetAttributeBaseValue(AttributeConstants.Sanity, 3f);
            g.ASC.Tags.AddTag(GameplayTagConstants.V5Unlocked); // 仅 V5 解锁：NextUnlockedForm(V1)=V5
            IsolateOthers(s, 3);

            // seed=3：首个 Next(100)=83 ≥30 Harmonics 落空 → 命中「远敌切 V5」直达分支
            var ai = new SimpleAI(3, new DeterministicRandom(3));
            var cmd = ai.GetActionInput(s, 3) as UseSkillCommand;

            Assert.IsNotNull(cmd, "V5 即下一落点时应发切换");
            int idx = cmd.SkillIndex;
            Assert.AreEqual(SkillSystem.HandsonicSkillId, g.Definition.Skills[idx].Id, "切换技能 125");
        }
    }
}
