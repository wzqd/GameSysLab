---
title: Lifetime 脚本说明
updated: 2026-09-27
tags: [reference, core, lifecycle]
---

# Lifetime

源码：`SysLab/Assets/_Lab/Core/Lifecycle/Runtime/Lifetime.cs`。命名空间与程序集 `GameSysLab.Core.Lifecycle`。公开密封类，实现 IDisposable；纯 C#，隐式无参构造，不能挂载。

## 开发者接口

| 方法 | 作用与调用时机 | 参数 | 返回 | 语义与失败 |
| --- | --- | --- | --- | --- |
| `Add<T>(T resource) where T : IDisposable` | 把自有资源纳入统一清理范围；获得订阅或创建模块后立即登记。 | 非空的自有资源 | 原样返回 T，便于保存引用 | 同一对象实例仅登记一次；关闭后新资源立即 Dispose；空值抛参数异常 |
| `Dispose()` | 结束此清理范围并逆序释放已登记资源；模块退出或组件禁用时调用。 | 无 | void | 先关闭，再逆序释放；单项失败继续其余清理，最后 AggregateException；重复调用安全 |

所有调用限创建线程，包括关闭后的 Add 和重复 Dispose。晚到资源立即释放时若抛错，异常直接传给 Add 调用者；已经登记的失败资源不会自动重试。

## 内部成员

| 成员 / 方法 | 作用 |
| --- | --- |
| `threadId / CheckThread()` | 记录并校验所属线程 |
| `resources` | 尚待释放的资源强引用，按登记顺序保存；清理后清空 |
| `registered` | ConditionalWeakTable 弱键身份记录，去重且不因历史记录延长资源存活 |
| `Registration` | 私有空标记类，作为弱键表的值 |
| `disposed` | 先于清理设为 true，允许清理回调重入 Add / Dispose |

只按引用身份去重，不按 Equals；值类型 IDisposable 的装箱副本不是同一对象。需要保证去重时使用引用类型资源。

## 典型用法

```csharp
var subscriptions = new Lifetime();
subscriptions.Add(bus.Subscribe<LanguageChanged>(OnLanguageChanged));
// 所属界面关闭时，在同一线程调用。
subscriptions.Dispose();
```

这里 LanguageChanged 和 OnLanguageChanged 由业务定义，LanguageChanged 必须实现 IEvent 并保持数据不可变；示意代码不是项目内现成类型。总线来自外部，因此不要 `subscriptions.Add(bus)`。下一次启用创建新的 Lifetime；旧集合不能重开。Lifetime 不负责查找服务、取消任意 Task、卸载场景或 Destroy Unity 对象；只能调用已登记资源自己的 Dispose。

见 [[Guides/Events/角色事件通知]]、[[Guides/Lifecycle/界面关闭时清理订阅]]。
