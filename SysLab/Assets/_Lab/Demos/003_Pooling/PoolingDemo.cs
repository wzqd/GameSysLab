using System.Collections.Generic;
using GameSysLab.Modules.Pooling;
using GameSysLab.Modules.Pooling.Unity;
using UnityEngine;

/// <summary>独立演示：手动借还，不依赖 Timer 或全局服务。</summary>
public sealed class PoolingDemo : MonoBehaviour
{
    public GameObject prefab;
    private GameObjectPoolHost host;
    private readonly List<PoolLease<GameObject>> leases = new List<PoolLease<GameObject>>();
    private void Start() { CreatePool(); }
    private void CreatePool()
    {
        host = gameObject.AddComponent<GameObjectPoolHost>();
        host.Configure(prefab, 3); host.Initialize();
    }
    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(20, 20, 340, 350), GUI.skin.box);
        GUILayout.Label("Pooling：引用租约 / 缓存上限 3");
        GUILayout.Label($"借出 {host.ActiveCount} / 空闲 {host.InactiveCount}");
        if (!host.IsClosed && GUILayout.Button("借出并修改位置"))
        {
            var lease = host.Rent(); leases.Add(lease);
            lease.Value.transform.SetParent(null);
            lease.Value.transform.position = new Vector3((leases.Count - 1) % 6 - 2.5f, (leases.Count - 1) / 6, 0);
            lease.Value.SetActive(true);
        }
        if (GUILayout.Button("归还一个") && leases.Count > 0)
        { leases[leases.Count - 1].Dispose(); leases.RemoveAt(leases.Count - 1); }
        if (GUILayout.Button("归还全部")) { foreach (var lease in leases) lease.Dispose(); leases.Clear(); }
        if (GUILayout.Button("关闭池")) { host.Shutdown(); leases.Clear(); }
        if (host.IsClosed && GUILayout.Button("创建新池")) { Destroy(host); CreatePool(); }
        GUILayout.EndArea();
    }
}
