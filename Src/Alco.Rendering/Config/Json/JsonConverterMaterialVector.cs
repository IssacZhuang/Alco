using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Rendering;

/// <summary>
/// Json converter for material vector values (surface parameters and PBR factors).
/// Accepted shapes: a number (broadcast to every component), a hex color string
/// (<c>"#RRGGBB"</c> / <c>"#RRGGBBAA"</c>), or a component object — vector-style
/// (<c>{"x": 1, "y": 2, ...}</c>) or color-style (<c>{"r": 1, "g": 0.5, ...}</c>);
/// missing components read zero. Hex colors land as authored (sRGB byte values
/// normalized to [0, 1]; no linearization).
/// </summary>
public class JsonConverterMaterialVector4 : JsonConverter<Vector4>
{
    public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        float[] components = JsonConverterMaterialVector.ReadComponents(ref reader, 4);
        return new Vector4(components[0], components[1], components[2], components[3]);
    }

    public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("z", value.Z);
        writer.WriteNumber("w", value.W);
        writer.WriteEndObject();
    }
}

/// <summary>
/// The 3-component counterpart of <see cref="JsonConverterMaterialVector4"/> (hex
/// colors read rgb; alpha does not apply).
/// </summary>
public class JsonConverterMaterialVector3 : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        float[] components = JsonConverterMaterialVector.ReadComponents(ref reader, 3);
        return new Vector3(components[0], components[1], components[2]);
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("z", value.Z);
        writer.WriteEndObject();
    }
}

/// <summary>Shared token reading of the material vector converters.</summary>
internal static class JsonConverterMaterialVector
{
    private static readonly string[] VectorNames = ["x", "y", "z", "w"];
    private static readonly string[] ColorNames = ["r", "g", "b", "a"];

    public static float[] ReadComponents(ref Utf8JsonReader reader, int count)
    {
        float[] components = new float[count];

        if (reader.TokenType == JsonTokenType.Number)
        {
            float value = reader.GetSingle();
            Array.Fill(components, value);
            return components;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string? hex = reader.GetString();
            if (hex != null && ColorFloat.TryParse(hex, out ColorFloat color))
            {
                components[0] = color.R;
                if (count > 1) components[1] = color.G;
                if (count > 2) components[2] = color.B;
                if (count > 3) components[3] = color.A;
                return components;
            }
            throw new JsonException($"Invalid hex color string '{hex}' for a material vector value.");
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                "Expected a number, a hex color string or a component object for a material vector value.");
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a component name when reading a material vector value.");
            }
            string propertyName = reader.GetString() ?? string.Empty;
            reader.Read();
            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException($"Expected a number for component '{propertyName}' of a material vector value.");
            }
            float value = reader.GetSingle();

            int index = IndexOf(VectorNames, propertyName, count);
            if (index < 0)
            {
                index = IndexOf(ColorNames, propertyName, count);
            }
            if (index < 0)
            {
                throw new JsonException($"Unknown component '{propertyName}' of a material vector value.");
            }
            components[index] = value;
        }
        return components;
    }

    private static int IndexOf(string[] names, string propertyName, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (names[i].Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }
}
