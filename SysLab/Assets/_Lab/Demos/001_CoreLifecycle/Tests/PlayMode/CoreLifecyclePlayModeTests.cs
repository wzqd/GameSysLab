using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameSysLab.Core.Events;
using GameSysLab.Core.Events.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameSysLab.Demos.CoreLifecycle.Tests
{
    public sealed class CoreLifecyclePlayModeTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<Scene> scenes = new List<Scene>();

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            foreach (var go in objects) if (go != null) Object.Destroy(go);
            objects.Clear();
            yield return null;
            foreach (var scene in scenes)
                if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            scenes.Clear();
        }

        [UnityTest]
        public IEnumerator SubscriberDisableEnableAndDestroyReleaseOnlySubscriptions()
        {
            var host = CreateHost();
            var subscriber = CreateSubscriber(host.Bus);
            host.Bus.Publish(new DemoMessage(1));
            Assert.That(subscriber.ReceivedCount, Is.EqualTo(1));
            subscriber.enabled = false;
            Assert.That(ListenerCount(host.Bus), Is.Zero);
            host.Bus.Publish(new DemoMessage(2));
            Assert.That(subscriber.ReceivedCount, Is.EqualTo(1));
            subscriber.enabled = true;
            Assert.That(ListenerCount(host.Bus), Is.EqualTo(3));
            host.Bus.Publish(new DemoMessage(3));
            Assert.That(subscriber.ReceivedCount, Is.EqualTo(2));
            Object.Destroy(subscriber.gameObject);
            yield return null;
            Assert.That(ListenerCount(host.Bus), Is.Zero);
            Assert.That(host.Bus.TryQueueEvent(new DemoMessage(4)), Is.True);
        }

        [UnityTest]
        public IEnumerator DisabledHostKeepsQueueAndResumesOnEnable()
        {
            var host = CreateHost();
            var subscriber = CreateSubscriber(host.Bus);
            host.enabled = false;
            var bus = host.Bus;
            var posting = Task.Run(() => bus.TryQueueEvent(new DemoMessage(1)));
            while (!posting.IsCompleted) yield return null;
            Assert.That(posting.Result, Is.True);
            yield return null;
            Assert.That(subscriber.ReceivedCount, Is.Zero);
            Assert.That(host.Bus.GetDiagnosticsSnapshot().QueueLength, Is.EqualTo(1));
            host.enabled = true;
            yield return null;
            yield return null;
            Assert.That(subscriber.ReceivedCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DestroyedHostRejectsOldBackgroundProducer()
        {
            var host = CreateHost();
            var oldBus = host.Bus;
            CreateSubscriber(oldBus);
            oldBus.TryQueueEvent(new DemoMessage(1));
            Object.Destroy(host.gameObject);
            yield return null;
            var posting = Task.Run(() => oldBus.TryQueueEvent(new DemoMessage(2)));
            while (!posting.IsCompleted) yield return null;
            Assert.That(posting.Result, Is.False);
            Assert.That(oldBus.GetDiagnosticsSnapshot().QueueLength, Is.Zero);
            var replacement = CreateHost();
            Assert.That(ListenerCount(replacement.Bus), Is.Zero);
            Assert.That(replacement.Bus.TryQueueEvent(new DemoMessage(3)), Is.True);
        }

        [UnityTest]
        public IEnumerator SceneExitAndReentryLeaveNoOldSubscriptions()
        {
            var firstScene = SceneManager.CreateScene("CoreLifecycle_First");
            scenes.Add(firstScene);
            var firstHost = CreateHost();
            var firstSubscriber = CreateSubscriber(firstHost.Bus);
            SceneManager.MoveGameObjectToScene(firstHost.gameObject, firstScene);
            SceneManager.MoveGameObjectToScene(firstSubscriber.gameObject, firstScene);
            var oldBus = firstHost.Bus;
            oldBus.Publish(new DemoMessage(1));
            yield return SceneManager.UnloadSceneAsync(firstScene);
            Assert.That(oldBus.GetDiagnosticsSnapshot().IsDisposed, Is.True);
            Assert.That(ListenerCount(oldBus), Is.Zero);
            Assert.That(oldBus.TryQueueEvent(new DemoMessage(2)), Is.False);
            var secondScene = SceneManager.CreateScene("CoreLifecycle_Second");
            scenes.Add(secondScene);
            var secondHost = CreateHost();
            var secondSubscriber = CreateSubscriber(secondHost.Bus);
            SceneManager.MoveGameObjectToScene(secondHost.gameObject, secondScene);
            SceneManager.MoveGameObjectToScene(secondSubscriber.gameObject, secondScene);
            secondHost.Bus.Publish(new DemoMessage(3));
            Assert.That(secondSubscriber.ReceivedCount, Is.EqualTo(1));
            Assert.That(ListenerCount(secondHost.Bus), Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator ListenerFaultIsLoggedAndOtherListenersContinue()
        {
            var host = CreateHost();
            var lines = new List<string>();
            var subscriber = CreateSubscriber(host.Bus);
            subscriber.enabled = false;
            subscriber.Configure(host.Bus, lines.Add);
            subscriber.FaultEnabled = true;
            subscriber.enabled = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Demo 主动制造的监听故障"));
            host.Bus.Publish(new DemoMessage(1));
            Assert.That(lines.Count, Is.EqualTo(2));
            Assert.That(lines[1], Does.Contain("最后监听仍执行"));
            Assert.That(host.Bus.GetDiagnosticsSnapshot().ErrorCount, Is.EqualTo(1));
            yield return null;
        }

        private EventBusHost CreateHost()
        {
            var go = new GameObject("测试总线");
            objects.Add(go);
            var host = go.AddComponent<EventBusHost>();
            host.Initialize();
            return host;
        }

        private DemoSubscriber CreateSubscriber(EventBus bus)
        {
            var go = new GameObject("测试订阅者");
            objects.Add(go);
            go.SetActive(false);
            var subscriber = go.AddComponent<DemoSubscriber>();
            subscriber.Configure(bus, _ => { });
            go.SetActive(true);
            return subscriber;
        }

        private static int ListenerCount(EventBus bus)
        {
            int count = 0;
            foreach (var channel in bus.GetDiagnosticsSnapshot().Channels) count += channel.ActiveSubscriptions;
            return count;
        }
    }
}
