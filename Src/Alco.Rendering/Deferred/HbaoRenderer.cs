using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// HBAO+ (horizon-based ambient occlusion) renderer for the deferred PBR pipeline.
/// <br/>Reads the G-buffer depth and world-normal attachments, marches screen-space
/// horizon rays in a compute pass (HBAO.hlsl) and filters the noisy result with a
/// depth/normal-aware bilateral blur (HBAOBlur.hlsl). The blurred single-channel AO
/// texture (<see cref="AmbientOcclusionTexture"/>) is sampled by the deferred lighting
/// pass to modulate the sky ambient term.
/// <br/>Call <see cref="Render"/> after the G-buffer pass and before the lighting pass.
/// </summary>
public sealed class HbaoRenderer : AutoDisposable
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
        /// <summary>x=projScale (0.5 * viewportHeight * projection[1][1]) yz=viewport size in pixels (filled by <see cref="Render"/>) w=max step length in pixels.</summary>
        public Vector4 Params2;
    }

    private readonly RenderingSystem _rendering;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly ComputeMaterial _hbaoMaterial;
    private readonly ComputeMaterial _blurMaterial;
    private readonly GraphicsValueBuffer<HbaoData> _dataBuffer;

    private RenderTexture _rawAO;
    private RenderTexture _blurredAO;
    private RenderTexture? _boundGBuffer;

    /// <summary>
    /// The filtered ambient occlusion texture (AO in every channel), sampled by the
    /// deferred lighting pass.
    /// </summary>
    public RenderTexture AmbientOcclusionTexture => _blurredAO;

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
        _blurredAO = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, width, height, "hbao_blurred");

        _hbaoMaterial.SetRenderTexture("_aoOutput", _rawAO);
        _blurMaterial.SetRenderTexture("_aoInput", _rawAO);
        _blurMaterial.SetRenderTexture("_aoOutput", _blurredAO);
    }

    /// <summary>
    /// Recreate the AO textures at a new resolution. Call when the G-buffer resizes.
    /// </summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height in pixels.</param>
    public void Resize(uint width, uint height)
    {
        _rawAO.Dispose();
        _blurredAO.Dispose();
        _rawAO = _rendering.CreateRenderTexture(_rendering.PreferredLightMapPass, width, height, "hbao_raw");
        _blurredAO = _rendering.CreateRenderTexture(_rendering.PreferredLightMapPass, width, height, "hbao_blurred");
        _hbaoMaterial.SetRenderTexture("_aoOutput", _rawAO);
        _blurMaterial.SetRenderTexture("_aoInput", _rawAO);
        _blurMaterial.SetRenderTexture("_aoOutput", _blurredAO);
        _boundGBuffer = null;
    }

    /// <summary>
    /// Compute ambient occlusion from the G-buffer. Must be called after the G-buffer
    /// pass and before the lighting pass.
    /// </summary>
    /// <param name="gbuffer">The pipeline G-buffer (depth + world-normal attachments).</param>
    /// <param name="data">Per-frame HBAO data; the viewport components of <see cref="HbaoData.Params2"/> are filled by this method.</param>
    public void Render(RenderTexture gbuffer, ref HbaoData data)
    {
        data.Params2 = new Vector4(data.Params2.X, gbuffer.Width, gbuffer.Height, data.Params2.W);
        _dataBuffer.UpdateBuffer(data);

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
            _blurredAO.Dispose();
            _dataBuffer.Dispose();
            _commandBuffer.Dispose();
        }
    }
}
