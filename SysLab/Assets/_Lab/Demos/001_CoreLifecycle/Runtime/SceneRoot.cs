using System;
using System.Collections.Generic;
using GameSysLab.Core.Events;
using GameSysLab.Core.Events.Unity;
using GameSysLab.Core.Lifecycle;
using UnityEngine;

namespace GameSysLab.Demos.CoreLifecycle
{
    public enum RootState { New, Initializing, Running, Closing, Closed }

    /// <summary>当前内容场景的装配入口，拥有模块和局部总线，只借用全局服务。</summary>
    public sealed class SceneRoot : MonoBehaviour
    {
        [SerializeField] private EventBusHost host;
        [SerializeField] private DemoSubscriber subscriber;
        private Lifetime modules;
        private readonly Queue<string> messages = new Queue<string>();
        private bool producerFailureReported;
        private bool ownsDependencies;
        public RootState State { get; private set; }
        public SceneModule Module { get; private set; }
        public DemoProducer Producer { get; private set; }
        public EventBus Events => host.Bus;
        public EventBusHost Host => host;
        public DemoSubscriber Subscriber => subscriber;
        public bool FailInitialization { get; set; }
        public bool FailCleanup { get; set; }
        public IEnumerable<string> Messages => messages;

        /// <summary>供明确创建对象的装配代码配置引用；场景资源直接保存序列化引用。</summary>
        public void Configure(EventBusHost eventHost, DemoSubscriber view)
        {
            if (State != RootState.New) throw new InvalidOperationException("只能在初始化前配置场景入口。");
            host = eventHost;
            subscriber = view;
        }

        public void ValidateConfiguration()
        {
            if (State != RootState.New) throw new InvalidOperationException("场景入口已使用，不能再次初始化。");
            if (host == null || subscriber == null) throw new InvalidOperationException("SceneRoot 缺少 Host 或订阅组件引用。");
            if (host.gameObject.scene != gameObject.scene || subscriber.gameObject.scene != gameObject.scene)
                throw new InvalidOperationException("场景入口不能拥有其他场景的组件。");
            if (host.IsInitialized || host.IsClosed) throw new InvalidOperationException("场景 Host 必须尚未初始化。");
        }

        public void Initialize(EventBus globalEvents)
        {
            if (State != RootState.New) throw new InvalidOperationException("场景入口不允许重复初始化。");
            try
            {
                ValidateConfiguration();
                if (globalEvents == null) throw new ArgumentNullException(nameof(globalEvents));
                State = RootState.Initializing;
                ownsDependencies = true;
                modules = new Lifetime();
                host.enabled = false;
                var localEvents = host.Initialize();
                Module = modules.Add(new SceneModule(localEvents, globalEvents, FailCleanup));
                if (FailInitialization) throw new InvalidOperationException("Demo 主动制造的场景初始化故障");
                subscriber.Configure(localEvents, Write);
                State = RootState.Running;
                host.enabled = true;
                Write("场景已完成装配。");
            }
            catch (Exception original)
            {
                try { Shutdown(); }
                catch (Exception cleanup) { throw new AggregateException("初始化及清理均失败。", original, cleanup); }
                throw;
            }
        }

        public DemoProducer StartProducer(bool fail = false)
        {
            if (State != RootState.Running) throw new InvalidOperationException("场景未运行。");
            if (Producer != null && !Producer.Completion.IsCompleted) throw new InvalidOperationException("生产者仍在运行。");
            ObserveProducerFailure();
            Producer = new DemoProducer(Events, failAt: fail ? 1 : -1);
            producerFailureReported = false;
            return Producer;
        }

        public void Shutdown()
        {
            if (State == RootState.Closed || State == RootState.Closing) return;
            State = RootState.Closing;
            var errors = new List<Exception>();
            if (!ownsDependencies) { State = RootState.Closed; return; }
            Cleanup(() => { if (host != null) host.enabled = false; }, errors);
            Cleanup(() => Producer?.Dispose(), errors);
            Cleanup(() => { if (subscriber != null) subscriber.Shutdown(); }, errors);
            Cleanup(() => modules?.Dispose(), errors);
            Cleanup(() => { if (host != null) host.Shutdown(); }, errors);
            State = RootState.Closed;
            if (errors.Count != 0) throw new AggregateException("场景清理出现错误，其余资源已继续释放。", errors);
        }

        private static void Cleanup(Action action, List<Exception> errors)
        {
            try { action(); }
            catch (Exception error) { errors.Add(error); }
        }

        private void Update() { ObserveProducerFailure(); }
        private void ObserveProducerFailure()
        {
            if (Producer?.Failure == null || producerFailureReported) return;
            producerFailureReported = true;
            Debug.LogException(Producer.Failure, this);
        }

        private void OnDestroy()
        {
            try { Shutdown(); }
            catch (Exception error) { Debug.LogException(error); }
            ObserveProducerFailure();
        }

        private void Write(string text)
        {
            if (messages.Count == 12) messages.Dequeue();
            messages.Enqueue(text);
        }

        private void OnGUI()
        {
            if (State == RootState.New)
                GUI.Label(new Rect(20, 20, 700, 30), "内容场景尚未初始化，请从 Bootstrap 场景运行，或由装配入口传入依赖。");
        }
    }
}
