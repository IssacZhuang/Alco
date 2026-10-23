using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Alco.LLM;

/// <summary>
/// Patches chat completion requests to DeepSeek (and other reasoning-model) APIs
/// by injecting <c>reasoning_content: ""</c> on every assistant message that has
/// <c>tool_calls</c> but lacks <c>reasoning_content</c>.
/// </summary>
/// <remarks>
/// The API validates field presence rather than content, so an empty <c>reasoning_content</c>
/// is sufficient for assistant messages that carry <c>tool_calls</c>.
/// </remarks>
public class ReasoningContentHandler : DelegatingHandler
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ReasoningContentHandler"/> class.
    /// </summary>
    public ReasoningContentHandler()
        : base(new HttpClientHandler())
    {
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await PatchRequestBodyAsync(request, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }

    private static async Task PatchRequestBodyAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is null)
            return;

        var body = await request.Content.ReadAsStringAsync(ct);

        JsonNode? root;
        try { root = JsonNode.Parse(body); }
        catch { return; }
        if (root is null)
            return;

        var messages = root["messages"]?.AsArray();
        if (messages is null)
            return;

        var modified = false;
        foreach (var msg in messages)
        {
            if (msg is null)
                continue;
            if (msg["role"]?.GetValue<string>() != "assistant")
                continue;
            if (msg["tool_calls"] is null)
                continue;
            if (msg["reasoning_content"] is not null)
                continue;

            msg["reasoning_content"] = "";
            modified = true;
        }

        if (modified)
        {
            request.Content = new StringContent(
                root.ToJsonString(), Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json"));
        }
    }
}
