using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Rendering;

/// <summary>
/// JSON converter for authored <see cref="ShaderValue"/> entries accepting numbers,
/// booleans, hex color strings, numeric arrays, and vector/color component objects.
/// </summary>
public class JsonConverterShaderValue : JsonConverter<ShaderValue>
{
    private static readonly string[] VectorNames = ["x", "y", "z", "w"];
    private static readonly string[] ColorNames = ["r", "g", "b", "a"];

    /// <inheritdoc/>
    public override ShaderValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
            case JsonTokenType.False:
                return reader.GetBoolean();

            case JsonTokenType.Number:
                if (reader.TryGetInt32(out int intValue))
                    return intValue;
                return reader.GetSingle();

            case JsonTokenType.String:
            {
                string? hex = reader.GetString();
                if (hex != null && ColorFloat.TryParse(hex, out ColorFloat color))
                    return new Vector4(color.R, color.G, color.B, color.A);
                throw new JsonException($"Invalid hex color string '{hex}' for a material parameter value.");
            }

            case JsonTokenType.StartArray:
                return ReadArray(ref reader);

            case JsonTokenType.StartObject:
                return ReadComponents(ref reader);

            default:
                throw new JsonException(
                    "Expected a number, boolean, hex color string, component object, or numeric array for a material parameter value.");
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ShaderValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case ShaderValueKind.Bool32:
                writer.WriteBooleanValue(value.GetInt() != 0);
                break;

            case ShaderValueKind.Int32:
                if (value.ComponentCount > 1)
                {
                    writer.WriteStartArray();
                    for (int i = 0; i < value.ComponentCount; i++)
                        writer.WriteNumberValue(value.GetInt(i));
                    writer.WriteEndArray();
                }
                else
                {
                    writer.WriteNumberValue(value.GetInt());
                }
                break;

            case ShaderValueKind.UInt32:
                if (value.ComponentCount > 1)
                {
                    writer.WriteStartArray();
                    for (int i = 0; i < value.ComponentCount; i++)
                        writer.WriteNumberValue(value.GetUInt(i));
                    writer.WriteEndArray();
                }
                else
                {
                    writer.WriteNumberValue(value.GetUInt());
                }
                break;

            default:
                ReadOnlySpan<float> floats = value.GetFloats();
                if (floats.Length == 1)
                {
                    writer.WriteNumberValue(floats[0]);
                }
                else if (floats.Length <= 4)
                {
                    writer.WriteStartObject();
                    for (int i = 0; i < floats.Length; i++)
                        writer.WriteNumber(VectorNames[i], floats[i]);
                    writer.WriteEndObject();
                }
                else
                {
                    writer.WriteStartArray();
                    for (int i = 0; i < floats.Length; i++)
                        writer.WriteNumberValue(floats[i]);
                    writer.WriteEndArray();
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
                throw new JsonException("Expected a number element in a material parameter array value.");
            elements.Add(reader.GetSingle());
        }
        return ShaderValue.Floats([.. elements]);
    }

    private static ShaderValue ReadComponents(ref Utf8JsonReader reader)
    {
        Span<float> components = stackalloc float[4];
        int maxIndex = 0;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected a component name when reading a material parameter value.");
            string propertyName = reader.GetString() ?? string.Empty;
            reader.Read();
            if (reader.TokenType != JsonTokenType.Number)
                throw new JsonException($"Expected a number for component '{propertyName}' of a material parameter value.");
            float value = reader.GetSingle();

            int index = IndexOf(VectorNames, propertyName);
            if (index < 0)
                index = IndexOf(ColorNames, propertyName);
            if (index < 0)
                throw new JsonException($"Unknown component '{propertyName}' of a material parameter value.");
            components[index] = value;
            maxIndex = Math.Max(maxIndex, index + 1);
        }

        return ShaderValue.Floats(components, maxIndex > 0 ? maxIndex : 1);
    }

    private static int IndexOf(string[] names, string propertyName)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i].Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
