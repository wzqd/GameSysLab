---
title: TimerHost Unity 接入
tags: [reference, timer, unity]
---

# TimerHost

源码：`SysLab/Assets/_Lab/Modules/Timer/Unity/TimerHost.cs`。命名空间和程序集 `GameSysLab.Modules.Timer.Unity`，引用纯 C# Timer 程序集。公开 MonoBehaviour，挂在负责该组任务寿命的游戏或关卡对象上。

## 配置与属性

| 成员 | 默认值 / 类型 | 用途 |
| --- | --- | --- |
| Inspector `timeMode` / Time Mode | GameTime | Initialize 时固定选择时间来源，之后 Inspector 改动不影响已有实例 |
| `TimeMode` | TimerTimeMode，可读写 | 动态创建时在初始化前配置；运行中 setter 抛 InvalidOperationException；运行中 getter 返回实际固定模式 |
| `Scheduler` | 只读 TimerScheduler | 借用实例；初始化前访问抛 InvalidOperationException，关闭后抛 ObjectDisposedException |
| `IsInitialized / IsClosed` | 只读 bool | 是否存在可用实例、是否永久关闭 |

同文件 `TimerTimeMode`：GameTime 使用 Time.deltaTime，跟随 timeScale；UnscaledTime 使用 Time.unscaledDeltaTime，用于暂停菜单和提示。不提供全局 Instance。所有 Host 操作在 Unity 主线程进行。

## 方法和 Unity 回调

以下均无参数。

| 方法 | 作用与调用时机 | 返回 / 失败 |
| --- | --- | --- |
| `Initialize()` | 启动代码创建服务并把实例交给业务对象；Awake 不自动创建 | TimerScheduler，重复返回同一个；关闭后抛错；非法模式抛参数异常 |
| `Shutdown()` | 拥有者结束时永久取消所有任务 | void，幂等，不能重新开启 |
| `Update()` | 初始化后每帧按固定时间模式推进，汇总异常写 Console | private void；禁用组件或对象时不执行 |
| `OnDestroy()` | 兜底调用 Shutdown | private void |
| `ValidateMode(TimerTimeMode)` | 校验只允许两种有效枚举值 | private static void |

内部 `activeTimeMode` 保存固定模式，`scheduler` 保存自有实例，`closed` 防止重新开启。

## 挂载和退出

给“关卡计时”对象添加 TimerHost；启动脚本用序列化字段持有它，在 Inspector 拖入引用，先 Initialize 再创建任务。菜单和游戏需要不同时间时挂两个 Host 并分别传入；无需每个任务都选择时间模式。

禁用仅暂停自动推进，恢复后不补禁用时间；外部手动调用 Scheduler.AdvanceTime 仍会推进。借用者不要直接 Dispose Scheduler，以免 Host 继续驱动已关闭实例；由拥有者调用 Shutdown。角色或界面只取消自己的句柄。

用法见 [[Guides/Timer/游戏中的延迟与倒计时]]。
