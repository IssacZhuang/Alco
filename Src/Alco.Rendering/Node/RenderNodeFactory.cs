using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;

/// <summary>
/// A type-keyed blackboard of shared services that render node factories pull their dependencies from.
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
/// A factory service role exposing the resource whose depth attachment is the sampleable scene depth.
/// </summary>
public sealed class SceneDepthSource
{
    /// <summary>Creates the role over the scene color resource.</summary>
    /// <param name="resource">The resource; its depth attachment is the sampleable scene depth.</param>
    public SceneDepthSource(RenderGraphTexture resource)
    {
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
    }

    /// <summary>The scene color resource; its depth attachment is the sampleable scene depth.</summary>
    public RenderGraphTexture Resource { get; }
}

/// <summary>
/// The environment a <see cref="RenderNodeFactory"/> creates its node in: the rendering system, target render graph, and service blackboard.
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
/// A serializable recipe for creating a render node from its replaceable shader and parameter surface.
/// Loaded factories are shared cached assets and should be treated as immutable.
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
