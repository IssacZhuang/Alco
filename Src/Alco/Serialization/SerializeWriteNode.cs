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

    public abstract void SetValue<T>(string key, T value) where T : unmanaged;
    public abstract void SetEnum<T>(string key, T value) where T : struct, Enum;
    public abstract void SetString(string key, string value);
}