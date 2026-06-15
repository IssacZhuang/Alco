using System.Text.Json.Serialization;

namespace Alco.LLM;

/// <summary>
/// Base type for structured, compact tool results that are reformatted before entering LLM history.
/// </summary>
/// <remarks>
/// <para>
/// A tool may return an <see cref="AgentToolResult"/> subtype instead of a plain <see cref="string"/>.
/// <see cref="ToolResultFormatter"/> then converts it to compact text for the model, while the raw
/// object is preserved on runtime events so UI/debug consumers do not lose structured data.
/// </para>
/// <para>
/// Plain <see cref="string"/> returns bypass this hierarchy entirely and enter history unchanged,
/// which lets tools opt in incrementally without breaking unmigrated ones.
/// </para>
/// </remarks>
public abstract record AgentToolResult;

/// <summary>
/// Operation-confirmation result. The model sees <see cref="Message"/> as plain text.
/// Use for place/set/remove style tools whose result is a fixed, short confirmation.
/// </summary>
/// <param name="Message">The confirmation message shown to the model verbatim.</param>
public sealed record ToolOk(
    [property: JsonPropertyName("message")] string Message) : AgentToolResult;

/// <summary>
/// List-enumeration result. The model sees compact JSON: <c>{"items","total","truncated","hint"}</c>.
/// Tools should apply business-aware pagination, filtering, and ordering before creating this result;
/// formatter length limits are only a final safety cap.
/// </summary>
/// <param name="Items">Pre-formatted compact item strings for the model-facing page or subset chosen by the tool.</param>
/// <param name="TotalCount">The true total before tool-side pagination, filtering, or truncation; may be greater than <see cref="Items"/> length.</param>
/// <param name="Truncated">Whether more data exists beyond <see cref="Items"/>.</param>
/// <param name="Hint">Optional guidance for querying the next page or a narrower result, e.g. <c>"Use offset:10 for more"</c>.</param>
public sealed record ToolList(
    [property: JsonPropertyName("items")] string[] Items,
    [property: JsonPropertyName("total")] int TotalCount,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("hint")] string? Hint = null) : AgentToolResult;

/// <summary>
/// Structured single-item result. <see cref="Data"/> is JSON-serialized for the model.
/// Do not pass a JSON <em>string</em>; pass a parsed object or
/// <see cref="System.Text.Json.Nodes.JsonNode"/> to avoid double-encoding.
/// Prefer compact model-facing DTOs, records, anonymous objects, primitives, or JSON nodes.
/// Do not pass live runtime objects with engine resources, cyclic references, or large object graphs.
/// </summary>
/// <param name="Data">The compact structured object to serialize as JSON for the model.</param>
public sealed record ToolData(
    [property: JsonPropertyName("data")] object Data) : AgentToolResult;

/// <summary>
/// Unified tool error. The model sees compact JSON <c>{"error","code"}</c>.
/// </summary>
/// <param name="Error">Human-readable error message.</param>
/// <param name="Code">Stable machine-readable error code, e.g. <c>"NO_GAME"</c>.</param>
public sealed record ToolError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("code")] string Code) : AgentToolResult;
