using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Rendering;

/// <summary>
/// Json converter for <see cref="Shader"/>: an asset references a shader by
/// module name string; the reference resolves (and validates) through the
/// shader system at deserialization time, so a typo fails at asset load with
/// the file's context instead of at first use. Variant axes are not part of the
/// reference — they are specialization arguments requested where the shader is
/// used (GetGraphicsPipeline/material construction), the way defines used to be.
/// An empty or whitespace string reads as null, for optional shader slots.
/// </summary>
public class JsonConverterShader : JsonConverter<Shader>
{
    private readonly ShaderSystem _shaderSystem;

    public JsonConverterShader(ShaderSystem shaderSystem)
    {
        _shaderSystem = shaderSystem;
    }

    public override Shader? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? name = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (name == null)
        {
            throw new JsonException("Expected a shader module name string.");
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            return _shaderSystem.GetShader(name.Trim());
        }
        catch (Exception exception)
        {
            throw new JsonException(
                $"Shader module '{name}' failed to resolve: {exception.Message}", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, Shader value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }
}
