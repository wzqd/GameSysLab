using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameSysLab.Core.Events;
using GameSysLab.Core.Events.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GameSysLab.Demos.CoreLifecycle.Tests
{
    public sealed class SceneCompositionTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<GameRoot> apps = new List<GameRoot>();
        private readonly List<UnityAction<Scene, LoadSceneMode>> hooks = new List<UnityAction<Scene, LoadSceneMode>>();
        private readonly HashSet<Scene> originalScenes = new HashSet<Scene>();
        private EventBus global;

        [SetUp] public void Prepare()
        {
            originalScenes.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++) originalScenes.Add(SceneManager.GetSceneAt(i));
            global = new EventBus(error => Assert.Fail(error.Exception.ToString()), new EventBusOptions { ThrowListenerExceptions = true });
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var hook in hooks) SceneManager.sceneLoaded -= hook;
            hooks.Clear();
            foreach (var app in apps) if (app != null) app.Shutdown();
            apps.Clear();
            foreach (var go in objects) if (go != null) Object.Destroy(go);
            objects.Clear();
            yield return null;
            var extra = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!originalScenes.Contains(scene)) extra.Add(scene);
            }
            foreach (var scene in extra)
                if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            global.Dispose();
            yield return null;
        }

        [UnityTest] public IEnumerator HostExplicitInitializationAndShutdownAreIdempotent()
        {
            var host = NewObject("Host").AddComponent<EventBusHost>();
            Assert.Throws<InvalidOperationException>(() => { var unused = host.Bus; });
            yield return null;
            var bus = host.Initialize();
            Assert.That(host.Initialize(), Is.SameAs(bus));
            bus.TryQueueEvent(new DemoMessage(1));
            host.Shutdown(); host.Shutdown();
            Assert.That(bus.GetDiagnosticsSnapshot().QueueLength, Is.Zero);
            Assert.That(bus.TryQueueEvent(new DemoMessage(2)), Is.False);
            Assert.Throws<ObjectDisposedException>(() => host.Initialize());
            Assert.Throws<ObjectDisposedException>(() => { var unused = host.Bus; });
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest] public IEnumerator ActiveAndInactiveComponentsCanBeConfiguredWithoutAwakeOrdering()
        {
            foreach (bool active in new[] { true, false })
            {
                var go = NewObject("订阅者");
                go.SetActive(active);
                var view = go.AddComponent<DemoSubscriber>();
                global.Publish(new DemoMessage(1));
                Assert.That(view.ReceivedCount, Is.Zero);
                view.Configure(global, _ => { });
                go.SetActive(true);
                global.Publish(new DemoMessage(2));
                Assert.That(view.ReceivedCount, Is.EqualTo(1));
                view.enabled = false; view.enabled = true;
                global.Publish(new DemoMessage(3));
                Assert.That(view.ReceivedCount, Is.EqualTo(2));
                view.Shutdown(); view.Shutdown();
                view.enabled = false; view.enabled = true;
                Assert.That(Count(global), Is.Zero);
            }
            yield return null;
        }

        [Test] public void InitializationAndCleanupFailuresPreserveBothErrorsAndReleaseSubscriptions()
        {
            var root = NewSceneRoot();
            root.FailInitialization = true;
            root.FailCleanup = true;
            var error = Assert.Throws<AggregateException>(() => root.Initialize(global));
            Assert.That(error.Flatten().InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(root.State, Is.EqualTo(RootState.Closed));
            Assert.That(root.Host.IsClosed, Is.True);
            Assert.That(Count(global), Is.Zero);
            Assert.DoesNotThrow(root.Shutdown);
            Assert.Throws<InvalidOperationException>(() => root.Initialize(global));
        }

        [Test] public void InvalidSceneOwnershipCannotDisposeBorrowedHost()
        {
            var root = NewSceneRoot();
            var otherScene = SceneManager.CreateScene("测试借用资源");
            var borrowed = NewObject("其他场景拥有的 Host").AddComponent<EventBusHost>();
            SceneManager.MoveGameObjectToScene(borrowed.gameObject, otherScene);
            var borrowedBus = borrowed.Initialize();
            root.Configure(borrowed, root.Subscriber);
            Assert.Throws<InvalidOperationException>(() => root.Initialize(global));
            Assert.That(borrowedBus.TryQueueEvent(new GlobalNotice(1)), Is.True);
            Assert.That(borrowed.IsInitialized, Is.True);
            Assert.That(root.State, Is.EqualTo(RootState.Closed));
        }

        [UnityTest] public IEnumerator RepeatedSceneSwitchesPreserveGlobalIdentityAndRemoveOldListeners()
        {
            var app = NewApp();
            var globalBus = app.GlobalEvents;
            Assert.That(app.TrySwitch(GameRoot.SceneAPath), Is.True);
            Assert.That(app.TrySwitch(GameRoot.SceneBPath), Is.False);
            yield return WaitTransition(app);
            var first = app.Current.Events;
            var firstModule = app.Current.Module;
            app.GlobalEvents.Publish(new GlobalNotice(1));
            Assert.That(firstModule.GlobalNotifications, Is.EqualTo(1));
            app.Current.Host.enabled = false;
            first.TryQueueEvent(new DemoMessage(900));
            foreach (var path in new[] { GameRoot.SceneBPath, GameRoot.SceneAPath, GameRoot.SceneBPath, GameRoot.SceneAPath })
            {
                Assert.That(app.TrySwitch(path), Is.True);
                yield return WaitTransition(app);
                Assert.That(app.LastError, Is.Null);
                Assert.That(app.GlobalEvents, Is.SameAs(globalBus));
                Assert.That(app.Current.Events, Is.Not.SameAs(first));
                Assert.That(Count(globalBus), Is.EqualTo(1));
                Assert.That(app.Current.Module.LocalNotifications, Is.Zero);
            }
            app.GlobalEvents.Publish(new GlobalNotice(2));
            Assert.That(firstModule.GlobalNotifications, Is.EqualTo(1));
            Assert.That(first.GetDiagnosticsSnapshot().IsDisposed, Is.True);
            Assert.That(first.GetDiagnosticsSnapshot().QueueLength, Is.Zero);
        }

        [UnityTest] public IEnumerator BackgroundCancellationAndLateOldResultsDoNotReachNewScene()
        {
            var app = NewApp();
            app.TrySwitch(GameRoot.SceneAPath);
            yield return WaitTransition(app);
            var oldBus = app.Current.Events;
            var producer = app.Current.StartProducer();
            app.TrySwitch(GameRoot.SceneBPath);
            yield return WaitTransition(app);
            yield return WaitTask(producer.Completion);
            Assert.That(producer.WasCancelled, Is.True);
            Assert.That(producer.Failure, Is.Null);
            var late = Task.Run(() => oldBus.TryQueueEvent(new DemoMessage(999)));
            yield return WaitTask(late);
            Assert.That(late.Result, Is.False);
            Assert.That(app.Current.Module.LocalNotifications, Is.Zero);
            yield return null;
            Assert.That(app.PendingProducerCount, Is.Zero);
        }

        [UnityTest] public IEnumerator ProducerFailureIsObservedWithoutFaultingItsTask()
        {
            var producer = new DemoProducer(global, delayMilliseconds: 0, failAt: 0);
            yield return WaitTask(producer.Completion);
            Assert.That(producer.Completion.IsFaulted, Is.False);
            Assert.That(producer.Failure, Is.TypeOf<InvalidOperationException>());
            producer.Dispose(); producer.Dispose();
        }

        [UnityTest] public IEnumerator BadLoadAndMissingRootKeepOldSceneRunning()
        {
            var app = NewApp();
            app.TrySwitch(GameRoot.SceneAPath);
            yield return WaitTransition(app);
            var previous = app.Current;
            Assert.That(app.TrySwitch("Assets/NoSuchScene.unity"), Is.False);
            Assert.That(app.TrySwitch("ContentA"), Is.False);
            Assert.That(app.Current, Is.SameAs(previous));
            Assert.That(app.TrySwitch("Assets/_Lab/Demos/001_CoreLifecycle/CoreLifecycle.unity"), Is.True);
            yield return WaitTransition(app);
            Assert.That(app.LastError, Is.Not.Null);
            Assert.That(app.Current, Is.SameAs(previous));
            Assert.That(previous.State, Is.EqualTo(RootState.Running));
            Assert.That(Count(app.GlobalEvents), Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator DuplicateRootsAndMissingReferencesAreRejectedBeforeOldSceneCloses()
        {
            var app = NewApp();
            app.TrySwitch(GameRoot.SceneAPath);
            yield return WaitTransition(app);
            var old = app.Current;
            bool duplicate = true;
            UnityAction<Scene, LoadSceneMode> hook = (scene, _) =>
            {
                if (scene.path != GameRoot.SceneBPath) return;
                if (duplicate)
                {
                    var go = new GameObject("重复入口");
                    SceneManager.MoveGameObjectToScene(go, scene);
                    go.AddComponent<SceneRoot>();
                }
                else GameRoot.FindSceneRoot(scene).Configure(null, null);
            };
            hooks.Add(hook); SceneManager.sceneLoaded += hook;
            app.TrySwitch(GameRoot.SceneBPath);
            yield return WaitTransition(app);
            Assert.That(app.LastError.Message, Does.Contain("多个 SceneRoot"));
            Assert.That(app.Current, Is.SameAs(old));
            duplicate = false;
            app.TrySwitch(GameRoot.SceneBPath);
            yield return WaitTransition(app);
            Assert.That(app.LastError.Message, Does.Contain("缺少 Host"));
            Assert.That(app.Current, Is.SameAs(old));
            Assert.That(old.State, Is.EqualTo(RootState.Running));
        }

        [UnityTest] public IEnumerator FailedNewInitializationLeavesBootstrapReadyForRetry()
        {
            var app = NewApp();
            app.TrySwitch(GameRoot.SceneAPath);
            yield return WaitTransition(app);
            var old = app.Current.Events;
            app.FailNextInitialization = true;
            app.FailNextCleanup = true;
            app.TrySwitch(GameRoot.SceneBPath);
            yield return WaitTransition(app);
            Assert.That(app.LastError, Is.TypeOf<AggregateException>());
            Assert.That(app.Current == null, Is.True);
            Assert.That(Count(app.GlobalEvents), Is.Zero);
            Assert.That(old.TryQueueEvent(new DemoMessage(1)), Is.False);
            Assert.That(app.TrySwitch(GameRoot.SceneAPath), Is.True);
            yield return WaitTransition(app);
            Assert.That(app.Current.State, Is.EqualTo(RootState.Running));
        }

        [UnityTest] public IEnumerator UnloadFailureKeepsOwnershipAndCleanupCanRetry()
        {
            var app = NewApp();
            app.TrySwitch(GameRoot.SceneAPath);
            yield return WaitTransition(app);
            var scene = app.Current.gameObject.scene;
            app.FailNextUnload = true;
            app.TrySwitch(GameRoot.SceneBPath);
            yield return WaitTransition(app);
            Assert.That(scene.isLoaded, Is.True);
            Assert.That(app.Current.State, Is.EqualTo(RootState.Closed));
            Assert.That(Count(app.GlobalEvents), Is.Zero);
            Assert.That(app.TrySwitch(GameRoot.SceneBPath), Is.False);
            Assert.That(app.TryCleanup(), Is.True);
            yield return WaitTransition(app);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.False);
            Assert.That(app.TrySwitch(GameRoot.SceneBPath), Is.True);
            yield return WaitTransition(app);
            Assert.That(app.Current.State, Is.EqualTo(RootState.Running));
        }

        [UnityTest] public IEnumerator ExternalUnloadCleansGlobalSubscriptionsAndCurrentReference()
        {
            var app = NewApp();
            app.TrySwitch(GameRoot.SceneAPath);
            yield return WaitTransition(app);
            var old = app.Current.Events;
            yield return SceneManager.UnloadSceneAsync(app.Current.gameObject.scene);
            Assert.That(app.Current == null, Is.True);
            Assert.That(Count(app.GlobalEvents), Is.Zero);
            Assert.That(old.TryQueueEvent(new DemoMessage(1)), Is.False);
            Assert.That(app.TrySwitch(GameRoot.SceneBPath), Is.True);
            yield return WaitTransition(app);
        }

        [UnityTest] public IEnumerator DestroyedSceneRootCanBeCleanedWithoutUnityMissingReferenceErrors()
        {
            var app = NewApp();
            app.TrySwitch(GameRoot.SceneAPath);
            yield return WaitTransition(app);
            Object.Destroy(app.Current.gameObject);
            yield return null;
            yield return null;
            Assert.That(Count(app.GlobalEvents), Is.Zero);
            Assert.That(app.TryCleanup(), Is.True);
            yield return WaitTransition(app);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest] public IEnumerator DestroyDuringLoadDoesNotLeaveAnOrphanScene()
        {
            var app = NewApp();
            var bus = app.GlobalEvents;
            app.TrySwitch(GameRoot.SceneAPath);
            Object.Destroy(app.gameObject);
            float deadline = Time.realtimeSinceStartup + 10;
            // 等待真实加载完成通知，而不是用固定几帧假定异步操作已完成。
            bool loaded = false;
            UnityAction<Scene, LoadSceneMode> hook = (scene, _) => { if (scene.path == GameRoot.SceneAPath) loaded = true; };
            hooks.Add(hook); SceneManager.sceneLoaded += hook;
            while (!loaded && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(loaded, Is.True);
            while (SceneManager.GetSceneByPath(GameRoot.SceneAPath).IsValid() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneManager.GetSceneByPath(GameRoot.SceneAPath).IsValid(), Is.False);
            Assert.That(bus.TryQueueEvent(new DemoMessage(1)), Is.False);
        }

        [UnityTest] public IEnumerator DuplicateApplicationRootIsRejectedAndClosedRootCanBeReplaced()
        {
            var first = NewApp();
            var second = NewApp(false);
            Assert.Throws<InvalidOperationException>(second.Initialize);
            Assert.That(second.State, Is.EqualTo(RootState.Closed));
            Assert.That(first.GlobalEvents.TryQueueEvent(new DemoMessage(1)), Is.True);
            first.Shutdown(); first.Shutdown();
            var third = NewApp();
            Assert.That(third.GlobalEvents.TryQueueEvent(new DemoMessage(2)), Is.True);
            yield return null;
        }

        [UnityTest] public IEnumerator DirectContentSceneDoesNotCreateGlobalServices()
        {
            yield return SceneManager.LoadSceneAsync(GameRoot.SceneAPath, LoadSceneMode.Additive);
            var root = GameRoot.FindSceneRoot(SceneManager.GetSceneByPath(GameRoot.SceneAPath));
            yield return null;
            Assert.That(root.State, Is.EqualTo(RootState.New));
            Assert.That(root.Host.IsInitialized, Is.False);
            Assert.That(root.Module, Is.Null);
        }

        [UnityTest] public IEnumerator CleanupFailureStillAllowsTheNextSceneToRun()
        {
            var app = NewApp();
            app.FailNextCleanup = true;
            app.TrySwitch(GameRoot.SceneAPath);
            yield return WaitTransition(app);
            var oldBus = app.Current.Events;
            app.TrySwitch(GameRoot.SceneBPath);
            yield return WaitTransition(app);
            Assert.That(app.LastError, Is.Not.Null);
            Assert.That(app.Current.State, Is.EqualTo(RootState.Running));
            Assert.That(Count(app.GlobalEvents), Is.EqualTo(1));
            Assert.That(oldBus.TryQueueEvent(new DemoMessage(1)), Is.False);
        }

        [UnityTest] public IEnumerator RealBootstrapStartsOnceAndRejectsASecondBootstrap()
        {
            const string path = "Assets/_Lab/Demos/001_CoreLifecycle/Scenes/Bootstrap.unity";
            var bootstraps = new List<Scene>();
            UnityAction<Scene, LoadSceneMode> hook = (scene, _) => { if (scene.path == path) bootstraps.Add(scene); };
            hooks.Add(hook); SceneManager.sceneLoaded += hook;
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            yield return null;
            var first = bootstraps[0].GetRootGameObjects()[0].GetComponent<GameRoot>();
            Assert.That(first, Is.Not.Null);
            apps.Add(first);
            yield return WaitTransition(first);
            var events = first.GlobalEvents;
            Assert.That(first.Current, Is.Not.Null);
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            yield return null;
            var second = bootstraps[1].GetRootGameObjects()[0].GetComponent<GameRoot>();
            apps.Add(second);
            Assert.That(second.State, Is.EqualTo(RootState.Closed));
            Assert.That(second.LastError, Is.Not.Null);
            Assert.That(first.GlobalEvents, Is.SameAs(events));
            Assert.That(Count(events), Is.EqualTo(1));
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            objects.Add(go);
            return go;
        }

        private GameRoot NewApp(bool initialize = true)
        {
            var go = NewObject("测试 GameRoot");
            var host = go.AddComponent<EventBusHost>();
            var app = go.AddComponent<GameRoot>();
            app.Configure(host);
            apps.Add(app);
            if (initialize) app.Initialize();
            return app;
        }

        private SceneRoot NewSceneRoot()
        {
            var go = NewObject("测试 SceneRoot");
            var host = go.AddComponent<EventBusHost>();
            var view = go.AddComponent<DemoSubscriber>();
            var root = go.AddComponent<SceneRoot>();
            root.Configure(host, view);
            return root;
        }

        private static IEnumerator WaitTransition(GameRoot app)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (app.IsSwitching && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(app.IsSwitching, Is.False, "场景切换超时");
        }

        private static IEnumerator WaitTask(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "后台任务结束超时");
        }

        private static int Count(EventBus bus)
        {
            int count = 0;
            foreach (var channel in bus.GetDiagnosticsSnapshot().Channels) count += channel.ActiveSubscriptions;
            return count;
        }
    }
}
