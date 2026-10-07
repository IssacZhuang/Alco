using System;
using System.Collections.Generic;

namespace Alco;

public abstract class SerializeWriteNode : SerializeNode
{
    public override void BindEnum<T>(string key, ref T value, T @default = default)
    {
        SetEnum(key, value);
    }

    public override void BindString(string key, ref string value, string @default = "")
    {
        SetString(key, value);
    }

    public override void BindValue<T>(string key, ref T value, T @default = default)
    {
        SetValue(key, value);
    }

    /// <summary>
    /// Binds a nullable unmanaged value by writing it as a single entry.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type to bind.</typeparam>
    /// <param name="key">The key identifier for the value in the serialization format.</param>
    /// <param name="value">Reference to the nullable value to be serialized.</param>
    public override void BindValue<T>(string key, ref T? value)
    {
        SetNullableValue(key, value);
    }

    public abstract void SetValue<T>(string key, T value) where T : unmanaged;

    /// <summary>
    /// Writes a nullable unmanaged value under the given key.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type to write.</typeparam>
    /// <param name="key">The key identifier for the value in the serialization format.</param>
    /// <param name="value">The nullable value to write.</param>
    public abstract void SetNullableValue<T>(string key, T? value) where T : unmanaged;
    public abstract void SetEnum<T>(string key, T value) where T : unmanaged, Enum;
    public abstract void SetString(string key, string value);
}