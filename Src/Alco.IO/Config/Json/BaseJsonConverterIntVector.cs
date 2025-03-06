using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.IO;

/// <summary>
/// Base JSON converter for integer vector types.
/// The value will be serialized as a number array like [x,y,z,w].
/// </summary>
public unsafe abstract class BaseJsonConverterIntVector<T> : JsonConverter<T>
{
    /// <summary>
    /// Read an integer array from the reader.
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// <param name="array">The array to read the integer values into.</param>
    /// <param name="length">The expected length of the array.</param>
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
    /// Write an integer array to the writer.
    /// </summary>
    /// <param name="writer">The json writer.</param>
    /// <param name="array">The array of integer values to write.</param>
    /// <param name="length">The length of the array.</param>
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