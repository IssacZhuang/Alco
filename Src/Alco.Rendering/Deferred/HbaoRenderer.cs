using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// HBAO+ (horizon-based ambient occlusion) renderer for the deferred PBR pipeline.
/// <br/>Reads the G-buffer depth and world-normal attachments, marches screen-space
/// horizon rays in a compute pass (HBAO.hlsl) and filters the noisy result with a
/// depth/normal-aware bilateral blur (HBAOBlur.hlsl). The blur pass writes the
/// filtered AO to a standalone full-resolution texture (<see cref="AOResult"/>),
/// which the pipeline binds to the deferred lighting material's _aoTexture slot.
/// <br/>Implements <see cref="IRenderPlugin"/> so it can be registered with
/// <see cref="PBRDeferredPipeline.RegisterPlugin"/> and executes automatically at
/// the <see cref="RenderInjectionPoint.AfterGBuffer"/> injection point.
/// </summary>
public sealed class HbaoRenderer : AutoDisposable, IRenderPlugin
{
    /// <summary>
    /// Per-frame HBAO data uploaded to both compute passes. Layout must match the
    /// <c>_data</c> cbuffer in HBAOCommon.hlsli exactly.
    /// </summary>
    public struct HbaoData
    {
        /// <summary>Inverse of the camera view-projection matrix.</summary>
        public Matrix4x4 InvViewProjection;
        /// <summary>World-space camera position (w unused).</summary>
        public Vector4 CameraPosition;
        /// <summary>World-space camera right axis (w unused).</summary>
        public Vector4 CameraRight;
        /// <summary>World-space camera up axis (w unused).</summary>
        public Vector4 CameraUp;
        /// <summary>World-space camera forward axis (w unused).</summary>
        public Vector4 CameraForward;
        /// <summary>x=radius (world units) y=intensity exponent z=angle bias (sin space) w=1/radius^2.</summary>
        public Vector4 Params;
        /// <summary>x=projScale (0.5 * viewportHeight * projection[1][1]) yz=viewport size in pixels (filled by <see cref="Execute"/>) w=max step length in pixels.</summary>
        public Vector4 Params2;
        /// <summary>x=strength (0 disables; scales how much of the blurred AO is written to the result texture) yzw=unused.</summary>
        public Vector4 Params3;
    }

    private readonly RenderingSystem _rendering;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly ComputeMaterial _hbaoMaterial;
    private readonly ComputeMaterial _blurMaterial;
    private readonly GraphicsValueBuffer<HbaoData> _dataBuffer;

    private RenderTexture _rawAO;
    private RenderTexture _aoResult;
    private RenderTexture? _boundGBuffer;

    /// <summary>
    /// Per-frame data; fill before the pipeline executes this plugin. The viewport
    /// components of <see cref="HbaoData.Params2"/> (yz) are filled automatically
    /// by <see cref="Execute"/>.
    /// </summary>
    public HbaoData Data;

    /// <summary>The full-resolution AO result texture (r = occlusion [0,1], white = unoccluded).</summary>
    public RenderTexture AOResult => _aoResult;

    /// <inheritdoc />
    public string Name => "HBAO+";

    /// <inheritdoc />
    public RenderInjectionPoint InjectionPoint => RenderInjectionPoint.AfterGBuffer;

    /// <summary>
    /// Create the HBAO+ renderer with the given compute shaders.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="hbaoShader">The raw AO shader (HBAO.hlsl).</param>
    /// <param name="blurShader">The bilateral blur shader (HBAOBlur.hlsl).</param>
    /// <param name="width">The initial AO texture width in pixels (match the G-buffer).</param>
    /// <param name="height">The initial AO texture height in pixels (match the G-buffer).</param>
    public HbaoRenderer(RenderingSystem rendering, Shader hbaoShader, Shader blurShader, uint width, uint height)
    {
        _rendering = rendering;
        _device = rendering.GraphicsDevice;
        _commandBuffer = _device.CreateCommandBuffer("hbao");
        _hbaoMaterial = rendering.CreateComputeMaterial(hbaoShader);
        _blurMaterial = rendering.CreateComputeMaterial(blurShader);
        _dataBuffer = rendering.CreateGraphicsValueBuffer<HbaoData>("hbao_data");
        _hbaoMaterial.SetBuffer("_data", _dataBuffer);
        _blurMaterial.SetBuffer("_data", _dataBuffer);

        // RGBA16Float (light map layout): proven as both a compute storage target and
        // a filterable sampled texture.
        _rawAO = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, width, height, "hbao_raw");
        _aoResult = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, width, height, "hbao_result");

        _hbaoMaterial.SetRenderTexture("_aoOutput", _rawAO);
        _blurMaterial.SetRenderTexture("_aoInput", _rawAO);
        _blurMaterial.SetRenderTexture("_aoResult", _aoResult);
    }

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        _rawAO.Dispose();
        _aoResult.Dispose();
        _rawAO = _rendering.CreateRenderTexture(_rendering.PreferredLightMapPass, width, height, "hbao_raw");
        _aoResult = _rendering.CreateRenderTexture(_rendering.PreferredLightMapPass, width, height, "hbao_result");
        _hbaoMaterial.SetRenderTexture("_aoOutput", _rawAO);
        _blurMaterial.SetRenderTexture("_aoInput", _rawAO);
        _blurMaterial.SetRenderTexture("_aoResult", _aoResult);
        _boundGBuffer = null;
    }

    /// <inheritdoc />
    public void Execute(RenderPluginContext context)
    {
        Render(context.GBuffer);
        context.AOResult = _aoResult;
    }

    /// <summary>
    /// Compute ambient occlusion from the G-buffer. Must be called after the G-buffer
    /// pass and before the lighting pass.
    /// </summary>
    /// <param name="gbuffer">The pipeline G-buffer (depth + world-normal attachments).</param>
    private void Render(RenderTexture gbuffer)
    {
        Data.Params2 = new Vector4(Data.Params2.X, gbuffer.Width, gbuffer.Height, Data.Params2.W);
        _dataBuffer.UpdateBuffer(Data);

        // The G-buffer render texture is recreated on resize; avoid rebinding every frame.
        if (!ReferenceEquals(_boundGBuffer, gbuffer))
        {
            _hbaoMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
            _hbaoMaterial.SetRenderTexture("_normal", gbuffer, 1);
            _blurMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
            _blurMaterial.SetRenderTexture("_normal", gbuffer, 1);
            _boundGBuffer = gbuffer;
        }

        _commandBuffer.Begin();
        using (GPUCommandBuffer.ComputePass computePass = _commandBuffer.BeginCompute())
        {
            _hbaoMaterial.DispatchBySize(computePass, gbuffer.Width, gbuffer.Height, 1);
            _blurMaterial.DispatchBySize(computePass, gbuffer.Width, gbuffer.Height, 1);
        }
        _commandBuffer.End();
        _device.Submit(_commandBuffer);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rawAO.Dispose();
            _aoResult.Dispose();
            _dataBuffer.Dispose();
            _commandBuffer.Dispose();
        }
    }
}
