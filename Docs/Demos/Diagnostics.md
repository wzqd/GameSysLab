# 日志与诊断演示

打开 `Assets/_Lab/Demos/004_Diagnostics/Diagnostics.unity` 并运行。

- 选择 Info、Warning、Error 或 None，点击相应消息按钮，在 Console 查看模块名与严重程度。
- 选择 Warning 后点击延迟 Info 按钮，消息构造次数不增加；切回 Info 后会增加。
- 勾选输出器故障，再发消息：故障计数增加，按钮界面继续响应；取消勾选即可恢复。
- 错误按钮故意输出一条错误日志，带演示业务异常，不代表场景启动失败。Console 开启 Error Pause 时可能暂停，恢复播放即可。
- 清除故障记录只清 Logger 内部计数，不清 Unity Console。

演示只引用 Diagnostics，不引用其他实验模块。无额外资源引用或必填参数；启动时创建，禁用时放弃引用。场景追加构建列表，不修改原入口顺序。
