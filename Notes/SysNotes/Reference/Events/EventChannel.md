---
title: EventChannel 内部实现说明
updated: 2026-09-27
tags: [reference, internal, events]
---

# EventChannel

源码：`SysLab/Assets/_Lab/Core/Events/Runtime/EventChannel.cs`。命名空间 `GameSysLab.Core.Events`。`IEventChannel`、`EventChannel<T>` 都是 internal；开发者通过 [[Reference/Events/EventBus]] 使用，不能挂载或直接作为模块依赖。

## 数据成员

| 成员 | 作用 |
| --- | --- |
| `bus` | 所属总线，提供计时配置和统一报错 |
| `listeners` | 已排序的稳定监听列表 |
| `pending` | 本通道派发中新增的监听 |
| `active / removed` | 活动数与待整理失效数 |
| `dispatching / closed` | 派发状态、永久关闭标记 |
| `published` | 通道派发次数 |
| `Order` | 优先级降序、订阅 ID 升序比较器 |

## 方法

| 方法 | 参数 → 返回 | 作用 |
| --- | --- | --- |
| 构造函数 | EventBus → 新通道 | 保存所属总线 |
| `Subscribe` | Action&lt;T&gt; handler、long id、int priority、string label → IDisposable | 创建独立订阅；派发中入 pending，否则有序插入 |
| `Dispatch` | T message、ref List&lt;Exception&gt; failures → void | 直接遍历有效监听，统计、报错，finally 整理并合并新增订阅 |
| `Clear` | 无 → void | 关闭通道，清空所有节点的回调和列表 |
| `Snapshot` | 无 → EventChannelDiagnostics | 分配只读快照；接口 IEventChannel 也提供此方法和 Clear |
| `InsertOrdered` | Subscription node → void | 二分寻找位置后 List.Insert；移动元素仍可能 O(n) |
| `Remove` | 无 → void | 节点已失效后更新计数，必要时触发整理 |
| `Compact` | List&lt;Subscription&gt; nodes → void | 原地稳定压缩，移除尾部失效元素，O(n) |

本层依赖总线已完成线程、参数、重入检查，不是独立的安全公共 API。

## 私有 Subscription

实现 `IDisposable`，实例通过接口句柄交给订阅者。`owner` 指向通道；`threadId` 保证创建线程退订；`Handler` 保存 Action&lt;T&gt;；`Id / Priority / Label` 为不可变元数据；`Calls / Ticks` 是调用与耗时计数。

- 构造函数接收 owner、handler、id、priority、label；无额外默认参数。
- `Dispose()`：检查线程，清空 Handler 和 owner，再通知旧通道 Remove；重复调用安全。
- `Deactivate()`：只清空回调和所有者，用于整条通道关闭。
- `Snapshot()`：返回该节点的 SubscriptionDiagnostics。

## 整理时机与成本

逻辑失效为 O(1)，但一次退订可能触发 O(n) 整理。派发结束统一整理；非派发时失效数至少 16 且达到列表一半才整理；最后一个订阅退出时立即清空列表。通道保留到总线关闭，列表容量不保证立即归还。稳定发布不创建监听快照；诊断 Snapshot 与派发不是同一条路径。
