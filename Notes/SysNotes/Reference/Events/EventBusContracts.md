---
title: EventBus 配置与诊断类型
updated: 2026-09-27
tags: [reference, core, events]
---

# EventBusContracts

源码：`SysLab/Assets/_Lab/Core/Events/Runtime/EventBusContracts.cs`。命名空间 `GameSysLab.Core.Events`；程序集 `GameSysLab.Core.Events`。本文件包含以下六个公开类型，均不是 Unity 组件。

## IEvent

```csharp
public interface IEvent { }
```

空标记接口，没有成员、方法或运行时操作。事件类型显式实现它，表示“这份数据用于通知已经发生的事实”。Subscribe、Publish、TryQueueEvent 的泛型参数都要求 `where T : IEvent`，普通 int、string 或业务对象不能直接发送。

默认使用 readonly struct；较大数据可以用不可变类。接口不强制不可变性，事件作者仍需保证数据发出后不被修改，包括其中引用的集合。优先保存 ID、数值和独立的不可变数据。

仍按声明的泛型 T 精确匹配：`Subscribe<IEvent>` 不会收到 `Publish<HealthChanged>`。不要为统一接口而把所有事件装入 IEvent 变量再发送；小型结构体按具体类型发送也避免不必要的装箱。HealthChanged 为业务自行定义的类型，见 [[Guides/Events/角色事件通知]]。

## EventBusOptions

可写配置类，通过隐式无参构造创建，仅在 [[Reference/Events/EventBus]] 构造时读取。

| 属性 | 类型 / 默认值 | 含义 |
| --- | --- | --- |
| `QueueCapacity` | int / 4096 | 最大排队条数，必须 > 0；满时拒绝新消息 |
| `ThrowListenerExceptions` | bool / false | true 时当前事件或 ProcessQueuedEvents 整批结束后汇总监听异常 |
| `MeasureListenerTime` | bool / false | 开启单监听累计耗时；关闭时耗时为 0 |

修改配置对象不会修改已有总线。Host Inspector 只提供容量和每帧预算，不提供后两个选项。

## EventError

只读结构体，由总线生成并传给报告器；构造函数 internal，开发者读取属性。

| 属性 | 类型 | 含义 |
| --- | --- | --- |
| `EventType` | Type | 本次派发的精确事件类型 |
| `SubscriptionId` | long | 失败订阅在该总线内的 ID |
| `Label` | string | 订阅时传入的可空标签 |
| `Exception` | Exception | 监听抛出的原始异常 |

不主动保存事件负载或订阅者实例；异常对象本身仍可能携带调用方附加数据。不建议无限缓存错误对象。

## SubscriptionDiagnostics

只读类，internal 构造，由快照创建。

| 属性 | 类型 | 含义 |
| --- | --- | --- |
| `Id / Label / Priority` | long / string / int | 订阅身份、标签和执行优先级 |
| `InvocationCount` | long | 实际进入监听的次数，包含抛错的调用；不依赖耗时采样开关 |
| `ElapsedStopwatchTicks` | long | 采样开启时累计监听耗时，不含错误报告器耗时 |

秒数 = `ElapsedStopwatchTicks / (double)System.Diagnostics.Stopwatch.Frequency`；不是 `TimeSpan.Ticks`。

## EventChannelDiagnostics

| 属性 | 类型 | 含义 |
| --- | --- | --- |
| `EventType` | Type | 通道的精确事件类型 |
| `PublishCount` | long | 通道创建后的派发次数；首次订阅前的发布不计入此通道 |
| `ActiveSubscriptions` | int | 快照中的活动订阅数，即 Subscriptions.Count |
| `Subscriptions` | ReadOnlyCollection&lt;SubscriptionDiagnostics&gt; | 活动监听的独立快照，派发中读取可包含待加入监听 |

## EventBusDiagnostics

| 属性 | 类型 | 含义 |
| --- | --- | --- |
| `IsDisposed` | bool | 读取时是否关闭 |
| `PublishCount` | long | 同步发布与出队派发总次数，包括没有监听的事件 |
| `PostedCount` | long | 成功入队累计数，不等于已送达数 |
| `RejectedPosts` | long | 满队列或关闭造成的拒绝累计数，不含空参数异常 |
| `ErrorCount` | long | 监听失败次数，不另外累计报告器失败 |
| `QueueLength` | int | 读取时仍在队列中的消息数 |
| `Channels` | ReadOnlyCollection&lt;EventChannelDiagnostics&gt; | 按类型全名排序的通道快照；关闭后为空 |

两个诊断类的构造同为 internal，无开发者可调用的业务方法。快照不会随总线继续变化，不保留回调用于诊断。只在调试按钮或低频采样时读取；不要用计数驱动业务流程。使用入口见 [[Reference/Events/EventBus]]。
