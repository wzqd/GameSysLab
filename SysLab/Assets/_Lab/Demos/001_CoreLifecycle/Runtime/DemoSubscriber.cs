using System;
using GameSysLab.Core.Events;
using GameSysLab.Core.Lifecycle;
using UnityEngine;

namespace GameSysLab.Demos.CoreLifecycle
{
    public readonly struct DemoMessage : IEvent
    {
        public int Value { get; }
        public DemoMessage(int value) { Value = value; }
    }

    /// <summary>展示组件自己的启停订阅范围，不拥有传入的总线。</summary>
    public sealed class DemoSubscriber : MonoBehaviour
    {
        private EventBus bus;
        private Action<string> write;
        private Lifetime subscriptions;
        private bool closed;
        public bool FaultEnabled { get; set; }
        public int ReceivedCount { get; private set; }

        public void Configure(EventBus eventBus, Action<string> writeLine)
        {
            if (closed) throw new ObjectDisposedException(nameof(DemoSubscriber));
            if (subscriptions != null) throw new InvalidOperationException("已有活动订阅，不能更换依赖。");
            bus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            write = writeLine ?? throw new ArgumentNullException(nameof(writeLine));
            if (isActiveAndEnabled) Subscribe();
        }

        private void OnEnable()
        {
            if (!closed && bus != null) Subscribe();
        }

        private void Subscribe()
        {
            if (subscriptions != null) return;
            subscriptions = new Lifetime();
            try
            {
                if (bus == null) throw new InvalidOperationException("订阅者启用前必须配置总线。");
                subscriptions.Add(bus.Subscribe<DemoMessage>(OnFirst, 100, "优先监听"));
                subscriptions.Add(bus.Subscribe<DemoMessage>(OnPotentialFault, 0, "可注入故障"));
                subscriptions.Add(bus.Subscribe<DemoMessage>(OnLast, -100, "最后监听"));
            }
            catch
            {
                subscriptions.Dispose();
                subscriptions = null;
                throw;
            }
        }

        private void OnDisable()
        {
            var previous = subscriptions;
            subscriptions = null;
            previous?.Dispose();
        }

        public void Shutdown()
        {
            if (closed) return;
            closed = true;
            try { OnDisable(); }
            finally { bus = null; write = null; }
        }

        private void OnDestroy() { Shutdown(); }

        private void OnFirst(DemoMessage message)
        {
            ReceivedCount++;
            write($"#{message.Value} 优先级 100：第一个监听");
        }

        private void OnPotentialFault(DemoMessage message)
        {
            if (FaultEnabled) throw new InvalidOperationException("Demo 主动制造的监听故障");
            write($"#{message.Value} 优先级 0：中间监听");
        }

        private void OnLast(DemoMessage message) => write($"#{message.Value} 优先级 -100：最后监听仍执行");
    }
}
