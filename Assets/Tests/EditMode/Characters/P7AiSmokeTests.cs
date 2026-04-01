using System.Collections.Generic;
using AI;
using Core;
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

        /// <summary>全 AI 跑一局（maxTurns 上限），返回各回合边界哈希与终局哈希。</summary>
        private static (List<long> turnHashes, long finalHash, int turns) RunAiGame(int seed)
        {
            var state = new WorldState(4, seed);
            var ais = new SimpleAI[4];
            for (int i = 0; i < 4; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
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
    }
}
