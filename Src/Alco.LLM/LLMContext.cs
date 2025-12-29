using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Alco.LLM;

/// <summary>
/// Represents the context for LLM operations, wrapping the Semantic Kernel.
/// </summary>
public class LLMContext
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
}

