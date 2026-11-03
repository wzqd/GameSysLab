---
title: LogEntry 与日志输出契约
tags: [diagnostics, reference]
---
# LogEntry、LogLevel 与 ILogSink

源码：`SysLab/Assets/_Lab/Modules/Diagnostics/Runtime/LogEntry.cs`。命名空间、程序集 GameSysLab.Modules.Diagnostics；均公共类型，无 Unity 依赖，不挂载。

## 职责与成员

LogLevel 包含 Info、Warning、Error，按严重程度升序；None 只用于关闭 Logger 输出。

LogEntry 为 readonly struct，成员均只读：Timestamp（DateTimeOffset）、Level（LogLevel）、Category（string）、Message（string）、Exception（可空 Exception）。Logger 使用 UTC 当前时间，时间用于记录，不保证单调顺序。异常保留引用，记录不是深拷贝。输出器若保存记录，负责其保存期限。

## 方法与契约

| 方法 | 作用与调用时机 | 参数与默认值 | 返回值 | 失败语义 |
| --- | --- | --- | --- | --- |
| LogEntry(timestamp, level, category, message, exception=null) | Logger 或自定义调用方准备输出记录 | 时间、有效记录级别、非空白模块名、非 null 消息、可选异常 | 值类型记录 | 非法级别 ArgumentOutOfRangeException；模块名 ArgumentException；空消息 ArgumentNullException |
| ILogSink.Write(entry) | Logger 过滤后同步交付 | LogEntry，无默认值 | void | 实现可抛输出异常，由 Logger 记录；直接调用者自行处理 |

默认结构体不是有效输出记录。记录本身无线程检查；Logger 与具体 sink 定义调用线程。ILogSink 只承担输出，不自动拥有业务资源、不负责重试，也不要求实现 IDisposable。实现不可通过同一入口递归记录自己的错误。

[[Reference/Diagnostics/Logger]] · [[Reference/Diagnostics/UnityConsoleSink]]
