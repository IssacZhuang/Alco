using System;
using System.Collections.Generic;

namespace Alco;

/// <summary>
/// A serialization node used during the post-load phase.
/// This node does not read or write any data. It only traverses objects
/// and invokes <see cref="ISerializable.OnSerialize"/> with <see cref="SerializeMode.PostLoad"/>.
/// </summary>
public sealed class PostLoadSerializeNode : SerializeNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PostLoadSerializeNode"/> class.
    /// </summary>
    /// <param name="onError">Optional error callback.</param>
    public PostLoadSerializeNode(Action<string>? onError = null)
    {
        OnError = onError;
    }

    /// <summary>
    /// No-op for post-load: values are not read or written.
    /// </summary>
    public override void BindValue<T>(string key, ref T value, T @default = default)
    {
        // Intentionally empty: no serialization during post-load
    }

    /// <summary>
    /// No-op for post-load: enums are not read or written.
    /// </summary>
    public override void BindEnum<T>(string key, ref T value, T @default = default)
    {
        // Intentionally empty: no serialization during post-load
    }

    /// <summary>
    /// No-op for post-load: strings are not read or written.
    /// </summary>
    public override void BindString(string key, ref string value, string @default = "")
    {
        // Intentionally empty: no serialization during post-load
    }

    /// <summary>
    /// Invokes post-load serialization on the provided serializable value.
    /// </summary>
    public override void BindSerializable<T>(string key, T value)
    {
        try
        {
            value.OnSerialize(this, SerializeMode.PostLoad);
        }
        catch (Exception ex)
        {
            AddError($"Failed to post-load '{key}': {ex}");
        }
    }

    /// <summary>
    /// Invokes post-load serialization on an optional serializable value if present.
    /// </summary>
    public override void BindSerializableOptional<T>(string key, ref T? value, Func<SerializeReadNode, T> onCreate) where T : default
    {
        try
        {
            if (value is not null)
            {
                value.OnSerialize(this, SerializeMode.PostLoad);
            }
        }
        catch (Exception ex)
        {
            AddError($"Failed to post-load optional '{key}': {ex}");
        }
    }

    /// <summary>
    /// No-op for post-load: raw memory is not processed.
    /// </summary>
    public override void BindMemory<T>(string key, Span<T> memory)
    {
        // Intentionally empty
    }

    /// <summary>
    /// No-op for post-load: primitive collections are not processed.
    /// </summary>
    public override void BindCollection<T>(string key, ICollection<T> value)
    {
        // Intentionally empty
    }

    /// <summary>
    /// No-op for post-load: string collections are not processed.
    /// </summary>
    public override void BindCollection(string key, ICollection<string> value)
    {
        // Intentionally empty
    }

    /// <summary>
    /// Traverses and invokes post-load on each serializable item in the collection.
    /// </summary>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value)
    {
        int index = 0;
        foreach (T item in value)
        {
            try
            {
                item.OnSerialize(this, SerializeMode.PostLoad);
            }
            catch (Exception ex)
            {
                AddError($"Failed to post-load list item at index {index} for key '{key}': {ex}");
            }
            index++;
        }
    }

    /// <summary>
    /// Traverses and invokes post-load on each serializable item in the collection.
    /// </summary>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value, Func<SerializeReadNode, T> onCreate)
    {
        int index = 0;
        foreach (T item in value)
        {
            try
            {
                item.OnSerialize(this, SerializeMode.PostLoad);
            }
            catch (Exception ex)
            {
                AddError($"Failed to post-load list item at index {index} for key '{key}': {ex}");
            }
            index++;
        }
    }

    /// <summary>
    /// No-op for post-load: dictionaries of primitive or binary data are not processed.
    /// </summary>
    public override void BindDictionary<TValue>(string key, IDictionary<string, TValue> value)
    {
        // Intentionally empty
    }

    /// <summary>
    /// No-op for post-load: dictionaries of primitive or binary data are not processed.
    /// </summary>
    public override void BindDictionary(string key, IDictionary<string, string> value)
    {
        // Intentionally empty
    }

    /// <summary>
    /// No-op for post-load: dictionaries of primitive or binary data are not processed.
    /// </summary>
    public override void BindDictionary(string key, IDictionary<string, byte[]> value)
    {
        // Intentionally empty
    }
}


