using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.IO;

/// <summary>
/// Base JSON converter for Half vector types.
/// The value will be serialized as a number array like [x,y,z,w].
/// </summary>
public unsafe abstract class BaseJsonConverterHalfVector<T> : JsonConverter<T>
{
    /// <summary>
    /// Read a Half array from the reader.
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// <param name="array">The array to read the Half values into.</param>
    /// <param name="length">The expected length of the array.</param>
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
    /// Write a Half array to the writer.
    /// </summary>
    /// <param name="writer">The json writer.</param>
    /// <param name="array">The array of Half values to write.</param>
    /// <param name="length">The length of the array.</param>
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