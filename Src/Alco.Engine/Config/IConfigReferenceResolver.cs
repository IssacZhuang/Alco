using System.Diagnostics.CodeAnalysis;

namespace Alco.Engine;

public interface IConfigReferenceResolver
{
    public bool TryResolve(
        string id,
        string propertyName,
        Type propertyType,
        [NotNullWhen(true)] out Configable? config);

    public void AddLoadingConfig(string id, Configable config);
    public void RemoveLoadingConfig(string id);

    public void ResolveReferenceFor(Configable config);


}

