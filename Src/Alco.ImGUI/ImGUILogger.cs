
using System.Collections.Concurrent;
using System.Numerics;

namespace Alco.ImGUI;

/// <summary>
/// ImGUI-based logger that provides a visual interface for displaying log messages with filtering and selection capabilities.
/// </summary>
public class ImGUILogger : ILogger
{
    /// <summary>
    /// Flags enumeration representing different types of log messages.
    /// </summary>
    [Flags]
    public enum LogType
    {
        /// <summary>Informational messages.</summary>
        Info = 1 << 0,
        /// <summary>Warning messages.</summary>
        Warning = 1 << 1,
        /// <summary>Error messages.</summary>
        Error = 1 << 2,
        /// <summary>Success messages.</summary>
        Success = 1 << 3,
        /// <summary>All message types.</summary>
        All = Info | Warning | Error | Success
    }

    private struct LogInfo
    {
        public LogType Type;
        public string Message;
        public int Id; // Unique identifier

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
    private string _selectedLogMessage = string.Empty; // Store selected log message content
    private int _logIdCounter = 0;
    private double _lastClickTime = 0;
    private int _lastClickedLogId = -1;
    private const double DoubleClickThreshold = 0.5;

    /// <summary>
    /// Gets or sets the filter for log message types to display.
    /// </summary>
    public LogType Filter
    {
        get => _filter;
        set
        {
            _filter = value;
        }
    }

    /// <summary>
    /// Event triggered when a log entry is double-clicked.
    /// </summary>
    public event Action<string>? OnLogDoubleClick;

    /// <summary>
    /// Gets or sets whether the logger window is open.
    /// </summary>
    public bool IsOpen = true;

    /// <summary>
    /// Logs an error message.
    /// </summary>
    /// <param name="message">The error message to log.</param>
    public void Error(ReadOnlySpan<char> message)
    {
        EnqueueLog(LogType.Error, message.ToString());
        _consoleLogger.Error(message);
    }

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    /// <param name="message">The informational message to log.</param>
    public void Info(ReadOnlySpan<char> message)
    {
        EnqueueLog(LogType.Info, message.ToString());
        _consoleLogger.Info(message);
    }

    /// <summary>
    /// Logs a success message.
    /// </summary>
    /// <param name="message">The success message to log.</param>
    public void Success(ReadOnlySpan<char> message)
    {
        EnqueueLog(LogType.Success, message.ToString());
        _consoleLogger.Success(message);
    }

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    /// <param name="message">The warning message to log.</param>
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

    /// <summary>
    /// Draws the ImGUI logger window with log filtering, display, and detail panels.
    /// </summary>
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
        // Calculate available height for logs dynamically
        float availableHeight = ImGui.GetContentRegionAvail().Y;
        float detailAreaHeight = 100; // Reserve space for separator + "Details:" text + detail panel
        float logAreaHeight = Math.Max(100, availableHeight - detailAreaHeight);

        ImGui.BeginChild("ScrollingRegion", new Vector2(0, logAreaHeight), ImGuiChildFlags.None);

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
            ReadOnlySpan<char> logText = _stringBuilder.AsReadOnlySpan();

            ImGui.PushID(log.Id);
            if (ImGui.Selectable(logText, isSelected))
            {
                _selectedLogId = log.Id;
                _selectedLogMessage = log.Message; // Update selected log message content

                // Detect double click
                double currentTime = ImGui.GetTime();
                if (_lastClickedLogId == log.Id && (currentTime - _lastClickTime) < DoubleClickThreshold)
                {
                    // Double click event
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

        // Separator
        ImGui.Separator();

        // Selected log detail area
        ImGui.Text("Details:");

        // Use remaining available height for detail area
        float remainingHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("SelectedLogDetail", new Vector2(0, remainingHeight), ImGuiChildFlags.None);
        if (!string.IsNullOrEmpty(_selectedLogMessage))
        {
            ImGui.TextWrapped(_selectedLogMessage);
        }
        else
        {
            ImGui.TextDisabled("Select a log entry to view details...");
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
        _selectedLogMessage = string.Empty; // Clear selected log message content
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