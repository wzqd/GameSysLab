using System;
using System.Collections;
using System.Collections.Generic;
using GameSysLab.Core.Events;
using GameSysLab.Core.Events.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameSysLab.Demos.CoreLifecycle
{
    /// <summary>具体 Demo 的应用入口：显式装配，保留启动场景，每次运行一个内容场景。</summary>
    public sealed class GameRoot : MonoBehaviour
    {
        public const string SceneAPath = "Assets/_Lab/Demos/001_CoreLifecycle/Scenes/ContentA.unity";
        public const string SceneBPath = "Assets/_Lab/Demos/001_CoreLifecycle/Scenes/ContentB.unity";
        [SerializeField] private EventBusHost globalHost;
        [SerializeField] private bool startAutomatically = true;
        private readonly List<Scene> ownedScenes = new List<Scene>();
        private readonly List<DemoProducer> finishingProducers = new List<DemoProducer>();
        private readonly Dictionary<Scene, AsyncOperation> unloading = new Dictionary<Scene, AsyncOperation>();
        private bool ownsGlobalHost;
        private readonly Queue<string> messages = new Queue<string>();
        private Scene currentScene;
        private Scene candidate;
        private SceneLoadTicket loading;
        private string diagnostics = "点击刷新诊断。";
        private int notice;
        private bool failInitialization, failCleanup;
        public RootState State { get; private set; }
        public SceneRoot Current { get; private set; }
        public bool IsSwitching { get; private set; }
        public Exception LastError { get; private set; }
        public EventBus GlobalEvents => globalHost.Bus;
        public int PendingProducerCount => finishingProducers.Count;
        // 故障注入只属于实验入口，用于验证恢复路径，不进入 Core。
        public bool FailNextInitialization { get; set; }
        public bool FailNextCleanup { get; set; }
        public bool FailNextUnload { get; set; }

        public void Configure(EventBusHost host, bool autoStart = false)
        {
            if (State != RootState.New) throw new InvalidOperationException("应用入口已经初始化。");
            globalHost = host;
            startAutomatically = autoStart;
        }

        private void Start()
        {
            if (!startAutomatically || State != RootState.New) return;
            try { Initialize(); TrySwitch(SceneAPath); }
            catch (Exception error) { Record(error); }
        }

        public void Initialize()
        {
            if (State != RootState.New) throw new InvalidOperationException("应用入口不允许重复初始化。");
            try
            {
                // 仅启动边界检查根入口重复，不向业务提供全局查找能力。
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    foreach (var go in SceneManager.GetSceneAt(i).GetRootGameObjects())
                        foreach (var root in go.GetComponentsInChildren<GameRoot>(true))
                            if (root != this && root.State != RootState.Closed)
                                throw new InvalidOperationException("重复的 GameRoot / Bootstrap，拒绝创建第二套全局服务。");
                if (globalHost == null || globalHost.gameObject.scene != gameObject.scene)
                    throw new InvalidOperationException("GameRoot 缺少同一启动场景的全局 Host。");
                if (globalHost.IsInitialized || globalHost.IsClosed)
                    throw new InvalidOperationException("全局 Host 必须尚未初始化，不能接管其他入口的实例。");
                State = RootState.Initializing;
                globalHost.Initialize();
                ownsGlobalHost = true;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                State = RootState.Running;
                Write("应用入口已初始化。");
            }
            catch
            {
                // 验证失败时不处置尚未取得所有权的 Host，防止错误配置指向别人的资源。
                if (State == RootState.Initializing && globalHost != null) globalHost.Shutdown();
                State = RootState.Closed;
                throw;
            }
        }

        public bool TrySwitch(string path)
        {
            if (State != RootState.Running || IsSwitching) return Reject("应用未运行或已有切换正在进行。");
            if (HasPendingCleanup()) return Reject("仍有关闭但未卸载的场景，请先重试清理。");
            // 使用构建列表的完整路径，拒绝简称；否则加载通知的路径无法准确对应归属。
            bool knownPath = false;
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                if (SceneUtility.GetScenePathByBuildIndex(i) == path) { knownPath = true; break; }
            if (!knownPath || path == gameObject.scene.path || !Application.CanStreamedLevelBeLoaded(path))
                return Reject("目标场景路径无效、未加入构建场景列表，或指向启动场景。");
            LastError = null;
            failInitialization = FailNextInitialization;
            failCleanup = FailNextCleanup;
            FailNextInitialization = FailNextCleanup = false;
            IsSwitching = true;
            StartCoroutine(RunTransition(SwitchSteps(path)));
            return true;
        }

        public bool TryCleanup()
        {
            if (State != RootState.Running || IsSwitching) return false;
            LastError = null;
            IsSwitching = true;
            StartCoroutine(RunTransition(CleanupSteps()));
            return true;
        }

        private IEnumerator SwitchSteps(string path)
        {
            loading = new SceneLoadTicket(path);
            yield return loading.Operation;
            if (State != RootState.Running) yield break;
            candidate = loading.Scene;
            if (!candidate.IsValid() || !candidate.isLoaded) throw new InvalidOperationException("本次加载没有得到目标场景实例。");
            ownedScenes.Add(candidate);
            loading = null;
            var next = FindSceneRoot(candidate);
            next.ValidateConfiguration();

            if (currentScene.IsValid() && currentScene.isLoaded)
            {
                StopRoot(Current);
                var old = currentScene;
                yield return BeginUnload(old);
                if (old.IsValid() && old.isLoaded) throw new InvalidOperationException("旧场景卸载失败，新场景不会启动。");
            }
            if (State != RootState.Running) yield break;
            next.FailInitialization = failInitialization;
            next.FailCleanup = failCleanup;
            next.Initialize(GlobalEvents);
            Current = next;
            currentScene = candidate;
            candidate = default;
            Write($"已进入 {currentScene.name}，句柄 {currentScene.handle}。");
            RefreshDiagnostics();
        }

        private IEnumerator CleanupSteps()
        {
            foreach (var scene in ownedScenes.ToArray())
            {
                if (!scene.IsValid() || !scene.isLoaded) continue;
                if (scene == currentScene && Current != null && Current.State == RootState.Running) continue;
                StopSceneRoots(scene);
                yield return BeginUnload(scene);
                if (scene.IsValid() && scene.isLoaded) throw new InvalidOperationException("清理后场景仍加载，保留归属以便重试。");
            }
        }

        private IEnumerator RunTransition(IEnumerator steps)
        {
            try
            {
                while (State == RootState.Running)
                {
                    bool more;
                    try { more = steps.MoveNext(); }
                    catch (Exception error) { Record(error); break; }
                    if (!more) break;
                    yield return steps.Current;
                }
                if (candidate.IsValid() && candidate.isLoaded)
                {
                    StopSceneRoots(candidate);
                    AsyncOperation cleanup = null;
                    try { cleanup = BeginUnload(candidate); }
                    catch (Exception error) { Record(error); }
                    if (cleanup != null) yield return cleanup;
                }
            }
            finally
            {
                (steps as IDisposable)?.Dispose();
                loading?.Abandon();
                loading = null;
                candidate = default;
                IsSwitching = false;
            }
        }

        private AsyncOperation BeginUnload(Scene scene)
        {
            if (FailNextUnload)
            {
                FailNextUnload = false;
                throw new InvalidOperationException("Demo 主动制造的卸载故障，可重试清理。");
            }
            return UnloadOwnedScene(scene);
        }

        private AsyncOperation UnloadOwnedScene(Scene scene)
        {
            if (unloading.TryGetValue(scene, out var pending)) return pending;
            var operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null) throw new InvalidOperationException("场景卸载未返回操作，保留场景归属。");
            unloading[scene] = operation;
            return operation;
        }

        public static SceneRoot FindSceneRoot(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("目标场景尚未加载。");
            SceneRoot result = null;
            foreach (var go in scene.GetRootGameObjects())
                foreach (var root in go.GetComponentsInChildren<SceneRoot>(true))
                {
                    if (result != null) throw new InvalidOperationException("目标场景包含多个 SceneRoot。");
                    result = root;
                }
            return result != null ? result : throw new InvalidOperationException("目标场景缺少 SceneRoot。");
        }

        private bool HasPendingCleanup()
        {
            foreach (var scene in ownedScenes)
                if (scene.IsValid() && scene.isLoaded &&
                    (scene != currentScene || Current == null || Current.State != RootState.Running)) return true;
            return false;
        }

        private void StopRoot(SceneRoot root)
        {
            if (root == null) return;
            if (root.Producer != null && !finishingProducers.Contains(root.Producer)) finishingProducers.Add(root.Producer);
            try { root.Shutdown(); }
            catch (Exception error) { Record(error); }
        }

        private void StopSceneRoots(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (var go in scene.GetRootGameObjects())
                foreach (var root in go.GetComponentsInChildren<SceneRoot>(true)) StopRoot(root);
        }

        private void OnSceneUnloaded(Scene scene)
        {
            ownedScenes.Remove(scene);
            unloading.Remove(scene);
            if (scene != currentScene) return;
            Current = null;
            currentScene = default;
            Write("内容场景已卸载，全局服务继续运行。");
        }

        public void Shutdown()
        {
            if (State == RootState.Closing || State == RootState.Closed) return;
            State = RootState.Closing;
            loading?.Abandon();
            loading = null;
            foreach (var scene in ownedScenes.ToArray())
            {
                StopSceneRoots(scene);
                if (scene.IsValid() && scene.isLoaded)
                    try { UnloadOwnedScene(scene); }
                    catch (Exception error) { Record(error); }
            }
            foreach (var producer in finishingProducers) producer.Dispose();
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            try { if (ownsGlobalHost && globalHost != null) globalHost.Shutdown(); }
            catch (Exception error) { Record(error); }
            Current = null;
            State = RootState.Closed;
        }

        private void OnDestroy() { Shutdown(); }

        private void Update()
        {
            for (int i = finishingProducers.Count - 1; i >= 0; i--)
            {
                var producer = finishingProducers[i];
                if (!producer.Completion.IsCompleted) continue;
                if (producer.Failure != null) Record(producer.Failure);
                Write($"旧生产者结束：取消 {producer.WasCancelled}，接收 {producer.Accepted}，拒绝 {producer.Rejected}。");
                finishingProducers.RemoveAt(i);
            }
        }

        private bool Reject(string message) { Record(new InvalidOperationException(message)); return false; }
        private void Record(Exception error)
        {
            LastError = LastError == null ? error : new AggregateException(LastError, error);
            Write(error.Message);
        }

        private void Write(string message)
        {
            if (messages.Count == 8) messages.Dequeue();
            messages.Enqueue(message);
        }

        private void RefreshDiagnostics()
        {
            var global = GlobalEvents.GetDiagnosticsSnapshot();
            int globalListeners = 0;
            foreach (var channel in global.Channels) globalListeners += channel.ActiveSubscriptions;
            var local = Current != null && Current.State == RootState.Running ? Current.Events.GetDiagnosticsSnapshot() : null;
            diagnostics = $"全局监听 {globalListeners} / 派发 {global.PublishCount}；局部队列 {local?.QueueLength ?? 0} / 派发 {local?.PublishCount ?? 0}";
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20, 20, Mathf.Min(960, Screen.width - 40), Screen.height - 40), GUI.skin.box);
            GUILayout.Label("001 · GameRoot / SceneRoot 显式装配与场景切换");
            // Unity 已销毁对象仍可能保留托管包装，不能用 ?. 判断其存活。
            string sceneName = Current != null ? Current.gameObject.scene.name : "无";
            GUILayout.Label($"应用 {State}，当前 {sceneName}，切换中 {IsSwitching}");
            GUI.enabled = State == RootState.Running && !IsSwitching;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("进入 / 重进 A")) TrySwitch(SceneAPath);
            if (GUILayout.Button("进入 B")) TrySwitch(SceneBPath);
            if (GUILayout.Button("重试清理")) TryCleanup();
            if (GUILayout.Button("全局通知")) GlobalEvents.Publish(new GlobalNotice(++notice));
            GUILayout.EndHorizontal();
            FailNextInitialization = GUILayout.Toggle(FailNextInitialization, "下次初始化失败（验证回滚清理）");
            FailNextCleanup = GUILayout.Toggle(FailNextCleanup, "下个场景退出时制造清理失败");
            FailNextUnload = GUILayout.Toggle(FailNextUnload, "下次卸载失败（保留归属，允许重试）");
            if (Current != null && Current.State == RootState.Running)
            {
                GUILayout.Label($"模块收到：全局 {Current.Module.GlobalNotifications} / 局部 {Current.Module.LocalNotifications}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("局部发布")) Current.Events.Publish(new DemoMessage(++notice));
                GUI.enabled &= Current.Producer == null || Current.Producer.Completion.IsCompleted;
                if (GUILayout.Button("后台投递")) Current.StartProducer();
                if (GUILayout.Button("后台故障")) Current.StartProducer(true);
                GUILayout.EndHorizontal();
                GUI.enabled = !IsSwitching;
                Current.Subscriber.enabled = GUILayout.Toggle(Current.Subscriber.enabled, "启用展示订阅者");
                Current.Host.enabled = GUILayout.Toggle(Current.Host.enabled, "启用局部队列派发");
                foreach (var line in Current.Messages) GUILayout.Label(line);
            }
            GUI.enabled = State == RootState.Running;
            if (GUILayout.Button("刷新诊断")) RefreshDiagnostics();
            GUI.enabled = true;
            GUILayout.Label(diagnostics);
            if (LastError != null) GUILayout.Label($"最近错误：{LastError.GetBaseException().Message}");
            foreach (var line in messages) GUILayout.Label(line);
            GUILayout.EndArea();
        }
    }
}
