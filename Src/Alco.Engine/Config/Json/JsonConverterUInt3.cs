using System;
using System.Text.Json;

namespace Alco.Engine;

/// <summary>
/// JSON converter for Alco.uint3 type.
/// Serializes uint3 as a JSON object of three unsigned integer values in the format {"x": 1, "y": 2, "z": 3}.
/// </summary>
public unsafe class JsonConverterUInt3 : BaseJsonConverterUIntVector<uint3>
{
    private static readonly string[] ComponentNames = { "x", "y", "z" };

    /// <summary>
    /// Reads a JSON object and converts it to a uint3.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (uint3).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A uint3 instance containing the deserialized x, y, and z values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected object format.</exception>
    public override uint3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        uint* array = stackalloc uint[3];
        ReadUIntObject(ref reader, array, ComponentNames);
        return new uint3(array[0], array[1], array[2]);
    }

    /// <summary>
    /// Writes a uint3 value as a JSON object.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The uint3 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, uint3 value, JsonSerializerOptions options)
    {
        // The uint3 can be used as a uint array based on the memory layout of the struct
        uint* ptr = (uint*)&value;
        WriteUIntObject(writer, ptr, ComponentNames);
    }
}