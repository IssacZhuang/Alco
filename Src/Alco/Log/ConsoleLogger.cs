using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Alco;

/// <summary>
/// Console logger that outputs colored messages using ANSI escape codes
/// </summary>
public class ConsoleLogger : AutoDisposable, ILogger
{
    private readonly ThreadLocal<SpanStringBuilder> _builder = new ThreadLocal<SpanStringBuilder>(() => new SpanStringBuilder());

    // Serializes writes across every ConsoleLogger instance: each instance wraps its own
    // StreamWriter around the shared stdout handle, and StreamWriter is not thread-safe —
    // unsynchronized concurrent writes corrupt the writer state and throw. The writer keeps
    // the underlying stdout stream open (leaveOpen) so disposing one logger never closes
    // the handle out from under the others.
    private static readonly object WriteLock = new();
    private readonly StreamWriter _writer;

    // ANSI color codes
    private const string AnsiReset = "\x1b[0m";
    private const string AnsiInfo = "\x1b[36m";    // Cyan for Info
    private const string AnsiError = "\x1b[31m";   // Red for Error
    private const string AnsiSuccess = "\x1b[32m"; // Green for Success
    private const string AnsiWarning = "\x1b[33m"; // Yellow for Warning

    public ConsoleLogger()
    {
        _writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true,
        };
    }

    protected override void Dispose(bool disposing)
    {
        _writer.Dispose();
    }

    private void WriteColored(string ansiColor, ReadOnlySpan<char> message)
    {
        var builder = _builder.Value!;
        builder.Clear();
        builder.Append(ansiColor);
        builder.Append(message);
        builder.Append(AnsiReset);
        lock (WriteLock)
        {
            try
            {
                _writer.WriteLine(builder.AsReadOnlySpan());
            }
            catch (Exception)
            {
                // A logger must never throw into its caller: the console pipe may be closed
                // or detached (test host shutdown, redirected output ending) — drop the line.
            }
        }
    }

    /// <summary>
    /// Logs an informational message in cyan color
    /// </summary>
    /// <param name="message">The message to log</param>
    public void Info(ReadOnlySpan<char> message) => WriteColored(AnsiInfo, message);

    /// <summary>
    /// Logs an error message in red color
    /// </summary>
    /// <param name="message">The message to log</param>
    public void Error(ReadOnlySpan<char> message) => WriteColored(AnsiError, message);

    /// <summary>
    /// Logs a success message in green color
    /// </summary>
    /// <param name="message">The message to log</param>
    public void Success(ReadOnlySpan<char> message) => WriteColored(AnsiSuccess, message);

    /// <summary>
    /// Logs a warning message in yellow color
    /// </summary>
    /// <param name="message">The message to log</param>
    public void Warning(ReadOnlySpan<char> message) => WriteColored(AnsiWarning, message);
}
