using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// Base JSON converter for unsigned integer vector types.
/// The value will be serialized as a JSON object like {"x":1,"y":2,"z":3,"w":4}.
/// </summary>
public unsafe abstract class BaseJsonConverterUIntVector<T> : JsonConverter<T>
{
    /// <summary>
    /// Read an unsigned integer array from the reader expecting a JSON object with component properties.
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// <param name="array">The array to read the unsigned integer values into.</param>
    /// <param name="componentNames">The component names (e.g., ["x", "y", "z", "w"]).</param>
    protected void ReadUIntObject(ref Utf8JsonReader reader, uint* array, string[] componentNames)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            uint value = reader.GetUInt32();
            for (int i = 0; i < componentNames.Length; i++)
            {
                array[i] = value;
            }
            return;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object when reading unsigned integer vector");
        }

        // Initialize all components to zero
        for (int i = 0; i < componentNames.Length; i++)
        {
            array[i] = 0;
        }

        reader.Read();

        while (reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name when reading unsigned integer vector");
            }

            string propertyName = reader.GetString()!.ToLowerInvariant();
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException($"Expected number value for property '{propertyName}' when reading unsigned integer vector");
            }

            uint value = reader.GetUInt32();

            // Find matching component and set value
            bool found = false;
            for (int i = 0; i < componentNames.Length; i++)
            {
                if (componentNames[i].Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    array[i] = value;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new JsonException($"Unknown property '{propertyName}' when reading unsigned integer vector");
            }

            reader.Read();
        }
    }

    /// <summary>
    /// Write an unsigned integer array as a JSON object with component properties.
    /// </summary>
    /// <param name="writer">The json writer.</param>
    /// <param name="array">The unsigned integer array to write.</param>
    /// <param name="componentNames">The component names (e.g., ["x", "y", "z", "w"]).</param>
    protected void WriteUIntObject(Utf8JsonWriter writer, uint* array, string[] componentNames)
    {
        writer.WriteStartObject();
        for (int i = 0; i < componentNames.Length; i++)
        {
            writer.WriteNumber(componentNames[i], array[i]);
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// Read an unsigned integer array from the reader (legacy array format support).
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// <param name="array">The array to read the unsigned integer values into.</param>
    /// <param name="length">The expected length of the array.</param>
    [Obsolete("Use ReadUIntObject for object format. This method is kept for backward compatibility.")]
    protected void ReadUIntArray(ref Utf8JsonReader reader, uint* array, int length)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array when reading unsigned integer vector");
        }

        reader.Read();

        for (int i = 0; i < length; i++)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                array[i] = reader.GetUInt32();
                reader.Read();
            }
            else
            {
                throw new JsonException($"Expected {length} unsigned integer values in the array, Got {i} unsigned integer values.");
            }
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("Expected end of array when reading unsigned integer vector");
        }
    }

    /// <summary>
    /// Write an unsigned integer array to the writer (legacy array format support).
    /// </summary>
    /// <param name="writer">The json writer.</param>
    /// <param name="array">The array of unsigned integer values to write.</param>
    /// <param name="length">The length of the array.</param>
    [Obsolete("Use WriteUIntObject for object format. This method is kept for backward compatibility.")]
    protected void WriteUIntArray(Utf8JsonWriter writer, uint* array, int length)
    {
        writer.WriteStartArray();
        for (int i = 0; i < length; i++)
        {
            writer.WriteNumberValue(array[i]);
        }
        writer.WriteEndArray();
    }
}