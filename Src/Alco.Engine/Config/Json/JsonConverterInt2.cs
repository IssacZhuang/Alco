using System;
using System.Text.Json;

namespace Alco.Engine;

/// <summary>
/// JSON converter for Alco.int2 type.
/// Serializes int2 as a JSON object of two integer values in the format {"x": 1, "y": 2}.
/// </summary>
public unsafe class JsonConverterInt2 : BaseJsonConverterIntVector<int2>
{
    private static readonly string[] ComponentNames = { "x", "y" };

    /// <summary>
    /// Reads a JSON object and converts it to an int2.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (int2).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>An int2 instance containing the deserialized x and y values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected object format.</exception>
    public override int2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int* array = stackalloc int[2];
        ReadIntObject(ref reader, array, ComponentNames);
        return new int2(array[0], array[1]);
    }

    /// <summary>
    /// Writes an int2 value as a JSON object.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The int2 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, int2 value, JsonSerializerOptions options)
    {
        // The int2 can be used as an int array based on the memory layout of the struct
        int* ptr = (int*)&value;
        WriteIntObject(writer, ptr, ComponentNames);
    }
}