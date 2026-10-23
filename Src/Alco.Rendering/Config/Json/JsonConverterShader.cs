using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.Rendering;

/// <summary>
/// Converts <see cref="Shader"/> references to and from shader module names, resolved
/// through the shader system; an empty or whitespace string reads as null.
/// </summary>
public class JsonConverterShader : JsonConverter<Shader>
{
    private readonly ShaderSystem _shaderSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonConverterShader"/> class.
    /// </summary>
    /// <param name="shaderSystem">The shader system used to resolve shaders.</param>
    public JsonConverterShader(ShaderSystem shaderSystem)
    {
        _shaderSystem = shaderSystem;
    }

    /// <summary>
    /// Reads a shader module name and resolves it through the shader system.
    /// </summary>
    /// <param name="reader">The JSON reader positioned at the shader reference.</param>
    /// <param name="typeToConvert">The target type.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The resolved shader, or null for an empty or whitespace name.</returns>
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

    /// <summary>
    /// Writes the shader's module name.
    /// </summary>
    /// <param name="writer">The target JSON writer.</param>
    /// <param name="value">The shader to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, Shader value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }
}
