# CoreLifecycle 演示运行说明

这是演示工程的操作资料，放在 Obsidian 模块笔记之外。正式模块用法见 [模块索引](../../Notes/SysNotes/Reference/脚本索引.md)。

## 单场景事件演示

Unity 打开 `Assets/_Lab/Demos/001_CoreLifecycle/CoreLifecycle.unity`。运行后可同步发布、后台投递、启停订阅者、暂停 Host、注入监听错误、关闭并重建总线、刷新诊断。

CoreLifecycleDemo 创建 Host 和 DemoSubscriber；DemoProducer 在后台投递有限消息。它们都是演示脚本，不是业务模块接入时必须使用的类型。

## 切场景演示

打开 `Assets/_Lab/Demos/001_CoreLifecycle/Scenes/Bootstrap.unity`，自动进入 ContentA；可以切换或重进 A/B、发送局部和全局通知、观察后台取消。

GameRoot 的 Global Host 引用同场景 Host，Start Automatically 默认启用；ContentA/B 的 SceneRoot 引用各自 Host 和 DemoSubscriber。故障按钮可验证初始化回滚、清理失败和卸载重试。卸载故障后先重试清理，再切换。直接打开内容场景不会自动初始化。

GameRoot 只管理这套演示的场景策略，不是 Core 的全局服务入口。`TrySwitch(完整 Assets 路径)` 返回 true 代表接受请求，用 IsSwitching / Current / LastError 观察结果；Shutdown 发出取消与卸载请求，不代表异步工作已经结束。

构建列表仍保留 SampleScene 为原入口；若打包此演示，另将 Bootstrap 设为入口并包含内容场景。未验证 Player / IL2CPP。测试可在 Test Runner 中选择 `GameSysLab.Demos.CoreLifecycle.PlayModeTests`。
