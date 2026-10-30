using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace GameSysLab.Modules.Timer
{
    /// <summary>由拥有者推进时间的独立计时器；所有操作限创建线程。</summary>
    public sealed class TimerScheduler : IDisposable
    {
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly List<TimerHandle> tasks = new List<TimerHandle>();
        private bool advancing;
        private bool disposed;

        /// <summary>等待指定秒数后执行一次；返回的句柄由任务使用者负责清理。</summary>
        public TimerHandle ScheduleOnce(double delaySeconds, Action callback)
        {
            return Schedule(delaySeconds, callback, false, nameof(delaySeconds));
        }

        /// <summary>每隔指定秒数执行；第一次也等待完整周期，卡顿后不补执行。</summary>
        public TimerHandle ScheduleRepeating(double intervalSeconds, Action callback)
        {
            return Schedule(intervalSeconds, callback, true, nameof(intervalSeconds));
        }

        private TimerHandle Schedule(double seconds, Action callback, bool repeating, string parameter)
        {
            CheckUsable();
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
                throw new ArgumentOutOfRangeException(parameter, "时长必须是有限正数。");
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var members = callback.GetInvocationList();
            foreach (var member in members)
                if (member.Method.IsDefined(typeof(AsyncStateMachineAttribute), false))
                    throw new ArgumentException("计时回调不支持 async void。", nameof(callback));
            if (members.Length != 1)
                throw new ArgumentException("请分别创建任务，不接受多播回调。", nameof(callback));
            var task = new TimerHandle(this, seconds, callback, repeating, threadId);
            tasks.Add(task);
            return task;
        }

        /// <summary>推进有限非负秒数；先更新剩余时间，再按创建顺序执行到期任务。</summary>
        public void AdvanceTime(double deltaSeconds)
        {
            CheckUsable();
            if (advancing) throw new InvalidOperationException("不允许嵌套推进同一个计时器。");
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (deltaSeconds == 0) return;

            advancing = true;
            int count = tasks.Count;
            List<Exception> failures = null;
            try
            {
                // 固定本轮范围，新任务不消耗调用前已经过去的时间。
                for (int i = 0; i < count; i++) tasks[i].AdvanceRemaining(deltaSeconds);
                for (int i = 0; i < count && !disposed; i++)
                {
                    try { tasks[i].InvokeIfDue(); }
                    catch (Exception error)
                    {
                        if (failures == null) failures = new List<Exception>();
                        failures.Add(error);
                    }
                }
            }
            finally
            {
                advancing = false;
                Compact();
            }
            if (failures != null) throw new AggregateException("计时回调失败，其他到期任务已继续执行。", failures);
        }

        /// <summary>永久关闭并取消所有未结束任务；重复释放安全。</summary>
        public void Dispose()
        {
            CheckThread();
            if (disposed) return;
            disposed = true;
            foreach (var task in tasks) task.CancelFromOwner();
            tasks.Clear();
        }

        internal void Remove(TimerHandle task)
        {
            // 遍历中仅失效，避免修改下标；遍历外立即移除，成本为 O(n)。
            if (!advancing) tasks.Remove(task);
        }

        private void Compact()
        {
            int write = 0;
            for (int read = 0; read < tasks.Count; read++)
                if (!tasks[read].IsTerminal) tasks[write++] = tasks[read];
            if (write < tasks.Count) tasks.RemoveRange(write, tasks.Count - write);
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("计时器只能在创建线程使用。");
        }

        private void CheckUsable()
        {
            CheckThread();
            if (disposed) throw new ObjectDisposedException(nameof(TimerScheduler));
        }
    }
}
