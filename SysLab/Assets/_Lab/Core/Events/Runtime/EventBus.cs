using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace GameSysLab.Core.Events
{
    /// <summary>
    /// 创建线程负责同步派发与生命周期，任意线程只能投递消息。
    /// 通常应在 Unity 主线程创建；发布者应先完成状态变更，再通知监听者。
    /// </summary>
    public sealed class EventBus : IDisposable
    {
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly Dictionary<Type, IEventChannel> channels = new Dictionary<Type, IEventChannel>();
        private readonly object queueLock = new object();
        private readonly Queue<IQueuedEvent> queue = new Queue<IQueuedEvent>();
        private readonly Action<EventError> reportError;
        private readonly int queueCapacity;
        private readonly bool strict;
        internal readonly bool MeasureTime;
        private bool disposed;
        private bool processing;
        private long nextId, published, posted, rejected, errors;

        public EventBus(Action<EventError> reportError, EventBusOptions options = null)
        {
            this.reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
            options = options ?? new EventBusOptions();
            if (options.QueueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(options.QueueCapacity));
            queueCapacity = options.QueueCapacity;
            strict = options.ThrowListenerExceptions;
            MeasureTime = options.MeasureListenerTime;
        }

        /// <summary>优先级较大者先执行，同级按订阅顺序执行；句柄由订阅者持有并释放。</summary>
        public IDisposable Subscribe<T>(Action<T> handler, int priority = 0, string label = null) where T : IEvent
        {
            CheckUsable();
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var members = handler.GetInvocationList();
            foreach (var member in members)
                if (member.Method.IsDefined(typeof(AsyncStateMachineAttribute), false))
                    throw new ArgumentException("同步事件不支持 async void，请使用独立异步服务。", nameof(handler));
            if (members.Length != 1)
                throw new ArgumentException("请分别订阅回调，不接受多播委托。", nameof(handler));
            return GetChannel<T>().Subscribe(handler, ++nextId, priority, label);
        }

        /// <summary>立即通知；同一总线的回调中需要继续发送时请使用 TryQueueEvent。</summary>
        public void Publish<T>(T message) where T : IEvent
        {
            CheckUsable();
            CheckNotProcessing();
            ValidateMessage(message);
            List<Exception> failures = null;
            processing = true;
            try { Dispatch(message, ref failures); }
            finally { processing = false; }
            ThrowFailures(failures);
        }

        /// <summary>线程安全投递。队列满或已经关闭返回 false；不复制负载，投递后不得修改。</summary>
        public bool TryQueueEvent<T>(T message) where T : IEvent
        {
            ValidateMessage(message);
            lock (queueLock)
            {
                if (disposed || queue.Count >= queueCapacity) { rejected++; return false; }
                queue.Enqueue(new QueuedEvent<T>(message));
                posted++;
                return true;
            }
        }

        /// <summary>只处理入口时已排队的消息，新投递留给下一轮；返回本轮出队数量。</summary>
        public int ProcessQueuedEvents(int maxEvents = 256)
        {
            CheckUsable();
            CheckNotProcessing();
            if (maxEvents <= 0) throw new ArgumentOutOfRangeException(nameof(maxEvents));
            int limit;
            lock (queueLock) limit = Math.Min(maxEvents, queue.Count);
            int count = 0;
            List<Exception> failures = null;
            processing = true;
            try
            {
                while (count < limit)
                {
                    IQueuedEvent item;
                    lock (queueLock)
                    {
                        if (disposed) break;
                        item = queue.Dequeue();
                    }
                    count++;
                    item.Dispatch(this, ref failures);
                }
            }
            finally { processing = false; }
            ThrowFailures(failures);
            return count;
        }

        public EventBusDiagnostics GetDiagnosticsSnapshot()
        {
            CheckThread();
            var result = new List<EventChannelDiagnostics>(channels.Count);
            foreach (var channel in channels.Values) result.Add(channel.Snapshot());
            result.Sort((a, b) => string.CompareOrdinal(a.EventType.FullName, b.EventType.FullName));
            lock (queueLock)
                return new EventBusDiagnostics(disposed, published, posted, rejected, errors, queue.Count, result);
        }

        public void Dispose()
        {
            CheckThread();
            lock (queueLock)
            {
                if (disposed) return;
                disposed = true;
                queue.Clear();
            }
            foreach (var channel in channels.Values) channel.Clear();
            channels.Clear();
        }

        internal void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("此操作必须在创建总线的线程执行；后台线程只能 TryQueueEvent。");
        }

        internal void Report(EventError error, ref List<Exception> failures)
        {
            errors++;
            if (strict) AddFailure(ref failures, error.Exception);
            try { reportError(error); }
            catch (Exception reportingError)
            {
                if (!strict) AddFailure(ref failures, error.Exception);
                AddFailure(ref failures, reportingError);
            }
        }

        private EventChannel<T> GetChannel<T>() where T : IEvent
        {
            if (!channels.TryGetValue(typeof(T), out var channel))
            {
                channel = new EventChannel<T>(this);
                channels.Add(typeof(T), channel);
            }
            // 字典只允许由本方法建立类型与泛型通道的对应关系。
            return (EventChannel<T>)channel;
        }

        private void Dispatch<T>(T message, ref List<Exception> failures) where T : IEvent
        {
            published++;
            if (channels.TryGetValue(typeof(T), out var channel))
                ((EventChannel<T>)channel).Dispatch(message, ref failures);
        }

        private void CheckUsable()
        {
            CheckThread();
            if (disposed) throw new ObjectDisposedException(nameof(EventBus));
        }

        private void CheckNotProcessing()
        {
            if (processing) throw new InvalidOperationException("不允许重入派发，请使用 TryQueueEvent 延后通知。");
        }

        private static void ValidateMessage<T>(T message) where T : IEvent
        {
            // 泛型空值检查不会将非空值类型负载存入 object 容器。
            if (message is null) throw new ArgumentNullException(nameof(message));
        }

        private static void AddFailure(ref List<Exception> failures, Exception error)
        { if (failures == null) failures = new List<Exception>(); failures.Add(error); }

        private static void ThrowFailures(List<Exception> failures)
        { if (failures != null) throw new AggregateException("事件处理发生错误；其他监听已继续执行。", failures); }

        private interface IQueuedEvent { void Dispatch(EventBus bus, ref List<Exception> failures); }
        private sealed class QueuedEvent<T> : IQueuedEvent where T : IEvent
        {
            private readonly T message;
            public QueuedEvent(T message) { this.message = message; }
            public void Dispatch(EventBus bus, ref List<Exception> failures) => bus.Dispatch(message, ref failures);
        }
    }
}
