using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Json converter for authored material-parameter values (<see cref="ShaderValue"/>).
/// Accepted shapes: a JSON number (an integer without a fraction reads as
/// <see langword="int"/>, otherwise float), <c>true</c>/<c>false</c>, a hex color
/// string (<c>"#RRGGBB"</c> / <c>"#RRGGBBAA"</c>, as authored), a component object
/// — vector-style (<c>{"x": 1, ...}</c>) or color-style (<c>{"r": 1, ...}</c>),
/// filling as many components as the object names — or an array of numbers for
/// array members.
/// </summary>
public class JsonConverterShaderValue : JsonConverter<ShaderValue>
{
    private static readonly string[] VectorNames = ["x", "y", "z", "w"];
    private static readonly string[] ColorNames = ["r", "g", "b", "a"];

    public override ShaderValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
            case JsonTokenType.False:
                return reader.GetBoolean();

            case JsonTokenType.Number:
            {
                if (reader.TryGetInt32(out int intValue))
                {
                    return intValue;
                }
                return reader.GetSingle();
            }

            case JsonTokenType.String:
            {
                string? hex = reader.GetString();
                if (hex != null && ColorFloat.TryParse(hex, out ColorFloat color))
                {
                    return new Vector4(color.R, color.G, color.B, color.A);
                }
                throw new JsonException($"Invalid hex color string '{hex}' for a material parameter value.");
            }

            case JsonTokenType.StartArray:
                return ReadArray(ref reader);

            case JsonTokenType.StartObject:
                return ReadComponents(ref reader);

            default:
                throw new JsonException(
                    "Expected a number, a boolean, a hex color string, a component object or an array for a material parameter value.");
        }
    }

    public override void Write(Utf8JsonWriter writer, ShaderValue value, JsonSerializerOptions options)
    {
        // The engine never writes material assets; a faithful authoring dump of
        // the common shapes keeps the converter symmetric for tooling.
        switch (value.Kind)
        {
            case ShaderValueKind.Bool32:
                writer.WriteBooleanValue(value.GetInt() != 0);
                break;
            case ShaderValueKind.Int32:
            case ShaderValueKind.UInt32:
                if (value.ElementCount > 1)
                {
                    WriteInts(writer, value);
                }
                else
                {
                    writer.WriteNumberValue(value.GetInt());
                }
                break;
            default:
                if (value.ComponentCount == 16)
                {
                    writer.WriteStartArray();
                    foreach (float component in value.GetFloats())
                    {
                        writer.WriteNumberValue(component);
                    }
                    writer.WriteEndArray();
                }
                else if (value.ElementCount > 1)
                {
                    writer.WriteStartArray();
                    for (int element = 0; element < value.ElementCount; element++)
                    {
                        WriteFloats(writer, value.GetFloats(element));
                    }
                    writer.WriteEndArray();
                }
                else
                {
                    WriteFloats(writer, value.GetFloats());
                }
                break;
        }
    }

    private static ShaderValue ReadArray(ref Utf8JsonReader reader)
    {
        List<float> elements = [];
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException("Expected a number element in a material parameter array value.");
            }
            elements.Add(reader.GetSingle());
        }
        return ShaderValue.Floats([.. elements]);
    }

    private static ShaderValue ReadComponents(ref Utf8JsonReader reader)
    {
        float[] components = new float[4];
        int maxIndex = 0;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a component name when reading a material parameter value.");
            }
            string propertyName = reader.GetString() ?? string.Empty;
            reader.Read();
            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException($"Expected a number for component '{propertyName}' of a material parameter value.");
            }
            float value = reader.GetSingle();

            int index = IndexOf(VectorNames, propertyName);
            if (index < 0)
            {
                index = IndexOf(ColorNames, propertyName);
            }
            if (index < 0)
            {
                throw new JsonException($"Unknown component '{propertyName}' of a material parameter value.");
            }
            components[index] = value;
            maxIndex = Math.Max(maxIndex, index + 1);
        }
        return ShaderValue.Floats(components, maxIndex > 0 ? maxIndex : 1);
    }

    private static void WriteFloats(Utf8JsonWriter writer, ReadOnlySpan<float> components)
    {
        if (components.Length == 1)
        {
            writer.WriteNumberValue(components[0]);
            return;
        }
        string[] names = ["x", "y", "z", "w"];
        writer.WriteStartObject();
        for (int i = 0; i < components.Length && i < 4; i++)
        {
            writer.WriteNumber(names[i], components[i]);
        }
        writer.WriteEndObject();
    }

    private static void WriteInts(Utf8JsonWriter writer, ShaderValue value)
    {
        writer.WriteStartArray();
        for (int element = 0; element < value.ElementCount; element++)
        {
            writer.WriteNumberValue(value.GetInt(element));
        }
        writer.WriteEndArray();
    }

    private static int IndexOf(string[] names, string propertyName)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i].Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }
}
