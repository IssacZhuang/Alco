using System;
using System.Threading;

namespace Alco;

/// <summary>
/// Tools for working with text
/// </summary>
public static class UtilsText
{
    private static readonly char[] _formatCache = new char[256];

    /// <summary>
    /// Convert value to ReadOnlySpan of char
    /// <br/> Not thread safe
    /// </summary>
    /// <param name="value"> Value to convert </param>
    /// <typeparam name="T"> Type of value </typeparam>
    /// <returns>ReadOnlySpan of char</returns>
    public static ReadOnlySpan<char> ToCharSpan<T>(T value) where T : ISpanFormattable
    {
        if (value.TryFormat(_formatCache, out var written, ReadOnlySpan<char>.Empty, null))
        {
            return new ReadOnlySpan<char>(_formatCache, 0, written);
        }

        return ReadOnlySpan<char>.Empty;
    }

    private static readonly ThreadLocal<char[]> _formatCacheThreadSafe = new ThreadLocal<char[]>(() => new char[256]);

    /// <summary>
    /// Convert value to ReadOnlySpan of char
    /// <br/> Thread safe
    /// </summary>
    /// <param name="value"> Value to convert </param>
    /// <typeparam name="T"> Type of value </typeparam>
    /// <returns>ReadOnlySpan of char</returns>
    public static ReadOnlySpan<char> ToCharSpanThreadSafe<T>(T value) where T : ISpanFormattable
    {
        var formatCache = _formatCacheThreadSafe.Value;
        if (value.TryFormat(formatCache, out var written, ReadOnlySpan<char>.Empty, null))
        {
            return new ReadOnlySpan<char>(formatCache, 0, written);
        }

        return ReadOnlySpan<char>.Empty;
    }
}