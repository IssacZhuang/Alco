

using System;
using System.Collections.Generic;

namespace Alco;

/// <summary>
/// Base class for nodes that read values from serialized data.
/// </summary>
public abstract class SerializeReadNode : SerializeNode
{
    public override void BindEnum<T>(string key, ref T value, T @default = default)
    {
        value = GetEnum(key, @default);
    }

    public override void BindString(string key, ref string value, string @default = "")
    {
        value = GetString(key, @default);
    }

    public override void BindValue<T>(string key, ref T value, T @default = default)
    {
        value = GetValue(key, @default);
    }

    /// <summary>
    /// Binds a nullable unmanaged value by reading it from a single entry; a missing
    /// key or a false presence flag yields <c>null</c>.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type to bind.</typeparam>
    /// <param name="key">The key identifier for the value in the serialization format.</param>
    /// <param name="value">Reference to the nullable value to be deserialized.</param>
    public override void BindValue<T>(string key, ref T? value)
    {
        value = GetNullableValue<T>(key);
    }

    /// <summary>
    /// Reads an unmanaged value under the given key.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type.</typeparam>
    /// <param name="key">The key identifying the value.</param>
    /// <param name="default">The value returned when the key is missing.</param>
    /// <returns>The read value.</returns>
    public abstract T GetValue<T>(string key, T @default = default) where T : unmanaged;

    /// <summary>
    /// Reads a nullable unmanaged value under the given key; a missing key yields <c>null</c>.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type to read.</typeparam>
    /// <param name="key">The key identifier for the value in the serialization format.</param>
    /// <returns>The nullable value, or <c>null</c> when the key is missing or the payload is unreadable.</returns>
    public abstract T? GetNullableValue<T>(string key) where T : unmanaged;
    /// <summary>
    /// Reads an enum value under the given key.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="key">The key identifying the value.</param>
    /// <param name="default">The value returned when the key is missing.</param>
    /// <returns>The read enum value.</returns>
    public abstract T GetEnum<T>(string key, T @default = default) where T : unmanaged, Enum;
    /// <summary>
    /// Reads a string under the given key.
    /// </summary>
    /// <param name="key">The key identifying the string.</param>
    /// <param name="default">The value returned when the key is missing.</param>
    /// <returns>The read string.</returns>
    public abstract string GetString(string key, string @default = "");
}
