using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Alco.LLM;

/// <summary>
/// Configuration for LLMSession.
/// </summary>
public class LLMSessionConfig
{
    /// <summary>
    /// The system prompt to initialize the chat history with.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Controls the temperature for the LLM response.
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Controls whether to automatically invoke kernel functions (tools).
    /// </summary>
    public bool AutoInvokeKernelFunctions { get; set; } = true;
}

/// <summary>
/// Represents the session for LLM operations, wrapping the Semantic Kernel and ChatHistory.
/// </summary>
public sealed class LLMSession
{
    private readonly IChatCompletionService _chatCompletionService;
    private readonly ChatHistory _chatHistory;
    private readonly OpenAIPromptExecutionSettings _promptExecutionSettings;
    private readonly Kernel _kernel;

    /// <summary>
    /// Initializes a new instance of the <see cref="LLMSession"/> class.
    /// </summary>
    /// <param name="kernel">The Semantic Kernel instance to use.</param>
    /// <param name="config">Optional configuration for the session.</param>
    public LLMSession(Kernel kernel, LLMSessionConfig? config = null)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        
        config ??= new LLMSessionConfig();

        _promptExecutionSettings = new OpenAIPromptExecutionSettings()
        {
            ToolCallBehavior = config.AutoInvokeKernelFunctions ? ToolCallBehavior.AutoInvokeKernelFunctions : null,
            Temperature = config.Temperature,
        };

        _chatHistory = new ChatHistory();
        if (!string.IsNullOrEmpty(config.SystemPrompt))
        {
            _chatHistory.AddSystemMessage(config.SystemPrompt);
        }
    }

    public async Task<string> ChatAsync(string message)
    {
        _chatHistory.AddUserMessage(message);
        var result = await _chatCompletionService.GetChatMessageContentAsync(_chatHistory, _promptExecutionSettings, _kernel);
        return result.ToString();
    }

    public async IAsyncEnumerable<string> ChatStreamingAsync(string message)
    {
        _chatHistory.AddUserMessage(message);
        var stream = _chatCompletionService.GetStreamingChatMessageContentsAsync(_chatHistory, _promptExecutionSettings, _kernel);


        var fullContent = new StringBuilder();

        await foreach (var content in stream)
        {
            // Handle regular text content
            if (!string.IsNullOrEmpty(content.Content))
            {
                fullContent.Append(content.Content);
                yield return content.Content;
            }

            // Handle tool calls in streaming
            foreach (var item in content.Items)
            {
                if (item is StreamingFunctionCallUpdateContent functionCall)
                {
                    if (!string.IsNullOrEmpty(functionCall.Name))
                    {
                        string toolNotification = $"{functionCall.Name}]";
                        fullContent.Append(toolNotification);
                        yield return toolNotification;
                    }

                    if (!string.IsNullOrEmpty(functionCall.Arguments))
                    {
                        string toolNotification = $"{functionCall.Arguments}";
                        fullContent.Append(toolNotification);
                        yield return toolNotification;
                    }
                }
            }
        }
        _chatHistory.AddAssistantMessage(fullContent.ToString());
    }
}
