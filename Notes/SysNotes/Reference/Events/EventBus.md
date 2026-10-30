---
title: EventBus 脚本说明
updated: 2026-09-27
tags: [reference, core, events]
---

# EventBus

源码：`SysLab/Assets/_Lab/Core/Events/Runtime/EventBus.cs`。命名空间 `GameSysLab.Core.Events`；公开密封类，实现 `IDisposable`。纯 C#，不能挂到 GameObject。所属程序集 `GameSysLab.Core.Events`。

## 职责与所有权

负责精确泛型类型的同步通知和有界延后队列。创建者拥有总线；订阅者只拥有返回的订阅句柄。除 `TryQueueEvent` 外，公开操作和句柄释放都限创建线程；Unity 中应在主线程创建。

Subscribe、Publish、TryQueueEvent 均带 `where T : IEvent` 约束；内部通道及排队封装沿用同一约束。事件需实现 [[Reference/Events/EventBusContracts#IEvent|IEvent]]，默认定义为不可变数据；这不增加继承广播或运行时注册。

## 公开方法

| 方法 | 作用与调用时机 | 参数与默认值 | 返回值 | 失败与边界 |
| --- | --- | --- | --- | --- |
| `EventBus(reportError, options = null)` | 创建独立事件总线，固定线程与错误处理策略；由资源拥有者在启动时调用。 | `Action<EventError>` 必填错误报告器；配置空值使用默认配置 | 新实例 | 报告器为空、容量 ≤ 0 抛参数异常；配置在构造时复制 |
| `Subscribe<T>(handler, priority = 0, label = null)` | 登记一个事件监听，并取得可独立释放的退订句柄；模块或界面开始监听时调用。 | 单个同步 `Action<T>`；优先级越大越早；标签用于诊断 | `IDisposable` 独立订阅句柄 | 拒绝空回调、直接 async void、多播委托；重复订阅各自生效 |
| `Publish<T>(message)` | 在当前调用中立即通知该类型监听；发布已经完成的状态变化。 | 非空负载，按声明的 `T` 匹配 | `void` | 立即通知；已关闭、错误线程、重入抛错；严格模式结束后汇总异常 |
| `TryQueueEvent<T>(message)` | 将事件加入有界队列，交给后续 ProcessQueuedEvents 通知；用于后台投递或延后事件链。 | 非空负载；任意线程调用 | `bool`：成功入队为 true | 满队列或已关闭为 false；空值仍抛错；成功入队不保证最终送达 |
| `ProcessQueuedEvents(maxEvents = 256)` | 在所属线程消费一批排队消息并执行监听；用于主循环或 Host.Update。 | 正整数，本次最多处理的消息数 | `int`：实际出队数 | 仅处理调用开始时已有消息；严格模式整批结束后抛错，此时没有返回计数 |
| `GetDiagnosticsSnapshot()` | 生成当前总线与订阅的只读诊断快照；用于主动排错和观察计数。 | 无 | `EventBusDiagnostics` 独立只读快照 | 主动读取会分配；关闭后仍可读取总计，通道已清空 |
| `Dispose()` | 永久关闭总线，解除监听并丢弃队列；仅由总线拥有者在退出时调用。 | 无 | `void` | 幂等；清空排队引用与所有监听；错误线程仍抛错 |

## 派发约定

### 方法改名迁移（2026-09-27）

| 旧名 | 当前名称 | 改名意图 |
| --- | --- | --- |
| `TryPost` | `TryQueueEvent` | 明确“尝试把事件排队”，成功不是已执行 |
| `Drain` | `ProcessQueuedEvents` | 明确“处理排队事件”，不是无条件清空队列 |
| `GetDiagnostics` | `GetDiagnosticsSnapshot` | 明确返回的是一次快照，会分配且不会自动更新 |

这是源代码级不兼容改名，不保留旧名别名；本仓库调用处与测试已迁移。行为、参数和默认值不变。Subscribe / Publish 保持通用事件术语，Dispose 保持 IDisposable 契约，Lifetime.Add 保持资源集合的登记含义。

### 执行与失败

- 同优先级按订阅先后；派发中新增的同类型订阅本轮不执行。未执行的监听被退订后，本轮跳过。
- 回调中关闭总线，剩余监听停止。回调中再次调用同一总线 `Publish` / `ProcessQueuedEvents` 被拒绝；事件链使用 `TryQueueEvent`。
- 一个监听失败后继续其他监听；默认只报告，严格模式汇总。报告器失败时，原始异常与报告器异常都在本轮结束后抛出。
- 回调里未捕获的 API 误用异常，也会按监听异常处理。报告器执行期间同样不能重入派发。
- 不做继承广播、异步监听、弱引用、消息复制或重放。禁止 async void 的检查不能识别普通回调间接启动的异步任务。
- `Publish` 会阻塞到监听结束；队列预算是消息数量，不是时间预算，不能打断慢监听。

## 内部成员与辅助方法

| 成员 | 作用 |
| --- | --- |
| `threadId` | 记录创建线程 |
| `channels` | 类型到强类型通道的唯一映射 |
| `queueLock`、`queue` | 保护投递、出队与关闭；锁内不执行监听 |
| `reportError`、`queueCapacity`、`strict`、`MeasureTime` | 固定构造配置；最后一项供通道使用 |
| `disposed`、`processing` | 关闭状态与总线级重入保护 |
| `nextId` | 单总线递增订阅 ID |
| `published / posted / rejected / errors` | 派发、接收入队、拒绝、监听错误累计值 |
| `GetChannel<T>()` | 取得或首次创建通道；类型转换边界 |
| `Dispatch<T>(message, ref failures)` | 累加派发量并进入对应通道；未订阅类型也计总派发量 |
| `CheckThread / CheckUsable / CheckNotProcessing` | 检查线程、关闭、重入 |
| `ValidateMessage<T>` | 拒绝空负载 |
| `Report(error, ref failures)` | 计错、报告并按配置收集异常 |
| `AddFailure / ThrowFailures` | 仅错误路径创建集合，最终抛 `AggregateException` |
| `IQueuedEvent / QueuedEvent<T>` | 私有排队封装，保存类型化 message；出队时调用泛型 Dispatch |

稳定订阅、监听不分配不抛错、关闭耗时采样且不读取诊断时，同步发布已测零新增托管分配。订阅、队列封装、诊断及异常路径不承诺零分配。详见 [[Reference/Events/EventChannel]]、[[Reference/Events/EventBusContracts]]、[[Guides/Events/角色事件通知]]。
