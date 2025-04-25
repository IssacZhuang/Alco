using System.Diagnostics.CodeAnalysis;

namespace Alco.Engine;

public interface IConfigReferenceResolver
{
    public bool TryResolve(
        string id,
        string propertyName,
        Type propertyType,
        [NotNullWhen(true)] out Configable? config);
}

