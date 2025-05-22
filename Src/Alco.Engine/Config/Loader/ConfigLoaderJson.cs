using System.Text;
using System.Text.Json;

namespace Alco.Engine;

public class ConfigLoaderJson : IConfigLoader
{
    private readonly JsonSerializerOptions _options;
    private readonly ConfigReferenceResolver _configReferenceResolver;

    public ConfigLoaderJson(JsonSerializerOptions options, ConfigReferenceResolver configReferenceResolver)
    {
        _options = options;
        _configReferenceResolver = configReferenceResolver;
    }

    public string Name => "Config.Json";

    public IReadOnlyList<string> FileExtensions => [".json"];

    public Configable CreateConfig(string name, ReadOnlySpan<byte> data)
    {
        Configable config = JsonSerializer.Deserialize<Configable>(data, _options) ??
            throw new InvalidOperationException($"Failed to deserialize {name}");

        config.Id = name;
        _configReferenceResolver.AddLoadingConfig(name, config);

        try
        {
            _configReferenceResolver.ResolveReferenceFor(config);
            return config;
        }
        catch
        {
            throw;
        }
        finally
        {
            _configReferenceResolver.RemoveLoadingConfig(name);
        }
    }
}
