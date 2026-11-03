# GameSysLab

一个研究游戏系统架构的 Unity 实验室。模块按需使用，通过显式传参组合，不依赖全局服务入口。

**先读 [项目模块导览](Docs/项目模块导览.md)**，了解各模块做什么、用在哪里，以及它们之间的关系。

## 打开项目

- Unity Hub 打开 `SysLab/`，使用工程记录的 **Unity 6000.6.0f1**。
- URP **17.6.0**；保持工程已有包版本。
- Obsidian 打开 `Notes/SysNotes/`，也可以直接在 GitHub 阅读 Markdown。

## 已有模块

| 模块 | 用途 | 使用指南 |
| --- | --- | --- |
| 事件通知 | 将已经发生的变化通知其他对象 | [角色事件通知](Notes/SysNotes/Guides/Events/角色事件通知.md) |
| 生命周期 | 对象退出时清理订阅与拥有的资源 | [界面关闭时清理订阅](Notes/SysNotes/Guides/Lifecycle/界面关闭时清理订阅.md) |
| 计时器 | 延迟、循环、暂停、恢复与取消 | [游戏中的延迟与倒计时](Notes/SysNotes/Guides/Timer/游戏中的延迟与倒计时.md) |
| 对象池 | 借出、归还、重置与复用实例（初版） | [游戏中的实例复用](Notes/SysNotes/Guides/Pooling/游戏中的实例复用.md) |
| 日志与诊断 | 分级记录、过滤与可替换输出 | [游戏中的日志记录](Notes/SysNotes/Guides/Diagnostics/游戏中的日志记录.md) |

Timer、Pooling、Diagnostics 的纯 C# 核心各自独立。日志不会要求其他模块反向依赖它。Pooling 的完整故障矩阵、性能与场景重进验收尚待补齐。

## 代码怎么组织

- **Core**：事件与生命周期等少量公共基础。
- **Modules**：可选的独立能力，如计时器、对象池、日志。
- **Systems**：按需建立的玩法规则，如背包、技能、任务。
- **Integrations**：按需建立的跨模块协作。
- **Demos**：创建依赖并展示行为的独立场景。

普通对象不用继承统一基类。创建者负责资源归属与清理；组件通过明确引用或配置方法取得依赖。Core 不引用具体模块，玩法不反向引用表现层。

## 运行演示

在 Unity 打开对应场景后运行；详细操作见链接。

| 演示 | 场景（相对 SysLab） | 说明 |
| --- | --- | --- |
| 事件与场景生命周期 | `Assets/_Lab/Demos/001_CoreLifecycle/Scenes/Bootstrap.unity` | [操作说明](Docs/Demos/CoreLifecycle.md) |
| 计时器 | `Assets/_Lab/Demos/002_Timer/Timer.unity` | [操作说明](Docs/Demos/Timer.md) |
| 对象池 | `Assets/_Lab/Demos/003_Pooling/Pooling.unity` | [操作说明](Docs/Demos/Pooling.md) |
| 日志与诊断 | `Assets/_Lab/Demos/004_Diagnostics/Diagnostics.unity` | [操作说明](Docs/Demos/Diagnostics.md) |

## 按需查阅

- [笔记入口](Notes/SysNotes/README.md)：模块用法与设计说明。
- [脚本参考](Notes/SysNotes/Reference/脚本索引.md)：具体方法、参数和限制。
- [日志能力与验证边界](Notes/SysNotes/Experiments/004_Diagnostics.md)：最近回归及未覆盖范围。

提交 Unity 源码、资源及 `.meta`，不提交缓存、本机配置、原始运行产物或附件。详见 [.gitignore](.gitignore)。未声称 Player、IL2CPP 或目标设备已验证。
