# GameSysLab

一个研究游戏系统、状态与架构的 Unity 学习实验室。先建立小型 Core，再按需接入基础模块和玩法系统，通过独立 Demo、测试和笔记理解设计取舍。

## 打开项目

- Unity Hub：打开 `SysLab/`，使用 Unity **6000.6.0f1**。
- 渲染管线：URP **17.6.0**，实际版本以工程记录为准。
- Obsidian：打开 `Notes/SysNotes/`。

## 架构

**薄 Core + 可选模块 + 显式装配。**

- Core：类型事件工具与必要的生命周期清理基础。
- Modules：计时、对象池、UI、本地化等独立基础能力。
- Systems：背包、属性、Buff、技能等玩法规则。
- Integrations：系统之间的连接与数据转换。
- Demos：选择组件、创建依赖、展示行为。

每个 Demo 只创建自己需要的对象。普通领域对象不必依赖 Core，也不必继承统一基类。动态资源加载在出现具体需求后单独研究。

## 当前进度

已完成 Core 事件与生命周期模块：纯 C# EventBus、强类型通道、优先级、有界投递队列、线程检查、统一错误报告、Lifetime 和只读诊断；已补齐 GameRoot / SceneRoot 显式装配、场景切换与后台取消。已新增独立 Timer 模块，支持延迟、循环、暂停、恢复、重置、取消及两种 Unity 时间。Unity 6000.6.0f1 编译通过，全项目 55 项 EditMode 与 29 项 PlayMode 测试全部通过。

推荐在 Unity 打开 `Assets/_Lab/Demos/001_CoreLifecycle/Scenes/Bootstrap.unity` 并运行，自动进入 A。可切换/重进 A、B，发送全局或局部通知、启动后台生产、注入初始化/清理/卸载故障，检查订阅与退出行为。详细归属、API 和恢复边界见[演示运行说明](Docs/Demos/CoreLifecycle.md)。

原 `Assets/_Lab/Demos/001_CoreLifecycle/CoreLifecycle.unity` 仍可独立演示事件分发。SampleScene 与原构建入口顺序保持不变；新增场景已追加到构建场景列表供加载，尚未验证独立 Player 构建。

预热后，10,000 次稳定订阅的同步发布记录到 0 次托管分配，使用 Unity GC.Alloc 并带已知分配对照。订阅变化与排队仍会分配；完整条件、数据、旧 API 迁移及未验证平台见[实验笔记](Notes/SysNotes/Experiments/001_CoreLifecycle.md)。

当前 Events / Lifecycle / Timer 已完成，下一步为 Pooling；后续 UI → Localization → 小型集成。每阶段先完成最小版本，玩法系统后置。

- [笔记入口](Notes/SysNotes/README.md)：在 Obsidian 中阅读关联笔记。

## 仓库内容

提交 Unity 工程源文件、学习笔记及复现实验所需的小型数据和工具。Unity 缓存、构建结果、本机笔记配置、实际存档及本地素材不提交，具体规则见 [.gitignore](.gitignore)。

测试入口：Unity Test Runner → EditMode → `GameSysLab.Core.Events.Tests` / `GameSysLab.Core.Lifecycle.Tests`；PlayMode → `GameSysLab.Demos.CoreLifecycle.PlayModeTests`。性能结果写入本地 `SysLab/TestResults/event-benchmark.txt`。

## 使用边界

事件类型需实现空 IEvent，默认使用不可变数据；Subscribe、Publish、TryQueueEvent 通过泛型约束限制类型。创建 EventBus 时必须传入错误报告入口。同步 Publish 和所有生命周期操作限创建线程；后台仅调用 TryQueueEvent，队列满或关闭返回 false。同一总线禁止嵌套 Publish/ProcessQueuedEvents，事件链改用 TryQueueEvent。监听异常默认报告并继续，严格模式在本轮结束后汇总抛出。

订阅由订阅者自己的 Lifetime 管理，总线由创建者管理。EventBusHost 改为显式 `Initialize()` 创建、`Shutdown()` 关闭；Awake 不再创建总线。禁用只暂停自动派发，销毁兜底关闭。

GameRoot 创建全局服务，SceneRoot 创建场景模块；普通模块通过构造函数、组件通过 Configure 获得依赖。组件提前启用时等待配置，不查找全局对象。场景退出清理其对全局服务的订阅，关闭局部总线并取消后台生产。不提供静态 Instance、自动注册、异步监听或服务查找。

开发文档入口：[脚本成员索引](Notes/SysNotes/Reference/脚本索引.md)、[Core 使用指南](Notes/SysNotes/Guides/Events/角色事件通知.md)、[Unity 挂载与场景装配](Notes/SysNotes/Guides/Events/Unity中接入事件模块.md)。后续模块沿用笔记库中的脚本说明和模块用法模板。

Timer 使用：[延迟复活与倒计时](Notes/SysNotes/Guides/Timer/游戏中的延迟与倒计时.md)、[验证与性能](Notes/SysNotes/Experiments/002_Timer.md)。独立场景为 Assets/_Lab/Demos/002_Timer/Timer.unity，演示操作见 [Timer 演示](Docs/Demos/Timer.md)。Runtime 不依赖 Unity、EventBus 或 Lifetime。
