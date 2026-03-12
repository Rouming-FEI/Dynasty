using System;
using System.IO;
using AI;
using Core;
using Core.Sync;
using Gameplay.Characters;
using Gameplay.Judgment;
using Network.Local;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>WorldState 玩家块级 Delta：计算（全局块恒变化 + 玩家块字节比较）、
    /// 原地应用（覆盖全局字段 + 变化角色槽）、哈希自校验兜底。</summary>
    public class DeltaTests
    {
        private const uint GlobalFlag = 0x80000000u;

        private static WorldState BuildState()
        {
            var state = new WorldState(4, 42);
            var tm = new TurnManager();
            state.TurnManager = tm;
            tm.StartGame(state, new[]
            {
                CharacterDefCatalog.Get(1), // 守护者
                CharacterDefCatalog.Get(2), // 魅惑精灵
                CharacterDefCatalog.Get(5), // 魔术师
                CharacterDefCatalog.Get(7)  // 时间窃贼
            });
            return state;
        }

        private static WorldState DeepCopy(WorldState s)
        {
            return WorldStateSerializer.Deserialize(WorldStateSerializer.Serialize(s));
        }

        /// <summary>delta 布局 [baseTick:4][targetHash:8][flags:4]，flags 在偏移 12。</summary>
        private static uint ReadFlags(byte[] delta)
        {
            return BitConverter.ToUInt32(delta, 12);
        }

        [Test]
        public void 单玩家变化_Apply后哈希一致()
        {
            var from = BuildState();
            var to = DeepCopy(from);
            to.GetCharacterByPlayer(0).CurrentLocationId = 7;   // 仅 P0 变化

            byte[] delta = WorldStateDelta.Compute(from, to);
            Assert.That(ReadFlags(delta), Is.EqualTo(GlobalFlag | 1u), "仅 P0 + 全局变化");

            var baseCopy = DeepCopy(from);
            WorldStateDelta.Apply(baseCopy, delta);

            Assert.That(baseCopy.ComputeStateHash(), Is.EqualTo(to.ComputeStateHash()), "Apply 后哈希必须等于 to");
            StateAssert.AssertEqual(to, baseCopy);
            Assert.That(baseCopy.GetCharacterByPlayer(0).CurrentLocationId, Is.EqualTo(7), "P0 槽位被覆盖");
            Assert.That(baseCopy.GetCharacterByPlayer(1).CurrentLocationId, Is.EqualTo(1), "未变化玩家不受影响");

            byte[] full = WorldStateSerializer.Serialize(to);
            Assert.That(delta.Length, Is.LessThan(full.Length), "单玩家变化 delta 应小于全量（全局 + 1 玩家块 vs 全局 + 4 玩家块）");
        }

        [Test]
        public void 全部玩家变化_Delta退化全量()
        {
            var from = BuildState();
            var to = DeepCopy(from);
            for (int i = 0; i < to.Characters.Length; i++)
                to.Characters[i].CurrentLocationId += 10;   // 全部变化

            byte[] delta = WorldStateDelta.Compute(from, to);
            Assert.That(ReadFlags(delta), Is.EqualTo(GlobalFlag | 0xFu), "全部玩家 + 全局变化");

            var baseCopy = DeepCopy(from);
            WorldStateDelta.Apply(baseCopy, delta);

            Assert.That(baseCopy.ComputeStateHash(), Is.EqualTo(to.ComputeStateHash()));
            StateAssert.AssertEqual(to, baseCopy);
            byte[] full = WorldStateSerializer.Serialize(to);
            Assert.That(delta.Length, Is.EqualTo(full.Length), "全变化 delta 与全量等价（16B 头部 + 全局 + 全部玩家块）");
        }

        [Test]
        public void 无变化_仅全局块()
        {
            var from = BuildState();
            var to = DeepCopy(from);

            byte[] delta = WorldStateDelta.Compute(from, to);
            Assert.That(ReadFlags(delta), Is.EqualTo(GlobalFlag), "无玩家变化：flags 仅全局位");

            var baseCopy = DeepCopy(from);
            WorldStateDelta.Apply(baseCopy, delta);
            Assert.That(baseCopy.ComputeStateHash(), Is.EqualTo(to.ComputeStateHash()), "Apply 后哈希必须等于 to");
            StateAssert.AssertEqual(to, baseCopy);

            byte[] full = WorldStateSerializer.Serialize(to);
            Assert.That(delta.Length, Is.LessThan(full.Length), "无变化 delta（仅全局块）仍小于全量");
        }

        [Test]
        public void 部分玩家变化_标志位与载荷正确()
        {
            var from = BuildState();
            var to = DeepCopy(from);
            to.GetCharacterByPlayer(0).CurrentLocationId = 8;
            to.GetCharacterByPlayer(2).CurrentLocationId = 9;

            byte[] delta = WorldStateDelta.Compute(from, to);
            Assert.That(ReadFlags(delta), Is.EqualTo(GlobalFlag | 0x5u), "P0+P2 + 全局变化");

            var baseCopy = DeepCopy(from);
            WorldStateDelta.Apply(baseCopy, delta);

            Assert.That(baseCopy.ComputeStateHash(), Is.EqualTo(to.ComputeStateHash()));
            StateAssert.AssertEqual(to, baseCopy);
            Assert.That(baseCopy.GetCharacterByPlayer(2).CurrentLocationId, Is.EqualTo(9), "P2 槽位被覆盖");
            Assert.That(baseCopy.GetCharacterByPlayer(3).CurrentLocationId, Is.EqualTo(3), "未变化玩家不受影响");
        }

        [Test]
        public void 坏Delta_自校验拒绝()
        {
            var from = BuildState();
            var to = DeepCopy(from);
            to.GetCharacterByPlayer(0).CurrentLocationId = 7;
            byte[] delta = WorldStateDelta.Compute(from, to);

            // 1) 篡改 baseTick → base 不匹配拒绝
            var badBase = (byte[])delta.Clone();
            badBase[2] ^= 0xFF;
            Assert.Throws<InvalidDataException>(() => WorldStateDelta.Apply(DeepCopy(from), badBase));

            // 2) 篡改目标哈希 → 应用后哈希自校验拒绝
            var badHash = (byte[])delta.Clone();
            badHash[6] ^= 0x01;
            Assert.Throws<InvalidDataException>(() => WorldStateDelta.Apply(DeepCopy(from), badHash));

            // 3) 应用到错误 base（tick 不同）→ baseTick 校验拒绝
            var wrongBase = DeepCopy(from);
            wrongBase.CurrentTick = 999;
            Assert.Throws<InvalidDataException>(() => WorldStateDelta.Apply(wrongBase, delta));
        }

        /// <summary>固定非平筹码（总计 R=5/S=3/P=4，石头唯一最高），避免全平作废死循环（同 SnapshotTests）。</summary>
        private static ChipAllocation FixedChip(int pid)
        {
            var a = new ChipAllocation(pid);
            switch (pid)
            {
                case 0: a.Set(3, 0, 0); break;
                case 1: a.Set(2, 1, 0); break;
                case 2: a.Set(0, 2, 1); break;
                default: a.Set(0, 0, 3); break;
            }
            return a;
        }

        [Test]
        public void 真实判定轮_Delta往返一致()
        {
            // 参考 SnapshotTests.完整对局快照往返：3 回合 4 人局，
            // from = 回合 1 结束状态，to = 回合 3 结束状态（LastJudgment/LastAllocations/ActionOrder 均为非默认值）
            var state = new WorldState(4, 42);
            var ais = new SimpleAI[state.PlayerCount];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.StartGame(new[]
            {
                CharacterDefCatalog.Get(1), // 守护者
                CharacterDefCatalog.Get(2), // 魅惑精灵
                CharacterDefCatalog.Get(5), // 魔术师
                CharacterDefCatalog.Get(7)  // 时间窃贼
            });
            lockstep.JudgmentInputProvider = FixedChip;

            int guard = 0;
            while (state.CurrentTurn < 2 && state.Phase != TurnPhase.GameOver && guard++ < 120)
                lockstep.RunTurn();
            Assert.That(state.Phase, Is.Not.EqualTo(TurnPhase.GameOver), "回合 1 内不应终局");
            WorldState from = DeepCopy(state);   // 回合 1 结束状态（活状态深拷贝）

            while (state.CurrentTurn < 4 && state.Phase != TurnPhase.GameOver && guard++ < 240)
                lockstep.RunTurn();
            WorldState to = state;   // 回合 3 结束状态

            // 前置：确实存在哈希不覆盖的非默认字段差异可测
            Assert.That(to.HasJudgmentResult, Is.True, "判定轮后应有判定结果");
            Assert.That(to.TurnManager.ActionOrder.Length, Is.GreaterThan(0), "判定后行动顺序非空");

            byte[] delta = WorldStateDelta.Compute(from, to);
            var baseCopy = DeepCopy(from);
            WorldStateDelta.Apply(baseCopy, delta);

            Assert.That(baseCopy.ComputeStateHash(), Is.EqualTo(to.ComputeStateHash()), "Apply 后哈希必须等于 to");
            StateAssert.AssertEqual(to, baseCopy);
        }

        [Test]
        public void Apply到已接线活状态_TurnManager对象同一性保持()
        {
            // Task 11 重连客户端场景：baseState 是已接线的活状态，Apply 不得换掉 TurnManager 引用
            var state = new WorldState(4, 42);
            var ais = new SimpleAI[state.PlayerCount];
            for (int i = 0; i < ais.Length; i++) ais[i] = new SimpleAI(i);
            var lockstep = new LocalLockstep(state, ais);
            lockstep.StartGame(new[]
            {
                CharacterDefCatalog.Get(1),
                CharacterDefCatalog.Get(2),
                CharacterDefCatalog.Get(5),
                CharacterDefCatalog.Get(7)
            });

            var to = DeepCopy(state);
            to.GetCharacterByPlayer(0).CurrentLocationId = 7;

            byte[] delta = WorldStateDelta.Compute(state, to);
            WorldStateDelta.Apply(state, delta);   // 直接原地应用到活状态

            Assert.That(lockstep.TurnManager, Is.SameAs(state.TurnManager),
                "Apply 后驱动器所持 TurnManager 必须与 state 同一（换引用会导致驱动器脱钩分叉）");
            Assert.That(state.ComputeStateHash(), Is.EqualTo(to.ComputeStateHash()), "活状态应用后哈希必须等于 to");
            StateAssert.AssertEqual(to, state);
        }

        [Test]
        public void 截断Delta_抛InvalidDataException()
        {
            var from = BuildState();
            var to = DeepCopy(from);
            to.GetCharacterByPlayer(0).CurrentLocationId = 7;
            byte[] delta = WorldStateDelta.Compute(from, to);

            // 截断到一半：头部读完后全局块必然越界 → EndOfStreamException 统一重抛为 InvalidDataException
            byte[] truncated = new byte[delta.Length / 2];
            Array.Copy(delta, truncated, truncated.Length);
            Assert.Throws<InvalidDataException>(() => WorldStateDelta.Apply(DeepCopy(from), truncated));
        }
    }
}
