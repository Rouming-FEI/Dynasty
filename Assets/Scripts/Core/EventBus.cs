using Core.GAS;
using System;
using System.Collections.Generic;

namespace Core
{
    public delegate void EventBusHandler(ref GameplayEvent e);

    public delegate void EventBusRecorderHook(int eventType, ref GameplayEvent e);

    public class EventBus
    {
        private struct Subscription
        {
            public int EventType;
            public int Priority;
            public int Order;
            public EventBusHandler Handler;
        }

        private Subscription[] _subscriptions;
        private int _count;
        private int _nextOrder;
        private int _dispatchDepth;
        private bool _depthWarningActive;
        private int _depthViolations;

        /// <summary>旁路录制钩子（调试/回放用；null 零开销）。记录 handler 修改后的最终事件值。
        /// 回调约束：禁止修改状态/抛异常——违反会破坏帧同步一致性。
        /// 收 ref e：对 e 的改动会泄漏回调用方，录制器只读不写。</summary>
        public EventBusRecorderHook RecorderHook;

        /// <summary>深度越限告警出口（Unity 层可接 Debug.LogError；null 零开销）。
        /// 幂等：同一风暴（深度未归零）内只触发一次。</summary>
        public Action<string> DepthWarningSink;

        /// <summary>当前派发深度（熔断诊断可观测）。</summary>
        public int DispatchDepth => _dispatchDepth;

        /// <summary>累计深度越限次数（每次被熔断丢弃的事件派发 +1）。</summary>
        public int DepthViolationCount => _depthViolations;

        public EventBus(int initialCapacity = 64)
        {
            _subscriptions = new Subscription[initialCapacity];
            _count = 0;
            _nextOrder = 0;
            _dispatchDepth = 0;
            _depthWarningActive = false;
            _depthViolations = 0;
        }

        public void Subscribe(int eventType, int priority, EventBusHandler handler)
        {
            if (handler == null) return;
            if (_count >= _subscriptions.Length)
                Array.Resize(ref _subscriptions, _subscriptions.Length * 2);
            _subscriptions[_count++] = new Subscription
            {
                EventType = eventType, Priority = priority, Order = _nextOrder++, Handler = handler
            };
            // Stable sort: EventType -> Priority -> Order
            Array.Sort(_subscriptions, 0, _count, SubscriptionComparer.Instance);
        }

        public void Dispatch(int eventType, ref GameplayEvent e)
        {
            if (_dispatchDepth >= GameConfig.EventMaxDepth)
            {
                _depthViolations++;
                if (!_depthWarningActive)
                {
                    _depthWarningActive = true;
                    // 熔断是异常路径，允许告警消息分配（Hot Path 零 GC 约束不覆盖）
                    string msg = $"[EventBus] 事件派发深度越限：depth={_dispatchDepth} >= " +
                                 $"EventMaxDepth={GameConfig.EventMaxDepth}，事件 type={eventType} 被熔断丢弃（事件风暴）";
                    System.Diagnostics.Debug.WriteLine(msg);
                    DepthWarningSink?.Invoke(msg);
                }
                return;
            }
            _dispatchDepth++;
            try
            {
                for (int i = 0; i < _count; i++)
                    if (_subscriptions[i].EventType == eventType)
                        _subscriptions[i].Handler(ref e);
                if (RecorderHook != null) RecorderHook(eventType, ref e);
            }
            finally
            {
                _dispatchDepth--;
                if (_dispatchDepth == 0) _depthWarningActive = false;
            }
        }

        private class SubscriptionComparer : IComparer<Subscription>
        {
            public static readonly SubscriptionComparer Instance = new();
            public int Compare(Subscription x, Subscription y)
            {
                int c = x.EventType.CompareTo(y.EventType);
                if (c != 0) return c;
                c = x.Priority.CompareTo(y.Priority);
                if (c != 0) return c;
                return x.Order.CompareTo(y.Order);
            }
        }
    }
}
