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
    private readonly StreamWriter _writer;

    // ANSI color codes
    private const string AnsiReset = "\x1b[0m";
    private const string AnsiInfo = "\x1b[36m";    // Cyan for Info
    private const string AnsiError = "\x1b[31m";   // Red for Error
    private const string AnsiSuccess = "\x1b[32m"; // Green for Success
    private const string AnsiWarning = "\x1b[33m"; // Yellow for Warning

    public ConsoleLogger()
    {
        var stdout = Console.OpenStandardOutput();
        _writer = new StreamWriter(stdout, Encoding.UTF8);
        _writer.AutoFlush = true;
    }

    protected override void Dispose(bool disposing)
    {
        _writer.Dispose();
    }

    /// <summary>
    /// Logs an informational message in cyan color
    /// </summary>
    /// <param name="message">The message to log</param>
    public void Info(ReadOnlySpan<char> message)
    {
        var builder = _builder.Value!;
        builder.Clear();
        builder.Append(AnsiInfo);
        builder.Append(message);
        builder.Append(AnsiReset);
        _writer.WriteLine(builder.AsReadOnlySpan());
    }

    /// <summary>
    /// Logs an error message in red color
    /// </summary>
    /// <param name="message">The message to log</param>
    public void Error(ReadOnlySpan<char> message)
    {
        var builder = _builder.Value!;
        builder.Clear();
        builder.Append(AnsiError);
        builder.Append(message);
        builder.Append(AnsiReset);
        _writer.WriteLine(builder.AsReadOnlySpan());
    }

    /// <summary>
    /// Logs a success message in green color
    /// </summary>
    /// <param name="message">The message to log</param>
    public void Success(ReadOnlySpan<char> message)
    {
        var builder = _builder.Value!;
        builder.Clear();
        builder.Append(AnsiSuccess);
        builder.Append(message);
        builder.Append(AnsiReset);
        _writer.WriteLine(builder.AsReadOnlySpan());
    }

    /// <summary>
    /// Logs a warning message in yellow color
    /// </summary>
    /// <param name="message">The message to log</param>
    public void Warning(ReadOnlySpan<char> message)
    {
        var builder = _builder.Value!;
        builder.Clear();
        builder.Append(AnsiWarning);
        builder.Append(message);
        builder.Append(AnsiReset);
        _writer.WriteLine(builder.AsReadOnlySpan());
    }
}