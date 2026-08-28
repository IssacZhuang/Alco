using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Alco.Rendering;

public class PolymorphicJsonTypeResolver : DefaultJsonTypeInfoResolver
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Type[]> s_derivedTypesCache = new();

    private readonly FrozenSet<Type> _typeNeedDerived;

    public PolymorphicJsonTypeResolver()
    {
        HashSet<Type> typeNeedDerived = new();
        _typeNeedDerived = typeNeedDerived.ToFrozenSet();
    }

    public PolymorphicJsonTypeResolver(ReadOnlySpan<Type> typesNeedDerived)
    {
        HashSet<Type> typeNeedDerived = new();
        for (int i = 0; i < typesNeedDerived.Length; i++)
        {
            typeNeedDerived.Add(typesNeedDerived[i]);
        }
        _typeNeedDerived = typeNeedDerived.ToFrozenSet();
    }

    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = base.GetTypeInfo(type, options);
        if (_typeNeedDerived.Contains(type))
        {
            SetAllDerivedType(typeInfo);
        }

        return typeInfo;
    }


    private static void SetAllDerivedType(JsonTypeInfo typeInfo)
    {
        // Concrete types assignable to a base type never change within a process,
        // so the assembly-wide scan runs once per base type instead of once per
        // JsonSerializerOptions instance (each ConfigDatabase builds its own options).
        var derivedTypes = s_derivedTypesCache.GetOrAdd(typeInfo.Type, static baseType =>
            AppDomain.CurrentDomain.GetAssemblies()
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
                .Where(t => baseType.IsAssignableFrom(t) &&
                           !t.IsInterface &&
                           !t.IsAbstract)
                .ToArray());

        if (derivedTypes.Length > 0)
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type",
                IgnoreUnrecognizedTypeDiscriminators = false,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
            };


            foreach (Type derivedType in derivedTypes)
            {
                typeInfo.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(derivedType, derivedType.FullName ?? derivedType.Name));
            }
        }


    }
}