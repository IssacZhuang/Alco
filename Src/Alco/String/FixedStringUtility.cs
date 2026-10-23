using System;

namespace Alco;

/// <summary>
/// Utilities for locating trimmed ranges in fixed character spans.
/// </summary>
public static class FixedStringUtility
{
    /// <summary>
    /// Locates the first and last non-whitespace characters after applying the requested trim sides.
    /// </summary>
    /// <param name="str">The characters to inspect.</param>
    /// <param name="trimType">The sides to trim.</param>
    /// <param name="start">The index of the first retained character.</param>
    /// <param name="end">The index of the last retained character.</param>
    public static void GetTrimIndex(this ReadOnlySpan<char> str, TrimType trimType, out int start, out int end)
    {
        // end will point to the first non-trimmed character on the right.
        // start will point to the first non-trimmed character on the left.

        int length = str.Length;
        end = length - 1;
        start = 0;
        if ((trimType & TrimType.Head) != 0)
        {
            for (start = 0; start < length; start++)
            {
                if (!char.IsWhiteSpace(str[start]))
                {

                    break;
                }
            }
        }

        if ((trimType & TrimType.Tail) != 0)
        {
            for (end = length - 1; end >= start; end--)
            {
                if (!char.IsWhiteSpace(str[end]))
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Locates the first and last characters not contained in <paramref name="trimChars"/> after applying the requested trim sides.
    /// </summary>
    /// <param name="str">The characters to inspect.</param>
    /// <param name="trimChars">The characters to remove from the trimmed sides.</param>
    /// <param name="trimType">The sides to trim.</param>
    /// <param name="start">The index of the first retained character.</param>
    /// <param name="end">The index of the last retained character.</param>
    public static void GetTrimIndex(
        this ReadOnlySpan<char> str,
        ReadOnlySpan<char> trimChars,
        TrimType trimType,
        out int start,
        out int end)
    {
        // end will point to the first non-trimmed character on the right.
        // start will point to the first non-trimmed character on the left.
        int length = str.Length;
        int trimCharsLength = trimChars.Length;
        end = length - 1;
        start = 0;


        if ((trimType & TrimType.Head) != 0)
        {
            for (start = 0; start < length; start++)
            {
                int i = 0;
                char ch = str[start];
                for (i = 0; i < trimCharsLength; i++)
                {
                    if (trimChars[i] == ch)
                    {
                        break;
                    }
                }
                if (i == trimCharsLength)
                {
                    // The character is not in trimChars, so stop trimming.
                    break;
                }
            }
        }

        if ((trimType & TrimType.Tail) != 0)
        {
            for (end = length - 1; end >= start; end--)
            {
                int i = 0;
                char ch = str[end];
                for (i = 0; i < trimCharsLength; i++)
                {

                    if (trimChars[i] == ch)
                    {
                        break;
                    }
                }
                if (i == trimCharsLength)
                {
                    // The character is not in trimChars, so stop trimming.
                    break;
                }
            }
        }
    }
}

