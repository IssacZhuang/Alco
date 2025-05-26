using System;
using System.Collections;
using System.Collections.Generic;

namespace Alco;

public class BinarySerializeReadNode : SerializeReadNode
{
    protected BinaryTable _content;
    public BinaryTable Content => _content;
    public BinarySerializeReadNode(BinaryTable content)
    {
        _content = content;
    }

    public override void BindDeep<T>(string key, ref T value)
    {
        if (_content.TryGetTable(key, out BinaryTable? table))
        {
            value.OnSerialize(new BinarySerializeReadNode(table), SerializeMode.Load);
        }
    }

    public override void BindDeepNullable<T>(string key, ref T? value, Func<SerializeReadNode, T> onCreate) where T : default
    {
        if (_content.TryGetTable(key, out BinaryTable? table))
        {
            BinarySerializeReadNode subNode = new BinarySerializeReadNode(table);
            value ??= onCreate(subNode);
            value.OnSerialize(subNode, SerializeMode.Load);
        }
    }

    public unsafe override void BindMemory<T>(string key, Span<T> memory)
    {
        if (_content.TryGetBinary(key, out byte[]? binaryValue))
        {
            int length = Math.Min(memory.Length * sizeof(T), binaryValue.Length);
            fixed (T* ptrMemory = memory)
            {
                Span<byte> span = new Span<byte>(ptrMemory, length);
                binaryValue.AsSpan().CopyTo(span);
            }
        }
    }

    public override void BindCollection<T>(string key, IList<T> value)
    {
        value.Clear();
        if (_content.TryGetArray(key, out BinaryArray? array))
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array.TryGetBinary(i, out byte[]? binaryValue))
                {
                    value.Add(UtilsBinary.DecodeToValue<T>(binaryValue));
                }
            }
        }
    }

    public override void BindCollection(string key, IList<string> value)
    {
        value.Clear();
        if (_content.TryGetArray(key, out BinaryArray? array))
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array.TryGetString(i, out string? stringValue))
                {
                    value.Add(stringValue);
                }
            }
        }
    }

    public override void BindCollectionDeep<T>(string key, IList<T> value)
    {
        value.Clear();
        if (_content.TryGetArray(key, out BinaryArray? array))
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array.TryGetTable(i, out BinaryTable? table))
                {
                    T item = new T();
                    item.OnSerialize(new BinarySerializeReadNode(table), SerializeMode.Load);
                    value.Add(item);
                }
            }
        }
    }

    public override T GetValue<T>(string key, T @default = default)
    {
        if (_content.TryGetValue(key, out T v))
        {
            return v;
        }
        else
        {
            return @default;
        }
    }

    public override T GetEnum<T>(string key, T @default = default)
    {
        if (_content.TryGetEnum(key, out T v))
        {
            return v;
        }
        else
        {
            return @default;
        }
    }

    public override string GetString(string key, string @default = "")
    {
        if (_content.TryGetString(key, out string? stringValue))
        {
            return stringValue;
        }
        else
        {
            return @default;
        }
    }
}