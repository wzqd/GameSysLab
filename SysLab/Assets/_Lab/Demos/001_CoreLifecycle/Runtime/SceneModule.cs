using System;
using GameSysLab.Core.Events;
using GameSysLab.Core.Lifecycle;

namespace GameSysLab.Demos.CoreLifecycle
{
    public readonly struct GlobalNotice : IEvent
    {
        public int Value { get; }
        public GlobalNotice(int value) { Value = value; }
    }

    /// <summary>普通模块只接收需要的总线，不认识 GameRoot、SceneRoot 或 Unity。</summary>
    public sealed class SceneModule : IDisposable
    {
        private readonly Lifetime subscriptions = new Lifetime();
        private readonly bool failOnDispose;
        private bool closed;
        public int GlobalNotifications { get; private set; }
        public int LocalNotifications { get; private set; }

        public SceneModule(EventBus localEvents, EventBus globalEvents, bool failOnDispose = false)
        {
            this.failOnDispose = failOnDispose;
            try
            {
                subscriptions.Add(globalEvents.Subscribe<GlobalNotice>(_ => GlobalNotifications++, label: "场景模块的全局订阅"));
                subscriptions.Add(localEvents.Subscribe<DemoMessage>(_ => LocalNotifications++, label: "场景模块的局部订阅"));
            }
            catch { subscriptions.Dispose(); throw; }
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            subscriptions.Dispose();
            if (failOnDispose) throw new InvalidOperationException("Demo 主动制造的模块清理故障");
        }
    }
}
