using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

/// <summary>
/// Abstract base class for serialization nodes that provide binding methods for various data types.
/// Supports serialization and deserialization of primitive types, strings, enums, collections, and complex objects.
/// </summary>
public abstract class SerializeNode
{
    protected Action<string>? OnError {get; set;}

    /// <summary>
    /// Binds a value type (unmanaged) with an optional default value.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type to bind.</typeparam>
    /// <param name="key">The key identifier for the value in the serialization format.</param>
    /// <param name="value">Reference to the value to be serialized or deserialized.</param>
    /// <param name="default">The default value to use if the key is not found during deserialization.</param>
    public abstract void BindValue<T>(string key, ref T value, T @default = default) where T : unmanaged;

    /// <summary>
    /// Binds a string value with an optional default value.
    /// </summary>
    /// <param name="key">The key identifier for the string in the serialization format.</param>
    /// <param name="value">Reference to the string to be serialized or deserialized.</param>
    /// <param name="default">The default string value to use if the key is not found during deserialization.</param>
    public abstract void BindString(string key, ref string value, string @default = "");

    /// <summary>
    /// Binds a complex object that implements ISerializable for deep serialization.
    /// Note: This method requires an existing object instance and is primarily used for serialization
    /// or when deserializing into an already created object. For deserialization scenarios where
    /// you need to create new objects, use <see cref="SerializeNode.BindSerializableOptional"/> instead.
    /// </summary>
    /// <typeparam name="T">The type that implements ISerializable.</typeparam>
    /// <param name="key">The key identifier for the object in the serialization format.</param>
    /// <param name="value">The existing object to be serialized or deserialized into.</param>
    public abstract void BindSerializable<T>(string key, T value) where T : ISerializable;

    /// <summary>
    /// Binds a nullable complex object that implements ISerializable with a factory function for creation during deserialization.
    /// This method is preferred for deserialization scenarios where objects need to be created dynamically.
    /// The factory function is only called during deserialization when the value is present in the data.
    /// </summary>
    /// <typeparam name="T">The type that implements ISerializable.</typeparam>
    /// <param name="key">The key identifier for the object in the serialization format.</param>
    /// <param name="value">Reference to the nullable object to be serialized or deserialized.</param>
    /// <param name="onCreate">Factory function to create a new instance during deserialization when the value is not null.</param>
    public abstract void BindSerializableOptional<T>(string key, ref T? value, Func<SerializeReadNode, T> onCreate) where T : ISerializable;

    /// <summary>
    /// Binds an enumeration value with an optional default value.
    /// </summary>
    /// <typeparam name="T">The enum type to bind.</typeparam>
    /// <param name="key">The key identifier for the enum in the serialization format.</param>
    /// <param name="value">Reference to the enum value to be serialized or deserialized.</param>
    /// <param name="default">The default enum value to use if the key is not found during deserialization.</param>
    public abstract void BindEnum<T>(string key, ref T value, T @default = default) where T : struct, Enum;

    /// <summary>
    /// Binds a span of unmanaged memory for efficient serialization of contiguous data.
    /// </summary>
    /// <typeparam name="T">The unmanaged type contained in the memory span.</typeparam>
    /// <param name="key">The key identifier for the memory data in the serialization format.</param>
    /// <param name="memory">The span of memory to be serialized or deserialized.</param>
    public abstract void BindMemory<T>(string key, Span<T> memory) where T : unmanaged;

    /// <summary>
    /// Binds a collection of unmanaged value types.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type contained in the collection.</typeparam>
    /// <param name="key">The key identifier for the collection in the serialization format.</param>
    /// <param name="value">The collection of unmanaged values to be serialized or deserialized.</param>
    public abstract void BindCollection<T>(string key, ICollection<T> value) where T : unmanaged;

    /// <summary>
    /// Binds a collection of string values.
    /// </summary>
    /// <param name="key">The key identifier for the string collection in the serialization format.</param>
    /// <param name="value">The collection of strings to be serialized or deserialized.</param>
    public abstract void BindCollection(string key, ICollection<string> value);

    public abstract void BindArraySerializable<T>(string key, IReadOnlyList<T> value) where T : ISerializable;

    /// <summary>
    /// Binds a collection of complex objects that implement ISerializable for deep serialization.
    /// </summary>
    /// <typeparam name="T">The type that implements ISerializable and has a parameterless constructor.</typeparam>
    /// <param name="key">The key identifier for the collection in the serialization format.</param>
    /// <param name="value">The collection of serializable objects to be serialized or deserialized.</param>
    public abstract void BindCollectionSerializable<T>(string key, ICollection<T> value) where T : ISerializable, new();

    /// <summary>
    /// Binds a collection of complex objects that implement ISerializable for deep serialization.
    /// </summary>
    /// <typeparam name="T">The type that implements ISerializable and has a parameterless constructor.</typeparam>
    /// <param name="key">The key identifier for the collection in the serialization format.</param>
    /// <param name="value">The collection of serializable objects to be serialized or deserialized.</param>
    /// <param name="onCreate">Factory function to create a new instance during deserialization when the value is not null.</param>
    public abstract void BindCollectionSerializable<T>(string key, ICollection<T> value, Func<SerializeReadNode, T> onCreate) where T : ISerializable;

    /// <summary>
    /// Binds an array of unmanaged types by converting it to a span for memory binding.
    /// This is a convenience method that calls BindMemory with the array converted to a span.
    /// </summary>
    /// <typeparam name="T">The unmanaged type contained in the array.</typeparam>
    /// <param name="key">The key identifier for the array data in the serialization format.</param>
    /// <param name="array">The array to be serialized or deserialized.</param>
    public void BindMemory<T>(string key, T[] array) where T : unmanaged
    {
        BindMemory(key, array.AsSpan());
    }

    /// <summary>
    /// Binds a dictionary of unmanaged value types.
    /// </summary>
    /// <typeparam name="TValue">The unmanaged value type contained in the dictionary.</typeparam>
    /// <param name="key">The key identifier for the dictionary in the serialization format.</param>
    /// <param name="value">The dictionary of unmanaged values to be serialized or deserialized.</param>
    public abstract void BindDictionary<TValue>(string key, IDictionary<string, TValue> value) where TValue : unmanaged;

    /// <summary>
    /// Binds a dictionary of string values.
    /// </summary>
    /// <param name="key">The key identifier for the string dictionary in the serialization format.</param>
    /// <param name="value">The dictionary of strings to be serialized or deserialized.</param>
    public abstract void BindDictionary(string key, IDictionary<string, string> value);

    /// <summary>
    /// Binds a dictionary of binary data.
    /// </summary>
    /// <param name="key">The key identifier for the dictionary in the serialization format.</param>
    /// <param name="value">The dictionary of binary data to be serialized or deserialized.</param>
    public abstract void BindDictionary(string key, IDictionary<string, byte[]> value);


    public abstract void BindReference<T>(string key, ref T? referenceable) where T : IReferenceable;

    protected void AddError(string error)
    {
        if(OnError != null)
        {
            OnError(error);
        }
        else
        {
            Log.Error(error);
        }
    }
}