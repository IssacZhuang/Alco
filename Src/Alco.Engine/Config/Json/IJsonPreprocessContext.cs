using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Alco.Engine;

/// <summary>
/// Public context interface provided to handlers of <see cref="JsonPreprocessor.BeforeProcessJsonDocument"/>.
/// Allows retrieving mutable JSON nodes for editing during preprocessing.
/// </summary>
public interface IJsonPreprocessContext
{
	/// <summary>
	/// Tries to get a non-abstract document as a mutable <see cref="JsonNode"/> by its Id.
	/// Returned node is cached within the context and applied after the event completes.
	/// </summary>
	/// <param name="id">The document Id.</param>
	/// <param name="node">The resulting <see cref="JsonNode"/> when found.</param>
	/// <returns>True if found; otherwise false.</returns>
	bool TryGetDocumentNode(string id, [NotNullWhen(true)] out JsonNode? node);

	/// <summary>
	/// Tries to get an abstract document as a mutable <see cref="JsonNode"/> by its Id.
	/// Returned node is cached within the context and applied after the event completes.
	/// </summary>
	/// <param name="id">The abstract document Id.</param>
	/// <param name="node">The resulting <see cref="JsonNode"/> when found.</param>
	/// <returns>True if found; otherwise false.</returns>
	bool TryGetAbstractDocumentNode(string id, [NotNullWhen(true)] out JsonNode? node);
}

