using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Linq;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Reflection;

namespace Alco.Engine;

public class ConfigJsonTypeResolver : DefaultJsonTypeInfoResolver
{
    private readonly IConfigReferenceResolver _configResolver;

    public ConfigJsonTypeResolver(IConfigReferenceResolver configResolver)
    {
        ArgumentNullException.ThrowIfNull(configResolver);
        _configResolver = configResolver;
    }

    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = base.GetTypeInfo(type, options);
        if (typeInfo.Type.IsAssignableTo(typeof(Configable)))
        {
            SetAllDerivedType(typeInfo);
        }

        SetPropertiesToUseReferenceConverter(typeInfo);
        return typeInfo;
    }

    private void SetPropertiesToUseReferenceConverter(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
        {
            if (property.PropertyType.IsAssignableTo(typeof(Configable)))
            {
                property.CustomConverter = new JsonConverterConfigReference(
                    property.PropertyType,
                    _configResolver
                );
            }
        }
    }

    private static void SetAllDerivedType(JsonTypeInfo typeInfo)
    {
        typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$type",
            IgnoreUnrecognizedTypeDiscriminators = false,
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
        };

        //todo: performance optimization
        var derivedTypes = AppDomain.CurrentDomain.GetAssemblies()
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
            .Where(t => typeInfo.Type.IsAssignableFrom(t) &&
                       !t.IsInterface &&
                       !t.IsAbstract)
            .ToArray();

        foreach (Type derivedType in derivedTypes)
        {
            typeInfo.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(derivedType, derivedType.FullName ?? derivedType.Name));
        }
    }
}