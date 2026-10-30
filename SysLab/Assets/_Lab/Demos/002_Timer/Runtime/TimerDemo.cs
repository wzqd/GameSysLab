using GameSysLab.Core.Lifecycle;
using GameSysLab.Modules.Timer;
using GameSysLab.Modules.Timer.Unity;
using UnityEngine;

namespace GameSysLab.Demos.Timer
{
    /// <summary>独立计时验证界面；任务归使用者，总线等其他模块不参与。</summary>
    public sealed class TimerDemo : MonoBehaviour
    {
        private GameObject generation;
        private TimerHost gameHost;
        private TimerHost menuHost;
        private Lifetime tasks;
        private Lifetime tipTasks;
        private TimerHandle revive;
        private bool alive;
        private bool tipVisible;
        private int refreshCount;
        private float previousTimeScale;

        public TimerHandle Countdown => revive;
        public TimerHost GameHost => gameHost;
        public TimerHost MenuHost => menuHost;

        private void OnEnable()
        {
            previousTimeScale = Time.timeScale;
            generation = new GameObject("当前计时器实例");
            generation.transform.SetParent(transform, false);
            gameHost = generation.AddComponent<TimerHost>();
            var menu = new GameObject("不随游戏暂停的计时器");
            menu.transform.SetParent(generation.transform, false);
            menuHost = menu.AddComponent<TimerHost>();
            menuHost.TimeMode = TimerTimeMode.UnscaledTime;
            gameHost.Initialize(); menuHost.Initialize();
            tasks = new Lifetime();
            tasks.Add(gameHost.Scheduler.ScheduleRepeating(1, () => refreshCount++));
            BeginRevive();
        }

        private void BeginRevive()
        {
            revive?.Cancel();
            alive = false;
            // 单个可替换任务直接保存句柄，避免长寿集合积累已结束的历史句柄。
            revive = gameHost.Scheduler.ScheduleOnce(5, () => alive = true);
        }

        private void ShowTip()
        {
            CloseTip();
            tipVisible = true;
            tipTasks = new Lifetime();
            tipTasks.Add(menuHost.Scheduler.ScheduleOnce(3, CloseTip));
        }

        private void CloseTip()
        {
            tipVisible = false;
            var previous = tipTasks; tipTasks = null; previous?.Dispose();
        }

        private void OnDisable()
        {
            CloseTip(); revive?.Cancel(); tasks?.Dispose(); tasks = null;
            if (gameHost != null) gameHost.Shutdown();
            if (menuHost != null) menuHost.Shutdown();
            if (generation != null) Destroy(generation);
            Time.timeScale = previousTimeScale;
        }

        private void OnGUI()
        {
            // 使用统一参考宽度，避免高分辨率 Game 视图截图中文字过小。
            Matrix4x4 previousMatrix = GUI.matrix;
            float scale = Screen.width / 1280f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUILayout.BeginArea(new Rect(24, 24, 1180, Screen.height / scale - 48), GUI.skin.box);
            GUILayout.Label("002 · Timer：延迟复活、提示关闭与周期刷新");
            GUILayout.Label($"角色：{(alive ? "已复活" : "等待复活")}；倒计时 {revive.RemainingSeconds:F2} 秒；状态 {revive.State}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("重新创建 5 秒复活任务")) BeginRevive();
            bool active = revive.State == TimerState.Scheduled || revive.State == TimerState.Paused;
            GUI.enabled = active;
            if (GUILayout.Button("暂停任务")) revive.Pause();
            if (GUILayout.Button("恢复任务")) revive.Resume();
            if (GUILayout.Button("重置（保持暂停状态）")) revive.Reset();
            if (GUILayout.Button("取消任务")) revive.Cancel();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            bool paused = GUILayout.Toggle(Time.timeScale == 0, "暂停游戏时间（菜单计时继续）");
            Time.timeScale = paused ? 0 : previousTimeScale;
            gameHost.enabled = GUILayout.Toggle(gameHost.enabled, "启用游戏计时 Host（禁用后不补时间）");
            GUILayout.Label($"游戏时间每秒刷新次数：{refreshCount}");
            if (GUILayout.Button("显示提示：3 秒未缩放时间后自动关闭")) ShowTip();
            if (tipVisible)
            {
                GUILayout.Label("获得一件物品！暂停游戏也会自动关闭此提示。");
                if (GUILayout.Button("立即关闭提示并清理任务")) CloseTip();
            }
            GUILayout.Label("已完成 / 已取消的任务不可重置；重新创建后才能再次计时。");
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
        }
    }
}
