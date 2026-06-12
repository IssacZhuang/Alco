using System.Text.Json.Serialization;

namespace Alco.LLM;

/// <summary>
/// Represents a binary tool result that the HTTP adapter can return directly.
/// </summary>
public sealed class BinaryToolResult
{
    [JsonIgnore]
    public byte[] Data { get; }

    public string ContentType { get; }
    public string? FileDownloadName { get; }

    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Headers { get; }

    public int ByteLength => Data.Length;

    public BinaryToolResult(
        byte[] data,
        string contentType,
        string? fileDownloadName = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        Data = data;
        ContentType = contentType;
        FileDownloadName = fileDownloadName;
        Headers = headers ?? new Dictionary<string, string>();
    }
}
