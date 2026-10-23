using System.Text.Json.Serialization;

namespace Alco.AgentControlProtocol;

/// <summary>
/// Represents a binary tool result that the HTTP adapter can return directly.
/// </summary>
public sealed class BinaryToolResult
{
    /// <summary>
    /// Gets the raw binary payload.
    /// </summary>
    [JsonIgnore]
    public byte[] Data { get; }

    /// <summary>
    /// Gets the MIME content type of <see cref="Data"/>.
    /// </summary>
    public string ContentType { get; }
    /// <summary>
    /// Gets the suggested file name when the result is downloaded.
    /// </summary>
    public string? FileDownloadName { get; }

    /// <summary>
    /// Gets additional HTTP headers to include in the response.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>
    /// Gets the size of the payload in bytes.
    /// </summary>
    public int ByteLength => Data.Length;

    /// <summary>
    /// Initializes a new instance of <see cref="BinaryToolResult"/>.
    /// </summary>
    /// <param name="data">The raw binary payload.</param>
    /// <param name="contentType">The MIME content type of the payload.</param>
    /// <param name="fileDownloadName">Optional suggested file name for downloads.</param>
    /// <param name="headers">Optional additional HTTP response headers.</param>
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
