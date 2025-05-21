using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

public class BinarySerializeWriteNode : SerializeNode
{
    protected BinaryTable _content = new BinaryTable();
    public BinaryTable Content => _content;

    public override void BindDeep<T>(string key, ref T value) 
    {
        BinarySerializeWriteNode node = new BinarySerializeWriteNode();
        value.OnSerialize(node, SerializeMode.Save);
        _content.Add(key, node._content);
    }

    public override void BindString(string key, ref string value, string @default = "")
    {
        _content.Add(key, value);
    }


    public override void BindValue<T>(string key, ref T value, T @default = default)
    {
        _content.Add(key, BinaryValue.CreateByValue(value));
    }

    public override void BindEnum<T>(string key, ref T value, T @default = default)
    {
        _content.Add(key, BinaryValue.CreateByEnum(value));
    }
    public override void BindMemory<T>(string key, Span<T> memory)
    {
        _content.Add(key, BinaryValue.CreateByMemory(memory));
    }

    public override void BindCollection<T>(string key, IList<T> value)
    {
        BinaryArray array = new BinaryArray();
        for (int i = 0; i < value.Count; i++)
        {
            array.Add(BinaryValue.CreateByValue(value[i]));
        }

        _content.Add(key, array);
    }

    public override void BindCollection(string key, IList<string> value)
    {
        BinaryArray array = new BinaryArray();
        for (int i = 0; i < value.Count; i++)
        {
            array.Add(value[i]);
        }

        _content.Add(key, array);
    }

    public override void BindCollectionDeep<T>(string key, IList<T> value)
    {
        BinaryArray array = new BinaryArray();
        for (int i = 0; i < value.Count; i++)
        {
            BinarySerializeWriteNode node = new BinarySerializeWriteNode();
            value[i].OnSerialize(node, SerializeMode.Save);
            array.Add(node._content);
        }

        _content.Add(key, array);
    }
}