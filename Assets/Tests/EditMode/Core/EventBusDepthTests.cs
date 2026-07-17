using System;
using NUnit.Framework;
using Core;
using Core.GAS;

namespace Dynasty.Tests
{
    public class EventBusDepthTests
    {
        private static GameplayEvent MakeEvent(int type = GameplayEventTypes.TurnStart)
            => new GameplayEvent(type, -1, -1);

        [Test]
        public void 递归风暴_深度越限时熔断_告警幂等且计数递增()
        {
            var bus = new EventBus();
            int warnings = 0;
            bus.DepthWarningSink = msg => warnings++;

            bus.Subscribe(GameplayEventTypes.TurnStart, 0, delegate (ref GameplayEvent e)
            {
                var copy = e;
                bus.Dispatch(GameplayEventTypes.TurnStart, ref copy);
            });

            var evt = MakeEvent();
            bus.Dispatch(GameplayEventTypes.TurnStart, ref evt);

            Assert.AreEqual(1, warnings, "一次递归风暴恰好告警 1 次（幂等）");
            Assert.AreEqual(1, bus.DepthViolationCount, "越限计数 = 1");
            Assert.AreEqual(0, bus.DispatchDepth, "风暴结束后深度归零");
        }

        [Test]
        public void 同风暴多次越限_只告警一次但计数全记()
        {
            var bus = new EventBus();
            int warnings = 0;
            bus.DepthWarningSink = msg => warnings++;

            bus.Subscribe(GameplayEventTypes.TurnStart, 0, delegate (ref GameplayEvent e)
            {
                var copy = e;
                bus.Dispatch(GameplayEventTypes.TurnStart, ref copy); // 递归到越限（第 1 次）
                var other = MakeEvent(GameplayEventTypes.TurnEnd);    // 同深度再派发（第 2 次）
                bus.Dispatch(GameplayEventTypes.TurnEnd, ref other);
            });

            var evt = MakeEvent();
            bus.Dispatch(GameplayEventTypes.TurnStart, ref evt);

            Assert.AreEqual(1, warnings, "同风暴内多次越限只告警 1 次");
            Assert.AreEqual(2, bus.DepthViolationCount, "每次越限都计入计数");
        }

        [Test]
        public void handler抛异常_深度计数恢复不泄漏()
        {
            var bus = new EventBus();
            bus.Subscribe(GameplayEventTypes.TurnStart, 0, delegate (ref GameplayEvent e)
            {
                throw new InvalidOperationException("boom");
            });

            var evt = MakeEvent();
            Assert.Throws<InvalidOperationException>(
                () => bus.Dispatch(GameplayEventTypes.TurnStart, ref evt));
            Assert.AreEqual(0, bus.DispatchDepth, "异常后深度归零（try/finally 保护）");

            int delivered = 0;
            bus.Subscribe(GameplayEventTypes.TurnEnd, 0, delegate (ref GameplayEvent e) { delivered++; });
            var ev2 = MakeEvent(GameplayEventTypes.TurnEnd);
            bus.Dispatch(GameplayEventTypes.TurnEnd, ref ev2);
            Assert.AreEqual(1, delivered, "异常后事件总线仍正常派发");
        }

        [Test]
        public void 深度未越限_无告警无计数()
        {
            var bus = new EventBus();
            int warnings = 0;
            bus.DepthWarningSink = msg => warnings++;
            int delivered = 0;
            bus.Subscribe(GameplayEventTypes.TurnStart, 0, delegate (ref GameplayEvent e) { delivered++; });

            var evt = MakeEvent();
            bus.Dispatch(GameplayEventTypes.TurnStart, ref evt);

            Assert.AreEqual(1, delivered, "正常派发");
            Assert.AreEqual(0, warnings, "不告警");
            Assert.AreEqual(0, bus.DepthViolationCount, "不计数");
            Assert.AreEqual(0, bus.DispatchDepth);
        }
    }
}
