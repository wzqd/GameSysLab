---
title: Logger
tags: [diagnostics, reference]
---
# Logger

源码：`SysLab/Assets/_Lab/Modules/Diagnostics/Runtime/Logger.cs`。公共 sealed 普通类；命名空间、程序集均为 GameSysLab.Modules.Diagnostics。程序集无 Unity、Core 或其他模块引用，不挂载。

## 职责与所有权

创建者保存入口；入口借用 ILogSink，不关闭输出器。没有全局实例或队列，不保留已写记录；仅持有最近一次输出错误，可主动清除。所有操作和查询限定创建线程。同步输出可能阻塞调用线程，不做后台调度。

## 成员

| 成员 | 类型 / 默认值 / 可写性 | 作用与生效时机 |
| --- | --- | --- |
| Category | string，只读 | 构造时设置的模块名 |
| MinimumLevel | LogLevel / Info，可写 | 每次调用时过滤；None 全关，修改立即生效 |
| FailureCount | long / 0，只读 | 输出器、消息函数失败或拒绝递归的累计次数，上限饱和 |
| LastFailure | Exception / null，只读 | 最近一次输出失败，不替代 LogEntry 中的业务异常 |

## 方法

| 签名 | 作用与调用时机 | 参数与默认值 | 返回值 | 异常与生命周期 |
| --- | --- | --- | --- | --- |
| Logger(category, sink, minimumLevel=Info) | 装配入口或使用者创建 | 非空白 string；非空 ILogSink；Info/Warning/Error/None | 新入口 | 模块名 ArgumentException；空 sink ArgumentNullException；非法级别 ArgumentOutOfRangeException |
| IsEnabled(level) | 调用方需要在计算前判断时使用 | Info/Warning/Error | bool | 非法级别或 None 拒绝 |
| Info(message) | 普通事实记录 | string | bool | 转发 Write |
| Warning(message) | 可恢复问题记录 | string | bool | 转发 Write |
| Error(message, exception=null) | 失败记录，保留业务异常 | string、可选 Exception | bool | 转发 Write |
| Write(level, message, exception=null) | 需要动态指定级别时 | 有效记录级别；string；可选异常 | 输出器返回则 true，过滤/输出失败/递归则 false | 先验证级别、再过滤；启用时空消息 ArgumentNullException；输出异常记录但不传播 |
| WriteLazy(level, messageFactory, exception=null) | 消息构造昂贵时 | Func<string>；可选异常 | 同上 | 过滤时不调用工厂；启用时空工厂 ArgumentNullException；工厂抛错或返回 null 记为失败 |
| ClearFailures() | 调用方检查完故障后清除引用 | 无 | void | 不改变配置；重复调用安全 |

错误线程一律 InvalidOperationException。输出器和工厂递归调用同一入口时，内层 false 并记录失败；外层输出器若正常返回，外层仍 true。不同入口独立；自定义输出器不应建立跨入口递归环。成功输出不自动清除历史故障。

内部 BeginWrite 控制递归，RecordFailure 保存有界错误状态，CheckThread 检查线程，ValidateLevel 区分过滤阈值与记录级别。普通方法不会格式化输出，格式由 sink 决定。字符串插值和委托创建发生在调用者处。

[[Guides/Diagnostics/游戏中的日志记录]] · [[Experiments/004_Diagnostics]]
