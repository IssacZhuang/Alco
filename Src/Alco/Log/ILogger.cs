
using System;

namespace Alco;

/// <summary>
/// Defines a logger that accepts messages at info, warning, error, and success levels.
/// </summary>
public interface ILogger
{
    void Info(ReadOnlySpan<char> message);
    void Warning(ReadOnlySpan<char> message);
    void Error(ReadOnlySpan<char> message);
    void Success(ReadOnlySpan<char> message);
}