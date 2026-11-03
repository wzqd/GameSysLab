---
title: 独立日志与诊断
status: verified
stage: minimal
tags: [diagnostics, logging]
---
# 独立日志与诊断

## 问题与边界

让使用者容易记录普通消息、警告和异常，同时不绑定事件总线、计时器、对象池或生命周期框架。实现位于 Modules/Diagnostics；Runtime 的 asmdef 无项目引用并设置 noEngineReferences，Unity 输出独立程序集。既有模块没有新增 Diagnostics 依赖。

## 当前契约

Logger 固定模块名、借用 ILogSink；默认最低 Info，可改 Warning/Error/None。每条记录有 UTC 时间、级别、模块名、消息和可选原始异常。过滤发生在输出和延迟消息构造之前。Info/Warning/Error 为常用入口，Write 支持动态级别，WriteLazy 按需构造消息。

同步调用限定创建线程；UnityConsoleSink 要求主线程创建使用。普通空参数/非法级别/错误线程属于调用错误；sink 或消息工厂异常保存到 FailureCount/LastFailure，返回 false，不覆盖业务异常；工厂返回 null 同样失败。递归输出拒绝内层；不通过日志上报日志自身的失败。最近一次失败引用可 ClearFailures 清除。没有自动重试、缓存队列和资源所有权转移。

true 仅表示输出器同步返回，不表示第三方持久化；业务不得依赖日志返回值提交状态。Logger 不提供 Dispose，因为不拥有输出器资源。使用者需要文件输出时自行实现并持有 sink，本轮未实现文件输出。

## 验证记录（2026-11-03）

Windows Unity 6000.6.0f1 编译通过。全项目 EditMode 75/75、PlayMode 32/32 通过，0 失败、0 跳过；Diagnostics 新增 14 EditMode、2 PlayMode。

- EditMode：所有过滤级别、结构化记录与原异常、时间范围、延迟构造跳过与恢复、消息函数故障、输出故障恢复、递归保护、入口隔离、错误线程及参数验证。
- PlayMode：Console 对应级别、异常内容保留、关闭输出、默认结构体拒绝。
- 独立场景已运行，合成屏幕截图确认中文按钮可读、无裁切。截图使用 source=screen；普通 camera 截图不含 IMGUI，不能作为 UI 证据。已恢复原 Bootstrap 场景。

复现：`unity command run_tests --project-path ./SysLab -- --mode editor`；PlayMode 使用 `--mode playmode --async_tests true`，随后 `unity command test_status --project-path ./SysLab`。原始结果在 LocalData/Diagnostics/editmode-results.json、playmode-results.json，截图 demo-ui.png。

## 限制

未验证 Player、IL2CPP、其他平台或高频性能；不承诺零分配。未增加日志窗口、文件输出、远程上报、自动状态采集、操作关联追踪或跨线程调度。自定义 sink 必须避免跨 Logger 的递归环。后台使用者可以创建独立 Logger 配合适合后台的输出器，但不能跨线程借用当前入口。

[[Guides/Diagnostics/游戏中的日志记录]] · [[Reference/Diagnostics/Logger]] · [[Reference/Diagnostics/LogEntry]] · [[Reference/Diagnostics/UnityConsoleSink]]
