using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;
public interface IConfigReferenceResolver
{
    public bool TryResolve(
        string id,
        string propertyName,
        Type propertyType,
        [NotNullWhen(true)] out BaseConfig? config);
}

