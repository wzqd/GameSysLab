---
title: GameObjectPoolHost
tags: [pooling, reference]
---
# GameObjectPoolHost

源码：SysLab/Assets/_Lab/Modules/Pooling/Unity/GameObjectPoolHost.cs。命名空间与程序集 GameSysLab.Modules.Pooling.Unity。公共 MonoBehaviour，挂在关卡拥有者上；拥有实例与禁用缓存节点，借用 Prefab，所有调用限 Unity 主线程。

## 成员

Inspector prefab 必填，maxInactive 默认 32、非负、单位为对象数；初始化时固定。ActiveCount、InactiveCount、IsClosed 只读。pool/cache/closed 分别保存池、缓存节点和永久关闭状态。

## 方法

| 方法 | 作用与调用时机 | 参数与返回 | 异常与生命周期 |
| --- | --- | --- | --- |
| Configure(source, capacity=32) | 装配入口初始化前配置 | GameObject source；int capacity 非负；无返回 | 空资源 ArgumentNullException；负容量 ArgumentOutOfRangeException；初始化后 InvalidOperationException |
| Initialize(reset=null) | 进入关卡显式建立实例池 | Action<GameObject> 可选；无返回 | 重复调用不替换回调；关闭 ObjectDisposedException；缺资源 InvalidOperationException |
| Rent() | 游戏行为需要实例时借出 | 返回 PoolLease<GameObject>，实例禁用 | 未初始化 InvalidOperationException；关闭 ObjectDisposedException；跳过失效缓存 |
| Shutdown() | 离开关卡关闭池 | 无参数返回，幂等 | 清理错误传播，缓存节点在 finally 中销毁 |
| OnDestroy() | Unity 销毁时兜底关闭 | 内部，无参数返回 | 捕获并记录清理异常 |

Initialize 创建工厂与回收回调，记录 Prefab 的局部 Transform；重置先禁用、调用业务回调、再恢复父节点和 Transform。没有 Awake 自动初始化、Update 或 OnDisable 自动归还。Unity Destroy 延后生效，测试须等待。

[[Guides/Pooling/游戏中的实例复用]] · [[Experiments/003_Pooling]]
