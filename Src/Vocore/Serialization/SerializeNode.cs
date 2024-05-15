using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Vocore;

public abstract class SerializeNode
{
    public abstract void BindValue<T>(string key, ref T value, T @default = default) where T : unmanaged;
    public abstract void BindString(string key, ref string value, string @default = "");
    public abstract void BindDeep<T>(string key, ref T value) where T : ISerializable, new();
    public abstract void BindBinary(string key, ref byte[] value);
    public abstract void BindCollection<T>(string key, IList<T> value) where T : unmanaged;
    public abstract void BindCollection(string key, IList<string> value);
    public abstract void BindCollectionDeep<T>(string key, IList<T> value) where T : ISerializable, new();
}