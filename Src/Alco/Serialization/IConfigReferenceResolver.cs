using System.Diagnostics.CodeAnalysis;

namespace Alco;

public interface IConfigReferenceResolver
{
    public bool TryResolve(string id, [NotNullWhen(true)] out IConfig? config);
}

