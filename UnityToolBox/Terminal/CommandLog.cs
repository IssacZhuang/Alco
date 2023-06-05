using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;

namespace UnityToolBox
{
    public enum TerminalLogType
    {
        Error = LogType.Error,
        Assert = LogType.Assert,
        Warning = LogType.Warning,
        Message = LogType.Log,
        Exception = LogType.Exception,
        Input,
        ShellMessage,
        MessageGreen,
        MessageRed,
        MessageYellow,
        MessageBlue,
        MessageGray,
    }

    public struct LogItem
    {
        public TerminalLogType type;
        public string message;
        public string stackTrace;
    }

    public class CommandLog
    {
        private ConcurrentQueue<LogItem> _logs = new ConcurrentQueue<LogItem>();
        private int _maxItems;

        public IEnumerable<LogItem> Logs
        {
            get { return _logs; }
        }

        public CommandLog(int maxItems)
        {
            this._maxItems = maxItems;
        }

        public void HandleLog(string message, TerminalLogType type)
        {
            HandleLog(message, "", type);
        }

        public void HandleLog(string message, string stackTrace, TerminalLogType type)
        {
            LogItem log = new LogItem()
            {
                message = message,
                stackTrace = stackTrace,
                type = type
            };

            _logs.Enqueue(log);

            if (_logs.Count > _maxItems)
            {
                _logs.TryDequeue(out LogItem _);
            }
        }

        public void Clear()
        {
            int length = _logs.Count;
            for (int i = 0; i < length; i++)
            {
                _logs.TryDequeue(out LogItem _);
            }
        }
    }
}
