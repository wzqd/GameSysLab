---
title: Timer 延迟任务与生命周期
type: experiment
id: "002"
status: verified
stage: robust
tags: [timer, lifecycle]
---

# Timer 延迟任务与生命周期

## 问题与选择

角色复活、提示关闭和周期刷新都需要延迟调用，但对象提前退出、游戏暂停、循环卡顿和回调中修改其他任务容易产生隐式行为。本模块使用显式实例与任务句柄，时间由调用方推进，不依赖事件总线。

TimerScheduler、TimerHandle 位于 `Modules/Timer/Runtime`，纯 C# 程序集设置 noEngineReferences 且没有 Core 依赖。TimerHost 位于独立 Unity 程序集，只选择时间来源并推进调度器。Lifetime 的配合发生在使用者处。

## 已实现契约

- 延迟、循环、推进统一使用 double 秒数。延迟/周期为有限正数，增量有限非负；零增量不执行回调。
- 稳定列表先更新全部本轮已有任务，再按创建顺序执行到期任务；新建任务下轮开始。没有按截止时间排序或本轮快照数组。
- 每次推进循环任务最多执行一次，下次等待完整周期；跳过卡顿错过的执行，不保持周期相位。
- Pause 冻结，Resume 续计，Reset 回到原时长且保留暂停状态。尚未执行的到期任务被暂停/重置/取消，本轮跳过；恢复也不补执行。
- 一次性在回调前 Completed；循环在回调前安排下一周期。终止状态不可重启，Cancel/Dispose 幂等。
- 句柄终止时清除 Action 和 owner；调度器关闭取消全部未结束任务。全部 Runtime 操作和查询限创建线程。
- 同一调度器不能重入推进。回调失败继续其余任务，本轮末 AggregateException；循环保留下一周期。拒绝空、直接 async void、多播回调。

遍历外取消立即 List.Remove，O(n)；逐个取消大量任务累计可能 O(n²)。遍历内失效，finally 稳定整理 O(n)。优先保持实现简单，本版不引入堆、时间轮、对象池或调度预算；扩展前先用真实负载证明需要。

## 测试与测量

首轮 Unity 编译通过；55/55 EditMode 通过，其中 Timer 新增 23 项、既有 Events/Lifecycle 32 项。覆盖边界到期、暂停重置、循环、自我控制、修改其他任务、清理引用、关闭、重入、线程、错误汇总与非法参数。

测量环境为当前 Windows Unity 6000.6.0f1 编辑器，GC.Alloc 记录器限制当前线程；先用 new byte[1024] 验证能记录到分配。计时使用 Stopwatch，Frequency = 10,000,000，数据包含记录器开销。

| 负载 | 次数 | 托管分配次数 | 总耗时 |
| --- | ---: | ---: | ---: |
| 稳定循环任务推进并回调 | 10,000 | 0 | 0.2942 ms |
| 创建一次性任务后立即取消 | 10,000 | 20,001 | 4.3162 ms |
| 集中取消预建任务 | 10,000 | 0 | 12.2749 ms |
| 预建任务同时到期执行 | 10,000 | 0 | 0.2721 ms |

稳定推进前预热 100 次，只有一个循环任务，回调不分配不抛错；已知分配对照为 1 次。创建测量复用同一个 Action；集中取消、集中到期的任务创建不计入各自测量。单次样本不承诺耗时，不把分配次数当成字节数。

原始结果：`SysLab/TestResults/timer-benchmark.txt`；本轮回归 JSON 保存到 `LocalData/Timer`。两处均为本地运行产物。

## Unity 接入验收

TimerHost 显式 Initialize；GameTime 跟随游戏暂停，UnscaledTime 可用于菜单。模式初始化后固定。禁用停止自动推进，恢复不补时间；Shutdown 和 OnDestroy 取消任务。

PlayMode 验证范围包含显式初始化、关闭不可重开、两种时间、禁用恢复、销毁、异常报告和真实场景卸载重进。首轮异常日志预期与 Unity 实际格式不符，修正测试后重新运行；最终全项目 29/29 PlayMode 通过，Timer 新增 6 项，既有场景回归 23 项全部通过，0 失败、0 跳过。

## 不包含与未验证

不提供后台计时、单独冷却系统、异步监听、持久化、通用服务查询或全局单例。没有目标设备、Player、IL2CPP、Domain Reload 关闭组合或长期压力结论。大量任务同时到期和单个慢回调仍可能阻塞主线程。

模块成员见 [[Reference/Timer/TimerScheduler]]、[[Reference/Timer/TimerHandle]]、[[Reference/Timer/TimerHost]]；游戏接入见 [[Guides/Timer/游戏中的延迟与倒计时]]。
