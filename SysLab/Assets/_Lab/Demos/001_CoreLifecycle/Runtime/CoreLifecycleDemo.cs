using System;
using System.Collections.Generic;
using GameSysLab.Core.Events;
using GameSysLab.Core.Events.Unity;
using UnityEngine;

namespace GameSysLab.Demos.CoreLifecycle
{
    /// <summary>独立装配入口；界面仅用于实验，不依赖未来的 UI 模块。</summary>
    public sealed class CoreLifecycleDemo : MonoBehaviour
    {
        private EventBusHost host;
        private DemoSubscriber subscriber;
        private GameObject generation;
        private EventBus previousBus;
        private readonly Queue<string> lines = new Queue<string>();
        private string diagnostics = "点击刷新诊断读取快照。";
        private int sequence;
        private DemoProducer producer;
        private bool failureReported;
        private Vector2 scroll;

        private void OnEnable() { Rebuild(); }
        private void OnDisable() { Close(); }

        private void Rebuild()
        {
            Close();
            generation = new GameObject("当前总线与订阅者");
            generation.transform.SetParent(transform, false);
            host = generation.AddComponent<EventBusHost>();
            host.Initialize();
            var child = new GameObject("随启停释放订阅");
            child.transform.SetParent(generation.transform, false);
            child.SetActive(false);
            subscriber = child.AddComponent<DemoSubscriber>();
            subscriber.Configure(host.Bus, Write);
            child.SetActive(true);
            Write("已建立新总线与 3 个监听。");
            RefreshDiagnostics();
        }

        private void Close()
        {
            if (host == null) return;
            previousBus = host.Bus;
            // Destroy 延至帧末；先解除订阅并关闭总线，避免旧生产者继续投递。
            generation.SetActive(false);
            producer?.Dispose();
            subscriber.Shutdown();
            host.Shutdown();
            Destroy(generation);
            host = null;
            subscriber = null;
            Write("旧总线已关闭，排队消息已丢弃。");
        }

        private void StartProducer()
        {
            if (producer != null && !producer.Completion.IsCompleted) return;
            int first = sequence + 1;
            sequence += 8;
            producer = new DemoProducer(host.Bus, first);
            failureReported = false;
            Write("后台开始投递 8 条消息，关闭总线会同时请求取消。");
        }

        private void Update()
        {
            if (producer?.Failure == null || failureReported) return;
            failureReported = true;
            Debug.LogException(producer.Failure, this);
        }

        private void RefreshDiagnostics()
        {
            var snapshot = (host != null ? host.Bus : previousBus)?.GetDiagnosticsSnapshot();
            if (snapshot == null) return;
            int count = 0;
            foreach (var channel in snapshot.Channels) count += channel.ActiveSubscriptions;
            diagnostics = $"关闭：{snapshot.IsDisposed}    监听：{count}    派发：{snapshot.PublishCount}\n" +
                $"队列：{snapshot.QueueLength}    接收投递：{snapshot.PostedCount}    拒绝：{snapshot.RejectedPosts}    错误：{snapshot.ErrorCount}";
        }

        private void Write(string text)
        {
            if (lines.Count >= 18) lines.Dequeue();
            lines.Enqueue(text);
        }

        private void OnGUI()
        {
            GUI.skin.label.fontSize = 17;
            GUI.skin.button.fontSize = 17;
            GUILayout.BeginArea(new Rect(24, 20, Mathf.Min(Screen.width - 48, 940), Screen.height - 40), GUI.skin.box);
            GUILayout.Label("001 · 事件分发与订阅生命周期");
            GUILayout.Label("所有监听在主线程执行；队列默认每帧最多 256 条。禁用 Host 只暂停自动派发。");
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUI.enabled = host != null;
            if (GUILayout.Button("同步发布")) host.Bus.Publish(new DemoMessage(++sequence));
            GUI.enabled = host != null && (producer == null || producer.Completion.IsCompleted);
            if (GUILayout.Button("后台投递 8 条")) StartProducer();
            GUI.enabled = host != null;
            if (GUILayout.Button("关闭总线")) Close();
            GUI.enabled = true;
            if (GUILayout.Button("重建")) Rebuild();
            GUILayout.EndHorizontal();
            if (host != null)
            {
                subscriber.enabled = GUILayout.Toggle(subscriber.enabled, "启用订阅者（关闭立即退订，再开重新订阅）");
                host.enabled = GUILayout.Toggle(host.enabled, "启用 Host 自动派发（关闭后可继续投递）");
                subscriber.FaultEnabled = GUILayout.Toggle(subscriber.FaultEnabled, "注入中间监听故障（Console 报错，最后监听继续）");
            }
            if (GUILayout.Button("刷新诊断快照")) RefreshDiagnostics();
            GUILayout.Label(diagnostics);
            GUILayout.Label($"最近后台任务：接收 {producer?.Accepted ?? 0} / 拒绝 {producer?.Rejected ?? 0} / 取消 {producer?.WasCancelled ?? false}");
            GUILayout.Space(8);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (string line in lines) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
