using Core;
using Core.Sync;
using NUnit.Framework;

namespace Dynasty.Tests.Network
{
    /// <summary>
    /// 数据幽灵分身状态字段（WorldState 全局）：快照/Delta/哈希三通道回归。
    /// 字段随任何对局长期存在于序列化格式（默认 false/0），因此测试不依赖角色 16 存在。
    /// </summary>
    public class GhostCloneSyncTests
    {
        private static WorldState NewState(int seed = 42)
        {
            var state = new WorldState(2, seed);
            state.TurnManager = new Core.TurnManager();
            state.TurnManager.StartGame(state);
            return state;
        }

        [Test]
        public void 默认值_分身未激活()
        {
            var s = NewState();
            Assert.IsFalse(s.GhostCloneActive);
            Assert.AreEqual(0f, s.GhostCloneSanity, 0.001f);
        }

        [Test]
        public void 哈希_分身字段参与计算()
        {
            var a = NewState();
            var b = NewState();
            a.GhostCloneActive = true;
            a.GhostCloneSanity = 2.5f;
            Assert.AreNotEqual(a.ComputeStateHash(), b.ComputeStateHash(), "分身字段必须进哈希");
        }

        [Test]
        public void 快照往返_分身字段保持()
        {
            var s = NewState();
            s.GhostCloneActive = true;
            s.GhostCloneSanity = 2.5f;
            s.CurrentTick = 7;               // 顺带推进，验证非零状态往返
            byte[] bytes = WorldStateSerializer.Serialize(s);
            WorldState r = WorldStateSerializer.Deserialize(bytes);
            Assert.IsTrue(r.GhostCloneActive);
            Assert.AreEqual(2.5f, r.GhostCloneSanity, 0.001f);
        }

        [Test]
        public void Delta往返_分身字段应用()
        {
            var from = NewState();
            var to = NewState();
            to.GhostCloneActive = true;
            to.GhostCloneSanity = 1.5f;
            to.CurrentTurn = 3;
            to.CurrentTick = 9;

            byte[] delta = WorldStateDelta.Compute(from, to);
            WorldStateDelta.Apply(from, delta);   // 原地应用 + 内部哈希自校验
            Assert.IsTrue(from.GhostCloneActive);
            Assert.AreEqual(1.5f, from.GhostCloneSanity, 0.001f);
        }
    }
}
