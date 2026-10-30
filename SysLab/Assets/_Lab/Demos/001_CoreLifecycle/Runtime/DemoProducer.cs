using System;
using System.Threading;
using System.Threading.Tasks;
using GameSysLab.Core.Events;

namespace GameSysLab.Demos.CoreLifecycle
{
    /// <summary>有限后台生产者；仅持有普通数据与启动时的总线，不持有 Unity 对象。</summary>
    public sealed class DemoProducer : IDisposable
    {
        private readonly object gate = new object();
        private readonly EventBus target;
        private readonly int first, count, delayMilliseconds, failAt;
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        private int accepted, rejected, cancelled;
        private Exception failure;
        public Task Completion { get; }
        public int Accepted => Volatile.Read(ref accepted);
        public int Rejected => Volatile.Read(ref rejected);
        public bool WasCancelled => Volatile.Read(ref cancelled) != 0;
        public Exception Failure => Volatile.Read(ref failure);

        public DemoProducer(EventBus target, int first = 1, int count = 8, int delayMilliseconds = 150, int failAt = -1)
        {
            this.target = target ?? throw new ArgumentNullException(nameof(target));
            if (count <= 0 || delayMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(count));
            this.first = first;
            this.count = count;
            this.delayMilliseconds = delayMilliseconds;
            this.failAt = failAt;
            Completion = Task.Run(RunAsync);
        }

        public void Dispose()
        {
            // Cancel 与任务结束后的 Dispose 使用同一把锁，不提前释放工作线程仍在用的信号。
            lock (gate) cancellation?.Cancel();
        }

        private async Task RunAsync()
        {
            var token = cancellation.Token;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if (i == failAt) throw new InvalidOperationException("Demo 主动制造的后台生产故障");
                    if (target.TryQueueEvent(new DemoMessage(first + i))) Interlocked.Increment(ref accepted);
                    else Interlocked.Increment(ref rejected);
                    await Task.Delay(delayMilliseconds, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Volatile.Write(ref cancelled, 1);
            }
            catch (Exception error)
            {
                // 在工作线程观察异常，主线程通过 Failure 报告；Task 不产生无人观察的异常。
                Volatile.Write(ref failure, error);
            }
            finally
            {
                lock (gate)
                {
                    cancellation.Dispose();
                    cancellation = null;
                }
            }
        }
    }
}
