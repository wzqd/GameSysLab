using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GameSysLab.Core.Events
{
    /// <summary>标记可通过事件总线发送的事实；实现类型应保持数据不可变。</summary>
    public interface IEvent { }

    /// <summary>构造时复制配置，之后修改配置对象不会影响总线。</summary>
    public sealed class EventBusOptions
    {
        public int QueueCapacity { get; set; } = 4096;
        public bool ThrowListenerExceptions { get; set; }
        public bool MeasureListenerTime { get; set; }
    }

    /// <summary>错误上下文不保存事件负载或订阅者实例。</summary>
    public readonly struct EventError
    {
        public Type EventType { get; }
        public long SubscriptionId { get; }
        public string Label { get; }
        public Exception Exception { get; }
        internal EventError(Type type, long id, string label, Exception exception)
        { EventType = type; SubscriptionId = id; Label = label; Exception = exception; }
    }

    public sealed class SubscriptionDiagnostics
    {
        public long Id { get; }
        public string Label { get; }
        public int Priority { get; }
        public long InvocationCount { get; }
        public long ElapsedStopwatchTicks { get; }
        internal SubscriptionDiagnostics(long id, string label, int priority, long calls, long ticks)
        { Id = id; Label = label; Priority = priority; InvocationCount = calls; ElapsedStopwatchTicks = ticks; }
    }

    public sealed class EventChannelDiagnostics
    {
        public Type EventType { get; }
        public long PublishCount { get; }
        public int ActiveSubscriptions => Subscriptions.Count;
        public ReadOnlyCollection<SubscriptionDiagnostics> Subscriptions { get; }
        internal EventChannelDiagnostics(Type type, long count, List<SubscriptionDiagnostics> subscriptions)
        { EventType = type; PublishCount = count; Subscriptions = subscriptions.AsReadOnly(); }
    }

    /// <summary>主动读取时生成的独立只读快照，不适合每帧读取。</summary>
    public sealed class EventBusDiagnostics
    {
        public bool IsDisposed { get; }
        public long PublishCount { get; }
        public long PostedCount { get; }
        public long RejectedPosts { get; }
        public long ErrorCount { get; }
        public int QueueLength { get; }
        public ReadOnlyCollection<EventChannelDiagnostics> Channels { get; }
        internal EventBusDiagnostics(bool disposed, long published, long posted, long rejected,
            long errors, int queued, List<EventChannelDiagnostics> channels)
        {
            IsDisposed = disposed; PublishCount = published; PostedCount = posted;
            RejectedPosts = rejected; ErrorCount = errors; QueueLength = queued;
            Channels = channels.AsReadOnly();
        }
    }
}
