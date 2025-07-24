using System;
using System.Collections.Generic;

namespace Dynasty.Core
{
    public delegate void EventBusHandler(ref GameplayEvent e);

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

        public EventBus(int initialCapacity = 64)
        {
            _subscriptions = new Subscription[initialCapacity];
            _count = 0;
            _nextOrder = 0;
            _dispatchDepth = 0;
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
            if (_dispatchDepth >= GameConfig.EventMaxDepth) return;
            _dispatchDepth++;
            for (int i = 0; i < _count; i++)
                if (_subscriptions[i].EventType == eventType)
                    _subscriptions[i].Handler(ref e);
            _dispatchDepth--;
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
