---
title: Core 事件与订阅生命周期
type: experiment
id: "001"
status: verified
stage: robust
updated: 2026-09-27
tags:
  - core
  - events
  - lifecycle
---

# Core 事件与订阅生命周期

## 问题与结论

怎样让普通 C# 事件工具可复用，同时明确通知顺序、失败行为、后台投递和订阅退出时机？

采用实例化 EventBus、类型化通道、有界延后队列和独立 Lifetime。Runtime 不引用 Unity；Unity Host 负责逐帧派发和 Console 错误报告。具体事件由使用模块定义，不引入全局实例、服务查找或自动装配。

> [!success] 初版已验证范围（2026-09-26）
> Unity 6000.6.0f1 / Windows 编辑器：31 项 EditMode、5 项 PlayMode 全部通过，0 跳过。性能证据使用 Unity GC.Alloc 记录器；未进行目标设备、IL2CPP 或长期运行压力验证。

2026-09-27 增量结果：Host 已迁移到显式 Initialize / Shutdown，补齐游戏与关卡边界的显式服务创建和清理；原 5 项 PlayMode 已迁移并保留，加入 18 项装配/切场景测试，共 23/23 通过。31 项 EditMode 回归通过，本地测试证据为 LocalData/SceneLifecycle 下的测试结果 JSON。

## 代码入口与依赖

工程根目录下的路径：

| 入口 | 职责 |
| --- | --- |
| `SysLab/Assets/_Lab/Core/Events/Runtime/` | EventBus、类型化通道、选项、错误上下文、诊断快照 |
| `SysLab/Assets/_Lab/Core/Lifecycle/Runtime/Lifetime.cs` | 独立资源拥有者，逆序清理 |
| `SysLab/Assets/_Lab/Core/Events/Unity/EventBusHost.cs` | Initialize 创建，Update 派发，Shutdown 关闭，OnDestroy 兜底 |
| `Core/Events/Tests/EditMode/`、`Core/Lifecycle/Tests/EditMode/` | 纯 C# 行为测试与编辑器性能实验 |
| `Demos/001_CoreLifecycle/Tests/PlayMode/` | Unity 生命周期与日志验证 |

Events.Runtime 和 Lifecycle.Runtime 的程序集均设置 `noEngineReferences`。Lifetime 不引用 EventBus；Unity 层引用 Events；由项目启动代码组合使用。

## 公开接口

```csharp
EventBus(Action<EventError> reportError, EventBusOptions options = null);
IDisposable Subscribe<T>(Action<T> handler, int priority = 0, string label = null) where T : IEvent;
void Publish<T>(T message) where T : IEvent;
bool TryQueueEvent<T>(T message) where T : IEvent;
int ProcessQueuedEvents(int maxEvents = 256);
EventBusDiagnostics GetDiagnosticsSnapshot();
void Dispose();

T Lifetime.Add<T>(T resource) where T : IDisposable;
void Lifetime.Dispose();
```

`EventBusOptions` 在构造时复制：QueueCapacity 默认 4096；ThrowListenerExceptions 默认 false；MeasureListenerTime 默认 false。后续修改 options 不改变总线。

## 状态与不变量

总线只有打开与关闭两个长期状态，处理事件期间另设派发标记。关闭不可逆；重建应创建新实例。

- 每个 Type 只对应同类型 EventChannel<T>，节点保存 Action<T>。类型转换只发生在通道查找边界，回调不经 object 转换。
- Publish<T> 按 T 精确匹配；Publish<IEvent>(某个具体事件) 不通知该具体事件类型的监听，不做继承或接口广播。
- 每次订阅独立，ID 在当前总线内递增；重复 handler 不合并。
- 高优先级先通知，同优先级按订阅顺序通知。优先级不替代必须依次完成的业务调用。
- 同步派发直接访问稳定列表。新增监听待当前事件结束后加入；未执行但已退订的监听跳过。同批后续队列事件可以看到新监听。
- 退订立即清空回调和所属通道引用。回调中关闭总线会跳过剩余监听和未出队消息。
- 同一总线派发期间再次 Publish 或 ProcessQueuedEvents 抛 InvalidOperationException。事件链使用 TryQueueEvent；不同总线彼此独立。
- Publish、Subscribe、退订、ProcessQueuedEvents、诊断和 Dispose 只能由创建线程调用。在 Unity 主线程的显式装配入口创建；Host 通过 Initialize 创建。TryQueueEvent 是唯一跨线程入口。
- 回调、错误报告入口和资源均有明确拥有者。总线关闭不回滚监听者已经修改的外部状态。

逻辑退订 O(1)，实际调用可能触发 O(n) 整理。派发结束清除无效节点；非派发时无效数至少 16 且占列表至少一半才整理，最后一个监听退出立即清空。节点列表保持稳定顺序；单次新增二分查找后插入，批量待加入监听合并后排序。没有每次发布的快照、排序或逐个查找删除。通道保留至总线关闭，列表容量也可保留供复用。

## 队列契约

短锁保护 FIFO 队列、容量检查、计数和关闭，不在锁内执行用户回调。

- TryQueueEvent 返回 true 表示入队成功，**不保证一定派发**：总线可能随后关闭，订阅者也可能退出。
- 队列满或已关闭返回 false，拒绝最新消息；调用者自行决定重试、记录或放弃。
- ProcessQueuedEvents 只处理入口时已有消息，最多 maxEvents 条；处理中投递的消息留到下一次 ProcessQueuedEvents。默认预算 256，maxEvents 必须大于 0。
- 多个生产者只保证实际入队的顺序；业务需要的跨线程顺序由生产者协调。
- 监听集合以出队时为准。引用负载不复制；建议不可变事件，投递后不得修改。
- 每次成功入队创建一个类型化封装，队列扩容可能另有分配。没有消息池或排队零分配承诺。
- 关闭在相同锁下拒绝新投递并清空队列引用；不释放事件负载自身的 IDisposable。
- 单个慢监听仍会阻塞线程；预算限制消息数，不能抢占回调。耗时工作交给外部服务，完成后投递结果。

## 参数与错误语义

| 情况 | 结果 |
| --- | --- |
| 空错误入口、空回调、空引用或空 IEvent 消息 | ArgumentNullException |
| 非正队列容量或派发预算 | ArgumentOutOfRangeException |
| 直接 async void 回调、多播 Action | ArgumentException；多播必须拆成独立订阅 |
| 非所属线程操作、重入 Publish/ProcessQueuedEvents | InvalidOperationException |
| 关闭后 Publish/Subscribe/ProcessQueuedEvents | ObjectDisposedException |
| 关闭后 TryQueueEvent / 重复 Dispose | false / 安全返回 |
| 关闭后 GetDiagnosticsSnapshot | 可读最终总计；通道明细已清空 |

默认监听异常送入必填 reportError，包含原始异常、事件类型、订阅 ID、标签，其余监听继续执行，不向发布者重抛。严格模式在当前事件结束后抛 AggregateException；ProcessQueuedEvents 在整批完成后汇总抛出。

报告入口自身抛错时不递归报告：保留原始监听错误和报告错误，继续通知，当前轮结束汇总抛出，即使未开启严格模式也一样。ErrorCount 统计监听错误次数，不将报告失败重复计数。回调内部发生的 API 误用会在调用点抛出，然后遵循同样的监听错误策略；它不会因此静默消失。

订阅通过 AsyncStateMachineAttribute 检查直接绑定方法，包括多播成员。普通同步方法内部间接启动的任务无法检测，任务异常、取消和资源清理由调用者负责。错误报告器也应同步完成，不使用 async void。

## 生命周期与使用方式

Lifetime 拥有所加入的资源，所有调用限创建线程：

- 释放前先标记关闭，按添加顺序的逆序清理；单项失败不妨碍其他资源，最后汇总抛出。
- 同一引用实例只登记一次；幂等 Dispose；关闭后 Add 立即释放，释放失败直接反馈给 Add 调用方。
- 清理过程中 Add 同样立即释放，重复添加自身不会递归释放。资源释放失败后不自动重试。
- 身份去重使用弱键历史记录，已登记未清理的资源仍由列表强持有。这不是弱引用订阅。
- 值类型 IDisposable 的复制不代表同一引用实例，优先使用引用类型资源或始终复用同一装箱对象。
- 借用资源不要 Add；例如订阅者只加入订阅句柄，不加入借来的总线。

下方 HealthChanged 使用 [[Guides/Events/角色事件通知]] 中实现 IEvent 的不可变业务事件定义。

```csharp
using GameSysLab.Core.Events;
using GameSysLab.Core.Lifecycle;

using (var bus = new EventBus(error => System.Console.Error.WriteLine(error.Exception)))
using (var subscriptions = new Lifetime())
{
    subscriptions.Add(bus.Subscribe<HealthChanged>(
        value => System.Console.WriteLine(value.CurrentHealth), priority: 10, label: "显示血量"));
    bus.Publish(new HealthChanged(1, 80, 100));
    bool accepted = bus.TryQueueEvent(new HealthChanged(1, 60, 100));
    if (accepted) bus.ProcessQueuedEvents();
}
```

Unity 的 EventBusHost 不提供静态 Instance。装配入口显式调用 Initialize 创建；Update 调用 ProcessQueuedEvents；Shutdown 关闭，OnDestroy 兜底。重复 Initialize 返回同一实例；关闭后不能重新初始化。未初始化或已关闭时 Update 不派发。禁用组件仅停止自动派发，队列仍有容量限制，显式 Publish 仍可同步通知。

UI 组件未获得总线时不开始订阅；获得依赖且启用后建立自己的 Lifetime，在 OnDisable 清理。下次启用创建新集合，永久退出时清空借用引用。拥有者使用 Host.Shutdown，不直接 Dispose 借用的 Host.Bus。具体接法见 [[Guides/Lifecycle/界面关闭时清理订阅]]。

## 诊断

GetDiagnosticsSnapshot 主动生成独立只读快照：

- 总派发次数（含无监听事件和队列派发）、成功入队数、拒绝数、监听错误数、当前队列长度与关闭状态。
- 已建立通道的类型、派发次数和活动订阅列表；通道从首次订阅建立，之前无监听的派发只计总量。
- 订阅 ID、标签、优先级、执行次数；可选累积 Stopwatch ticks，换算秒需除以 Stopwatch.Frequency。
- 时间采样关闭时 ticks 为 0；开启后不包含错误报告器耗时。快照可含当前事件内待生效的新订阅。

正常路径不格式化日志，不保存事件负载或订阅者供诊断。诊断快照允许分配，不建议每帧读取，不用于驱动业务。报告回调不能重入派发；没有诊断回调、堆栈捕获或 EditorWindow。

## 验证与性能证据

2026-09-26，Windows、Unity 6000.6.0f1 编辑器 Mono，URP 17.6.0，Test Framework 1.8.0。运行时与 Unity 接入程序集编译通过。

- EditMode 31/31：类型隔离、优先级/稳定顺序、重复订阅、派发增删、关闭、严格/默认错误、报告失败与恢复、重入、FIFO、容量/预算、后台并发与关闭竞争、线程检查、非法参数/委托、诊断快照；Lifetime 逆序、幂等、失败、去重、晚到和清理中新增。
- PlayMode 5/5：订阅者禁用/启用/销毁、Host 暂停/恢复、销毁后旧后台投递失败、独立场景卸载再创建、监听故障日志与继续通知。
- 性能测试固定 10,000 次；稳定一个 int 监听，回调仅增加计数，不抛错、不分配，100 次同步预热。关闭监听计时采样，不读取诊断快照。
- 使用 Unity ProfilerRecorder 的 GC.Alloc，限制当前线程；已知 new byte[1024] 对照必须记录到分配才接受结果。记录分配**次数**，不把标记耗时冒充字节数。
- Stopwatch 测时包含记录器开启时的开销，单次编辑器样本不是性能基准保证。订阅变动复用同一委托；集中清理预先建好 10,000 个句柄；队列含首次扩容和一次 ProcessQueuedEvents。

最终回归的单次实测（Stopwatch.Frequency = 10,000,000）：

| 负载 | 次数 | 托管分配次数 | 总耗时 |
| --- | ---: | ---: | ---: |
| 稳定监听同步发布 | 10,000 | 0 | 0.284 ms |
| 订阅后立即退订，另保留一个常驻监听 | 10,000 | 20,003 | 4.593 ms |
| 集中释放预建句柄，保留常驻监听 | 10,000 | 0 | 0.451 ms |
| 入队并派发，含首次扩容 | 10,000 | 10,013 | 1.695 ms |

已知分配对照记录到 1 次分配。此表只报告实际次数与耗时，不报告未经测得的字节数。性能测试只对稳定同步发布断言 0 分配，其他负载不设置耗时门槛。

> [!warning] 已纠正的测量方法
> 本机 GC.GetAllocatedBytesForCurrentThread 对明显发生分配的操作也返回 0，旧结果作废。零分配结论以带对照的 GC.Alloc 分配次数测试为依据。Unity 官方示例也使用该记录器的 Count 统计分配：[ProfilerRecorderOptions.SumAllSamplesInFrame](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame.html)。

复现：Unity Test Runner 的 EditMode 选择 GameSysLab.Core 两个测试程序集；PlayMode 选择 GameSysLab.Demos.CoreLifecycle.PlayModeTests。安装当前工程对应 Unity CLI 后也可执行：

```powershell
unity command run_tests --project-path ./SysLab -- --mode editor --filter GameSysLab.Core --filter_type assembly
unity command run_tests --project-path ./SysLab -- --mode playmode --filter GameSysLab.Demos.CoreLifecycle.PlayModeTests --filter_type assembly --async_tests true
unity command test_status --project-path ./SysLab
```

PlayMode 因域重载需要异步启动，轮询 test_status 获取完成结果。性能原始输出自动写入 SysLab/TestResults/event-benchmark.txt；本次测试 JSON、截图在 LocalData/EventBusV2，均仅本地保留。

## 相对旧教学版的变化

旧版无参构造改为必传 reportError；默认异常从“汇总抛出”改为“报告并继续”，严格模式可恢复汇总抛出的行为。嵌套发布深度 32 改为禁止重入，事件链改用 TryQueueEvent。新增优先级、创建线程检查、空消息/直接 async void/多播拒绝、有界队列、Lifetime 和诊断。原先每次发布的快照与线性查找退订已移除。

## 尚未覆盖的能力

未实现异步监听、事件重放、弱引用订阅、取消传播、队列重试、消息池、全局单例、作用域树、自动注册、依赖容器或加载框架。未验证 IL2CPP/AOT、非 Windows 平台、Domain Reload 关闭组合、长期生产压力和设备性能。异步方法属性检测在代码裁剪/AOT 平台应单独验证。

下一步可独立研究 Timer，通过外部 Advance(deltaTime) 推进；不必依赖 EventBus。

## 关联

[[Architecture/001_ModularArchitecture]]

## 场景接入增量（2026-09-27，已实现）

已验证全局服务存活时场景订阅清理、初始化失败与后台晚到结果。跨关卡设计原则见 [[Architecture/002_游戏与关卡的服务归属]]；当前证据不包含任意后台任务、资源加载器、IL2CPP 或其他设备。

同条件核心性能回归：10,000 次同步发布 0 次分配 / 0.357 ms；订阅退订 20,003 次分配 / 5.549 ms；集中清理 0 次分配 / 0.442 ms；入队及派发 10,013 次分配 / 1.673 ms。已知分配对照记录 1 次。此为新的单次编辑器样本，不替换上方 2026-09-26 的历史数据，也不作为耗时保证。

## 脚本查询与接入文档（2026-09-27）

逐类成员、参数和返回值见 [[Reference/脚本索引]]；实际开发接入见 [[Guides/Events/角色事件通知]] 与 [[Guides/Events/Unity中接入事件模块]]。本次为文档补充，没有修改运行时代码或重跑 Unity 测试。
方法参考表同时说明方法的作用与调用时机，便于区分“为什么调用”和参数、返回值、失败边界。

## 游戏用法文档与命名回归（2026-09-27）

Reference 调整为 Events、Lifecycle 两个模块目录；使用指南按角色受伤通知血条、Unity 接入和界面关闭退订组织。笔记只记录模块内容，演示操作资料已移到库外的 Docs/Demos。游戏与关卡的所有权原则见 [[Architecture/002_游戏与关卡的服务归属]]。

公开方法 TryPost、Drain、GetDiagnostics 分别改为 TryQueueEvent、ProcessQueuedEvents、GetDiagnosticsSnapshot。无旧名兼容别名，行为和默认值不变；仓库内运行时、Unity 接入、使用者及测试调用同步迁移。

本轮 Unity 编译通过；31/31 EditMode、23/23 PlayMode 通过，0 失败、0 跳过。结果保存于 `LocalData/DocumentationRefactor/editmode-results.json` 和 `playmode-results.json`。验证环境仍是当前 Windows Unity 编辑器，不新增 Player、IL2CPP 或目标设备结论。

同次 10,000 次负载记录：稳定同步发布 0 次分配 / 0.3466 ms；订阅变动 20,003 次分配 / 4.8142 ms；集中清理 0 次分配 / 0.3489 ms；入队及处理 10,013 次分配 / 1.6701 ms；已知分配对照 1 次。条件沿用上文，不作耗时保证。

## IEvent 标记约束（2026-09-27）

在 EventBusContracts.cs 增加空 IEvent；Subscribe、Publish、TryQueueEvent 及内部泛型通道/排队封装要求 T : IEvent。已有事件保持 readonly struct；不可变类同样允许。接口只标记用途，不自动保证不可变性，不增加注册、反射派发或继承广播；Subscribe<IEvent> 只匹配以 IEvent 为泛型参数发布的消息。Nullable<T> 不实现此接口，不能作为事件类型。

这是源代码约束变化，旧的 int/string 直接发布需改为业务事件类型。本轮仅增加标记与类型约束，队列仍只处理调用开始时已有的消息，也未增加 QueueEvent 抛错入口。

Unity 编译通过，32/32 EditMode、23/23 PlayMode 通过，0 失败、0 跳过。新增泛型约束检查，扩展接口与具体事件的精确匹配检查；不可变类的引用保持和已有线程、生命周期测试继续通过。结果保存在 LocalData/EventMarker。

10,000 次不可变 NumberEvent 同步发布仍为 0 次分配 / 0.4243 ms；已知分配对照 1 次。订阅变动 20,003 次 / 4.8432 ms，集中清理 0 次 / 0.3021 ms，入队并处理 10,013 次 / 1.7875 ms。单次 Windows 编辑器数据，不代表其他平台或耗时保证。
