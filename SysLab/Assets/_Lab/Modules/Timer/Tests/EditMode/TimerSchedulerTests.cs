using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using GameSysLab.Core.Lifecycle;
using NUnit.Framework;
using Unity.Profiling;

namespace GameSysLab.Modules.Timer.Tests
{
    public sealed class TimerSchedulerTests
    {
        private TimerScheduler timers;
        [SetUp] public void SetUp() { timers = new TimerScheduler(); }
        [TearDown] public void TearDown() { timers.Dispose(); }

        [Test] public void OnceCompletesBeforeCallbackAndNeverRepeats()
        {
            int calls = 0;
            TimerHandle handle = null;
            handle = timers.ScheduleOnce(2, () =>
            {
                Assert.That(handle.State, Is.EqualTo(TimerState.Completed));
                Assert.That(handle.RemainingSeconds, Is.Zero);
                calls++;
            });
            timers.AdvanceTime(0); timers.AdvanceTime(1.5);
            Assert.That(calls, Is.Zero); Assert.That(handle.RemainingSeconds, Is.EqualTo(.5));
            timers.AdvanceTime(.5); timers.AdvanceTime(100);
            Assert.That(calls, Is.EqualTo(1));
            handle.Cancel(); Assert.That(handle.State, Is.EqualTo(TimerState.Completed));
        }

        [Test] public void RepeatingSkipsMissedPeriodsAndWaitsFullInterval()
        {
            int calls = 0;
            var handle = timers.ScheduleRepeating(2, () => calls++);
            timers.AdvanceTime(1); Assert.That(calls, Is.Zero);
            timers.AdvanceTime(100); Assert.That(calls, Is.EqualTo(1));
            Assert.That(handle.RemainingSeconds, Is.EqualTo(2));
            timers.AdvanceTime(1); Assert.That(calls, Is.EqualTo(1));
            timers.AdvanceTime(1); Assert.That(calls, Is.EqualTo(2));
        }

        [Test] public void PauseResumeAndResetPreserveExpectedRemainingTime()
        {
            int calls = 0; var handle = timers.ScheduleOnce(4, () => calls++);
            timers.AdvanceTime(1); handle.Pause(); handle.Pause(); timers.AdvanceTime(20);
            Assert.That(handle.RemainingSeconds, Is.EqualTo(3));
            handle.Reset(); Assert.That(handle.State, Is.EqualTo(TimerState.Paused));
            Assert.That(handle.RemainingSeconds, Is.EqualTo(4));
            handle.Resume(); handle.Resume(); timers.AdvanceTime(3);
            handle.Reset(); Assert.That(handle.RemainingSeconds, Is.EqualTo(4));
            timers.AdvanceTime(4); Assert.That(calls, Is.EqualTo(1));
        }

        [TestCase(false)] [TestCase(true)]
        public void TerminalHandlesNeverRestartAndReleaseReferences(bool complete)
        {
            var handle = timers.ScheduleOnce(1, () => GC.KeepAlive(this));
            if (complete) timers.AdvanceTime(1); else handle.Cancel();
            handle.Cancel(); handle.Dispose();
            Assert.That(handle.State, Is.EqualTo(complete ? TimerState.Completed : TimerState.Cancelled));
            Assert.That(handle.RemainingSeconds, Is.Zero);
            Assert.Throws<InvalidOperationException>(handle.Pause);
            Assert.Throws<InvalidOperationException>(handle.Resume);
            Assert.Throws<InvalidOperationException>(handle.Reset);
            foreach (var name in new[] { "owner", "callback" })
                Assert.That(typeof(TimerHandle).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(handle), Is.Null);
        }

        [Test] public void DueTasksUseCreationOrderAndAllTimesAdvanceBeforeCallbacks()
        {
            var calls = new List<int>(); TimerHandle later = null;
            timers.ScheduleOnce(4, () => { Assert.That(later.RemainingSeconds, Is.Zero); calls.Add(1); });
            later = timers.ScheduleOnce(1, () => calls.Add(2));
            timers.AdvanceTime(5); Assert.That(calls, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test] public void TasksCreatedInCallbackBeginOnNextAdvance()
        {
            TimerHandle added = null; int calls = 0;
            timers.ScheduleOnce(1, () => added = timers.ScheduleOnce(2, () => calls++));
            timers.AdvanceTime(50);
            Assert.That(added.RemainingSeconds, Is.EqualTo(2)); Assert.That(calls, Is.Zero);
            timers.AdvanceTime(2); Assert.That(calls, Is.EqualTo(1));
        }

        [TestCase("cancel")] [TestCase("pause")] [TestCase("reset")] [TestCase("resume")]
        public void ModifyingAnotherDueTaskSkipsItsCurrentCallback(string action)
        {
            TimerHandle other = null; int calls = 0;
            timers.ScheduleOnce(1, () =>
            {
                if (action == "cancel") other.Cancel();
                else if (action == "reset") other.Reset();
                else { other.Pause(); if (action == "resume") other.Resume(); }
            });
            other = timers.ScheduleOnce(1, () => calls++);
            timers.AdvanceTime(1); Assert.That(calls, Is.Zero);
            timers.AdvanceTime(0); Assert.That(calls, Is.Zero);
            if (action == "pause") other.Resume();
            timers.AdvanceTime(1); Assert.That(calls, Is.EqualTo(action == "cancel" ? 0 : 1));
        }

        [TestCase("pause")] [TestCase("reset")] [TestCase("cancel")]
        public void RepeatingCallbackCanControlItself(string action)
        {
            TimerHandle handle = null; int calls = 0;
            handle = timers.ScheduleRepeating(2, () =>
            {
                calls++;
                Assert.That(handle.RemainingSeconds, Is.EqualTo(2));
                if (action == "pause") handle.Pause();
                else if (action == "reset") handle.Reset();
                else handle.Cancel();
            });
            timers.AdvanceTime(20); timers.AdvanceTime(2);
            Assert.That(calls, Is.EqualTo(action == "reset" ? 2 : 1));
        }

        [Test] public void ClosingInCallbackCancelsOthersAndNewTasks()
        {
            TimerHandle added = null;
            timers.ScheduleOnce(1, () => { added = timers.ScheduleOnce(1, () => Assert.Fail()); timers.Dispose(); });
            var other = timers.ScheduleOnce(1, () => Assert.Fail());
            timers.AdvanceTime(1);
            Assert.That(other.State, Is.EqualTo(TimerState.Cancelled));
            Assert.That(added.State, Is.EqualTo(TimerState.Cancelled));
            Assert.Throws<ObjectDisposedException>(() => timers.AdvanceTime(0));
            Assert.Throws<ObjectDisposedException>(() => timers.ScheduleOnce(1, () => { }));
            Assert.Throws<ObjectDisposedException>(() => timers.ScheduleRepeating(1, () => { }));
        }

        [Test] public void CallbackErrorsContinueAndRepeatingErrorKeepsNextPeriod()
        {
            var first = new Exception("一次性失败"); var second = new Exception("循环失败"); int calls = 0;
            var once = timers.ScheduleOnce(1, () => throw first);
            var repeat = timers.ScheduleRepeating(1, () => throw second);
            timers.ScheduleOnce(1, () => calls++);
            var failure = Assert.Throws<AggregateException>(() => timers.AdvanceTime(1));
            Assert.That(failure.InnerExceptions, Is.EqualTo(new[] { first, second }));
            Assert.That(calls, Is.EqualTo(1)); Assert.That(once.State, Is.EqualTo(TimerState.Completed));
            Assert.That(repeat.State, Is.EqualTo(TimerState.Scheduled));
            Assert.That(repeat.RemainingSeconds, Is.EqualTo(1));
            Assert.That(Assert.Throws<AggregateException>(() => timers.AdvanceTime(1)).InnerExceptions.Count, Is.EqualTo(1));
        }

        [Test] public void ReentrantAdvanceIsRejectedEvenWithZeroDelta()
        {
            timers.ScheduleOnce(1, () =>
            {
                Assert.Throws<InvalidOperationException>(() => timers.AdvanceTime(0));
                Assert.Throws<InvalidOperationException>(() => timers.AdvanceTime(1));
            });
            timers.AdvanceTime(1);
        }

        [Test] public void LifetimeAndSchedulerCloseCancelTasksWithoutCallingThem()
        {
            var lifetime = new Lifetime();
            var owned = lifetime.Add(timers.ScheduleOnce(1, () => Assert.Fail()));
            var other = timers.ScheduleRepeating(1, () => Assert.Fail()); other.Pause();
            lifetime.Dispose(); Assert.That(owned.State, Is.EqualTo(TimerState.Cancelled));
            timers.Dispose(); Assert.That(other.State, Is.EqualTo(TimerState.Cancelled));
        }

        [Test] public void CancelOutsideAdvanceRemovesImmediatelyAndOrderSurvivesCompaction()
        {
            var calls = new List<int>(); var handles = new List<TimerHandle>();
            for (int i = 0; i < 40; i++) { int id = i; handles.Add(timers.ScheduleOnce(1, () => calls.Add(id))); }
            for (int i = 0; i < 40; i += 2) handles[i].Cancel();
            var list = (System.Collections.IList)typeof(TimerScheduler).GetField("tasks", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(timers);
            Assert.That(list.Count, Is.EqualTo(20));
            timers.AdvanceTime(1);
            for (int i = 0; i < 20; i++) Assert.That(calls[i], Is.EqualTo(i * 2 + 1));
            Assert.That(list.Count, Is.Zero);
            int after = 0; timers.ScheduleOnce(1, () => after++); timers.AdvanceTime(1);
            Assert.That(after, Is.EqualTo(1));
        }

        [Test] public void InvalidNumbersCallbacksAndAsyncVoidAreRejected()
        {
            foreach (double value in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => timers.ScheduleOnce(value, () => { }));
                Assert.Throws<ArgumentOutOfRangeException>(() => timers.ScheduleRepeating(value, () => { }));
                if (value != 0) Assert.Throws<ArgumentOutOfRangeException>(() => timers.AdvanceTime(value));
            }
            Assert.Throws<ArgumentNullException>(() => timers.ScheduleOnce(1, null));
            Assert.Throws<ArgumentNullException>(() => timers.ScheduleRepeating(1, null));
            Action multi = () => { }; multi += () => { };
            Assert.Throws<ArgumentException>(() => timers.ScheduleOnce(1, multi));
            Assert.Throws<ArgumentException>(() => timers.ScheduleOnce(1, AsyncCallback));
            multi += AsyncCallback;
            Assert.Throws<ArgumentException>(() => timers.ScheduleRepeating(1, multi));
        }

        private static async void AsyncCallback() { await Task.Yield(); }

        [Test] public void AllOperationsIncludingTerminalReadsCheckThread()
        {
            var handle = timers.ScheduleOnce(1, () => { });
            Action[] operations = {
                () => timers.ScheduleOnce(1, () => { }), () => timers.ScheduleRepeating(1, () => { }),
                () => timers.AdvanceTime(0), timers.Dispose, handle.Pause, handle.Resume, handle.Reset,
                handle.Cancel, handle.Dispose, () => GC.KeepAlive(handle.State), () => GC.KeepAlive(handle.RemainingSeconds)
            };
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var action in operations)
                    Assert.That(Task.Run(() => { try { action(); return null; } catch (Exception error) { return error; } }).Result,
                        Is.TypeOf<InvalidOperationException>());
                timers.Dispose();
            }
        }

        [Test] public void IndependentSchedulersAndVeryLargeFiniteTimeWork()
        {
            using (var other = new TimerScheduler())
            {
                int calls = 0; var handle = timers.ScheduleOnce(double.MaxValue, () => calls++);
                other.AdvanceTime(double.MaxValue); Assert.That(calls, Is.Zero);
                timers.AdvanceTime(double.MaxValue); Assert.That(calls, Is.EqualTo(1));
            }
        }

        [Test] public void SteadyAdvanceHasZeroAllocationAndWorkloadsAreMeasured()
        {
            const int count = 10000;
            Action callback = () => { };
            var control = Measure(() => GC.KeepAlive(new byte[1024]));
            Assert.That(control.Allocations, Is.GreaterThan(0));
            var repeat = timers.ScheduleRepeating(1, callback);
            for (int i = 0; i < 100; i++) timers.AdvanceTime(1);
            var steady = Measure(() => { for (int i = 0; i < count; i++) timers.AdvanceTime(1); });
            Assert.That(steady.Allocations, Is.Zero);
            var churn = Measure(() => { for (int i = 0; i < count; i++) timers.ScheduleOnce(1, callback).Cancel(); });
            repeat.Cancel();
            var handles = new TimerHandle[count];
            for (int i = 0; i < count; i++) handles[i] = timers.ScheduleOnce(1, callback);
            var cancel = Measure(() => { foreach (var handle in handles) handle.Cancel(); });
            for (int i = 0; i < count; i++) timers.ScheduleOnce(1, callback);
            var due = Measure(() => timers.AdvanceTime(1));
            string report = $"count={count}\nfrequency={Stopwatch.Frequency}\ncontrol={control}\nsteady={steady}\ncreateCancel={churn}\ncancelBatch={cancel}\ndueBatch={due}\n";
            string directory = Path.Combine(UnityEngine.Application.dataPath, "../TestResults");
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "timer-benchmark.txt"), report);
            TestContext.WriteLine(report);
        }

        private static Measurement Measure(Action action)
        {
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1,
                ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                long start = Stopwatch.GetTimestamp(); action(); long ticks = Stopwatch.GetTimestamp() - start;
                recorder.Stop(); Assert.That(recorder.Valid, Is.True);
                return new Measurement(recorder.Count == 0 ? 0 : recorder.GetSample(0).Count, ticks);
            }
        }

        private readonly struct Measurement
        {
            public readonly long Allocations;
            private readonly long ticks;
            public Measurement(long allocations, long ticks) { Allocations = allocations; this.ticks = ticks; }
            public override string ToString() => $"allocations:{Allocations},ticks:{ticks}";
        }
    }
}
