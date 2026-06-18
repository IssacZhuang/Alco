using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// Base JSON converter for integer vector types.
/// The value will be serialized as a JSON object like {"x":1,"y":2,"z":3,"w":4}.
/// </summary>
public unsafe abstract class BaseJsonConverterIntVector<T> : JsonConverter<T>
{
    /// <summary>
    /// Creates a JSON Schema for an integer vector object with named components.
    /// </summary>
    /// <param name="componentNames">The property names (e.g. ["x", "y"]).</param>
    /// <returns>A JSON Schema object node.</returns>
    protected static JsonNode CreateVectorSchema(string[] componentNames)
    {
        var properties = new JsonObject();
        for (int i = 0; i < componentNames.Length; i++)
        {
            properties[componentNames[i]] = new JsonObject { ["type"] = "integer" };
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
    }
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
}