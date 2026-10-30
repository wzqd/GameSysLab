using System;
using UnityEngine;

namespace GameSysLab.Core.Events.Unity
{
    /// <summary>总线归此组件所有；禁用暂停队列派发，销毁关闭总线。</summary>
    public sealed class EventBusHost : MonoBehaviour
    {
        [SerializeField, Min(1)] private int queueCapacity = 4096;
        [SerializeField, Min(1)] private int eventsPerFrame = 256;
        private EventBus bus;
        private bool closed;
        public bool IsInitialized => bus != null && !closed;
        public bool IsClosed => closed;
        public EventBus Bus => closed
            ? throw new ObjectDisposedException(nameof(EventBusHost))
            : bus ?? throw new InvalidOperationException("请先显式调用 Host.Initialize。");

        /// <summary>由装配入口在主线程初始化；已初始化时返回原实例。</summary>
        public EventBus Initialize()
        {
            if (closed) throw new ObjectDisposedException(nameof(EventBusHost));
            if (bus == null)
                bus = new EventBus(ReportError, new EventBusOptions { QueueCapacity = Math.Max(1, queueCapacity) });
            return bus;
        }

        /// <summary>关闭不可逆；禁用仅暂停，需要重建时使用新 Host。</summary>
        public void Shutdown()
        {
            if (closed) return;
            bus?.Dispose();
            closed = true;
            bus = null;
        }

        private void Update()
        {
            if (!IsInitialized) return;
            try { bus.ProcessQueuedEvents(Math.Max(1, eventsPerFrame)); }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        private void OnDestroy() { Shutdown(); }

        private void ReportError(EventError error)
        {
            Debug.LogError($"事件 {error.EventType.Name}，订阅 {error.SubscriptionId}（{error.Label}）：{error.Exception}", this);
        }
    }
}
