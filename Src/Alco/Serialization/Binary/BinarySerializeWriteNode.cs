using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

public class BinarySerializeWriteNode : SerializeWriteNode
{
    protected BinaryTable _content = new BinaryTable();
    public BinaryTable Content => _content;

    public override void BindSerializable<T>(string key, T value)
    {
        BinarySerializeWriteNode node = new BinarySerializeWriteNode();
        value.OnSerialize(node, SerializeMode.Save);
        _content.Add(key, node._content);
    }

    public override void BindSerializableOptional<T>(string key, ref T? value, Func<SerializeReadNode, T> onCreate) where T : default
    {
        if (value == null)
        {
            _content.Add(key, new BinaryValue());
        }
        else
        {
            BinarySerializeWriteNode node = new BinarySerializeWriteNode();
            value.OnSerialize(node, SerializeMode.Save);
            _content.Add(key, node._content);
        }
    }

    public override void BindMemory<T>(string key, Span<T> memory)
    {
        _content.Add(key, BinaryValue.CreateByMemory(memory));
    }

    public override void BindList<T>(string key, IList<T> value)
    {
        BinaryArray array = new BinaryArray();
        for (int i = 0; i < value.Count; i++)
        {
            array.Add(BinaryValue.CreateByValue(value[i]));
        }

        _content.Add(key, array);
    }

    public override void BindList(string key, IList<string> value)
    {
        BinaryArray array = new BinaryArray();
        for (int i = 0; i < value.Count; i++)
        {
            array.Add(value[i]);
        }

        _content.Add(key, array);
    }

    public override void BindListSerializable<T>(string key, IList<T> value)
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

    public override void SetValue<T>(string key, T value)
    {
        _content.Add(key, BinaryValue.CreateByValue(value));
    }

    public override void SetEnum<T>(string key, T value)
    {
        _content.Add(key, BinaryValue.CreateByEnum(value));
    }

    public override void SetString(string key, string value)
    {
        _content.Add(key, value);
    }

    public override void BindDictionary<TValue>(string key, IDictionary<string, TValue> value)
    {
        BinaryTable table = new BinaryTable();
        foreach (var item in value)
        {
            table.Add(item.Key, BinaryValue.CreateByValue(item.Value));
        }
        _content.Add(key, table);
    }

    public override void BindDictionary(string key, IDictionary<string, string> value)
    {
        BinaryTable table = new BinaryTable();
        foreach (var item in value)
        {
            table.Add(item.Key, item.Value);
        }
        _content.Add(key, table);
    }

    public override void BindDictionary(string key, IDictionary<string, byte[]> value)
    {
        BinaryTable table = new BinaryTable();
        foreach (var item in value)
        {
            table.Add(item.Key, BinaryValue.CreateByMemory(item.Value.AsSpan()));
        }
        _content.Add(key, table);
    }
}