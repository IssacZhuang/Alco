namespace Alco.LLM;

/// <summary>
/// The LLM service provider type.
/// </summary>
public enum LLMProvider
{
    /// <summary>
    /// OpenAI and OpenAI-compatible APIs (DeepSeek, Ollama, etc.).
    /// </summary>
    OpenAI,

    /// <summary>
    /// Anthropic Claude API.
    /// </summary>
    Anthropic,

    /// <summary>
    /// Google Gemini API.
    /// </summary>
    Gemini,
}
