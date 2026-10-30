using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GameSysLab.Modules.Timer.Unity;
using GameSysLab.Demos.Timer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GameSysLab.Modules.Timer.PlayModeTests
{
    public sealed class TimerHostTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private float previousScale;
        private const string ScenePath = "Assets/_Lab/Demos/002_Timer/Timer.unity";

        [SetUp] public void SetUp() { previousScale = Time.timeScale; Time.timeScale = 1; }

        [UnityTearDown] public IEnumerator TearDown()
        {
            foreach (var go in objects) if (go != null) Object.Destroy(go);
            objects.Clear();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            Time.timeScale = previousScale;
            yield return null;
        }

        private TimerHost Create(TimerTimeMode mode = TimerTimeMode.GameTime)
        {
            var go = new GameObject("TimerHost 测试"); objects.Add(go);
            var host = go.AddComponent<TimerHost>(); host.TimeMode = mode;
            return host;
        }

        [Test] public void ExplicitInitializationAndPermanentShutdown()
        {
            var host = Create();
            Assert.That(host.IsInitialized, Is.False);
            Assert.Throws<InvalidOperationException>(() => GC.KeepAlive(host.Scheduler));
            Assert.Throws<ArgumentOutOfRangeException>(() => host.TimeMode = (TimerTimeMode)99);
            var timers = host.Initialize(); Assert.That(host.Initialize(), Is.SameAs(timers));
            Assert.Throws<InvalidOperationException>(() => host.TimeMode = TimerTimeMode.UnscaledTime);
            var task = timers.ScheduleOnce(1, () => Assert.Fail());
            host.Shutdown(); host.Shutdown();
            Assert.That(task.State, Is.EqualTo(TimerState.Cancelled));
            Assert.Throws<ObjectDisposedException>(() => host.Initialize());
            Assert.Throws<ObjectDisposedException>(() => GC.KeepAlive(host.Scheduler));
        }

        [UnityTest] public IEnumerator GamePauseOnlyFreezesScaledHost()
        {
            var scaled = Create(); var unscaled = Create(TimerTimeMode.UnscaledTime);
            var frozen = scaled.Initialize().ScheduleOnce(.05, () => Assert.Fail());
            int calls = 0; unscaled.Initialize().ScheduleOnce(.05, () => calls++);
            Time.timeScale = 0;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(frozen.RemainingSeconds, Is.EqualTo(.05));
            Assert.That(calls, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator DisableFreezesAndEnableDoesNotCatchUp()
        {
            var host = Create(TimerTimeMode.UnscaledTime);
            var task = host.Initialize().ScheduleOnce(10, () => { });
            yield return null; host.enabled = false;
            double remaining = task.RemainingSeconds;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(task.RemainingSeconds, Is.EqualTo(remaining));
            host.enabled = true; yield return null;
            Assert.That(task.RemainingSeconds, Is.GreaterThan(remaining - .15));
            Assert.That(task.State, Is.EqualTo(TimerState.Scheduled));
        }

        [UnityTest] public IEnumerator DestroyCancelsOldTasksAndNewHostIsIndependent()
        {
            var host = Create(); var old = host.Initialize();
            var task = old.ScheduleOnce(10, () => Assert.Fail());
            Object.Destroy(host.gameObject); yield return null;
            Assert.That(task.State, Is.EqualTo(TimerState.Cancelled));
            Assert.Throws<ObjectDisposedException>(() => old.AdvanceTime(1));
            Assert.That(Create().Initialize(), Is.Not.SameAs(old));
        }

        [UnityTest] public IEnumerator HostReportsAggregateAndKeepsUpdating()
        {
            var host = Create(); var scheduler = host.Initialize(); int calls = 0;
            scheduler.ScheduleOnce(.001, () => throw new Exception("预期计时故障"));
            scheduler.ScheduleOnce(.001, () => calls++);
            LogAssert.Expect(LogType.Exception, new Regex("Exception: 预期计时故障"));
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(calls, Is.EqualTo(1));
            scheduler.ScheduleOnce(.001, () => calls++);
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(calls, Is.EqualTo(2));
        }

        [UnityTest] public IEnumerator RealSceneUnloadAndReentryDoNotKeepOldTasks()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            var scene = SceneManager.GetSceneByPath(ScenePath);
            TimerDemo demo = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out TimerDemo found)) demo = found;
            Assert.That(demo, Is.Not.Null);
            var old = demo.Countdown; old.Pause();
            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.That(old.State, Is.EqualTo(TimerState.Cancelled));
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByPath(ScenePath);
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out TimerDemo found)) demo = found;
            Assert.That(demo.Countdown, Is.Not.SameAs(old));
            Assert.That(demo.Countdown.State, Is.EqualTo(TimerState.Scheduled));
            Assert.Throws<InvalidOperationException>(old.Reset);
        }
    }
}
