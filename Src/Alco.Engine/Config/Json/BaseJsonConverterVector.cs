using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// Json converter for VectorN.
/// The value will be serialized as a JSON object like {"x":1.0,"y":2.0,"z":3.0,"w":4.0}.
/// </summary>
public unsafe abstract class BaseJsonConverterVector<T> : JsonConverter<T>
{
    /// <summary>
    /// Read a float array from the reader expecting a JSON object with component properties.
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// <param name="array">The array to read the float values into.</param>
    /// <param name="componentNames">The component names (e.g., ["x", "y", "z", "w"]).</param>
    protected void ReadFloatObject(ref Utf8JsonReader reader, float* array, string[] componentNames)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object when reading VectorN");
        }

        // Initialize all components to zero
        for (int i = 0; i < componentNames.Length; i++)
        {
            array[i] = 0.0f;
        }

        reader.Read();

        while (reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name when reading VectorN");
            }

            string propertyName = reader.GetString()!.ToLowerInvariant();
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException($"Expected number value for property '{propertyName}' when reading VectorN");
            }

            float value = reader.GetSingle();

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
                throw new JsonException($"Unknown property '{propertyName}' when reading VectorN");
            }

            reader.Read();
        }
    }

    /// <summary>
    /// Write a float array as a JSON object with component properties.
    /// </summary>
    /// <param name="writer">The json writer.</param>
    /// <param name="array">The float array to write.</param>
    /// <param name="componentNames">The component names (e.g., ["x", "y", "z", "w"]).</param>
    protected void WriteFloatObject(Utf8JsonWriter writer, float* array, string[] componentNames)
    {
        writer.WriteStartObject();
        for (int i = 0; i < componentNames.Length; i++)
        {
            writer.WriteNumber(componentNames[i], array[i]);
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// Read a float array from the reader (legacy array format support).
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// <param name="array">The array to read the float values into.</param>
    /// <param name="length">The number of float element in the array.</param>
    [Obsolete("Use ReadFloatObject for object format. This method is kept for backward compatibility.")]
    protected void ReadFloatArray(ref Utf8JsonReader reader, float* array, int length)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array when reading VectorN");
        }

        reader.Read();

        for (int i = 0; i < length; i++)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                array[i] = reader.GetSingle();
                reader.Read();
            }
            else
            {
                throw new JsonException($"Expected {length} float values in the array, Got {i} float values.");
            }
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("Expected end of array when reading VectorN");
        }
    }

    /// <summary>
    /// Write a float array as a JSON array (legacy array format support).
    /// </summary>
    /// <param name="writer">The json writer.</param>
    /// <param name="array">The float array to write.</param>
    /// <param name="length">The number of elements to write.</param>
    [Obsolete("Use WriteFloatObject for object format. This method is kept for backward compatibility.")]
    protected void WriteFloatArray(Utf8JsonWriter writer, float* array, int length)
    {
        writer.WriteStartArray();
        for (int i = 0; i < length; i++)
        {
            writer.WriteNumberValue(array[i]);
        }
        writer.WriteEndArray();
    }
}
