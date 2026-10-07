using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alco.LLM;

/// <summary>
/// Converts <see cref="AgentToolResult"/> instances into compact, model-facing text before they
/// enter LLM history. Immutable and thread-safe after construction.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="AgentToolResult"/> instances pass through this formatter. Plain <see cref="string"/>
/// tool returns bypass it entirely and enter history unchanged, keeping unmigrated tools working
/// without modification.
/// </para>
/// <para>
/// The formatted value goes into LLM history only; runtime events preserve the raw
/// <see cref="AgentToolResult"/> so UI/debug consumers retain structured data.
/// </para>
/// </remarks>
public sealed class ToolResultFormatter
{
    /// <summary>
    /// Default hard cap on formatted structured output.
    /// </summary>
    public const int DefaultMaxFormattedLength = 8192;

    private const string TruncationMarker = "...[truncated]";

    private static readonly JsonSerializerOptions s_compactOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolResultFormatter"/> class.
    /// </summary>
    /// <param name="jsonOptions">Optional serializer options to reuse engine converters for tool data.</param>
    /// <param name="maxFormattedLength">Hard cap for model-facing tool result text.</param>
    public ToolResultFormatter(JsonSerializerOptions? jsonOptions = null, int maxFormattedLength = DefaultMaxFormattedLength)
    {
        if (maxFormattedLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFormattedLength), "Max formatted length must be greater than zero.");
        }

        _jsonOptions = CreateCompactOptions(jsonOptions);
        MaxFormattedLength = maxFormattedLength;
    }

    /// <summary>
    /// Gets the hard cap for formatted model-facing tool result text.
    /// </summary>
    public int MaxFormattedLength { get; }

    /// <summary>
    /// Formats an <see cref="AgentToolResult"/> into compact text for the model.
    /// </summary>
    /// <param name="result">The structured tool result to format.</param>
    /// <returns>Compact, model-facing text capped at <see cref="MaxFormattedLength"/>.</returns>
    public string Format(AgentToolResult result)
    {
        return result switch
        {
            ToolOk ok => ApplyTextCap(ok.Message),
            ToolList list => FormatList(list),
            ToolData data => FormatData(data),
            ToolError err => FormatError(err),
            _ => ApplyTextCap(result.ToString() ?? string.Empty),
        };
    }

    private static JsonSerializerOptions CreateCompactOptions(JsonSerializerOptions? jsonOptions)
    {
        if (jsonOptions == null)
        {
            return s_compactOptions;
        }

        var compactOptions = new JsonSerializerOptions(jsonOptions)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        compactOptions.PropertyNamingPolicy ??= JsonNamingPolicy.CamelCase;
        return compactOptions;
    }

    private string FormatList(ToolList list)
    {
        string formatted = JsonSerializer.Serialize(list, _jsonOptions);
        if (formatted.Length <= MaxFormattedLength)
        {
            return formatted;
        }

        int originalLength = formatted.Length;
        int itemCount = list.Items.Length;
        while (itemCount > 0)
        {
            itemCount /= 2;
            var items = new string[itemCount];
            Array.Copy(list.Items, items, itemCount);

            var truncated = new ToolList(
                items,
                list.TotalCount,
                true,
                list.Hint ?? $"Showing {itemCount} of {list.TotalCount} items; refine the query for more.");

            formatted = JsonSerializer.Serialize(truncated, _jsonOptions);
            if (formatted.Length <= MaxFormattedLength)
            {
                return formatted;
            }
        }

        return FormatTruncatedJson("Tool list omitted because formatted JSON exceeded the size limit.", originalLength);
    }

    private string FormatData(ToolData data)
    {
        string formatted = JsonSerializer.Serialize(data.Data, _jsonOptions);
        if (formatted.Length <= MaxFormattedLength)
        {
            return formatted;
        }

        return FormatTruncatedJson("Tool data omitted because formatted JSON exceeded the size limit.", formatted.Length);
    }

    private string FormatError(ToolError error)
    {
        string formatted = JsonSerializer.Serialize(new
        {
            error = error.Error,
            code = error.Code,
        }, _jsonOptions);
        if (formatted.Length <= MaxFormattedLength)
        {
            return formatted;
        }

        return JsonSerializer.Serialize(new
        {
            error = ApplyTextCap(error.Error),
            code = error.Code,
            truncated = true,
        }, _jsonOptions);
    }

    private string FormatTruncatedJson(string message, int originalLength)
    {
        return JsonSerializer.Serialize(new
        {
            truncated = true,
            originalLength,
            message,
        }, _jsonOptions);
    }

    private string ApplyTextCap(string formatted)
    {
        if (formatted.Length <= MaxFormattedLength)
        {
            return formatted;
        }

        int sliceLength = Math.Max(0, MaxFormattedLength - TruncationMarker.Length);
        return string.Concat(formatted.AsSpan(0, sliceLength), TruncationMarker);
    }
}
