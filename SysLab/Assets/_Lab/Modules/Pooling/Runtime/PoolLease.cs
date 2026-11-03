using System;
using System.Threading;

namespace GameSysLab.Modules.Pooling
{
    /// <summary>一次借用的独立句柄；释放后不可重新使用，也不撤销调用者保存的裸引用。</summary>
    public sealed class PoolLease<T> : IDisposable where T : class
    {
        private ObjectPool<T> owner;
        private T value;
        private readonly int threadId;
        internal PoolLease(ObjectPool<T> owner, T value, int threadId)
        { this.owner = owner; this.value = value; this.threadId = threadId; }
        public bool IsValid { get { CheckThread(); return owner != null; } }
        public T Value { get { CheckThread(); return owner != null ? value : throw new ObjectDisposedException(nameof(PoolLease<T>)); } }
        public void Dispose() { CheckThread(); owner?.Return(this, value); }
        internal void Invalidate() { owner = null; value = null; }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId) throw new InvalidOperationException("租约只能在创建线程使用。");
        }
    }
}
