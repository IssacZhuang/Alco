using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.IO;

/// <summary>
/// Json converter for VectorN.
/// The value will be serialized as a number array like [x,y,z,w].
/// </summary>
public unsafe abstract class BaseJsonConverterVector<T> : JsonConverter<T>
{
    /// <summary>
    /// Read a float array from the reader.
    /// </summary>
    /// <param name="reader">The json reader.</param>
    /// /// <param name="array">The array to read the float values into.</param>
    /// <returns>The number of float element in the array.</returns>
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
