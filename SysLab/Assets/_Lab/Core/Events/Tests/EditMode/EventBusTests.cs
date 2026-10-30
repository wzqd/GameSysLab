using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Profiling;

namespace GameSysLab.Core.Events.Tests
{
    public sealed class EventBusTests
    {
        private EventBus bus;
        private List<EventError> errors;
        [SetUp] public void SetUp() { errors = new List<EventError>(); bus = new EventBus(errors.Add); }
        [TearDown] public void TearDown()
        {
            bus.Dispose();
            // 默认模式会捕获监听异常，测试断言不能因此被误判为通过。
            foreach (var error in errors) Assert.That(error.Exception, Is.Not.InstanceOf<AssertionException>());
        }

        [Test] public void PriorityAndRegistrationOrder_AreStable()
        {
            var calls = new List<int>();
            bus.Subscribe<NumberEvent>(_ => calls.Add(0));
            bus.Subscribe<NumberEvent>(_ => calls.Add(1), 10);
            bus.Subscribe<NumberEvent>(_ => calls.Add(2), 10);
            bus.Subscribe<NumberEvent>(_ => calls.Add(3), -1);
            bus.Publish(new NumberEvent(0));
            Assert.That(calls, Is.EqualTo(new[] { 1, 2, 0, 3 }));
        }

        [Test] public void ExactGenericTypeAndPayload_ArePreserved()
        {
            IEvent received = null;
            int concreteCalls = 0;
            var message = new TextEvent("text");
            bus.Subscribe<IEvent>(value => received = value);
            bus.Subscribe<TextEvent>(_ => concreteCalls++);
            bus.Publish<IEvent>(message);
            Assert.That(received, Is.SameAs(message));
            Assert.That(concreteCalls, Is.Zero);
            received = null;
            bus.Publish(message);
            Assert.That(concreteCalls, Is.EqualTo(1));
            Assert.That(received, Is.Null, "接口订阅不能收到具体类型广播");
            bus.Publish(new NumberEvent(12));
        }

        [Test] public void PublicGenericEntryPointsRequireEventMarker()
        {
            // 通过运行时构造泛型检查约束，避免向测试程序集写入无法编译的调用。
            foreach (string name in new[] { "Subscribe", "Publish", "TryQueueEvent" })
            {
                var method = typeof(EventBus).GetMethod(name);
                foreach (var type in new[] { typeof(int), typeof(string), typeof(object) })
                    Assert.Throws<ArgumentException>(() => method.MakeGenericMethod(type));
                Assert.DoesNotThrow(() => method.MakeGenericMethod(typeof(NumberEvent)));
                Assert.DoesNotThrow(() => method.MakeGenericMethod(typeof(TextEvent)));
            }
        }

        [Test] public void DuplicateHandlers_HaveIndependentIdempotentHandles()
        {
            int calls = 0;
            Action<NumberEvent> handler = _ => calls++;
            var first = bus.Subscribe(handler);
            var second = bus.Subscribe(handler);
            bus.Publish(new NumberEvent(1)); first.Dispose(); first.Dispose(); bus.Publish(new NumberEvent(2)); second.Dispose();
            Assert.That(calls, Is.EqualTo(3));
            Assert.That(bus.GetDiagnosticsSnapshot().Channels[0].ActiveSubscriptions, Is.Zero);
        }

        [Test] public void Instances_AreIsolated()
        {
            using (var other = new EventBus(_ => { }))
            {
                int calls = 0;
                bus.Subscribe<NumberEvent>(_ => calls++);
                other.Publish(new NumberEvent(1)); bus.Publish(new NumberEvent(2));
                Assert.That(calls, Is.EqualTo(1));
                bus.Dispose(); Assert.That(other.TryQueueEvent(new NumberEvent(3)), Is.True);
            }
        }

        [Test] public void AddRemoveAndPriorityChangesDuringDispatch_ApplySafely()
        {
            var calls = new List<int>();
            IDisposable later = null, added = null;
            bus.Subscribe<NumberEvent>(_ =>
            {
                calls.Add(1); later.Dispose();
                if (added == null) added = bus.Subscribe<NumberEvent>(x => calls.Add(3), 20);
            }, 10);
            later = bus.Subscribe<NumberEvent>(_ => calls.Add(2));
            bus.Publish(new NumberEvent(1)); Assert.That(calls, Is.EqualTo(new[] { 1 }));
            calls.Clear(); bus.Publish(new NumberEvent(2)); Assert.That(calls, Is.EqualTo(new[] { 3, 1 }));
        }

        [Test] public void PendingListenerCanBeCancelledAndSelfRemovalIsSafe()
        {
            int calls = 0;
            IDisposable self = null;
            self = bus.Subscribe<NumberEvent>(_ =>
            {
                calls++; self.Dispose();
                bus.Subscribe<NumberEvent>(x => Assert.Fail("已取消待加入监听被调用")).Dispose();
            });
            bus.Publish(new NumberEvent(1)); bus.Publish(new NumberEvent(2));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(bus.GetDiagnosticsSnapshot().Channels[0].ActiveSubscriptions, Is.Zero);
        }

        [Test] public void CompactionPreservesSurvivorsAndAllowsResubscription()
        {
            var handles = new List<IDisposable>(); var calls = new List<int>();
            for (int i = 0; i < 64; i++) { int id = i; handles.Add(bus.Subscribe<NumberEvent>(_ => calls.Add(id))); }
            for (int i = 0; i < 48; i++) handles[i].Dispose();
            bus.Publish(new NumberEvent(0));
            Assert.That(calls.Count, Is.EqualTo(16));
            for (int i = 0; i < 16; i++) Assert.That(calls[i], Is.EqualTo(i + 48));
            foreach (var handle in handles) handle.Dispose();
            calls.Clear(); bus.Subscribe<NumberEvent>(_ => calls.Add(99)); bus.Publish(new NumberEvent(0));
            Assert.That(calls, Is.EqualTo(new[] { 99 }));
        }

        [Test] public void CloseInsideListener_SkipsPendingCallbacksAndQueuedMessages()
        {
            bus.Subscribe<NumberEvent>(_ => { bus.Subscribe<NumberEvent>(x => Assert.Fail()); bus.Dispose(); }, 1);
            var handle = bus.Subscribe<NumberEvent>(_ => Assert.Fail());
            bus.TryQueueEvent(new NumberEvent(1)); bus.TryQueueEvent(new NumberEvent(2));
            Assert.That(bus.ProcessQueuedEvents(), Is.EqualTo(1)); handle.Dispose(); bus.Dispose();
            Assert.That(bus.TryQueueEvent(new NumberEvent(3)), Is.False);
            Assert.Throws<ObjectDisposedException>(() => bus.Publish(new NumberEvent(1)));
            Assert.Throws<ObjectDisposedException>(() => bus.Subscribe<NumberEvent>(_ => { }));
            Assert.Throws<ObjectDisposedException>(() => bus.ProcessQueuedEvents());
            Assert.That(bus.GetDiagnosticsSnapshot().QueueLength, Is.Zero);
        }

        [Test] public void DefaultErrorsContinueAndIncludeIdentity()
        {
            int calls = 0; var exception = new Exception("监听失败");
            bus.Subscribe<NumberEvent>(_ => throw exception, label: "fault");
            bus.Subscribe<NumberEvent>(_ => calls++);
            Assert.DoesNotThrow(() => bus.Publish(new NumberEvent(1)));
            Assert.That(calls, Is.EqualTo(1)); Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0].EventType, Is.EqualTo(typeof(NumberEvent)));
            Assert.That(errors[0].Label, Is.EqualTo("fault"));
            Assert.That(errors[0].SubscriptionId, Is.GreaterThan(0));
            Assert.That(errors[0].Exception, Is.SameAs(exception));
        }

        [TestCase(false)] [TestCase(true)]
        public void ErrorReporterFailureRetainsBothErrors(bool strict)
        {
            var original = new Exception("原始错误"); var reporting = new Exception("报告错误"); int calls = 0;
            using (var local = new EventBus(_ => throw reporting, new EventBusOptions { ThrowListenerExceptions = strict }))
            {
                local.Subscribe<NumberEvent>(_ => throw original); local.Subscribe<NumberEvent>(_ => calls++);
                var error = Assert.Throws<AggregateException>(() => local.Publish(new NumberEvent(1)));
                Assert.That(error.InnerExceptions, Is.EqualTo(new[] { original, reporting }));
                Assert.That(calls, Is.EqualTo(1));
            }
        }

        [Test] public void StrictDrainFinishesEntireBatchBeforeThrowing()
        {
            using (var local = new EventBus(errors.Add, new EventBusOptions { ThrowListenerExceptions = true }))
            {
                int calls = 0;
                local.Subscribe<NumberEvent>(_ => throw new Exception("故障")); local.Subscribe<NumberEvent>(_ => calls++);
                local.TryQueueEvent(new NumberEvent(1)); local.TryQueueEvent(new NumberEvent(2));
                var error = Assert.Throws<AggregateException>(() => local.ProcessQueuedEvents());
                Assert.That(calls, Is.EqualTo(2)); Assert.That(error.InnerExceptions.Count, Is.EqualTo(2));
                Assert.That(local.ProcessQueuedEvents(), Is.Zero);
            }
        }

        [Test] public void StrictPublishAggregatesAndRecoversForNextPublish()
        {
            using (var local = new EventBus(errors.Add, new EventBusOptions { ThrowListenerExceptions = true }))
            {
                int calls = 0;
                var first = new Exception("第一个错误");
                var second = new Exception("第二个错误");
                var a = local.Subscribe<NumberEvent>(_ => throw first);
                var b = local.Subscribe<NumberEvent>(_ => throw second);
                local.Subscribe<NumberEvent>(_ => calls++);
                var failure = Assert.Throws<AggregateException>(() => local.Publish(new NumberEvent(1)));
                Assert.That(failure.InnerExceptions, Is.EqualTo(new[] { first, second }));
                Assert.That(calls, Is.EqualTo(1));
                a.Dispose(); b.Dispose();
                Assert.DoesNotThrow(() => local.Publish(new NumberEvent(2)));
                Assert.That(calls, Is.EqualTo(2));
                Assert.That(local.GetDiagnosticsSnapshot().ErrorCount, Is.EqualTo(2));
            }
        }

        [Test] public void ReporterFailureInDrainStillFinishesBatch()
        {
            using (var local = new EventBus(_ => throw new Exception("报告失败")))
            {
                int calls = 0;
                local.Subscribe<NumberEvent>(_ => throw new Exception("监听失败"));
                local.Subscribe<NumberEvent>(_ => calls++);
                local.TryQueueEvent(new NumberEvent(1)); local.TryQueueEvent(new NumberEvent(2));
                var failure = Assert.Throws<AggregateException>(() => local.ProcessQueuedEvents());
                Assert.That(failure.InnerExceptions.Count, Is.EqualTo(4));
                Assert.That(calls, Is.EqualTo(2));
                Assert.That(local.GetDiagnosticsSnapshot().QueueLength, Is.Zero);
            }
        }

        [Test] public void DefaultDrainBudgetIs256AndReferencePayloadIsNotCopied()
        {
            var message = new TextEvent("不复制的引用事件");
            TextEvent received = null;
            bus.Subscribe<TextEvent>(value => received = value);
            bus.TryQueueEvent(message);
            for (int i = 0; i < 256; i++) bus.TryQueueEvent(new NumberEvent(i));
            Assert.That(bus.ProcessQueuedEvents(), Is.EqualTo(256));
            Assert.That(received, Is.SameAs(message));
            Assert.That(bus.GetDiagnosticsSnapshot().QueueLength, Is.EqualTo(1));
            Assert.That(bus.ProcessQueuedEvents(), Is.EqualTo(1));
        }

        [Test] public void ReentrantPublishAndDrainAreRejectedButPostIsAllowed()
        {
            bus.Subscribe<NumberEvent>(_ =>
            {
                Assert.Throws<InvalidOperationException>(() => bus.Publish(new TextEvent("nested")));
                Assert.Throws<InvalidOperationException>(() => bus.ProcessQueuedEvents());
                Assert.That(bus.TryQueueEvent(new TextEvent("later")), Is.True);
            });
            int calls = 0; bus.Subscribe<TextEvent>(_ => calls++);
            bus.Publish(new NumberEvent(1)); Assert.That(calls, Is.Zero);
            bus.ProcessQueuedEvents(); Assert.That(calls, Is.EqualTo(1));
        }

        [Test] public void ReporterCannotReenterDispatch()
        {
            EventBus local = null;
            local = new EventBus(_ =>
            {
                Assert.Throws<InvalidOperationException>(() => local.Publish(new NumberEvent(2)));
                Assert.Throws<InvalidOperationException>(() => local.ProcessQueuedEvents());
            });
            using (local) { local.Subscribe<NumberEvent>(_ => throw new Exception()); local.Publish(new NumberEvent(1)); }
        }

        [Test] public void QueueCapacityBudgetAndFifoAreExplicit()
        {
            var options = new EventBusOptions { QueueCapacity = 3 };
            using (var local = new EventBus(errors.Add, options))
            {
                options.QueueCapacity = 100;
                var received = new List<int>(); local.Subscribe<NumberEvent>(e => received.Add(e.Value));
                Assert.That(local.TryQueueEvent(new NumberEvent(1)) && local.TryQueueEvent(new NumberEvent(2)) && local.TryQueueEvent(new NumberEvent(3)), Is.True);
                Assert.That(local.TryQueueEvent(new NumberEvent(4)), Is.False);
                Assert.That(local.ProcessQueuedEvents(2), Is.EqualTo(2)); Assert.That(local.ProcessQueuedEvents(), Is.EqualTo(1));
                Assert.That(received, Is.EqualTo(new[] { 1, 2, 3 }));
                Assert.That(local.GetDiagnosticsSnapshot().RejectedPosts, Is.EqualTo(1));
            }
        }

        [Test] public void PostedDuringDrainWaitsForNextRoundAndUsesCurrentListeners()
        {
            int calls = 0;
            bus.TryQueueEvent(new NumberEvent(1));
            var handle = bus.Subscribe<NumberEvent>(_ => { calls++; bus.TryQueueEvent(new NumberEvent(2)); });
            Assert.That(bus.ProcessQueuedEvents(), Is.EqualTo(1)); Assert.That(calls, Is.EqualTo(1));
            handle.Dispose(); Assert.That(bus.ProcessQueuedEvents(), Is.EqualTo(1)); Assert.That(calls, Is.EqualTo(1));
        }

        [Test] public void BackgroundProducerPreservesItsFifoAndRunsOnOwnerThread()
        {
            int owner = Thread.CurrentThread.ManagedThreadId; var received = new List<int>();
            bus.Subscribe<NumberEvent>(x => { Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(owner)); received.Add(x.Value); });
            Task.Run(() => { for (int i = 0; i < 100; i++) Assert.That(bus.TryQueueEvent(new NumberEvent(i)), Is.True); }).GetAwaiter().GetResult();
            bus.ProcessQueuedEvents(); Assert.That(received.Count, Is.EqualTo(100));
            for (int i = 0; i < 100; i++) Assert.That(received[i], Is.EqualTo(i));
        }

        [Test] public void ConcurrentProducersDoNotLoseAcceptedMessages()
        {
            var received = new HashSet<int>(); bus.Subscribe<NumberEvent>(x => Assert.That(received.Add(x.Value), Is.True));
            var tasks = new Task[4];
            for (int p = 0; p < tasks.Length; p++)
            { int producer = p; tasks[p] = Task.Run(() => { for (int i = 0; i < 100; i++) Assert.That(bus.TryQueueEvent(new NumberEvent(producer * 100 + i)), Is.True); }); }
            Task.WaitAll(tasks); bus.ProcessQueuedEvents(1000); Assert.That(received.Count, Is.EqualTo(400));
        }

        [Test] public void PostingConcurrentWithCloseIsSafeAndOldProducerIsRejected()
        {
            using (var started = new ManualResetEventSlim())
            {
                var task = Task.Run(() => { started.Set(); for (int i = 0; i < 10000; i++) bus.TryQueueEvent(new NumberEvent(i)); });
                Assert.That(started.Wait(5000), Is.True); bus.Dispose();
                Assert.That(task.Wait(5000), Is.True);
                Assert.That(Task.Run(() => bus.TryQueueEvent(new NumberEvent(999))).Result, Is.False);
                Assert.That(bus.GetDiagnosticsSnapshot().QueueLength, Is.Zero);
            }
        }

        [Test] public void EveryOwnerOperationRejectsWrongThread()
        {
            var handle = bus.Subscribe<NumberEvent>(_ => { });
            Action[] operations = { () => bus.Publish(new NumberEvent(1)), () => bus.Subscribe<NumberEvent>(_ => { }),
                () => bus.ProcessQueuedEvents(), () => bus.GetDiagnosticsSnapshot(), () => bus.Dispose(), () => handle.Dispose() };
            foreach (var action in operations)
            {
                var error = Task.Run(() => { try { action(); return null; } catch (Exception e) { return e; } }).Result;
                Assert.That(error, Is.TypeOf<InvalidOperationException>());
            }
            bus.Publish(new NumberEvent(1)); handle.Dispose();
        }

        [Test] public void InvalidArgumentsAndUnsupportedHandlersAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new EventBus(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EventBus(_ => { }, new EventBusOptions { QueueCapacity = 0 }));
            Assert.Throws<ArgumentNullException>(() => bus.Subscribe<NumberEvent>(null));
            Assert.Throws<ArgumentNullException>(() => bus.Publish<TextEvent>(null));
            Assert.Throws<ArgumentNullException>(() => bus.TryQueueEvent<TextEvent>(null));
            Assert.Throws<ArgumentNullException>(() => bus.Publish<IEvent>(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => bus.ProcessQueuedEvents(0));
            Action<NumberEvent> handler = _ => { }; handler += _ => { };
            Assert.Throws<ArgumentException>(() => bus.Subscribe(handler));
            Assert.Throws<ArgumentException>(() => bus.Subscribe<NumberEvent>(AsyncHandler));
            handler += AsyncHandler;
            Assert.Throws<ArgumentException>(() => bus.Subscribe(handler));
        }

        private static async void AsyncHandler(NumberEvent value) { await Task.Yield(); }

        [Test] public void DiagnosticsAreIndependentReadOnlySnapshots()
        {
            using (var local = new EventBus(errors.Add, new EventBusOptions { MeasureListenerTime = true }))
            {
                var handle = local.Subscribe<NumberEvent>(_ => Thread.SpinWait(100), 5, "sample"); local.Publish(new NumberEvent(1));
                var before = local.GetDiagnosticsSnapshot(); handle.Dispose();
                Assert.That(before.Channels[0].ActiveSubscriptions, Is.EqualTo(1));
                Assert.That(before.Channels[0].Subscriptions[0].InvocationCount, Is.EqualTo(1));
                Assert.That(before.Channels[0].Subscriptions[0].ElapsedStopwatchTicks, Is.GreaterThan(0));
                Assert.That(local.GetDiagnosticsSnapshot().Channels[0].ActiveSubscriptions, Is.Zero);
                Assert.Throws<NotSupportedException>(() => ((IList<SubscriptionDiagnostics>)before.Channels[0].Subscriptions).Clear());
            }
        }

        [Test] public void SteadyPublishHasZeroAllocationAndWorkloadsAreMeasured()
        {
            const int n = 10000;
            // 先验证记录器能看到已知分配，不能把运行时不支持的恒零计数误认为零分配。
            var control = Measure(() => GC.KeepAlive(new byte[1024]));
            Assert.That(control.Allocations, Is.GreaterThan(0));
            using (var local = new EventBus(_ => { }, new EventBusOptions { QueueCapacity = n }))
            {
                int calls = 0; Action<NumberEvent> handler = _ => calls++;
                local.Subscribe(handler);
                for (int i = 0; i < 100; i++) local.Publish(new NumberEvent(i));
                var sync = Measure(() => { for (int i = 0; i < n; i++) local.Publish(new NumberEvent(i)); });
                Assert.That(sync.Allocations, Is.Zero, "预热后同步发布不应产生总线自身分配");
                var churn = Measure(() => { for (int i = 0; i < n; i++) local.Subscribe(handler).Dispose(); });
                var handles = new IDisposable[n]; for (int i = 0; i < n; i++) handles[i] = local.Subscribe(handler);
                var dispose = Measure(() => { foreach (var h in handles) h.Dispose(); });
                var queue = Measure(() =>
                {
                    for (int i = 0; i < n; i++) local.TryQueueEvent(new NumberEvent(i));
                    local.ProcessQueuedEvents(n);
                });
                Assert.That(queue.Allocations, Is.GreaterThanOrEqualTo(n));
                string report = $"count={n}\nstopwatchFrequency={Stopwatch.Frequency}\nrecorder=Unity GC.Alloc current thread\ncontrol={control}\nsync={sync}\nchurn={churn}\ndispose={dispose}\nqueue={queue}\n";
                string directory = Path.Combine(UnityEngine.Application.dataPath, "../TestResults");
                Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "event-benchmark.txt"), report);
                TestContext.WriteLine(report);
                Assert.That(calls, Is.EqualTo(20100));
            }
        }

        private static Measurement Measure(Action action)
        {
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1,
                ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                long start = Stopwatch.GetTimestamp();
                action();
                long ticks = Stopwatch.GetTimestamp() - start;
                recorder.Stop();
                Assert.That(recorder.Valid, Is.True, "当前平台必须支持 GC.Alloc 记录器");
                var sample = recorder.Count == 0 ? default : recorder.GetSample(0);
                // GC.Alloc 的 Count 是分配次数，Value 是标记耗时，不是字节数。
                return new Measurement(sample.Count, ticks);
            }
        }

        // 测试使用不可变事件，避免基础值类型绕过实际事件约定。
        private readonly struct NumberEvent : IEvent
        {
            public int Value { get; }
            public NumberEvent(int value) { Value = value; }
        }

        private sealed class TextEvent : IEvent
        {
            public string Value { get; }
            public TextEvent(string value) { Value = value; }
        }

        private readonly struct Measurement
        {
            public long Allocations { get; }
            private readonly long ticks;
            public Measurement(long allocations, long ticks)
            { Allocations = allocations; this.ticks = ticks; }
            public override string ToString() => $"allocations:{Allocations},ticks:{ticks}";
        }
    }
}
