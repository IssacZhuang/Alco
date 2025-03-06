using System;
using System.Text.Json;

namespace Alco.IO;

/// <summary>
/// JSON converter for Alco.int3 type.
/// Serializes int3 as a JSON array of three integer values in the format [x, y, z].
/// </summary>
public unsafe class JsonConverterInt3 : BaseJsonConverterIntVector<int3>
{
    /// <summary>
    /// Reads a JSON array and converts it to an int3.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (int3).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>An int3 instance containing the deserialized x, y, and z values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected array format.</exception>
    public override int3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        int* array = stackalloc int[3];
        ReadIntArray(ref reader, array, 3);
        return new int3(array[0], array[1], array[2]);
    }

    /// <summary>
    /// Writes an int3 value as a JSON array.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The int3 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, int3 value, JsonSerializerOptions options)
    {
        // The int3 can be used as an int array based on the memory layout of the struct
        int* ptr = (int*)&value;
        WriteIntArray(writer, ptr, 3);
    }
}