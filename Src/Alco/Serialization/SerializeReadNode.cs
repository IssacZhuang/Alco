

using System;
using System.Collections.Generic;

namespace Alco;

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

    public abstract T GetValue<T>(string key, T @default = default) where T : unmanaged;

    /// <summary>
    /// Reads a nullable unmanaged value under the given key; a missing key yields <c>null</c>.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type to read.</typeparam>
    /// <param name="key">The key identifier for the value in the serialization format.</param>
    /// <returns>The nullable value, or <c>null</c> when the key is missing or the payload is unreadable.</returns>
    public abstract T? GetNullableValue<T>(string key) where T : unmanaged;
    public abstract T GetEnum<T>(string key, T @default = default) where T : unmanaged, Enum;
    public abstract string GetString(string key, string @default = "");
}
