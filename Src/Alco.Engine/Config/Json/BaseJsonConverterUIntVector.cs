using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Engine;

/// <summary>
/// Base JSON converter for unsigned integer vector types.
/// The value will be serialized as a number array like [x,y,z,w].
/// </summary>
public unsafe abstract class BaseJsonConverterUIntVector<T> : JsonConverter<T>
{
    /// <summary>
    /// Read an unsigned integer array from the reader.
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// <param name="array">The array to read the unsigned integer values into.</param>
    /// <param name="length">The expected length of the array.</param>
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
    /// Write an unsigned integer array to the writer.
    /// </summary>
    /// <param name="writer">The json writer.</param>
    /// <param name="array">The array of unsigned integer values to write.</param>
    /// <param name="length">The length of the array.</param>
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