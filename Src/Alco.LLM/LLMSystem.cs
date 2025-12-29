using System;
using Alco.Engine;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

public class LLMSystem : BaseEngineSystem
{
    private Kernel? _kernel;

    /// <summary>
    /// Gets a value indicating whether the LLM system is connected to a kernel.
    /// </summary>
    public bool IsConnected => _kernel != null;

    /// <summary>
    /// Gets the current kernel instance if connected.
    /// </summary>
    public Kernel? Kernel => _kernel;

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
            _kernel = builder.Build();

            Log.Info($"LLMSystem connected successfully. Model: {modelId}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to connect LLMSystem: {ex.Message}");
            _kernel = null;
            throw;
        }
    }

    /// <summary>
    /// Disconnects the current session and clears the kernel.
    /// </summary>
    public void Disconnect()
    {
        if (!IsConnected)
        {
            return;
        }

        _kernel = null;
        Log.Info("LLMSystem disconnected.");
    }

    public override void Dispose()
    {
        Disconnect();
        base.Dispose();
    }
}

