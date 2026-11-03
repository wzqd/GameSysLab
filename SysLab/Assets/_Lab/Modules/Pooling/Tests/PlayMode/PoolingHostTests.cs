using System.Collections;
using GameSysLab.Modules.Pooling.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PoolingHostTests
{
    [UnityTest] public IEnumerator ReuseExternalDestroyAndShutdown()
    {
        var source = new GameObject("source"); source.SetActive(false);
        var root = new GameObject("owner"); var host = root.AddComponent<GameObjectPoolHost>();
        host.Configure(source); host.Initialize();
        var first = host.Rent(); var instance = first.Value;
        Assert.IsFalse(instance.activeSelf);
        instance.transform.SetParent(null); instance.SetActive(true); first.Dispose();
        Assert.IsFalse(instance.activeSelf);
        var second = host.Rent(); Assert.AreSame(instance, second.Value); second.Dispose();
        Object.Destroy(instance); yield return null;
        var third = host.Rent(); Assert.IsTrue(third.Value != null);
        var last = third.Value; last.transform.SetParent(null);
        host.enabled = false; Assert.IsTrue(third.IsValid);
        host.Shutdown(); Assert.IsFalse(third.IsValid);
        yield return null; Assert.IsTrue(last == null);
        Object.Destroy(root); Object.Destroy(source); yield return null;
    }
}
