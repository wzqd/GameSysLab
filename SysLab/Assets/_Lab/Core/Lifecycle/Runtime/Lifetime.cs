using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace GameSysLab.Core.Lifecycle
{
    /// <summary>拥有已登记资源，逆序清理；借用资源不应登记。所有调用限创建线程。</summary>
    public sealed class Lifetime : IDisposable
    {
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly List<IDisposable> resources = new List<IDisposable>();
        // 弱键记录身份，避免重复释放，同时不通过历史记录延长资源生命。
        private readonly ConditionalWeakTable<object, Registration> registered = new ConditionalWeakTable<object, Registration>();
        private bool disposed;

        public T Add<T>(T resource) where T : IDisposable
        {
            CheckThread();
            if (resource is null) throw new ArgumentNullException(nameof(resource));
            object key = resource;
            if (registered.TryGetValue(key, out _)) return resource;
            registered.Add(key, new Registration());
            if (disposed) resource.Dispose();
            else resources.Add(resource);
            return resource;
        }

        public void Dispose()
        {
            CheckThread();
            if (disposed) return;
            disposed = true;
            List<Exception> failures = null;
            for (int i = resources.Count - 1; i >= 0; i--)
            {
                try { resources[i].Dispose(); }
                catch (Exception error)
                { if (failures == null) failures = new List<Exception>(); failures.Add(error); }
            }
            resources.Clear();
            if (failures != null) throw new AggregateException("部分资源清理失败；其他资源已继续释放。", failures);
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("生命周期集合只能在创建线程使用。");
        }

        private sealed class Registration { }
    }
}
