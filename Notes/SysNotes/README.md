---
title: 游戏系统笔记
tags: [gamesyslab, index]
---

# 游戏系统笔记

这里记录可复用模块的用法、接口与设计依据。阅读顺序：先用法，再按需要查接口和实验结果。

## 开始使用

- [[Guides/Events/角色事件通知|角色受伤后，怎样通知血条]]
- [[Guides/Events/Unity中接入事件模块|Unity 里挂什么、引用怎么传、参数怎么调]]
- [[Guides/Lifecycle/界面关闭时清理订阅|界面关闭时，如何停止监听]]
- [[Guides/Timer/游戏中的延迟与倒计时|角色延迟复活、提示关闭和暂停倒计时]]

## 查询与设计

- [[Reference/脚本索引|按模块查询脚本和方法]]
- [[Architecture/001_ModularArchitecture|Core 与可选模块]]
- [[Architecture/002_游戏与关卡的服务归属|哪些对象跨关卡保留，哪些离场就清理]]
- [[Experiments/001_CoreLifecycle|事件与生命周期的行为约定、测试和测量]]

当前可复用模块为 Events、Lifecycle、Timer；下一步为 Pooling。Timer 的契约和实际验证见 [[Experiments/002_Timer]]。笔记不提供演示脚本的成员说明或操作手册。

## 写作模板

[[Templates/ScriptReferenceTemplate|脚本说明]] · [[Templates/ModuleUsageTemplate|使用指南]] · [[Templates/ExperimentTemplate|实验记录]]
