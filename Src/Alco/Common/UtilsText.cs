using System;
using System.Collections;
using System.Threading;

namespace Alco;

/// <summary>
/// Tools for working with text
/// </summary>
public static class UtilsText
{
    /// <summary>
    /// Performs a blur search (subsequence matching) on the given text.
    /// The search is case-insensitive.
    /// </summary>
    /// <param name="text">The text to search in</param>
    /// <param name="search">The search pattern to match</param>
    /// <returns>True if the search pattern is found as a subsequence in the text, otherwise false</returns>
    public static bool BlurSearch(ReadOnlySpan<char> text, ReadOnlySpan<char> search)
    {
        if (search.IsEmpty)
            return true;

        if (text.IsEmpty)
            return false;

        int searchIndex = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(search[searchIndex]))
            {
                searchIndex++;

                if (searchIndex == search.Length)
                    return true;
            }
        }

        return false;
    }
}