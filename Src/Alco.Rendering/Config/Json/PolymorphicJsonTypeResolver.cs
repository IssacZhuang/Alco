using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Alco.Rendering;

/// <summary>
/// A <see cref="IJsonTypeInfoResolver"/> that layers runtime $type polymorphism on top of
/// an inner resolver: for the requested root types it scans all loaded assemblies for
/// concrete derived types and attaches <see cref="JsonPolymorphismOptions"/> (discriminator
/// "$type" = derived type full name). The inner resolver defaults to reflection-based
/// metadata (<see cref="DefaultJsonTypeInfoResolver"/>); under NativeAOT pass a source
/// generated <see cref="JsonSerializerContext"/> instead, whose metadata is compiled
/// ahead of time (attach-on-top only works through the per-options infos the context
/// resolver interface returns, so route all calls through this class).
/// </summary>
public class PolymorphicJsonTypeResolver : IJsonTypeInfoResolver
{
    private static readonly DefaultJsonTypeInfoResolver s_reflectionResolver = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Type[]> s_derivedTypesCache = new();

    private readonly FrozenSet<Type> _typeNeedDerived;

    private readonly IJsonTypeInfoResolver _inner;

    public PolymorphicJsonTypeResolver(ReadOnlySpan<Type> typesNeedDerived)
        : this(typesNeedDerived, null)
    {
    }

    public PolymorphicJsonTypeResolver(ReadOnlySpan<Type> typesNeedDerived, IJsonTypeInfoResolver? innerResolver)
    {
        HashSet<Type> typeNeedDerived = new();
        for (int i = 0; i < typesNeedDerived.Length; i++)
        {
            typeNeedDerived.Add(typesNeedDerived[i]);
        }
        _typeNeedDerived = typeNeedDerived.ToFrozenSet();
        _inner = innerResolver ?? s_reflectionResolver;
    }

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = _inner.GetTypeInfo(type, options);
        if (typeInfo != null && _typeNeedDerived.Contains(type))
        {
            SetAllDerivedType(typeInfo, options, _inner);
        }

        return typeInfo;
    }


    private void SetAllDerivedType(JsonTypeInfo typeInfo, JsonSerializerOptions options, IJsonTypeInfoResolver inner)
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

        // Only expose derived types the inner resolver can actually provide metadata
        // for: the runtime scan sees every loaded assembly (test fixtures, editor
        // tooling), while a source generated context only knows its registered types.
        // Deserializing a polymorphic root resolves metadata for the whole derived
        // set up front, so one unresolvable type would fail every payload.
        JsonDerivedType[] resolvable = derivedTypes
            .Where(t => inner.GetTypeInfo(t, options) != null)
            .Select(t => new JsonDerivedType(t, t.FullName ?? t.Name))
            .ToArray();

        if (resolvable.Length > 0)
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type",
                IgnoreUnrecognizedTypeDiscriminators = false,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
            };


            foreach (JsonDerivedType derivedType in resolvable)
            {
                typeInfo.PolymorphismOptions.DerivedTypes.Add(derivedType);
            }
        }


    }
}
