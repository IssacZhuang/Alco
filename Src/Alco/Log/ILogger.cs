
using System;

namespace Alco;

public interface ILogger
{
    void Info(ReadOnlySpan<char> message);
    void Warning(ReadOnlySpan<char> message);
    void Error(ReadOnlySpan<char> message);
    void Success(ReadOnlySpan<char> message);
}