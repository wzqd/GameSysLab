using System;
using System.Threading;
using UnityEngine;

namespace GameSysLab.Modules.Diagnostics.Unity
{
    /// <summary>同步输出到 Unity Console；请在 Unity 主线程创建并使用。</summary>
    public sealed class UnityConsoleSink : ILogSink
    {
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        public void Write(LogEntry entry)
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("Unity 输出器只能在创建线程使用。");
            if (entry.Level < LogLevel.Info || entry.Level > LogLevel.Error || entry.Category == null || entry.Message == null)
                throw new ArgumentException("日志记录无效。", nameof(entry));
            var text = $"[{entry.Category}] {entry.Message}";
            // 保留原始异常类型、消息和堆栈，不替换或重新抛出业务异常。
            if (entry.Exception != null) text += "\n" + entry.Exception;
            switch (entry.Level)
            {
                case LogLevel.Info: Debug.Log(text); break;
                case LogLevel.Warning: Debug.LogWarning(text); break;
                case LogLevel.Error: Debug.LogError(text); break;
            }
        }
    }
}
