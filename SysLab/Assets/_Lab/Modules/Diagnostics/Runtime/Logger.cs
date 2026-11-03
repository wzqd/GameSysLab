using System;
using System.Threading;

namespace GameSysLab.Modules.Diagnostics
{
    /// <summary>独立日志入口；借用输出器，不缓存日志，不管理业务生命周期。</summary>
    public sealed class Logger
    {
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly string category;
        private readonly ILogSink sink;
        private LogLevel minimumLevel;
        private bool writing;
        private long failureCount;
        private Exception lastFailure;

        public string Category { get { CheckThread(); return category; } }
        public long FailureCount { get { CheckThread(); return failureCount; } }
        public Exception LastFailure { get { CheckThread(); return lastFailure; } }
        public LogLevel MinimumLevel
        {
            get { CheckThread(); return minimumLevel; }
            set { CheckThread(); ValidateLevel(value, true); minimumLevel = value; }
        }

        public Logger(string category, ILogSink sink, LogLevel minimumLevel = LogLevel.Info)
        {
            if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("模块名不能为空。", nameof(category));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            ValidateLevel(minimumLevel, true);
            this.category = category; this.minimumLevel = minimumLevel;
        }

        public bool IsEnabled(LogLevel level)
        {
            CheckThread(); ValidateLevel(level, false);
            return level >= minimumLevel && minimumLevel != LogLevel.None;
        }

        public bool Info(string message) => Write(LogLevel.Info, message);
        public bool Warning(string message) => Write(LogLevel.Warning, message);
        public bool Error(string message, Exception exception = null) => Write(LogLevel.Error, message, exception);

        /// <summary>过滤后同步输出；false 表示被过滤或输出失败，不代表业务失败。</summary>
        public bool Write(LogLevel level, string message, Exception exception = null)
        {
            if (!IsEnabled(level)) return false;
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (!BeginWrite()) return false;
            try { sink.Write(new LogEntry(DateTimeOffset.UtcNow, level, category, message, exception)); return true; }
            catch (Exception error) { RecordFailure(error); return false; }
            finally { writing = false; }
        }

        /// <summary>仅在级别启用时构造消息；闭包与委托本身仍可能由调用方分配。</summary>
        public bool WriteLazy(LogLevel level, Func<string> messageFactory, Exception exception = null)
        {
            if (!IsEnabled(level)) return false;
            if (messageFactory == null) throw new ArgumentNullException(nameof(messageFactory));
            if (!BeginWrite()) return false;
            try
            {
                var message = messageFactory();
                if (message == null) throw new InvalidOperationException("消息工厂返回了 null。");
                sink.Write(new LogEntry(DateTimeOffset.UtcNow, level, category, message, exception));
                return true;
            }
            catch (Exception error) { RecordFailure(error); return false; }
            finally { writing = false; }
        }

        /// <summary>主动清除失败计数与最后一次异常引用；不会影响输出配置。</summary>
        public void ClearFailures() { CheckThread(); failureCount = 0; lastFailure = null; }

        private bool BeginWrite()
        {
            if (!writing) { writing = true; return true; }
            RecordFailure(new InvalidOperationException("同一日志入口禁止递归输出。"));
            return false;
        }
        private void RecordFailure(Exception error) { if (failureCount < long.MaxValue) failureCount++; lastFailure = error; }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId) throw new InvalidOperationException("日志入口只能在创建线程使用。");
        }
        private static void ValidateLevel(LogLevel level, bool allowNone)
        {
            if (level < LogLevel.Info || level > (allowNone ? LogLevel.None : LogLevel.Error))
                throw new ArgumentOutOfRangeException(nameof(level));
        }
    }
}
