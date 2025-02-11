using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Linq;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Reflection;

namespace Alco;

public class ConfigJsonTypeResolver : DefaultJsonTypeInfoResolver
{
    private readonly List<Assembly> _assemblies = new List<Assembly>();

    public void AddAssemblies(params ReadOnlySpan<Assembly> assemblies)
    {
        _assemblies.AddRange(assemblies);
    }

    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = base.GetTypeInfo(type, options);
        if (typeInfo.Type != typeof(IJsonConfig))
        {
            return typeInfo;
        }


        typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$type",
            IgnoreUnrecognizedTypeDiscriminators = false,
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
        };

        var derivedTypes = _assemblies
            .SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch
                {
                    return Array.Empty<Type>();
                }
            })
            .Where(t => typeof(IJsonConfig).IsAssignableFrom(t) &&
                       !t.IsInterface &&
                       !t.IsAbstract)
            .ToArray();

        foreach (Type derivedType in derivedTypes)
        {
            typeInfo.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(derivedType, derivedType.FullName ?? derivedType.Name));
        }

        return typeInfo;
    }
}