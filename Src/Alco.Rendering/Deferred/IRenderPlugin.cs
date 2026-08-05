using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// Injection points in the deferred pipeline where render plugins execute.
/// </summary>
public enum RenderInjectionPoint
{
    /// <summary>
    /// After the G-buffer pass (G-buffer + depth ready), before deferred lighting.
    /// Ambient occlusion, screen-space reflections and global illumination plugins
    /// run here.
    /// </summary>
    AfterGBuffer,
}

/// <summary>
/// Context passed to render plugins at their injection point. Plugins read
/// shared pipeline resources (G-buffer, shadow map, camera) and register output
/// textures that the lighting pass consumes.
/// </summary>
public sealed class RenderPluginContext
{
    /// <summary>The rendering system for creating GPU resources.</summary>
    public required RenderingSystem Rendering { get; init; }

    /// <summary>The pipeline G-buffer render texture (albedo / normal / mrAO / emissive + depth).</summary>
    public required RenderTexture GBuffer { get; init; }

    /// <summary>The 2x2 cascaded shadow map render texture.</summary>
    public required RenderTexture ShadowMap { get; init; }

    /// <summary>Inverse of the camera view-projection matrix for this frame.</summary>
    public required Matrix4x4 InvViewProjection { get; init; }

    /// <summary>World-space camera transform (position + rotation + scale) for this frame.</summary>
    public required Transform3D CameraTransform { get; init; }

    /// <summary>G-buffer width in pixels.</summary>
    public uint Width { get; init; }

    /// <summary>G-buffer height in pixels.</summary>
    public uint Height { get; init; }

    /// <summary>
    /// The deferred lighting data computed by the caller this frame (sun direction,
    /// shadow cascades, sky colors, etc.). May be null when the pipeline is not a
    /// deferred PBR pipeline. GI plugins read lighting/shadow data from here instead
    /// of requiring the caller to manually copy it into a separate struct.
    /// </summary>
    public PBRDeferredPipeline.DeferredLightingData? LightingData { get; init; }

    /// <summary>
    /// The pipeline's shared point-light StructuredBuffer, or null when point lights
    /// are not used. GI plugins bind this to the inject pass automatically.
    /// </summary>
    public GraphicsBuffer? PointLightBuffer { get; init; }

    // ── Outputs set by plugins ──

    /// <summary>
    /// Set by an AO plugin to a full-resolution [0,1] occlusion texture.
    /// The lighting pass multiplies the material AO with this value. When null,
    /// the lighting pass uses a white (1.0) fallback — no screen-space AO.
    /// </summary>
    public RenderTexture? AOResult { get; set; }

    /// <summary>
    /// Set by a GI plugin to a full-resolution diffuse irradiance texture.
    /// When null, the lighting pass falls back to sky ambient only.
    /// </summary>
    public RenderTexture? GIDiffuse { get; set; }

    /// <summary>
    /// Set by a GI plugin to a full-resolution specular radiance texture.
    /// Must be set together with <see cref="GIDiffuse"/>.
    /// </summary>
    public RenderTexture? GISpecular { get; set; }
}

/// <summary>
/// Pluggable render effect (ambient occlusion, global illumination, etc.).
/// Register via <see cref="PBRDeferredPipeline.RegisterPlugin"/>; the pipeline
/// executes all plugins at their declared injection point between the G-buffer
/// and lighting passes, then binds the output textures to the lighting material.
/// </summary>
public interface IRenderPlugin : IDisposable
{
    /// <summary>Display name for diagnostics.</summary>
    string Name { get; }

    /// <summary>Where in the frame this plugin executes.</summary>
    RenderInjectionPoint InjectionPoint { get; }

    /// <summary>
    /// Execute the effect for this frame. Read inputs from
    /// <paramref name="context"/> and set output textures
    /// (<see cref="RenderPluginContext.AOResult"/>,
    /// <see cref="RenderPluginContext.GIDiffuse"/>,
    /// <see cref="RenderPluginContext.GISpecular"/>).
    /// </summary>
    void Execute(RenderPluginContext context);

    /// <summary>Recreate resolution-dependent resources.</summary>
    void Resize(uint width, uint height);
}
