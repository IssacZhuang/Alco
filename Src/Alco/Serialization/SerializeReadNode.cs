

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

    public abstract T GetValue<T>(string key, T @default = default) where T : unmanaged;
    public abstract T GetEnum<T>(string key, T @default = default) where T : struct, Enum;
    public abstract string GetString(string key, string @default = "");
}
