using System;
using Alco.Engine;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

public class LLMSystem : BaseEngineSystem
{
    private LLMContext? _context;

    /// <summary>
    /// Gets a value indicating whether the LLM system is connected.
    /// </summary>
    public bool IsConnected => _context != null;

    /// <summary>
    /// Gets the current LLM context if connected.
    /// </summary>
    public LLMContext? Context => _context;

    /// <summary>
    /// Connects to the LLM service with the specified configuration.
    /// </summary>
    /// <param name="modelId">The model ID to use (e.g., "gpt-4").</param>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="orgId">Optional organization ID.</param>
    public void Connect(string modelId, string apiKey, string? orgId = null)
    {
        if (IsConnected)
        {
            Log.Warning("LLMSystem is already connected. Disconnect first to reconnect.");
            return;
        }

        try
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId, apiKey, orgId);
            var kernel = builder.Build();
            _context = new LLMContext(kernel);

            Log.Info($"LLMSystem connected successfully. Model: {modelId}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to connect LLMSystem: {ex.Message}");
            _context = null;
            throw;
        }
    }

    /// <summary>
    /// Connects to an OpenAI-compatible LLM service with a custom endpoint.
    /// </summary>
    /// <param name="modelId">The model ID to use.</param>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="uri">The custom endpoint URI.</param>
    public void Connect(string modelId, string apiKey, Uri uri)
    {
        if (IsConnected)
        {
            Log.Warning("LLMSystem is already connected. Disconnect first to reconnect.");
            return;
        }

        try
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId, uri, apiKey);
            var kernel = builder.Build();
            _context = new LLMContext(kernel);

            Log.Info($"LLMSystem connected successfully to custom endpoint. Model: {modelId}, URI: {uri}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to connect LLMSystem to custom endpoint: {ex.Message}");
            _context = null;
            throw;
        }
    }

    /// <summary>
    /// Disconnects the current session and clears the context.
    /// </summary>
    public void Disconnect()
    {
        if (!IsConnected)
        {
            return;
        }

        _context = null;
        Log.Info("LLMSystem disconnected.");
    }

    public override void Dispose()
    {
        Disconnect();
        base.Dispose();
    }
}

