using System;
using UnityEngine;

namespace GameSysLab.Modules.Pooling.Unity
{
    /// <summary>拥有实例池，借用固定 Prefab；资源加载和业务激活由调用方负责。</summary>
    public sealed class GameObjectPoolHost : MonoBehaviour
    {
        [SerializeField] private GameObject prefab;
        [SerializeField, Min(0)] private int maxInactive = 32;
        private ObjectPool<GameObject> pool;
        private Transform cache;
        private bool closed;
        public bool IsClosed => closed;
        public int ActiveCount => pool?.ActiveCount ?? 0;
        public int InactiveCount => pool?.InactiveCount ?? 0;

        /// <summary>在初始化前配置资源；Host 不拥有传入的 Prefab。</summary>
        public void Configure(GameObject source, int capacity = 32)
        {
            if (closed || pool != null) throw new InvalidOperationException("只能在初始化前配置。");
            if (!source) throw new ArgumentNullException(nameof(source));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            prefab = source; maxInactive = capacity;
        }

        /// <summary>显式初始化；重置回调在归还禁用后执行，后续初始化不替换回调。</summary>
        public void Initialize(Action<GameObject> reset = null)
        {
            if (closed) throw new ObjectDisposedException(nameof(GameObjectPoolHost));
            if (pool != null) return;
            if (!prefab) throw new InvalidOperationException("请配置 Prefab。");
            if (maxInactive < 0) throw new ArgumentOutOfRangeException(nameof(maxInactive));
            var node = new GameObject("PoolingCache"); node.SetActive(false);
            cache = node.transform; cache.SetParent(transform, false);
            var source = prefab;
            var position = source.transform.localPosition;
            var rotation = source.transform.localRotation;
            var scale = source.transform.localScale;
            pool = new ObjectPool<GameObject>(() =>
            {
                var instance = Instantiate(source, cache, false);
                instance.SetActive(false);
                return instance;
            }, instance =>
            {
                if (!instance) return;
                instance.SetActive(false); reset?.Invoke(instance);
                if (!instance) return;
                instance.transform.SetParent(cache, false);
                instance.transform.localPosition = position;
                instance.transform.localRotation = rotation;
                instance.transform.localScale = scale;
            }, instance => { if (instance) { instance.SetActive(false); Destroy(instance); } }, maxInactive);
        }

        /// <summary>返回禁用实例，由调用方设置父节点与数据后激活。</summary>
        public PoolLease<GameObject> Rent()
        {
            if (closed) throw new ObjectDisposedException(nameof(GameObjectPoolHost));
            if (pool == null) throw new InvalidOperationException("请先初始化。");
            // 失效实例不能再次缓存，否则会无限循环。
            while (true)
            {
                var lease = pool.Rent();
                if (lease.Value) return lease;
                pool.Discard(lease);
            }
        }
        public void Shutdown()
        {
            if (closed) return;
            closed = true;
            try { pool?.Dispose(); }
            finally { if (cache) Destroy(cache.gameObject); }
        }
        private void OnDestroy()
        {
            try { Shutdown(); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
    }
}
