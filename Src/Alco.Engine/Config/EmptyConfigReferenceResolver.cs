using System;
using System.Diagnostics.CodeAnalysis;

namespace Alco.Engine;

public class EmptyConfigReferenceResolver : IConfigReferenceResolver
{
    public void AddLoadingConfig(string id, Configable config)
    {
        throw new NotImplementedException();
    }

    public void RemoveLoadingConfig(string id)
    {
        throw new NotImplementedException();
    }

    public void ResolveReferenceFor(Configable config)
    {
        throw new NotImplementedException();
    }

    public bool TryResolve(string id, Type propertyType, [NotNullWhen(true)] out Configable? config)
    {
        throw new NotImplementedException("EmptyConfigReferenceResolver does not resolve any config.");
    }
}
