using System;
using UnityEngine;

namespace GameSysLab.Modules.Timer.Unity
{
    /// <summary>游戏时间跟随 timeScale，未缩放时间适用于暂停菜单等场景。</summary>
    public enum TimerTimeMode { GameTime, UnscaledTime }

    /// <summary>拥有一个调度器，禁用只暂停自动推进；由启动代码显式初始化。</summary>
    public sealed class TimerHost : MonoBehaviour
    {
        [SerializeField] private TimerTimeMode timeMode = TimerTimeMode.GameTime;
        private TimerTimeMode activeTimeMode;
        private TimerScheduler scheduler;
        private bool closed;

        public bool IsInitialized => scheduler != null && !closed;
        public bool IsClosed => closed;
        public TimerScheduler Scheduler => closed
            ? throw new ObjectDisposedException(nameof(TimerHost))
            : scheduler ?? throw new InvalidOperationException("请先调用 TimerHost.Initialize。");

        /// <summary>供启动代码配置；初始化后固定，Inspector 后续修改也不改变当前实例。</summary>
        public TimerTimeMode TimeMode
        {
            get => IsInitialized ? activeTimeMode : timeMode;
            set
            {
                if (closed || scheduler != null) throw new InvalidOperationException("请在初始化前选择时间模式。");
                ValidateMode(value);
                timeMode = value;
            }
        }

        public TimerScheduler Initialize()
        {
            if (closed) throw new ObjectDisposedException(nameof(TimerHost));
            if (scheduler == null)
            {
                ValidateMode(timeMode);
                activeTimeMode = timeMode;
                scheduler = new TimerScheduler();
            }
            return scheduler;
        }

        public void Shutdown()
        {
            if (closed) return;
            scheduler?.Dispose();
            scheduler = null;
            closed = true;
        }

        private void Update()
        {
            if (!IsInitialized) return;
            double delta = activeTimeMode == TimerTimeMode.GameTime ? Time.deltaTime : Time.unscaledDeltaTime;
            try { scheduler.AdvanceTime(delta); }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        private void OnDestroy() { Shutdown(); }

        private static void ValidateMode(TimerTimeMode mode)
        {
            if (mode != TimerTimeMode.GameTime && mode != TimerTimeMode.UnscaledTime)
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }
}
