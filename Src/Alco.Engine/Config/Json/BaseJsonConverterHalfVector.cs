using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// Base JSON converter for Half vector types.
/// The value will be serialized as a JSON object like {"x":1.0,"y":2.0,"z":3.0,"w":4.0}.
/// </summary>
public unsafe abstract class BaseJsonConverterHalfVector<T> : JsonConverter<T>
{
    /// <summary>
    /// Read a Half array from the reader expecting a JSON object with component properties.
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// <param name="array">The array to read the Half values into.</param>
    /// <param name="componentNames">The component names (e.g., ["x", "y", "z", "w"]).</param>
    protected void ReadHalfObject(ref Utf8JsonReader reader, Half* array, string[] componentNames)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object when reading Half vector");
        }

        // Initialize all components to zero
        for (int i = 0; i < componentNames.Length; i++)
        {
            array[i] = (Half)0.0f;
        }

        reader.Read();

        while (reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name when reading Half vector");
            }

            string propertyName = reader.GetString()!.ToLowerInvariant();
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException($"Expected number value for property '{propertyName}' when reading Half vector");
            }

            float value = reader.GetSingle();

            // Find matching component and set value
            bool found = false;
            for (int i = 0; i < componentNames.Length; i++)
            {
                if (componentNames[i].Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    array[i] = (Half)value;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new JsonException($"Unknown property '{propertyName}' when reading Half vector");
            }

            reader.Read();
        }
    }

    /// <summary>
    /// Write a Half array as a JSON object with component properties.
    /// </summary>
    /// <param name="writer">The json writer.</param>
    /// <param name="array">The Half array to write.</param>
    /// <param name="componentNames">The component names (e.g., ["x", "y", "z", "w"]).</param>
    protected void WriteHalfObject(Utf8JsonWriter writer, Half* array, string[] componentNames)
    {
        writer.WriteStartObject();
        for (int i = 0; i < componentNames.Length; i++)
        {
            // Convert Half to float for JSON serialization
            writer.WriteNumber(componentNames[i], (float)array[i]);
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// Read a Half array from the reader (legacy array format support).
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// <param name="array">The array to read the Half values into.</param>
    /// <param name="length">The expected length of the array.</param>
    [Obsolete("Use ReadHalfObject for object format. This method is kept for backward compatibility.")]
    protected void ReadHalfArray(ref Utf8JsonReader reader, Half* array, int length)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array when reading Half vector");
        }

        reader.Read();

        for (int i = 0; i < length; i++)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                // Convert from float to Half
                array[i] = (Half)reader.GetSingle();
                reader.Read();
            }
            else
            {
                throw new JsonException($"Expected {length} Half values in the array, Got {i} Half values.");
            }
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("Expected end of array when reading Half vector");
        }
    }

    /// <summary>
    /// Write a Half array to the writer (legacy array format support).
    /// </summary>
    /// <param name="writer">The json writer.</param>
    /// <param name="array">The array of Half values to write.</param>
    /// <param name="length">The length of the array.</param>
    [Obsolete("Use WriteHalfObject for object format. This method is kept for backward compatibility.")]
    protected void WriteHalfArray(Utf8JsonWriter writer, Half* array, int length)
    {
        writer.WriteStartArray();
        for (int i = 0; i < length; i++)
        {
            // Convert Half to float for JSON serialization
            writer.WriteNumberValue((float)array[i]);
        }
        writer.WriteEndArray();
    }
}