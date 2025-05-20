using System.Collections;
using System.Collections.Generic;

namespace Alco;

public class BinarySerializeReadNode : SerializeNode
{
    protected BinaryTable _content;
    public BinaryTable Content => _content;
    public BinarySerializeReadNode(BinaryTable content)
    {
        _content = content;
    }
    public override void BindBinary(string key, ref byte[] value)
    {
        if (_content.TryGetBinary(key, out byte[]? binaryValue))
        {
            value = binaryValue;
        }
    }

    public override void BindDeep<T>(string key, ref T value) 
    {
        if (_content.TryGetTable(key, out BinaryTable? table))
        {
            value.OnSerialize(new BinarySerializeReadNode(table), SerializeMode.Load);
        }
    }

    public override void BindString(string key, ref string value, string @default = "")
    {
        if (_content.TryGetString(key, out string? stringValue))
        {
            value = stringValue;
        }
        else
        {
            value = @default;
        }
    }


    public override void BindValue<T>(string key, ref T value, T @default = default)
    {
        if (_content.TryGetValue(key, out T v))
        {
            value = v;
        }
        else
        {
            value = @default;
        }
    }

    public override void BindEnum<T>(string key, ref T value, T @default = default)
    {
        if (_content.TryGetEnum(key, out T v))
        {
            value = v;
        }
        else
        {
            value = @default;
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
}