using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameSysLab.Demos.CoreLifecycle
{
    /// <summary>一次加载的归属记录；入口在加载途中销毁时，完成后仍清理本次场景。</summary>
    internal sealed class SceneLoadTicket
    {
        private readonly string path;
        private readonly HashSet<Scene> previousScenes = new HashSet<Scene>();
        private bool abandoned;
        public AsyncOperation Operation { get; }
        public Scene Scene { get; private set; }

        public SceneLoadTicket(string path)
        {
            this.path = path;
            for (int i = 0; i < SceneManager.sceneCount; i++) previousScenes.Add(SceneManager.GetSceneAt(i));
            SceneManager.sceneLoaded += OnLoaded;
            try
            {
                Operation = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
                if (Operation == null) throw new InvalidOperationException("场景加载未返回操作。");
                Operation.completed += OnCompleted;
            }
            catch { SceneManager.sceneLoaded -= OnLoaded; throw; }
        }

        private void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive && scene.path == path && !previousScenes.Contains(scene))
                Scene = scene;
        }

        private void OnCompleted(AsyncOperation operation)
        {
            SceneManager.sceneLoaded -= OnLoaded;
            operation.completed -= OnCompleted;
            if (abandoned) UnloadAbandoned();
        }

        public void Abandon()
        {
            abandoned = true;
            if (Operation.isDone) UnloadAbandoned();
        }

        private void UnloadAbandoned()
        {
            if (!Scene.IsValid() || !Scene.isLoaded) return;
            try { SceneManager.UnloadSceneAsync(Scene); }
            catch (Exception error) { Debug.LogException(error); }
        }
    }
}
