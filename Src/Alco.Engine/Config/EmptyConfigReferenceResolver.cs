using System;
using System.Diagnostics.CodeAnalysis;

namespace Alco.Engine;

public class EmptyConfigReferenceResolver : IConfigReferenceResolver
{
    public bool TryResolve(string id, string propertyName, Type propertyType, [NotNullWhen(true)] out Configable? config)
    {
        throw new NotImplementedException("EmptyConfigReferenceResolver does not resolve any config.");
    }
}
