---
title: TimerScheduler 脚本说明
tags: [reference, timer]
---

# TimerScheduler

源码：`SysLab/Assets/_Lab/Modules/Timer/Runtime/TimerScheduler.cs`。命名空间和程序集 `GameSysLab.Modules.Timer`；公开密封 IDisposable 普通类，不能挂载。Runtime 不引用 Unity 或 Core。

## 职责与归属

管理一组延迟和循环任务，只在调用 AdvanceTime 时前进。游戏或关卡启动代码创建并拥有 Scheduler，角色和界面借用它；每个使用者负责取消自己的 TimerHandle。全部调用限创建线程，含重复 Dispose。

## 公开方法

| 方法 | 作用与调用时机 | 参数 | 返回与失败 |
| --- | --- | --- | --- |
| `new TimerScheduler()` | 建立独立计时器，记录创建线程；在游戏或关卡启动时创建 | 无 | 新实例，时间只由调用方推进 |
| `ScheduleOnce(double delaySeconds, Action callback)` | 等待后执行一次；用于复活、提示自动关闭 | 有限正秒数；单个非空同步回调，无默认值 | TimerHandle；非法时长、空回调、直接 async void 或多播委托抛参数异常 |
| `ScheduleRepeating(double intervalSeconds, Action callback)` | 等待完整周期后开始重复执行；用于周期刷新 | 同上，秒数代表周期 | TimerHandle；校验同上 |
| `AdvanceTime(double deltaSeconds)` | 推进已有任务并执行到期回调；由主循环调用 | 有限非负秒数，0 不执行 | void；重入、错误线程抛 InvalidOperationException；回调错误本轮末汇总抛 AggregateException |
| `Dispose()` | 永久取消全部未结束任务，释放回调；拥有者退出时调用 | 无 | void，幂等；关闭后创建任务或推进抛 ObjectDisposedException |

## 同轮行为

先更新所有已有任务的剩余时间，再按创建顺序执行到期任务，不按截止时间排序。回调中新建任务从下一轮开始计时。取消、暂停、重置尚未执行的到期任务会使其本轮跳过；恢复也不补本轮。

循环每轮最多执行一次，下一周期从本轮当前时间重新等待完整时长，卡顿不补执行、不保留原相位。一次性任务调用回调前已 Completed；循环任务调用前已安排下一周期，自我控制不会被返回后的逻辑覆盖。

一个回调失败不妨碍其他回调，一次性保持 Completed，循环保留下一周期。同一调度器禁止嵌套 AdvanceTime，包括推进 0 秒。回调关闭调度器后，其余任务不执行。

## 内部成员和方法

| 成员 / 方法 | 作用 |
| --- | --- |
| `threadId` | 创建线程身份 |
| `tasks` | 按创建顺序保存任务的稳定 List |
| `advancing / disposed` | 遍历保护与永久关闭标记 |
| `Schedule(seconds, callback, repeating, parameter)` | 共享校验和创建逻辑，parameter 用于指出非法参数名，返回新句柄 |
| `Remove(TimerHandle)` | 遍历外立即移除取消任务；遍历中等待整理，void |
| `Compact()` | 原地稳定清除终止任务，void，不生成快照 |
| `CheckThread / CheckUsable` | 检查所属线程及是否关闭，失败抛错 |

推进为 O(n)，遍历外移除为 O(n)，大量逐个取消累计可能 O(n²)。创建任务分配句柄和委托检查数组，列表扩容也可能分配；不宣称所有操作零分配。稳定推进的测量见 [[Experiments/002_Timer]]。

使用方法见 [[Guides/Timer/游戏中的延迟与倒计时]]；单任务控制见 [[Reference/Timer/TimerHandle]]。
