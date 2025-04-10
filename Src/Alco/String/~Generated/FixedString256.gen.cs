using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// Represents a fixed-length string with a maximum capacity of 256 characters.
/// This struct provides an efficient way to handle small strings with a predetermined maximum length.
/// </summary>
public partial unsafe struct FixedString256 : IEquatable<FixedString256>
{
    /// <summary>
    /// The maximum number of characters that can be stored in this fixed-length string.
    /// </summary>
    public const int MaxLength = 256;

    /// <summary>
    /// The internal fixed-size character buffer that stores the string data.
    /// </summary>
    public fixed char Buffer[MaxLength];

    /// <summary>
    /// Gets or sets the current length of the string stored in the buffer.
    /// </summary>
    public int Length;

    /// <summary>
    /// Gets or sets a character at the specified index in the fixed string.
    /// </summary>
    /// <param name="index">The zero-based index of the character to get or set.</param>
    /// <returns>The character at the specified index.</returns>
    public char this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Buffer[index];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Buffer[index] = value;
    }



    /// <summary>
    /// Initializes a new instance of the <see cref="FixedString256"/> struct with the specified string value.
    /// </summary>
    /// <param name="value">The string value to initialize with. If longer than MaxLength, it will be truncated.</param>
    /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
    public FixedString256(string value)
    {
        Set(value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedString256"/> struct with the specified character span.
    /// </summary>
    /// <param name="value">The character span to initialize with. If longer than MaxLength, it will be truncated.</param>
    public FixedString256(ReadOnlySpan<char> value)
    {
        Set(value);
    }

    /// <summary>
    /// Sets the content to a single character.
    /// </summary>
    /// <param name="value">The character to set.</param>
    public void Set(char value)
    {
        Buffer[0] = value;
        Length = 1;
    }

    /// <summary>
    /// Sets the content to the specified string value.
    /// </summary>
    /// <param name="value">The string value to set. If longer than MaxLength, it will be truncated.</param>
    /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
    public void Set(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        fixed (char* ptr = value)
        {
            Set(ptr, value.Length);
        }
    }

    /// <summary>
    /// Sets the content to the specified character span.
    /// </summary>
    /// <param name="value">The character span to set. If longer than MaxLength, it will be truncated.</param>
    public void Set(ReadOnlySpan<char> value)
    {
        fixed (char* ptr = value)
        {
            Set(ptr, value.Length);
        }
    }

    /// <summary>
    /// Sets the content using a pointer to a character array.
    /// </summary>
    /// <param name="str">Pointer to the character array.</param>
    /// <param name="length">The length of the character array.</param>
    public void Set(char* str, int length)
    {
        length = Math.Min(length, MaxLength);
        Length = length;
        fixed (char* ptr = Buffer)
        {
            for (int i = 0; i < length; i++)
            {
                ptr[i] = str[i];
            }
        }
    }

    /// <summary>
    /// Returns a span representing the characters in the fixed string.
    /// </summary>
    /// <returns>A span containing the characters in the fixed string.</returns>
    public Span<char> AsSpan()
    {
        fixed (char* ptr = Buffer)
        {
            return new Span<char>(ptr, Length);
        }
    }

    /// <summary>
    /// Returns a read-only span representing the characters in the fixed string.
    /// </summary>
    /// <returns>A read-only span containing the characters in the fixed string.</returns>
    public ReadOnlySpan<char> AsReadOnlySpan()
    {
        fixed (char* ptr = Buffer)
        {
            return new ReadOnlySpan<char>(ptr, Length);
        }
    }


    /// <summary>
    /// Converts the fixed string to a regular string.
    /// </summary>
    /// <returns>A new string containing the characters from this fixed string.</returns>
    public override string ToString()

    {
        fixed (char* buffer = Buffer)
        {
            return new string(buffer, 0, Length);
        }
    }


    /// <summary>
    /// Implicitly converts a ReadOnlySpan of characters to a FixedString256.
    /// </summary>
    /// <param name="value">The character span to convert.</param>
    public static implicit operator FixedString256(ReadOnlySpan<char> value)
    {
        return new FixedString256(value);
    }

    /// <summary>
    /// Implicitly converts a FixedString256 to a ReadOnlySpan of characters.
    /// </summary>
    /// <param name="value">The FixedString256 to convert.</param>
    public static implicit operator ReadOnlySpan<char>(FixedString256 value)
    {
        return new ReadOnlySpan<char>(value.Buffer, value.Length);
    }

    /// <summary>
    /// Implicitly converts a FixedString256 to a string.
    /// </summary>
    /// <param name="value">The FixedString256 to convert.</param>
    public static explicit operator string(FixedString256 value)
    {
        return value.ToString();
    }

    /// <summary>
    /// Implicitly converts a string to a FixedString256.
    /// </summary>
    /// <param name="value">The string to convert.</param>
    public static implicit operator FixedString256(string value)
    {
        return new FixedString256(value);
    }

    /// <summary>
    /// Determines whether this instance equals another object.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>true if the objects are equal; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is FixedString256 other)
        {
            return Equals(other);
        }
        return false;
    }

    /// <summary>
    /// Determines whether this instance equals another FixedString256.
    /// </summary>
    /// <param name="other">The FixedString256 to compare with.</param>
    /// <returns>true if the strings are equal; otherwise, false.</returns>
    public bool Equals(FixedString256 other)
    {
        if (Length != other.Length)
        {
            return false;
        }
        for (int i = 0; i < Length; i++)
        {
            if (Buffer[i] != other.Buffer[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Gets a hash code for this instance.
    /// </summary>
    /// <returns>A hash code value for this instance.</returns>
    public override int GetHashCode()
    {
        fixed (char* ptr = Buffer)
        {
            return string.GetHashCode(new ReadOnlySpan<char>(ptr, Length));
        }
    }

    /// <summary>
    /// Determines whether two FixedString256 instances are equal.
    /// </summary>
    /// <param name="left">The first string to compare.</param>
    /// <param name="right">The second string to compare.</param>
    /// <returns>true if the strings are equal; otherwise, false.</returns>
    public static bool operator ==(FixedString256 left, FixedString256 right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two FixedString256 instances are not equal.
    /// </summary>
    /// <param name="left">The first string to compare.</param>
    /// <param name="right">The second string to compare.</param>
    /// <returns>true if the strings are not equal; otherwise, false.</returns>
    public static bool operator !=(FixedString256 left, FixedString256 right)
    {
        return !(left == right);
    }

    #region String Builder
    /// <summary>
    /// Works like <see cref="System.Text.StringBuilder.Append"/><br/>
    /// Appends a single character to the end of the current content.
    /// If the buffer is full, the operation is ignored.
    /// </summary>
    /// <param name="value">The character to append.</param>
    public void Append(char value)
    {
        if (Length >= MaxLength)
        {
            return;
        }

        Buffer[Length] = value;
        Length++;
    }

    /// <summary>
    /// Works like <see cref="System.Text.StringBuilder.Append"/><br/>
    /// Appends a string to the end of the current content.
    /// If the resulting length would exceed MaxLength, the operation is ignored.
    /// </summary>
    /// <param name="value">The string to append.</param>
    /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
    public void Append(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        fixed (char* ptr = value)
        {
            Append(ptr, value.Length);
        }
    }

    /// <summary>
    /// Works like <see cref="System.Text.StringBuilder.Append"/><br/>
    /// Appends a character span to the end of the current content.
    /// If the resulting length would exceed MaxLength, the operation is ignored.
    /// </summary>
    /// <param name="value">The character span to append.</param>
    public void Append(ReadOnlySpan<char> value)
    {
        fixed (char* ptr = value)
        {
            Append(ptr, value.Length);
        }
    }

    /// <summary>
    /// Works like <see cref="System.Text.StringBuilder.Append"/><br/>
    /// Appends a value to the end of the current content.
    /// If the resulting length would exceed MaxLength, the operation is ignored.
    /// </summary>
    /// <typeparam name="T">The type of the value to append.</typeparam>
    /// <param name="value">The value to append.</param>
    public void Append<T>(T value) where T : ISpanFormattable
    {
        //take remain buffer
        fixed (char* ptr = Buffer)
        {
            Span<char> remainSpan = new Span<char>(ptr + Length, MaxLength - Length);
            if (value.TryFormat(remainSpan, out int charsWritten, null, null))
            {
                Length += charsWritten;
            }
        }
    }

    /// <summary>
    /// Works like <see cref="System.Text.StringBuilder.Append"/><br/>
    /// Appends characters from a pointer to the end of the current content.
    /// If the resulting length would exceed MaxLength, only the characters that fit will be appended.
    /// </summary>
    /// <param name="str">Pointer to the character array to append.</param>
    /// <param name="length">The length of the character array.</param>
    public void Append(char* str, int length)
    {
        if (Length + length > MaxLength)
        {
            return;
        }

        int lengthToAppend = Math.Min(length, MaxLength - Length);

        for (int i = 0; i < lengthToAppend; i++)
        {
            Buffer[Length + i] = str[i];
        }
        Length += lengthToAppend;
    }

    /// <summary>
    /// Works like <see cref="System.Text.StringBuilder.Clear"/><br/>
    /// Clears the content of the fixed string, setting its length to 0.
    /// </summary>
    public void Clear()
    {
        Length = 0;
    }

    #endregion

    #region Substring

    /// <summary>
    /// Returns a substring from the specified index to the end of the fixed string.
    /// </summary>
    /// <param name="startIndex">The zero-based starting index of the substring.</param>
    /// <returns>A new FixedString256 that is equivalent to the substring that begins at startIndex.</returns>
    public FixedString256 Substring(int startIndex)
    {
        if (startIndex == 0)
        {
            return this;
        }

        int length = Length - startIndex;
        if (length == 0)
        {
            return new FixedString256();
        }

        return AsReadOnlySpan().Slice(startIndex);
    }

    /// <summary>
    /// Returns a substring that has the specified length and starts at the specified index.
    /// </summary>
    /// <param name="startIndex">The zero-based starting index of the substring.</param>
    /// <param name="length">The number of characters in the substring.</param>
    /// <returns>A new FixedString256 that is equivalent to the substring of length that begins at startIndex.</returns>
    public FixedString256 Substring(int startIndex, int length)
    {
        return AsReadOnlySpan().Slice(startIndex, length);
    }


    #endregion

    #region Case

    /// <summary>
    /// Returns a copy of this string converted to lowercase using the specified culture.
    /// </summary>
    /// <param name="culture">The culture-specific information for the conversion. If null, uses the current culture.</param>
    /// <returns>A new FixedString256 with all characters converted to lowercase.</returns>
    public FixedString256 ToLower(CultureInfo? culture)
    {
        FixedString256 result = new FixedString256();
        AsReadOnlySpan().ToLower(result.AsSpan(), culture);
        return result;
    }

    /// <summary>
    /// Returns a copy of this string converted to lowercase using the current culture.
    /// </summary>
    /// <returns>A new FixedString256 with all characters converted to lowercase.</returns>
    public FixedString256 ToLower()
    {
        return ToLower(null);
    }

    /// <summary>
    /// Returns a copy of this string converted to lowercase using the invariant culture.
    /// </summary>
    /// <returns>A new FixedString256 with all characters converted to lowercase using the invariant culture.</returns>
    public FixedString256 ToLowerInvariant()
    {
        return ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns a copy of this string converted to uppercase using the specified culture.
    /// </summary>
    /// <param name="culture">The culture-specific information for the conversion. If null, uses the current culture.</param>
    /// <returns>A new FixedString256 with all characters converted to uppercase.</returns>
    public FixedString256 ToUpper(CultureInfo? culture)

    {
        FixedString256 result = new FixedString256();
        AsReadOnlySpan().ToUpper(result.AsSpan(), culture);
        return result;
    }

    /// <summary>
    /// Returns a copy of this string converted to uppercase using the current culture.
    /// </summary>
    /// <returns>A new FixedString256 with all characters converted to uppercase.</returns>
    public FixedString256 ToUpper()
    {
        return ToUpper(null);
    }

    /// <summary>
    /// Returns a copy of this string converted to uppercase using the invariant culture.
    /// </summary>
    /// <returns>A new FixedString256 with all characters converted to uppercase using the invariant culture.</returns>
    public FixedString256 ToUpperInvariant()
    {
        return ToUpper(CultureInfo.InvariantCulture);
    }


    #endregion

    #region Trim



    /// <summary>

    /// Removes all leading and trailing white-space characters from the current string.
    /// </summary>
    /// <returns>A new string with all leading and trailing white-space characters removed.</returns>
    public FixedString256 Trim()
    {
        return TrimWhiteSpaceHelper(TrimType.Both);
    }

    /// <summary>
    /// Removes all leading and trailing instances of a specified character from the current string.
    /// </summary>
    /// <param name="trimChar">The character to remove from the start and end of the string.</param>
    /// <returns>A new string with all leading and trailing instances of the specified character removed.</returns>
    public FixedString256 Trim(char trimChar)
    {
        return TrimHelper(new ReadOnlySpan<char>(&trimChar, 1), TrimType.Both);
    }

    /// <summary>
    /// Removes all leading and trailing occurrences of a set of characters from the current string.
    /// </summary>
    /// <param name="trimChars">An array of characters to remove from the start and end of the string. 
    /// If null or empty, white-space characters are removed instead.</param>
    /// <returns>A new string with all leading and trailing specified characters removed.</returns>
    public FixedString256 Trim(params char[] trimChars)
    {
        if (trimChars == null || trimChars.Length == 0)
        {
            return TrimWhiteSpaceHelper(TrimType.Head);
        }
        return TrimHelper(trimChars, TrimType.Both);
    }

    /// <summary>
    /// Removes all leading and trailing occurrences of a set of characters specified in a span from the current string.
    /// </summary>
    /// <param name="trimChars">A span of characters to remove from the start and end of the string.
    /// If empty, white-space characters are removed instead.</param>
    /// <returns>A new string with all leading and trailing specified characters removed.</returns>
    public unsafe string Trim(params ReadOnlySpan<char> trimChars)
    {
        if (trimChars.IsEmpty)
        {
            return TrimWhiteSpaceHelper(TrimType.Both).ToString();
        }

        return TrimHelper(trimChars, TrimType.Both).ToString();
    }

    /// <summary>
    /// Removes all leading white-space characters from the current string.
    /// </summary>
    /// <returns>A new string with all leading white-space characters removed.</returns>
    public FixedString256 TrimStart()
    {
        return TrimWhiteSpaceHelper(TrimType.Head);
    }

    /// <summary>
    /// Removes all leading instances of a specified character from the current string.
    /// </summary>
    /// <param name="trimChar">The character to remove from the start of the string.</param>
    /// <returns>A new string with all leading instances of the specified character removed.</returns>
    public FixedString256 TrimStart(char trimChar)
    {
        return TrimHelper(new ReadOnlySpan<char>(&trimChar, 1), TrimType.Head);
    }

    /// <summary>
    /// Removes all leading occurrences of a set of characters from the current string.
    /// </summary>
    /// <param name="trimChars">An array of characters to remove from the start of the string.
    /// If null or empty, white-space characters are removed instead.</param>
    /// <returns>A new string with all leading specified characters removed.</returns>
    public FixedString256 TrimStart(params char[] trimChars)
    {
        if (trimChars == null || trimChars.Length == 0)
        {
            return TrimWhiteSpaceHelper(TrimType.Head);
        }
        return TrimHelper(trimChars, TrimType.Head);
    }

    /// <summary>
    /// Removes all leading occurrences of a set of characters specified in a span from the current string.
    /// </summary>
    /// <param name="trimChars">A span of characters to remove from the start of the string.
    /// If empty, white-space characters are removed instead.</param>
    /// <returns>A new string with all leading specified characters removed.</returns>
    public unsafe string TrimStart(params ReadOnlySpan<char> trimChars)
    {
        if (trimChars.IsEmpty)
        {
            return TrimWhiteSpaceHelper(TrimType.Head).ToString();
        }
        return TrimHelper(trimChars, TrimType.Head).ToString();
    }

    /// <summary>
    /// Removes all trailing white-space characters from the current string.
    /// </summary>
    /// <returns>A new string with all trailing white-space characters removed.</returns>
    public FixedString256 TrimEnd()
    {
        return TrimWhiteSpaceHelper(TrimType.Tail);
    }

    /// <summary>
    /// Removes all trailing instances of a specified character from the current string.
    /// </summary>
    /// <param name="trimChar">The character to remove from the end of the string.</param>
    /// <returns>A new string with all trailing instances of the specified character removed.</returns>
    public FixedString256 TrimEnd(char trimChar)
    {
        return TrimHelper(new ReadOnlySpan<char>(&trimChar, 1), TrimType.Tail);
    }

    /// <summary>
    /// Removes all trailing occurrences of a set of characters from the current string.
    /// </summary>
    /// <param name="trimChars">An array of characters to remove from the end of the string.
    /// If null or empty, white-space characters are removed instead.</param>
    /// <returns>A new string with all trailing specified characters removed.</returns>
    public FixedString256 TrimEnd(params char[] trimChars)
    {
        if (trimChars == null || trimChars.Length == 0)
        {
            return TrimWhiteSpaceHelper(TrimType.Tail);
        }
        return TrimHelper(trimChars, TrimType.Tail);
    }

    /// <summary>
    /// Removes all trailing occurrences of a set of characters specified in a span from the current string.
    /// </summary>
    /// <param name="trimChars">A span of characters to remove from the end of the string.
    /// If empty, white-space characters are removed instead.</param>
    /// <returns>A new string with all trailing specified characters removed.</returns>
    public unsafe string TrimEnd(params ReadOnlySpan<char> trimChars)
    {
        if (trimChars.IsEmpty)
        {
            return TrimWhiteSpaceHelper(TrimType.Tail).ToString();
        }
        return TrimHelper(trimChars, TrimType.Tail).ToString();
    }

    /// <summary>
    /// Creates a new string containing the characters from the specified range of the current string.
    /// </summary>
    /// <param name="start">The starting index of the range to include.</param>
    /// <param name="end">The ending index of the range to include.</param>
    /// <returns>A new string containing only the characters from the specified range.</returns>
    private FixedString256 CreateTrimmedString(int start, int end)
    {
        int len = end - start + 1;
        fixed (char* ptr = Buffer)
        {
            return
                len == Length ? this :
                len == 0 ? new FixedString256() : new FixedString256(new ReadOnlySpan<char>(ptr + start, len));
        }
    }

    /// <summary>
    /// Helper method that removes white-space characters based on the specified trim type.
    /// </summary>
    /// <param name="trimType">Specifies whether to trim from the start, end, or both.</param>
    /// <returns>A new string with white-space characters removed according to the trim type.</returns>
    private FixedString256 TrimWhiteSpaceHelper(TrimType trimType)
    {
        UtilsFixedString.GetTrimIndex(this, trimType, out int start, out int end);
        return CreateTrimmedString(start, end);
    }

    /// <summary>
    /// Helper method that removes specified characters based on the trim type.
    /// </summary>
    /// <param name="trimChars">The characters to remove.</param>
    /// <param name="trimType">Specifies whether to trim from the start, end, or both.</param>
    /// <returns>A new string with specified characters removed according to the trim type.</returns>
    private FixedString256 TrimHelper(ReadOnlySpan<char> trimChars, TrimType trimType)
    {
        UtilsFixedString.GetTrimIndex(this, trimChars, trimType, out int start, out int end);
        return CreateTrimmedString(start, end);
    }

    #endregion

}




