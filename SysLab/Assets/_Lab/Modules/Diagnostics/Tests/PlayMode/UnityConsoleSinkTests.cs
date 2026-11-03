using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GameSysLab.Modules.Diagnostics;
using GameSysLab.Modules.Diagnostics.Unity;
using Logger = GameSysLab.Modules.Diagnostics.Logger;

public class UnityConsoleSinkTests
{
    [UnityTest] public IEnumerator SeverityAndExceptionAppearInConsole()
    {
        var log = new Logger("诊断测试", new UnityConsoleSink());
        LogAssert.Expect(LogType.Log, "[诊断测试] 完成"); Assert.IsTrue(log.Info("完成"));
        LogAssert.Expect(LogType.Warning, "[诊断测试] 回退"); Assert.IsTrue(log.Warning("回退"));
        LogAssert.Expect(LogType.Error, new Regex(@"\[诊断测试\] 失败\nSystem.InvalidOperationException: 原始异常"));
        Assert.IsTrue(log.Error("失败", new InvalidOperationException("原始异常")));
        log.MinimumLevel = LogLevel.None; Assert.IsFalse(log.Error("不输出"));
        yield return null; LogAssert.NoUnexpectedReceived();
    }
    [Test] public void DefaultEntryIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new UnityConsoleSink().Write(default));
    }
}
