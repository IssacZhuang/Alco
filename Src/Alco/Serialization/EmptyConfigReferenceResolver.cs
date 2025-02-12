using System;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

public class EmptyConfigReferenceResolver : IConfigReferenceResolver
{
    public bool TryResolve(string id, [NotNullWhen(true)] out IConfig? config)
    {
        throw new NotImplementedException("EmptyConfigReferenceResolver does not resolve any config.");
    }
}
