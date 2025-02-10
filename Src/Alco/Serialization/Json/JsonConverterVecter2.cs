using System;
using System.Numerics;
using System.Text.Json;

namespace Alco;

/// <summary>
/// JSON converter for System.Numerics.Vector2 type.
/// Serializes Vector2 as a JSON array of two float values in the format [x, y].
/// </summary>
public unsafe class JsonConverterVector2 : BaseJsonConverterVector<Vector2>
{
    /// <summary>
    /// Reads a JSON array and converts it to a Vector2.
    /// </summary>
    /// <param name="reader">The UTF8 JSON reader to read from.</param>
    /// <param name="typeToConvert">The type to convert (Vector2).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>A Vector2 instance containing the deserialized x and y values.</returns>
    /// <exception cref="JsonException">Thrown when the JSON format is invalid or doesn't match the expected array format.</exception>
    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        float* array = stackalloc float[2];
        ReadFloatArray(ref reader, array, 2);
        return new Vector2(array[0], array[1]);
    }

    /// <summary>
    /// Writes a Vector2 value as a JSON array.
    /// </summary>
    /// <param name="writer">The UTF8 JSON writer to write to.</param>
    /// <param name="value">The Vector2 value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        //the vector2 can be used as a float array base on the memory layout of the struct
        float* ptr = (float*)&value;
        WriteFloatArray(writer, ptr, 2);
    }
}

