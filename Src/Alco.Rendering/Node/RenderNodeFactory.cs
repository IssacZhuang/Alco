using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;

/// <summary>
/// The type-keyed blackboard a <see cref="RenderNodeFactory"/> pulls its
/// pipeline-shape dependencies from: the composing code registers the shared
/// services it owns — the post <see cref="RenderChain"/> and its content layout,
/// a material compiler, a camera, scene environment, graph resource roles — and
/// factory classes request them by type. The engine layer defines only the
/// container; which service types exist is decided by the composer and the
/// factories (e.g. a World3D factory asking for a World3D environment), so a
/// new render feature never requires touching this class or the context.
/// </summary>
public sealed class RenderNodeFactoryServices
{
    private readonly Dictionary<Type, object> _services = [];

    /// <summary>Registers <paramref name="service"/> under its own type, replacing
    /// an earlier registration. Returns itself for chained setup.</summary>
    public RenderNodeFactoryServices Add<T>(T service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _services[typeof(T)] = service;
        return this;
    }

    /// <summary>Gets the service registered for <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">No service of the type was
    /// registered — the message names the missing type and where to register it.</exception>
    public T Get<T>()
    {
        if (_services.TryGetValue(typeof(T), out object? service) && service is T typed)
        {
            return typed;
        }
        throw new InvalidOperationException(
            $"Creating this render node requires a '{typeof(T).Name}' service that the composing " +
            "code did not provide. Register it on the factory context's services " +
            "(RenderNodeFactoryServices.Add) before calling Create.");
    }

    /// <summary>Gets the service registered for <typeparamref name="T"/>, or null.</summary>
    public T? TryGet<T>() where T : class
    {
        return _services.TryGetValue(typeof(T), out object? service) ? service as T : null;
    }
}

/// <summary>
/// The environment a <see cref="RenderNodeFactory"/> creates its node in: the
/// rendering system, the target render graph, and the composer's service
/// blackboard. It never grows feature-specific members — resources a node depends
/// on for its existence (a G-buffer, a scene color target, a sibling node) are
/// registered as services by the composing code, which owns them anyway, and stay
/// type-safe end to end. A factory covers the replaceable surface only: which
/// slang modules back the node's shaders, and tunable parameters.
/// </summary>
public readonly struct RenderNodeFactoryContext
{
    /// <summary>The rendering system used to create GPU resources and resolve shaders.</summary>
    public RenderingSystem Rendering { get; }

    /// <summary>The render graph the created node will be registered in.</summary>
    public RenderGraph Graph { get; }

    /// <summary>The composer-registered services factories may pull dependencies from.</summary>
    public RenderNodeFactoryServices Services { get; }

    /// <summary>Creates a creation context with an empty service blackboard.</summary>
    public RenderNodeFactoryContext(RenderingSystem rendering, RenderGraph graph)
        : this(rendering, graph, new RenderNodeFactoryServices())
    {
    }

    /// <summary>Creates a creation context over the given service blackboard.</summary>
    public RenderNodeFactoryContext(RenderingSystem rendering, RenderGraph graph, RenderNodeFactoryServices services)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(services);
        Rendering = rendering;
        Graph = graph;
        Services = services;
    }
}

/// <summary>
/// A serializable recipe for creating a render node: the class holds the node's
/// replaceable surface (its <see cref="Shader"/>s, tunable parameters) as plain
/// properties, and <see cref="Create"/> assembles the node from them. Factories
/// are data — they load from <c>.rnfact</c> jsonc files whose <c>$type</c>
/// discriminator selects the factory class (the engine's polymorphic JSON
/// convention, discovered by assembly scan: writing a new factory class is all it
/// takes to make it configurable), with shader references resolving through the
/// shared shader system at load time, typed and validated (the material asset
/// convention) — retargeting a node's shaders is a config edit, never a code
/// change. Factories are shared cached assets: never mutate a loaded factory's
/// properties; runtime overrides go to the created node's own properties.
/// Everything a shader swap does not change (graph resources, sibling nodes,
/// scene services) stays in the composing code and keeps flowing through the
/// node's own constructor and Attach parameters.
/// </summary>
public abstract class RenderNodeFactory
{
    /// <summary>Creates the node from the factory's current property values.</summary>
    /// <param name="context">The creation environment.</param>
    public abstract IRenderNode Create(RenderNodeFactoryContext context);

    /// <summary>Creates the node and checks it is of the expected concrete type —
    /// the composing code's typed handle on the created node.</summary>
    public T CreateNode<T>(RenderNodeFactoryContext context) where T : class, IRenderNode
    {
        IRenderNode node = Create(context);
        if (node is not T typed)
        {
            throw new InvalidOperationException(
                $"The '{GetType().Name}' factory created a '{node.GetType().Name}' node, not the expected '{typeof(T).Name}'.");
        }
        return typed;
    }
}
