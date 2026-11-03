using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using GameSysLab.Modules.Diagnostics;
using Logger = GameSysLab.Modules.Diagnostics.Logger;

namespace GameSysLab.Modules.Diagnostics.Tests
{
    public class LoggerTests
    {
        private sealed class Sink : ILogSink
        {
            public readonly List<LogEntry> Entries = new List<LogEntry>();
            public Action<LogEntry> OnWrite;
            public void Write(LogEntry entry) { OnWrite?.Invoke(entry); Entries.Add(entry); }
        }
        [TestCase(LogLevel.Info, 3)]
        [TestCase(LogLevel.Warning, 2)]
        [TestCase(LogLevel.Error, 1)]
        [TestCase(LogLevel.None, 0)]
        public void ThresholdFiltersBeforeOutput(LogLevel threshold, int count)
        {
            var sink = new Sink(); var log = new Logger("角色", sink, threshold);
            log.Info("出生"); log.Warning("缺少图标"); log.Error("创建失败");
            Assert.AreEqual(count, sink.Entries.Count); Assert.AreEqual(0, log.FailureCount);
        }
        [Test] public void StructuredEntryPreservesExceptionAndTimestamp()
        {
            var sink = new Sink(); var log = new Logger("背包", sink);
            var error = new Exception("原始错误"); var start = DateTimeOffset.UtcNow;
            Assert.IsTrue(log.Error("添加失败", error)); var entry = sink.Entries[0];
            Assert.AreEqual("背包", entry.Category); Assert.AreEqual("添加失败", entry.Message);
            Assert.AreEqual(LogLevel.Error, entry.Level); Assert.AreSame(error, entry.Exception);
            Assert.That(entry.Timestamp, Is.GreaterThanOrEqualTo(start).And.LessThanOrEqualTo(DateTimeOffset.UtcNow));
        }
        [Test] public void DisabledLazyMessageNeverExecutesAndCanBeEnabledLater()
        {
            var sink = new Sink(); var log = new Logger("任务", sink, LogLevel.None); int calls = 0;
            Func<string> build = () => { calls++; return "完成"; };
            Assert.IsFalse(log.WriteLazy(LogLevel.Info, build)); Assert.AreEqual(0, calls);
            log.MinimumLevel = LogLevel.Info;
            Assert.IsTrue(log.WriteLazy(LogLevel.Info, build)); Assert.AreEqual(1, calls);
        }
        [Test] public void SinkFailureDoesNotMaskBusinessErrorAndNextWriteRecovers()
        {
            var failure = new Exception("输出故障"); var business = new Exception("业务故障");
            var sink = new Sink { OnWrite = entry => { Assert.AreSame(business, entry.Exception); throw failure; } };
            var log = new Logger("窗口", sink);
            Assert.IsFalse(log.Error("打开失败", business)); Assert.AreSame(failure, log.LastFailure);
            Assert.AreEqual(1, log.FailureCount); sink.OnWrite = null;
            Assert.IsTrue(log.Info("继续运行")); log.ClearFailures();
            Assert.AreEqual(0, log.FailureCount); Assert.IsNull(log.LastFailure);
        }
        [Test] public void MessageFactoryFailureIsContained()
        {
            var sink = new Sink(); var log = new Logger("任务", sink);
            Assert.IsFalse(log.WriteLazy(LogLevel.Info, () => throw new Exception("构造失败")));
            Assert.IsFalse(log.WriteLazy(LogLevel.Info, () => null));
            Assert.AreEqual(2, log.FailureCount); Assert.IsEmpty(sink.Entries);
            Assert.IsTrue(log.Info("恢复"));
        }
        [Test] public void RecursiveOutputIsBoundedAndDoesNotDisableFutureWrites()
        {
            var sink = new Sink(); var log = new Logger("任务", sink);
            sink.OnWrite = entry => Assert.IsFalse(log.Warning("递归"));
            Assert.IsTrue(log.Info("外层")); Assert.AreEqual(1, sink.Entries.Count);
            Assert.AreEqual(1, log.FailureCount); sink.OnWrite = null;
            Assert.IsTrue(log.Info("下一条"));
        }
        [Test] public void LazyFactoryReentryIsAlsoBounded()
        {
            var sink = new Sink(); var log = new Logger("任务", sink);
            Assert.IsTrue(log.WriteLazy(LogLevel.Info, () => { Assert.IsFalse(log.Info("内层")); return "外层"; }));
            Assert.AreEqual(1, sink.Entries.Count); Assert.AreEqual(1, log.FailureCount);
        }
        [Test] public void IndependentLoggersCanShareOutputWithoutSharingFilters()
        {
            var sink = new Sink(); var a = new Logger("A", sink, LogLevel.Error); var b = new Logger("B", sink);
            Assert.IsFalse(a.Info("过滤")); Assert.IsTrue(b.Info("保留"));
            Assert.AreEqual("B", sink.Entries[0].Category);
        }
        [Test] public void WrongThreadCannotWriteOrChangeConfiguration()
        {
            var log = new Logger("主线程", new Sink()); Exception writeError = null; Exception configError = null;
            var thread = new Thread(() =>
            {
                try { log.Info("后台"); } catch (Exception error) { writeError = error; }
                try { log.MinimumLevel = LogLevel.None; } catch (Exception error) { configError = error; }
            });
            thread.Start(); Assert.IsTrue(thread.Join(3000));
            Assert.IsInstanceOf<InvalidOperationException>(writeError); Assert.IsInstanceOf<InvalidOperationException>(configError);
            Assert.IsTrue(log.Info("正常"));
        }
        [Test] public void InvalidConfigurationAndArgumentsFailClearly()
        {
            var sink = new Sink();
            Assert.Throws<ArgumentException>(() => new Logger(" ", sink));
            Assert.Throws<ArgumentNullException>(() => new Logger("A", null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Logger("A", sink, (LogLevel)9));
            var log = new Logger("A", sink);
            Assert.Throws<ArgumentOutOfRangeException>(() => log.MinimumLevel = (LogLevel)(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => log.IsEnabled(LogLevel.None));
            Assert.Throws<ArgumentNullException>(() => log.Info(null));
            Assert.Throws<ArgumentNullException>(() => log.WriteLazy(LogLevel.Info, null));
            Assert.Throws<ArgumentException>(() => new LogEntry(DateTimeOffset.UtcNow, LogLevel.Info, "", "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LogEntry(DateTimeOffset.UtcNow, LogLevel.None, "A", "x"));
            Assert.Throws<ArgumentNullException>(() => new LogEntry(DateTimeOffset.UtcNow, LogLevel.Info, "A", null));
            Assert.AreEqual(LogLevel.Info, log.MinimumLevel);
        }
        [Test] public void FilteredMessagesDoNotValidateUnusedPayload()
        {
            var log = new Logger("A", new Sink(), LogLevel.None);
            Assert.IsFalse(log.Info(null)); Assert.IsFalse(log.WriteLazy(LogLevel.Info, null));
        }
    }
}
