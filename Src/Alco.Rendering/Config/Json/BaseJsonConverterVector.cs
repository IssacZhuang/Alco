using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Alco.Rendering;

/// <summary>
/// Json converter for VectorN.
/// The value will be serialized as a JSON object like {"x":1.0,"y":2.0,"z":3.0,"w":4.0}.
/// </summary>
public unsafe abstract class BaseJsonConverterVector<T> : JsonConverter<T>
{
    /// <summary>
    /// Creates a JSON Schema for a float vector object with named components.
    /// </summary>
    /// <param name="componentNames">The property names (e.g. ["x", "y"]).</param>
    /// <returns>A JSON Schema object node.</returns>
    protected static JsonNode CreateVectorSchema(string[] componentNames)
    {
        var properties = new JsonObject();
        for (int i = 0; i < componentNames.Length; i++)
        {
            properties[componentNames[i]] = new JsonObject { ["type"] = "number" };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
    }
    /// <summary>
    /// Read a float array from the reader expecting a JSON object with component properties.
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// <param name="array">The array to read the float values into.</param>
    /// <param name="componentNames">The component names (e.g., ["x", "y", "z", "w"]).</param>
    /// <param name="setDefaultValue">If true, initialize all components to zero.</param>
    protected void ReadFloatObject(ref Utf8JsonReader reader, float* array, ReadOnlySpan<string> componentNames, bool setDefaultValue = true)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            float value = reader.GetSingle();
            for (int i = 0; i < componentNames.Length; i++)
            {
                array[i] = value;
            }
            return;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object when reading VectorN");
        }

        if (setDefaultValue)
        {
            // Initialize all components to zero
            for (int i = 0; i < componentNames.Length; i++)
            {
                array[i] = 0.0f;
            }
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
}
