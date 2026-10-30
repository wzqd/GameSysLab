---
title: TimerHandle 与 TimerState
tags: [reference, timer, lifecycle]
---

# TimerHandle

源码：`SysLab/Assets/_Lab/Modules/Timer/Runtime/TimerHandle.cs`。命名空间和程序集 `GameSysLab.Modules.Timer`。公开密封 IDisposable 类，由 Scheduler 创建，构造函数 internal；不能挂载，也不应自行构造。

## 状态与查询

同文件 `TimerState` 枚举：Scheduled 正在计时；Paused 暂停；Completed 一次性任务已到期；Cancelled 被使用者或调度器取消。后两种不可逆。

| 属性 | 作用 | 返回 |
| --- | --- | --- |
| `State` | 判断是否可继续控制任务 | 只读 TimerState，创建时 Scheduled |
| `RemainingSeconds` | 显示剩余时间；按 Scheduler 的推进更新 | 只读 double 秒数；初始为配置时长，终止为 0 |

暂停时剩余时间不变，循环回调执行时已经是下一次完整周期。剩余时间不按系统时钟自动变化。全部属性读取与方法调用限创建线程，终止后同样检查。

## 方法

以下方法均无参数、返回 void。

| 方法 | 作用与调用时机 | 状态及失败 |
| --- | --- | --- |
| `Pause()` | 暂停单个任务，保留剩余时间；用于暂停复活或界面倒计时 | 重复暂停安全；若本轮到期尚未执行，取消本轮执行资格 |
| `Resume()` | 接着剩余时间计时 | 已 Scheduled 时无操作；不会补本轮回调 |
| `Reset()` | 恢复创建时完整延迟或周期 | 保持当前暂停状态；不能修改原配置时长，本轮回调跳过 |
| `Cancel()` | 永久取消并断开回调、所属调度器引用 | 幂等；Completed 不会变为 Cancelled |
| `Dispose()` | 与 Cancel 完全相同，供 Lifetime 等清理者调用 | 幂等 |

Completed / Cancelled 后 Pause、Resume、Reset 抛 InvalidOperationException；需要重来应创建新任务。一次性回调内部已经 Completed，因此也不能重置自己。循环回调可以暂停、重置或取消自己。

## 内部成员和方法

| 成员 / 方法 | 作用 |
| --- | --- |
| `threadId / durationSeconds / repeating` | 创建线程、最初时长、是否循环，均固定 |
| `owner / callback` | 活动期所属 Scheduler 与 Action；终止立即置空 |
| `state / remainingSeconds / dueThisAdvance` | 状态、剩余秒数、本轮待执行资格 |
| `IsTerminal` | 内部查询是否 Completed 或 Cancelled |
| 构造函数 | 接收 owner、秒数、回调、循环标记、线程 ID，初始化任务 |
| `AdvanceRemaining(double)` | 主动计时任务扣除本轮增量并标记到期；void |
| `InvokeIfDue()` | 执行仍有效的本轮到期回调，先决定下一状态；void |
| `CancelFromOwner()` | 调度器关闭时终止未结束任务，不反向修改遍历列表 |
| `End(TimerState)` | 设置终止状态、清空回调和 owner、剩余时间归零 |
| `CheckThread / CheckActive` | 检查线程及可控制状态 |

句柄不拥有 Scheduler，把句柄加入 [[Reference/Lifecycle/Lifetime]] 不会关闭其他任务。长期集合仍会保留登记过的句柄直到清理，但终止句柄不再持有业务回调；频繁替换同一倒计时可直接保存并取消旧句柄。
