using NUnit.Framework;
using Core;

namespace Dynasty.Tests
{
    /// <summary>验证测试程序集能看到 Simulation 层类型，且确定性 RNG 可复现。</summary>
    public class SmokeTest
    {
        [Test]
        public void 测试程序集可以引用Simulation层()
        {
            var state = new WorldState(3, 42);
            Assert.AreEqual(3, state.PlayerCount);
            Assert.AreEqual(TurnPhase.Waiting, state.Phase);
        }

        [Test]
        public void 相同种子的随机数序列一致()
        {
            var a = new DeterministicRandom(42);
            var b = new DeterministicRandom(42);
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(a.Next(100), b.Next(100));
        }
    }
}
