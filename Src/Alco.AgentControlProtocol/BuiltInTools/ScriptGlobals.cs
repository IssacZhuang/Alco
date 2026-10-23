using System;
using Alco.Engine;

namespace Alco.AgentControlProtocol;

/// <summary>
/// The default script globals exposing the bound engine instance as <c>Engine</c>.
/// </summary>
/// <remarks>
/// Hosts can supply application-typed globals through <see cref="AgentControlOptions.ScriptGlobalsFactory"/> instead.
/// </remarks>
public sealed class ScriptGlobals
{
    /// <summary>
    /// The engine instance the script tool is bound to.
    /// </summary>
    public readonly GameEngine Engine;

    /// <summary>
    /// Creates the default script globals bound to an engine.
    /// </summary>
    /// <param name="engine">The engine instance exposed to scripts.</param>
    public ScriptGlobals(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Engine = engine;
    }
}
