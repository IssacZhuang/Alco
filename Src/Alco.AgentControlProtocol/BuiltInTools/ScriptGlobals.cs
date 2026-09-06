using System;
using Alco.Engine;

namespace Alco.AgentControlProtocol;

/// <summary>
/// The default globals object injected into every script executed by
/// <see cref="ScriptTool"/> when the host supplies no globals factory: exposes the
/// bound engine instance so scripts can reach every public engine API (views, input,
/// UI, systems) through instance navigation without any host-side static accessor.
/// </summary>
/// <remarks>
/// Hosts that want application-typed globals (for example the game instance and map)
/// supply their own public globals class through
/// <see cref="AgentControlOptions.ScriptGlobalsFactory"/>; this class is then not used.
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
