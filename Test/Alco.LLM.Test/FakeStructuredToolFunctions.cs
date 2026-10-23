using Alco.AgentControlProtocol;
using System.ComponentModel;

namespace Alco.LLM.Test;

/// <summary>
/// Tool functions returning <see cref="AgentToolResult"/> subtypes (and one plain string),
/// used to verify the formatting pipeline without touching production tools.
/// </summary>
[AgentTools]
public static class FakeStructuredToolFunctions
{
    /// <summary>Returns a structured confirmation result.</summary>
    /// <param name="name">Name to include in the confirmation.</param>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Returns a structured confirmation result")]
    public static ToolOk ConfirmThing(string name)
    {
        return new ToolOk($"Confirmed '{name}'.");
    }

    /// <summary>Returns a structured data result.</summary>
    /// <param name="id">Identifier included in the data.</param>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Returns a structured data result")]
    public static ToolData GetData(int id)
    {
        return new ToolData(new { id, name = "Sample" });
    }

    /// <summary>Returns data that requires caller-provided JSON converters.</summary>
    /// <param name="value">Value wrapped by the converter-backed data.</param>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Returns data that requires caller-provided JSON converters")]
    public static ToolData GetConvertedData(int value)
    {
        return new ToolData(new ConverterBackedData(value));
    }

    /// <summary>Returns a structured tool error.</summary>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Returns a structured tool error")]
    public static ToolError ReportError()
    {
        return new ToolError("No game loaded", "NO_GAME");
    }

    /// <summary>Returns a plain string to verify passthrough.</summary>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Returns a plain string to verify passthrough")]
    public static string PlainString()
    {
        return "I am a plain string";
    }
}

/// <summary>
/// Test data whose model-facing JSON is controlled by a custom converter.
/// </summary>
/// <param name="Value">The value written by the custom converter.</param>
public sealed record ConverterBackedData(int Value);
