using System.ComponentModel;

namespace Alco.LLM.Test;

/// <summary>
/// Tool functions returning <see cref="AgentToolResult"/> subtypes (and one plain string),
/// used to verify the formatting pipeline without touching production tools.
/// </summary>
[AgentTools]
public static class FakeStructuredToolFunctions
{
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Returns a structured confirmation result")]
    public static ToolOk ConfirmThing(string name)
    {
        return new ToolOk($"Confirmed '{name}'.");
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Returns a structured data result")]
    public static ToolData GetData(int id)
    {
        return new ToolData(new { id, name = "Sample" });
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Returns data that requires caller-provided JSON converters")]
    public static ToolData GetConvertedData(int value)
    {
        return new ToolData(new ConverterBackedData(value));
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Returns a structured tool error")]
    public static ToolError ReportError()
    {
        return new ToolError("No game loaded", "NO_GAME");
    }

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
