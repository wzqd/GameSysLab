using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GameSysLab.Core.Events
{
    internal interface IEventChannel
    {
        void Clear();
        EventChannelDiagnostics Snapshot();
    }

    /// <summary>类型化稳定列表：退订只标记，安全时集中整理，不为每次发布制作快照。</summary>
    internal sealed class EventChannel<T> : IEventChannel where T : IEvent
    {
        private readonly EventBus bus;
        private readonly List<Subscription> listeners = new List<Subscription>();
        private readonly List<Subscription> pending = new List<Subscription>();
        private int active, removed;
        private bool dispatching, closed;
        private long published;
        private static readonly Comparison<Subscription> Order = (a, b) =>
        {
            int priority = b.Priority.CompareTo(a.Priority);
            return priority != 0 ? priority : a.Id.CompareTo(b.Id);
        };

        public EventChannel(EventBus bus) { this.bus = bus; }

        public IDisposable Subscribe(Action<T> handler, long id, int priority, string label)
        {
            var node = new Subscription(this, handler, id, priority, label);
            active++;
            if (dispatching) pending.Add(node);
            else InsertOrdered(node);
            return node;
        }

        private void InsertOrdered(Subscription node)
        {
            // 列表始终有序；单次订阅只查找插入点，避免重排全部既有监听。
            int low = 0, high = listeners.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (Order(listeners[middle], node) <= 0) low = middle + 1;
                else high = middle;
            }
            listeners.Insert(low, node);
        }

        public void Dispatch(T message, ref List<Exception> failures)
        {
            published++;
            dispatching = true;
            try
            {
                for (int i = 0; i < listeners.Count && !closed; i++)
                {
                    var node = listeners[i];
                    var handler = node.Handler;
                    if (handler == null) continue;
                    long start = bus.MeasureTime ? Stopwatch.GetTimestamp() : 0;
                    node.Calls++;
                    Exception failure = null;
                    try { handler(message); }
                    catch (Exception error) { failure = error; }
                    finally { if (bus.MeasureTime) node.Ticks += Stopwatch.GetTimestamp() - start; }
                    // 单监听耗时不包含错误报告器的耗时。
                    if (failure != null) bus.Report(new EventError(typeof(T), node.Id, node.Label, failure), ref failures);
                }
            }
            finally
            {
                dispatching = false;
                if (removed > 0) Compact(listeners);
                if (pending.Count > 0)
                {
                    foreach (var node in pending) if (node.Handler != null) listeners.Add(node);
                    pending.Clear();
                    listeners.Sort(Order);
                }
                removed = 0;
            }
        }

        private void Remove()
        {
            active--;
            removed++;
            if (dispatching) return;
            if (active == 0) { listeners.Clear(); pending.Clear(); removed = 0; }
            else if (removed >= 16 && removed * 2 >= listeners.Count)
            { Compact(listeners); removed = 0; }
        }

        private static void Compact(List<Subscription> nodes)
        {
            int write = 0;
            for (int read = 0; read < nodes.Count; read++)
                if (nodes[read].Handler != null) nodes[write++] = nodes[read];
            if (write < nodes.Count) nodes.RemoveRange(write, nodes.Count - write);
        }

        public void Clear()
        {
            closed = true;
            foreach (var node in listeners) node.Deactivate();
            foreach (var node in pending) node.Deactivate();
            listeners.Clear(); pending.Clear(); active = 0; removed = 0;
        }

        public EventChannelDiagnostics Snapshot()
        {
            var nodes = new List<SubscriptionDiagnostics>(active);
            foreach (var node in listeners) if (node.Handler != null) nodes.Add(node.Snapshot());
            foreach (var node in pending) if (node.Handler != null) nodes.Add(node.Snapshot());
            return new EventChannelDiagnostics(typeof(T), published, nodes);
        }

        private sealed class Subscription : IDisposable
        {
            private EventChannel<T> owner;
            private readonly int threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            public Action<T> Handler { get; private set; }
            public readonly long Id;
            public readonly int Priority;
            public readonly string Label;
            public long Calls, Ticks;
            public Subscription(EventChannel<T> owner, Action<T> handler, long id, int priority, string label)
            { this.owner = owner; Handler = handler; Id = id; Priority = priority; Label = label; }

            public void Dispose()
            {
                if (System.Threading.Thread.CurrentThread.ManagedThreadId != threadId)
                    throw new InvalidOperationException("必须在订阅创建线程退订。");
                var previousOwner = owner;
                if (previousOwner == null) return;
                Deactivate();
                previousOwner.Remove();
            }

            public void Deactivate() { Handler = null; owner = null; }
            public SubscriptionDiagnostics Snapshot() => new SubscriptionDiagnostics(Id, Label, Priority, Calls, Ticks);
        }
    }
}
