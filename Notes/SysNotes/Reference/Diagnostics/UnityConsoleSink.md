---
title: UnityConsoleSink
tags: [diagnostics, reference]
---
# UnityConsoleSink

源码：`SysLab/Assets/_Lab/Modules/Diagnostics/Unity/UnityConsoleSink.cs`。公共 sealed 普通类，命名空间及程序集 GameSysLab.Modules.Diagnostics.Unity。程序集仅引用 Diagnostics Runtime 和 Unity，不依赖其他项目模块。

## 职责与所有权

在 Unity 主线程创建和使用，借给 Logger。没有静态入口，不绑定 GameObject，不持有场景引用，不需要挂载或释放。无 Inspector 参数、自动初始化、Update 或 Unity 日志回调订阅。

## 成员与方法

内部 threadId 记录创建线程。

| 方法 | 作用与调用时机 | 参数 | 返回值 | 失败与生命周期 |
| --- | --- | --- | --- | --- |
| UnityConsoleSink() | 装配入口选择 Console 输出时创建 | 无 | 新输出器 | 必须在 Unity 主线程构造 |
| Write(entry) | Logger 同步交付后输出 | 有效 LogEntry | void | 错误线程 InvalidOperationException；无效记录 ArgumentException；Unity 输出错误由调用方 Logger 处理 |

Info 映射 Debug.Log，Warning 映射 Debug.LogWarning，Error 映射 Debug.LogError。文本为 `[Category] Message`；异常存在时附加换行和 Exception.ToString()，保留异常类型和原有堆栈。不额外输出第二条异常，也不重新抛出业务异常。Console 自身控制显示和过滤；此处不输出独立时间字段，原始记录仍包含 Timestamp。

若直接调用 Write，异常不会被 Logger 隔离；推荐通过 Logger 使用。线程检查只验证创建线程，不自动发现 Unity 主线程；调用方必须遵守主线程创建约定。

[[Guides/Diagnostics/游戏中的日志记录]] · [[Experiments/004_Diagnostics]]
