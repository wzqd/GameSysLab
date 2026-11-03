using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace GameSysLab.Modules.Pooling
{
    /// <summary>拥有实例的同步对象池；只限制空闲缓存，所有操作限创建线程。</summary>
    public sealed class ObjectPool<T> : IDisposable where T : class
    {
        private sealed class Identity : IEqualityComparer<T>
        {
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly Func<T> create;
        private readonly Action<T> reset;
        private readonly Action<T> release;
        private readonly int maxInactive;
        private readonly Stack<T> inactive = new Stack<T>();
        private readonly Dictionary<T, PoolLease<T>> owned = new Dictionary<T, PoolLease<T>>(new Identity());
        private bool closed;
        private bool busy;
        public int ActiveCount { get { CheckThread(); return owned.Count - inactive.Count; } }
        public int InactiveCount { get { CheckThread(); return inactive.Count; } }
        public bool IsClosed { get { CheckThread(); return closed; } }

        public ObjectPool(Func<T> create, Action<T> reset, Action<T> release, int maxInactive = 32)
        {
            this.create = create ?? throw new ArgumentNullException(nameof(create));
            this.reset = reset ?? throw new ArgumentNullException(nameof(reset));
            this.release = release ?? throw new ArgumentNullException(nameof(release));
            if (maxInactive < 0) throw new ArgumentOutOfRangeException(nameof(maxInactive));
            this.maxInactive = maxInactive;
        }

        /// <summary>借出实例；每次产生独立租约，不复用外部仍可能持有的句柄。</summary>
        public PoolLease<T> Rent()
        {
            Enter();
            try
            {
                if (closed) throw new ObjectDisposedException(nameof(ObjectPool<T>));
                T value;
                if (inactive.Count > 0) value = inactive.Pop();
                else
                {
                    value = create();
                    if (value == null || owned.ContainsKey(value))
                        throw new InvalidOperationException("工厂必须返回新的非空实例。");
                }
                var lease = new PoolLease<T>(this, value, threadId);
                owned[value] = lease;
                return lease;
            }
            finally { busy = false; }
        }

        /// <summary>主动淘汰本池借用，用于适配层检测到实例失效时，不执行重置。</summary>
        public void Discard(PoolLease<T> lease)
        {
            Enter();
            try
            {
                if (lease == null) throw new ArgumentNullException(nameof(lease));
                if (!lease.IsValid) return;
                var value = lease.Value;
                if (!owned.TryGetValue(value, out var current) || !ReferenceEquals(current, lease))
                    throw new ArgumentException("租约不属于本池。", nameof(lease));
                lease.Invalidate(); owned.Remove(value); release(value);
            }
            finally { busy = false; }
        }

        internal void Return(PoolLease<T> lease, T value)
        {
            Enter();
            try
            {
                if (!owned.TryGetValue(value, out var current) || !ReferenceEquals(current, lease)) return;
                lease.Invalidate();
                owned.Remove(value);
                try { reset(value); }
                catch (Exception resetError)
                {
                    try { release(value); }
                    catch (Exception releaseError) { throw new AggregateException(resetError, releaseError); }
                    throw;
                }
                if (inactive.Count < maxInactive)
                {
                    owned.Add(value, null);
                    inactive.Push(value);
                }
                else release(value);
            }
            finally { busy = false; }
        }

        /// <summary>先使所有借用失效，再逐项释放；错误不阻止其他实例清理。</summary>
        public void Dispose()
        {
            CheckThread();
            if (closed) return;
            Enter();
            closed = true;
            try
            {
                var values = new List<T>(owned.Keys);
                foreach (var lease in owned.Values) lease?.Invalidate();
                owned.Clear();
                inactive.Clear();
                List<Exception> errors = null;
                foreach (var value in values)
                {
                    try { release(value); }
                    catch (Exception error) { (errors ?? (errors = new List<Exception>())).Add(error); }
                }
                if (errors != null) throw new AggregateException(errors);
            }
            finally { busy = false; }
        }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId) throw new InvalidOperationException("对象池只能在创建线程使用。");
        }
        private void Enter()
        {
            CheckThread();
            if (busy) throw new InvalidOperationException("回调中不能重入修改同一个对象池。");
            busy = true;
        }
    }
}
