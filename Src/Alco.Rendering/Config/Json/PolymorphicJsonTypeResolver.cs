using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Alco.Rendering;

/// <summary>
/// An <see cref="IJsonTypeInfoResolver"/> that adds runtime <c>$type</c> polymorphism for
/// specified root types by discovering concrete derived types in loaded assemblies.
/// </summary>
public class PolymorphicJsonTypeResolver : IJsonTypeInfoResolver
{
    private static readonly DefaultJsonTypeInfoResolver s_reflectionResolver = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Type[]> s_derivedTypesCache = new();

    private readonly FrozenSet<Type> _typeNeedDerived;

    private readonly IJsonTypeInfoResolver _inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PolymorphicJsonTypeResolver"/> class
    /// using reflection-based metadata.
    /// </summary>
    /// <param name="typesNeedDerived">Root types that receive polymorphic derived types.</param>
    public PolymorphicJsonTypeResolver(ReadOnlySpan<Type> typesNeedDerived)
        : this(typesNeedDerived, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PolymorphicJsonTypeResolver"/> class.
    /// </summary>
    /// <param name="typesNeedDerived">Root types that receive polymorphic derived types.</param>
    /// <param name="innerResolver">The resolver that supplies base metadata; reflection-based when null.</param>
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

    /// <summary>
    /// Gets the contract metadata for a type, attaching polymorphism options for configured roots.
    /// </summary>
    /// <param name="type">The type to resolve.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The type metadata, or null when the inner resolver cannot provide it.</returns>
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
