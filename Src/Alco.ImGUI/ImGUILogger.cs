
using System.Collections.Concurrent;
using System.Numerics;

namespace Alco.ImGUI;

public class ImGUILogger : ILogger
{
    [Flags]
    public enum LogType
    {
        Info = 1 << 0,
        Warning = 1 << 1,
        Error = 1 << 2,
        Success = 1 << 3,
        All = Info | Warning | Error | Success
    }

    private struct LogInfo
    {
        public LogType Type;
        public string Message;
        public int Id; // 添加唯一标识符

        public LogInfo(LogType type, string message, int id)
        {
            Type = type;
            Message = message.ToString();
            Id = id;
        }
    }

    //also log to console
    private readonly ConsoleLogger _consoleLogger = new ConsoleLogger();
    private readonly ConcurrentQueue<LogInfo> _logQueue = new ConcurrentQueue<LogInfo>();

    private readonly SpanStringBuilder _stringBuilder = new SpanStringBuilder();

    private LogType _filter = LogType.All;
    private const int MaxLogEntries = 1000;


    private int _selectedLogId = -1;
    private int _logIdCounter = 0;
    private double _lastClickTime = 0;
    private int _lastClickedLogId = -1;
    private const double DoubleClickThreshold = 0.5; 

    public LogType Filter
    {
        get => _filter;
        set
        {
            _filter = value;
        }
    }

    public event Action<string>? OnLogDoubleClick;

    public bool IsOpen = true;

    public void Error(ReadOnlySpan<char> message)
    {
        EnqueueLog(LogType.Error, message.ToString());
        _consoleLogger.Error(message);
    }

    public void Info(ReadOnlySpan<char> message)
    {
        EnqueueLog(LogType.Info, message.ToString());
        _consoleLogger.Info(message);
    }

    public void Success(ReadOnlySpan<char> message)
    {
        EnqueueLog(LogType.Success, message.ToString());
        _consoleLogger.Success(message);
    }

    public void Warning(ReadOnlySpan<char> message)
    {
        EnqueueLog(LogType.Warning, message.ToString());
        _consoleLogger.Warning(message);
    }

    private void EnqueueLog(LogType type, string message)
    {
        LogInfo logInfo = new LogInfo(type, message, _logIdCounter++);
        _logQueue.Enqueue(logInfo);
        TrimLogQueue();
    }

    public void Draw()
    {
        if (!IsOpen)
        {
            return;
        }

        // Create ImGUI window
        ImGui.Begin("Logger", ref IsOpen);

        // Filter controls
        ImGui.Text("Filter:");
        ImGui.SameLine();
        
        bool showInfo = (_filter & LogType.Info) != 0;
        if (ImGui.Checkbox("Info", ref showInfo))
        {
            _filter = showInfo ? (_filter | LogType.Info) : (_filter & ~LogType.Info);
        }
        ImGui.SameLine();
        
        bool showWarning = (_filter & LogType.Warning) != 0;
        if (ImGui.Checkbox("Warning", ref showWarning))
        {
            _filter = showWarning ? (_filter | LogType.Warning) : (_filter & ~LogType.Warning);
        }
        ImGui.SameLine();
        
        bool showError = (_filter & LogType.Error) != 0;
        if (ImGui.Checkbox("Error", ref showError))
        {
            _filter = showError ? (_filter | LogType.Error) : (_filter & ~LogType.Error);
        }
        ImGui.SameLine();
        
        bool showSuccess = (_filter & LogType.Success) != 0;
        if (ImGui.Checkbox("Success", ref showSuccess))
        {
            _filter = showSuccess ? (_filter | LogType.Success) : (_filter & ~LogType.Success);
        }

        ImGui.Separator();

        // Clear button
        if (ImGui.Button("Clear"))
        {
            ClearLogs();
        }

        ImGui.Separator();

        // Log display area
        ImGui.BeginChild("ScrollingRegion", new Vector2(0, 0), ImGuiChildFlags.None);

        // Display logs
        var logs = _logQueue.ToArray();
        for (int i = 0; i < logs.Length; i++)
        {
            var log = logs[i];
            
            // Check filter
            if ((_filter & log.Type) == 0)
                continue;

            // Get color based on log type
            Vector4 color = GetLogColor(log.Type);
            
            // Build log string without heap allocation
            _stringBuilder.Clear();
            _stringBuilder.Append('[');
            _stringBuilder.Append(GetLogTypeString(log.Type));
            _stringBuilder.Append("] ");
            _stringBuilder.Append(log.Message);

            ImGui.PushStyleColor(ImGuiCol.Text, color);

            bool isSelected = _selectedLogId == log.Id;
            string logText = _stringBuilder.AsReadOnlySpan().ToString();

            ImGui.PushID(log.Id);
            if (ImGui.Selectable(logText, isSelected))
            {
                _selectedLogId = log.Id;

                // 检测双击
                double currentTime = ImGui.GetTime();
                if (_lastClickedLogId == log.Id && (currentTime - _lastClickTime) < DoubleClickThreshold)
                {
                    // 双击事件
                    OnLogDoubleClick?.Invoke(log.Message);
                }

                _lastClickedLogId = log.Id;
                _lastClickTime = currentTime;
            }

            ImGui.PopID();
            ImGui.PopStyleColor();
        }

        // Auto-scroll to bottom
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
        {
            ImGui.SetScrollHereY(1.0f);
        }

        ImGui.EndChild();
        ImGui.End();
    }

    private void TrimLogQueue()
    {
        // Keep only the most recent entries
        while (_logQueue.Count > MaxLogEntries)
        {
            _logQueue.TryDequeue(out _);
        }
    }

    private void ClearLogs()
    {
        while (_logQueue.TryDequeue(out _))
        {
            // Clear all entries
        }
        
        _selectedLogId = -1;
        _lastClickedLogId = -1;
    }

    private static Vector4 GetLogColor(LogType type)
    {
        return type switch
        {
            LogType.Info => new Vector4(0.0f, 0.7f, 1.0f, 1.0f),    // Light blue
            LogType.Warning => new Vector4(1.0f, 1.0f, 0.0f, 1.0f), // Yellow
            LogType.Error => new Vector4(1.0f, 0.0f, 0.0f, 1.0f),   // Red
            LogType.Success => new Vector4(0.0f, 1.0f, 0.0f, 1.0f), // Green
            _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)                // White
        };
    }

    private static string GetLogTypeString(LogType type)
    {
        return type switch
        {
            LogType.Info => "INFO",
            LogType.Warning => "WARN",
            LogType.Error => "ERROR",
            LogType.Success => "SUCCESS",
            _ => "UNKNOWN"
        };
    }
}