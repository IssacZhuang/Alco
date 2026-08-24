using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Json converter for <see cref="Shader"/>: an asset references a shader by module
/// name string, or by a <c>{ "module": ..., "specialization": [...] }</c> object
/// when the entry point is generic and needs generic value arguments (e.g. the
/// fxaa module's <c>MainPS&lt;let Quality : int&gt;</c>). The reference resolves
/// (and validates) through the shader system at deserialization time, so a typo
/// fails at asset load with the file's context instead of at first use.
/// Specialization arguments accept strings, numbers and booleans (raw JSON text,
/// so <c>2</c> means "2" and <c>true</c> means "true"). Shaders intern per
/// (module, specialization), so repeated references share one instance. An
/// empty or whitespace string reads as null, for optional shader slots.
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
        if (reader.TokenType == JsonTokenType.String)
        {
            string? name = reader.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            return Resolve(name.Trim(), []);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected a shader module name string or a { module, specialization } object.");
        }

        using JsonDocument document = JsonDocument.ParseValue(ref reader);

        string? module = null;
        List<string>? specialization = null;
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            switch (property.Name)
            {
                case "module":
                    module = property.Value.GetString();
                    break;
                case "specialization":
                    specialization = ReadSpecialization(property.Value);
                    break;
                default:
                    throw new JsonException(
                        $"Unknown shader reference field '{property.Name}' (expected 'module', 'specialization').");
            }
        }

        if (string.IsNullOrWhiteSpace(module))
        {
            throw new JsonException("The shader reference object requires a non-empty 'module' field.");
        }

        return Resolve(module.Trim(), specialization?.ToArray() ?? []);
    }

    public override void Write(Utf8JsonWriter writer, Shader value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }

    /// <summary>
    /// Resolves one module (with optional specialization arguments) through the
    /// shader system; any resolution or compilation failure becomes a load-time
    /// file error (the loader wraps JsonException with the file's name).
    /// </summary>
    private Shader Resolve(string module, string[] specializationArgs)
    {
        try
        {
            return specializationArgs.Length == 0
                ? _shaderSystem.GetShader(module)
                : _shaderSystem.GetShader(module, specializationArgs);
        }
        catch (Exception exception)
        {
            throw new JsonException(
                $"Shader module '{module}' failed to resolve: {exception.Message}", exception);
        }
    }

    private static List<string> ReadSpecialization(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray().Select(SpecializationArg).ToList();
        }
        return [SpecializationArg(value)];
    }

    private static string SpecializationArg(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!.Trim(),
        // Scalars pass their raw JSON text through: 2 → "2", true → "true".
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => throw new JsonException(
            $"Specialization arguments must be strings, numbers or booleans, got '{value.ValueKind}'."),
    };
}
