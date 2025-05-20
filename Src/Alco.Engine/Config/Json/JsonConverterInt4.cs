using System;
using System.Text.Json;

namespace Alco.Engine;

/// <summary>
/// JSON converter for Alco.int4 type.
/// Serializes int4 as a JSON array of four integer values in the format [x, y, z, w].
/// </summary>
public unsafe class JsonConverterInt4 : BaseJsonConverterIntVector<int4>
{
    /// <summary>
    /// Reads a JSON array and converts it to an int4.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (int4).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>An int4 instance containing the deserialized x, y, z, and w values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected array format.</exception>
    public override int4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int* array = stackalloc int[4];
        ReadIntArray(ref reader, array, 4);
        return new int4(array[0], array[1], array[2], array[3]);
    }

    /// <summary>
    /// Writes an int4 value as a JSON array.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The int4 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, int4 value, JsonSerializerOptions options)
    {
        // The int4 can be used as an int array based on the memory layout of the struct
        int* ptr = (int*)&value;
        WriteIntArray(writer, ptr, 4);
    }
}