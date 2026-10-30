# Timer 演示

在 Unity 打开 `Assets/_Lab/Demos/002_Timer/Timer.unity`，进入运行模式。场景已加入构建列表末尾供场景回归使用，没有更改原启动入口。

- 角色默认等待五秒复活，可暂停、恢复、重置或取消。完成/取消后点击“重新创建”建立新任务。
- 暂停游戏时间：复活和周期刷新停止，菜单提示继续倒计时。
- 显示提示后，三秒未缩放时间自动关闭；提前关闭会释放本次提示的任务集合。
- 禁用游戏 Host：任务保留，重新启用不补时间。
- 退出时关闭两个 Host、取消自有任务并恢复进入前的 timeScale。

TimerDemo 只负责演示装配与界面，不是模块公共 API。实际接入见 [Timer 指南](../../Notes/SysNotes/Guides/Timer/游戏中的延迟与倒计时.md)。PlayMode 测试还验证真实场景卸载、重进和旧任务取消；测试入口为 GameSysLab.Modules.Timer.PlayModeTests。
