# GameSysLab

一个研究游戏系统、状态与架构的 Unity 学习实验室。先建立小型 Core，再按需接入基础模块和玩法系统，通过独立 Demo、测试和笔记理解设计取舍。

## 打开项目

- Unity Hub：打开 `SysLab/`，使用 Unity **6000.6.0f1**。
- 渲染管线：URP **17.6.0**，实际版本以工程记录为准。
- Obsidian：打开 `Notes/SysNotes/`。

## 架构

**薄 Core + 可选模块 + 显式装配。**

- Core：启动与必要的生命周期基础。
- Modules：存档、对象池等可选能力。
- Systems：背包、属性、Buff、技能等玩法规则。
- Integrations：系统之间的连接与数据转换。
- Demos：选择组件、创建依赖、展示行为。

每个 Demo 只创建自己需要的对象。普通领域对象不必依赖 Core，也不必继承统一基类。动态资源加载在出现具体需求后单独研究。

## 当前进度

已建立 Unity 基础工程、项目方案与笔记入口。Core 和系统实验尚未实现，现有 SampleScene 为工程模板场景。

计划顺序：CoreLifecycle → Inventory → SaveLoad → InventorySave → StatsBuff → Ability。

- [笔记入口](Notes/SysNotes/README.md)：在 Obsidian 中阅读关联笔记。

## 仓库内容

提交 Unity 工程源文件、学习笔记及复现实验所需的小型数据和工具。Unity 缓存、构建结果、本机笔记配置、实际存档及本地素材不提交，具体规则见 [.gitignore](.gitignore)。

当前没有已实现的系统测试；后续测试入口为 Unity Test Runner，按 EditMode 与 PlayMode 区分。
