using System;
using System.Collections.Generic;

namespace Alco.AgentControlProtocol;

/// <summary>
/// Configuration for <see cref="AgentControlHost"/>: which tool types and instances
/// are registered, and which built-in tools (script execution, frame screenshots) are
/// enabled.
/// </summary>
public sealed class AgentControlOptions
{
    /// <summary>
    /// The default localhost port the agent control HTTP API listens on.
    /// </summary>
    public const int DefaultPort = 52100;

    /// <summary>
    /// Types whose static methods marked with <see cref="AgentFunctionAttribute"/> are
    /// registered (types usually carry the <see cref="AgentToolsAttribute"/> marker).
    /// </summary>
    public IList<Type> ToolTypes { get; init; } = Array.Empty<Type>();

    /// <summary>
    /// Instances whose instance and static methods marked with
    /// <see cref="AgentFunctionAttribute"/> are registered.
    /// </summary>
    public IList<object>? ToolInstances { get; init; }

    /// <summary>
    /// Whether the built-in <c>ExecuteScript</c> tool (arbitrary C# compilation and
    /// engine-main-thread execution) is registered. Available in every configuration;
    /// defaults to <c>true</c>. Disable when the process must not expose code execution
    /// to localhost clients.
    /// </summary>
    public bool EnableScriptExecution { get; init; } = true;

    /// <summary>
    /// Whether the built-in <c>CaptureScreenshot</c> tool (the presented frame,
    /// ImGui overlay included) is registered. Defaults to <c>true</c>.
    /// </summary>
    public bool EnableScreenshotTool { get; init; } = true;

    /// <summary>
    /// The compilation timeout in milliseconds applied to script executions when the
    /// caller omits or supplies a non-positive <c>timeoutMs</c> argument.
    /// </summary>
    public int ScriptCompilationTimeoutMs { get; init; } = 10_000;

    /// <summary>
    /// Optional factory invoked once per script execution (on the calling agent thread)
    /// to build the globals object whose public members scripts see as top-level names.
    /// The returned type must be public with public members (a Roslyn scripting
    /// constraint). When null, or when the factory returns null, the protocol's default
    /// <see cref="ScriptGlobals"/> is used, which exposes the engine instance as
    /// <c>Engine</c>. Supply a factory to expose host-typed state (for example the
    /// current game and map) alongside or instead of the engine.
    /// </summary>
    public Func<object?>? ScriptGlobalsFactory { get; init; }
}
