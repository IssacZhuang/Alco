using System;
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
    public static bool BlurSearch(string text, string search)
    {
        if (string.IsNullOrEmpty(search))
            return true;

        if (string.IsNullOrEmpty(text))
            return false;

        string lowerText = text.ToLower();
        string lowerSearch = search.ToLower();

        int searchIndex = 0;
        for (int i = 0; i < lowerText.Length; i++)
        {
            if (lowerText[i] == lowerSearch[searchIndex])
            {
                searchIndex++;

                if (searchIndex == lowerSearch.Length)
                    return true;
            }
        }

        return false;
    }
}