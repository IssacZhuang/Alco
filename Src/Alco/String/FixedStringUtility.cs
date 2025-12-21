using System;

namespace Alco;

public static class FixedStringUtility
{
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

