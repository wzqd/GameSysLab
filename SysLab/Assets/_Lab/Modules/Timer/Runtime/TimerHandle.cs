using System;
using System.Threading;

namespace GameSysLab.Modules.Timer
{
    /// <summary>任务的生命周期；完成和取消都是不可逆的终止状态。</summary>
    public enum TimerState { Scheduled, Paused, Completed, Cancelled }

    /// <summary>一个任务的控制句柄；不拥有调度器，可以交给外部生命周期集合清理。</summary>
    public sealed class TimerHandle : IDisposable
    {
        private readonly int threadId;
        private readonly double durationSeconds;
        private readonly bool repeating;
        private TimerScheduler owner;
        private Action callback;
        private TimerState state = TimerState.Scheduled;
        private double remainingSeconds;
        private bool dueThisAdvance;

        public TimerState State { get { CheckThread(); return state; } }
        public double RemainingSeconds { get { CheckThread(); return remainingSeconds; } }
        internal bool IsTerminal => state == TimerState.Completed || state == TimerState.Cancelled;

        internal TimerHandle(TimerScheduler owner, double seconds, Action callback, bool repeating, int threadId)
        {
            this.owner = owner;
            this.callback = callback;
            this.repeating = repeating;
            this.threadId = threadId;
            durationSeconds = remainingSeconds = seconds;
        }

        /// <summary>冻结剩余时间，并取消本轮尚未执行的到期回调。</summary>
        public void Pause()
        {
            CheckActive();
            state = TimerState.Paused;
            dueThisAdvance = false;
        }

        /// <summary>从剩余时间继续；不会补执行本轮已跳过的回调。</summary>
        public void Resume()
        {
            CheckActive();
            if (state != TimerState.Paused) return;
            state = TimerState.Scheduled;
            dueThisAdvance = false;
        }

        /// <summary>恢复最初完整时长，保持暂停状态；已结束任务不能重置。</summary>
        public void Reset()
        {
            CheckActive();
            remainingSeconds = durationSeconds;
            dueThisAdvance = false;
        }

        /// <summary>永久取消并释放回调引用；已完成任务仍保持 Completed。</summary>
        public void Cancel()
        {
            CheckThread();
            if (IsTerminal) return;
            var previousOwner = owner;
            End(TimerState.Cancelled);
            previousOwner.Remove(this);
        }

        public void Dispose() { Cancel(); }

        internal void AdvanceRemaining(double deltaSeconds)
        {
            dueThisAdvance = false;
            if (state != TimerState.Scheduled) return;
            remainingSeconds = Math.Max(0, remainingSeconds - deltaSeconds);
            dueThisAdvance = remainingSeconds == 0;
        }

        internal void InvokeIfDue()
        {
            if (!dueThisAdvance || state != TimerState.Scheduled) return;
            dueThisAdvance = false;
            var action = callback;
            // 先确定后续状态，回调内的自我控制才不会被返回后的代码覆盖。
            if (repeating) remainingSeconds = durationSeconds;
            else End(TimerState.Completed);
            action();
        }

        internal void CancelFromOwner()
        {
            if (!IsTerminal) End(TimerState.Cancelled);
        }

        private void End(TimerState finalState)
        {
            state = finalState;
            remainingSeconds = 0;
            dueThisAdvance = false;
            callback = null;
            owner = null;
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("任务句柄只能在创建线程使用。");
        }

        private void CheckActive()
        {
            CheckThread();
            if (IsTerminal) throw new InvalidOperationException("任务已经结束，请创建新任务。");
        }
    }
}
