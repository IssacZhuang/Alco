using System.Numerics;
using Alco.Graphics;

using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// A reflective shadow map (RSM) graph node for the voxel GI's sun-bounce
/// injection (CRYENGINE SVOTI style, see docs/GI_Sun_RSM_Injection.md): renders
/// the scene once per frame from the selected CSM cascade's sun view into a
/// depth + albedo + world-normal target (Rsm.slang), clearing all attachments.
/// Insert it after the shadow pass — typically
/// <c>graph.InsertBefore(gbufferNode, rsmNode)</c> — and register content
/// providers on <see cref="Content"/> (e.g. the same <see cref="ShadowRenderer"/>
/// registered on the shadow pass, after
/// <see cref="ShadowRenderer.EnableRsm"/>).
/// <br/>The RSM depth is expressed in the selected cascade's NDC (the vertex
/// shader unfolds the shared folded cascade matrices), so the GI cone trace's
/// depth matching against <see cref="PBRSceneEnvironment"/> cascade data applies
/// unchanged. Disable the node (<see cref="IRenderNode.IsEnabled"/>) when the GI
/// injection intensity is zero — the pass then costs nothing.
/// </summary>
public sealed class RGNode_RsmPass : AutoDisposable, IRenderGraphNode
{
    /// <summary>The default CSM cascade whose sun view defines the RSM (matching
    /// CRYENGINE's <c>e_svoTI_GsmCascadeLod</c> default): mid-distance, fine
    /// enough for near-field bounce light, wide enough to cover the camera
    /// surroundings.</summary>
    public const int DefaultCascadeIndex = 2;

    /// <summary>
    /// Create the attachment layout of an RSM target: two RGBA8 color attachments
    /// (sRGB albedo with alpha marking rendered texels, world normal) plus a
    /// <see cref="PixelFormat.Depth32Float"/> depth attachment.
    /// </summary>
    /// <param name="device">The GPU device.</param>
    /// <param name="name">The layout name for diagnostics.</param>
    public static GPUAttachmentLayout CreateLayout(GPUDevice device, string name = "rsm_pass")
        => device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [
                new ColorAttachment(PixelFormat.RGBA8Unorm),
                new ColorAttachment(PixelFormat.RGBA8Unorm),
            ],
            new DepthAttachment(PixelFormat.Depth32Float),
            name));

    private readonly RenderGraphTexture _rsmMap;

    /// <summary>
    /// Creates the RSM pass node.
    /// </summary>
    /// <param name="rsmMap">The RSM target resource: a texture whose layout
    /// carries albedo and world-normal attachments plus a
    /// <see cref="PixelFormat.Depth32Float"/> depth attachment.</param>
    /// <param name="cascadeIndex">The CSM cascade whose sun view-projection
    /// defines the RSM view.</param>
    public RGNode_RsmPass(RenderGraphTexture rsmMap, int cascadeIndex = DefaultCascadeIndex)
    {
        ArgumentNullException.ThrowIfNull(rsmMap);
        _rsmMap = rsmMap;
        CascadeIndex = cascadeIndex;
    }

    /// <summary>The content drawn inside the pass, in list order. Register
    /// content providers here — the pipeline the pass belongs to is not
    /// involved.
    /// <br/>Ownership: the pass does not take ownership of registered providers;
    /// disposing them (when disposable) is the caller's responsibility, unlike
    /// nodes handed to <c>RenderGraph.Use</c>, which transfers ownership to the
    /// graph.</summary>
    public List<IRsmPassContent> Content { get; } = new();

    /// <summary>The CSM cascade whose sun view-projection defines the RSM view.
    /// Content providers bake the index into recorded push constants, so
    /// changing it re-records their static bundles on the next frame.</summary>
    public int CascadeIndex { get; set; }

    /// <summary>Optional CPU/GPU stage instrumentation.</summary>
    public PassInstrumentation? Instrumentation { get; set; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <summary>The RSM target resource the pass renders into.</summary>
    public RenderGraphTexture RsmMap => _rsmMap;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        builder.Write(_rsmMap);
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        // The color clears carry alpha 0: the GI trace treats an albedo alpha
        // below 0.5 as "never rendered" so cleared (empty sky) regions cannot
        // match against the cleared far-plane depth.
        ReadOnlySpan<ClearColorData> clearColors =
        [
            new ClearColorData(0, Vector4.Zero),
            new ClearColorData(1, Vector4.Zero),
        ];
        RenderPassScope pass = Instrumentation != null
            ? Instrumentation.BeginPass(context.RenderContext, _rsmMap.Texture.FrameBuffer, clearColors, 1.0f)
            : context.RenderContext.BeginPass(_rsmMap.Texture.FrameBuffer, clearColors, 1.0f);
        using (pass)
        {
            List<IRsmPassContent> content = Content;
            for (int i = 0; i < content.Count; i++)
            {
                if (content[i].IsEnabled)
                {
                    content[i].OnRenderRsm(pass, CascadeIndex);
                }
            }

            Instrumentation?.ScheduleResolve(pass);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing) { }
}
