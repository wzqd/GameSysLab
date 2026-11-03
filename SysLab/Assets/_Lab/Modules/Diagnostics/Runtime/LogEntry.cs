using System;

namespace GameSysLab.Modules.Diagnostics
{
    /// <summary>最低输出级别；None 关闭输出，不作为记录级别使用。</summary>
    public enum LogLevel { Info, Warning, Error, None }

    /// <summary>一次同步输出的只读记录；异常对象保留原始引用。</summary>
    public readonly struct LogEntry
    {
        public DateTimeOffset Timestamp { get; }
        public LogLevel Level { get; }
        public string Category { get; }
        public string Message { get; }
        public Exception Exception { get; }

        public LogEntry(DateTimeOffset timestamp, LogLevel level, string category, string message, Exception exception = null)
        {
            if (level < LogLevel.Info || level > LogLevel.Error) throw new ArgumentOutOfRangeException(nameof(level));
            if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("模块名不能为空。", nameof(category));
            Timestamp = timestamp; Level = level; Category = category;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Exception = exception;
        }
    }

    /// <summary>输出替换边界；同步执行，不拥有调用方资源，不通过日志递归报告自身错误。</summary>
    public interface ILogSink
    {
        void Write(LogEntry entry);
    }
}
