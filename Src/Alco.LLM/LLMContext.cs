using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Alco.LLM;

/// <summary>
/// Represents the context for LLM operations, wrapping the Semantic Kernel.
/// </summary>
public sealed class LLMContext
{
    private readonly IChatCompletionService _chatCompletionService;
    private readonly ChatHistory _chatHistory;
    private readonly OpenAIPromptExecutionSettings _promptExecutionSettings;

    /// <summary>
    /// Gets the Semantic Kernel instance.
    /// </summary>
    public Kernel Kernel { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LLMContext"/> class.
    /// </summary>
    /// <param name="kernel">The Semantic Kernel instance to use.</param>
    public LLMContext(Kernel kernel)
    {
        Kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        _promptExecutionSettings = new OpenAIPromptExecutionSettings()
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
        };
        _chatHistory = new ChatHistory();
    }

    public async Task<string> ChatAsync(string message)
    {
        _chatHistory.AddUserMessage(message);
        var result = await _chatCompletionService.GetChatMessageContentAsync(_chatHistory, _promptExecutionSettings, Kernel);
        return result.ToString();
    }

    public async IAsyncEnumerable<string> ChatStreamingAsync(string message)
    {
        _chatHistory.AddUserMessage(message);
        var stream = _chatCompletionService.GetStreamingChatMessageContentsAsync(_chatHistory, _promptExecutionSettings, Kernel);


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

