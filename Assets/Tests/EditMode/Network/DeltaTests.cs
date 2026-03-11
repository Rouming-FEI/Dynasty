using System;
using System.IO;
using Core;
using Core.Sync;
using Gameplay.Characters;
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
    }
}
