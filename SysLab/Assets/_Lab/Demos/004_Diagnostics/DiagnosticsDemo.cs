using System;
using GameSysLab.Modules.Diagnostics;
using GameSysLab.Modules.Diagnostics.Unity;
using UnityEngine;
using Logger = GameSysLab.Modules.Diagnostics.Logger;

namespace GameSysLab.Demos.Diagnostics
{
    /// <summary>只展示日志模块的接入、过滤和故障隔离，不引用其他实验模块。</summary>
    public sealed class DiagnosticsDemo : MonoBehaviour
    {
        private sealed class SwitchableSink : ILogSink
        {
            private readonly UnityConsoleSink console = new UnityConsoleSink();
            public bool Fail;
            public void Write(LogEntry entry)
            {
                if (Fail) throw new InvalidOperationException("演示注入的输出故障");
                console.Write(entry);
            }
        }
        private Logger log;
        private SwitchableSink sink;
        private int builds;
        private string result = "选择级别，再点击消息按钮；输出位于 Unity Console。";
        private void OnEnable() { sink = new SwitchableSink(); log = new Logger("角色生成", sink); builds = 0; }
        private void OnDisable() { log = null; sink = null; }
        private void OnGUI()
        {
            if (log == null) return;
            var previous = GUI.matrix;
            float scale = Screen.width / 1280f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUILayout.BeginArea(new Rect(24, 24, 1180, Screen.height / scale - 48), GUI.skin.box);
            GUILayout.Label("日志与诊断 · 独立入口 / 同步输出 / 无全局服务");
            GUILayout.Label("最低级别（None 关闭全部输出）");
            log.MinimumLevel = (LogLevel)GUILayout.Toolbar((int)log.MinimumLevel, new[] { "Info", "Warning", "Error", "None" });
            if (GUILayout.Button("记录普通消息")) Show(log.Info("角色准备完成"));
            if (GUILayout.Button("记录警告")) Show(log.Warning("缺少头像，使用默认图标"));
            if (GUILayout.Button("记录错误与异常")) Show(log.Error("角色创建失败", new InvalidOperationException("演示业务异常")));
            if (GUILayout.Button("延迟构造 Info 消息")) Show(log.WriteLazy(LogLevel.Info, () => { builds++; return "本次才构造详细消息"; }));
            sink.Fail = GUILayout.Toggle(sink.Fail, "模拟输出器故障（不向 Console 输出，不抛回业务）");
            GUILayout.Label($"延迟消息构造次数：{builds}；输出故障次数：{log.FailureCount}");
            GUILayout.Label(result);
            if (log.LastFailure != null) GUILayout.Label("最近输出故障：" + log.LastFailure.Message);
            if (GUILayout.Button("清除故障记录")) log.ClearFailures();
            GUILayout.EndArea(); GUI.matrix = previous;
        }
        private void Show(bool success) { result = success ? "输出器已接收。" : "未输出：级别过滤或输出故障。"; }
    }
}
