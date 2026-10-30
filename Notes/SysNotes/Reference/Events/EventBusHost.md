---
title: EventBusHost 脚本说明
updated: 2026-09-27
tags: [reference, unity, events]
---

# EventBusHost

源码：`SysLab/Assets/_Lab/Core/Events/Unity/EventBusHost.cs`。命名空间及程序集 `GameSysLab.Core.Events.Unity`；公开 MonoBehaviour。拥有一个 [[Reference/Events/EventBus]]，提供 Unity 更新和日志接入。

## Inspector 与内部成员

| 字段 | 默认值 | 作用与生效时间 |
| --- | --- | --- |
| `queueCapacity` / Queue Capacity | 4096，Min(1) | Initialize 时复制给总线；运行中修改不改变已有队列容量 |
| `eventsPerFrame` / Events Per Frame | 256，Min(1) | 每次 Update 的最大出队数，运行中修改下一次 Update 生效 |
| `bus` | null，非序列化 | 所拥有的总线 |
| `closed` | false，非序列化 | 永久关闭标记 |

Host 使用时再次将两个整数至少限制为 1；纯 EventBus 构造则对非法容量抛错。Inspector 没有严格异常模式和耗时采样选项。

## 公开接口

| 成员 | 参数 / 返回 | 含义 |
| --- | --- | --- |
| `IsInitialized` | bool，只读 | 已创建总线且未关闭 |
| `IsClosed` | bool，只读 | 已永久关闭 |
| `Bus` | EventBus，只读 | 借用实例；未初始化抛 InvalidOperationException，关闭后抛 ObjectDisposedException |
| `Initialize()` | 无 → EventBus | 主线程显式创建；重复初始化返回同一实例；关闭后抛错 |
| `Shutdown()` | 无 → void | 关闭总线并清空字段；幂等，不可重开 |

所有 Host 操作在 Unity 主线程进行。业务只使用传入的 Bus；Host 的拥有者调用 Shutdown，借用者不要直接 Dispose Bus，以免 Host 状态失配。

## Unity 回调

| 方法 | 作用与调用时机 |
| --- | --- |
| `Initialize()` | 为所属作用域创建总线，并交给装配入口注入模块；在模块开始订阅之前显式调用 |
| `Shutdown()` | 永久结束 Host 拥有的总线，解除监听并清空排队引用；由拥有者退出时调用 |
| `Update()` | 将已投递消息转为主线程监听调用；Unity 在组件活动且启用时每帧触发 |
| `ReportError(EventError)` | 把监听错误及定位信息写入 Unity Console；由总线在监听失败后调用 |
| `OnDestroy()` | 对遗漏的显式关闭进行兜底；Unity 销毁组件时触发 |

`Update()` 在初始化后调用 ProcessQueuedEvents，并将汇总异常写入 Console。`ReportError(EventError)` 将监听类型、ID、标签与原始异常写入 Console。`OnDestroy()` 调用 Shutdown 兜底。没有 Awake 自动初始化。

## 挂载方式

在关卡常驻 GameObject 添加 EventBusHost，填写容量和每帧处理条数。你的关卡启动脚本用序列化字段保存此组件引用，先调用 Initialize，再把返回的 EventBus 传给角色和 UI；退出关卡时清理它们的订阅，最后调用 Host.Shutdown。

禁用组件或对象只停止自动 Update，不清理订阅、不关闭队列；后台仍可能把队列填满，手动 Publish 仍可同步执行。重建必须使用新 Host。全局 / 场景不是 Host 开关，而由游戏或关卡启动代码的持有与释放时机决定，见 [[Guides/Events/Unity中接入事件模块]]。
