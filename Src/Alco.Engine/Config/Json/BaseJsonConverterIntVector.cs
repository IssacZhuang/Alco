using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// Base JSON converter for integer vector types.
/// The value will be serialized as a JSON object like {"x":1,"y":2,"z":3,"w":4}.
/// </summary>
public unsafe abstract class BaseJsonConverterIntVector<T> : JsonConverter<T>
{
    /// <summary>
    /// Read an integer array from the reader expecting a JSON object with component properties.
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// <param name="array">The array to read the integer values into.</param>
    /// <param name="componentNames">The component names (e.g., ["x", "y", "z", "w"]).</param>
    protected void ReadIntObject(ref Utf8JsonReader reader, int* array, string[] componentNames)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            int value = reader.GetInt32();
            for (int i = 0; i < componentNames.Length; i++)
            {
                array[i] = value;
            }
            return;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object when reading integer vector");
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
                throw new JsonException("Expected property name when reading integer vector");
            }

            string propertyName = reader.GetString()!.ToLowerInvariant();
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException($"Expected number value for property '{propertyName}' when reading integer vector");
            }

            int value = reader.GetInt32();

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
                throw new JsonException($"Unknown property '{propertyName}' when reading integer vector");
            }

            reader.Read();
        }
    }

    /// <summary>
    /// Write an integer array as a JSON object with component properties.
    /// </summary>
    /// <param name="writer">The json writer.</param>
    /// <param name="array">The integer array to write.</param>
    /// <param name="componentNames">The component names (e.g., ["x", "y", "z", "w"]).</param>
    protected void WriteIntObject(Utf8JsonWriter writer, int* array, string[] componentNames)
    {
        writer.WriteStartObject();
        for (int i = 0; i < componentNames.Length; i++)
        {
            writer.WriteNumber(componentNames[i], array[i]);
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// Read an integer array from the reader (legacy array format support).
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// <param name="array">The array to read the integer values into.</param>
    /// <param name="length">The expected length of the array.</param>
    [Obsolete("Use ReadIntObject for object format. This method is kept for backward compatibility.")]
    protected void ReadIntArray(ref Utf8JsonReader reader, int* array, int length)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array when reading integer vector");
        }

        reader.Read();

        for (int i = 0; i < length; i++)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                array[i] = reader.GetInt32();
                reader.Read();
            }
            else
            {
                throw new JsonException($"Expected {length} integer values in the array, Got {i} integer values.");
            }
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("Expected end of array when reading integer vector");
        }
    }

    /// <summary>
    /// Write an integer array to the writer (legacy array format support).
    /// </summary>
    /// <param name="writer">The json writer.</param>
    /// <param name="array">The array of integer values to write.</param>
    /// <param name="length">The length of the array.</param>
    [Obsolete("Use WriteIntObject for object format. This method is kept for backward compatibility.")]
    protected void WriteIntArray(Utf8JsonWriter writer, int* array, int length)
    {
        writer.WriteStartArray();
        for (int i = 0; i < length; i++)
        {
            writer.WriteNumberValue(array[i]);
        }
        writer.WriteEndArray();
    }
}