using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Post-lighting screen-space reflections inspired by Complementary Unbound's
/// composite reflection path. The node copies the completed HDR scene, traces
/// against the deferred depth buffer, temporally/spatially resolves the result,
/// and composites it before bloom and tone mapping.
/// </summary>
/// <remarks>
/// Register this node after forward scene content and before content processors.
/// It intentionally runs after deferred lighting so a hit samples the real shaded
/// scene color instead of attempting to reconstruct lighting from the G-buffer.
/// </remarks>
public sealed class ScreenSpaceReflectionRenderer : AutoDisposable, IForwardRenderNode
{
    private struct SsrData
    {
        public Matrix4x4 InvViewProjection;
        public Matrix4x4 ViewProjection;
        public Matrix4x4 PreviousViewProjection;
        public Vector4 CameraPosition;
        public Vector4 RenderSize;
        public Vector4 Params;
        public Vector4 RayParams;
    }

    private readonly RenderingSystem _rendering;
    private readonly PBRDeferredPipeline _pipeline;
    private readonly VoxelGiRenderer _voxelGi;
    private readonly CameraPerspectiveBuffer _camera;
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Material _copyMaterial;
    private readonly Material _traceMaterial;
    private readonly Material _resolveMaterial;
    private readonly Material _compositeMaterial;
    private readonly GraphicsValueBuffer<SsrData> _dataBuffer;

    private RenderTexture _sceneCopy;
    private RenderTexture _reflectionRaw;
    private readonly RenderTexture[] _reflectionHistory = new RenderTexture[2];
    private int _historyReadIndex;
    private bool _historyValid;
    private bool _lastSsrOnly;
    private bool _isEnabled = true;
    private uint _frameIndex;
    private Matrix4x4 _previousViewProjection = Matrix4x4.Identity;

    /// <inheritdoc />
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }
            _isEnabled = value;
            _historyValid = false;
        }
    }

    /// <summary>Maximum world-space distance of the post-lighting SSR ray.</summary>
    public float MaxTraceDistance { get; set; } = 200.0f;

    /// <summary>Surfaces at or above this roughness use only the voxel fallback.</summary>
    public float RoughnessCutoff { get; set; } = 0.85f;

    /// <summary>
    /// Creates the post-lighting SSR node. Shader objects and the pipeline remain
    /// owned by their callers; the node owns its materials and intermediate textures.
    /// </summary>
    public ScreenSpaceReflectionRenderer(
        RenderingSystem rendering,
        PBRDeferredPipeline pipeline,
        VoxelGiRenderer voxelGi,
        CameraPerspectiveBuffer camera,
        Shader traceShader,
        Shader resolveShader,
        Shader compositeShader,
        Shader blitShader,
        uint width,
        uint height)
    {
        _rendering = rendering;
        _pipeline = pipeline;
        _voxelGi = voxelGi;
        _camera = camera;
        _renderContext = rendering.CreateRenderContext("post_lighting_ssr");
        _fullScreenMesh = rendering.MeshFullScreen;
        _copyMaterial = rendering.CreateMaterial(blitShader, "ssr_scene_copy");
        _traceMaterial = rendering.CreateMaterial(traceShader, "ssr_trace");
        _resolveMaterial = rendering.CreateMaterial(resolveShader, "ssr_resolve");
        _compositeMaterial = rendering.CreateMaterial(compositeShader, "ssr_composite");
        _dataBuffer = rendering.CreateGraphicsValueBuffer<SsrData>("ssr_post_data");

        _sceneCopy = CreateTexture(Math.Max(width, 1), Math.Max(height, 1), "ssr_scene_copy");
        uint traceWidth = TraceDimension(width);
        uint traceHeight = TraceDimension(height);
        _reflectionRaw = CreateTexture(traceWidth, traceHeight, "ssr_raw");
        _reflectionHistory[0] = CreateTexture(traceWidth, traceHeight, "ssr_history_a");
        _reflectionHistory[1] = CreateTexture(traceWidth, traceHeight, "ssr_history_b");

        _lastSsrOnly = voxelGi.SsrOnly;
        BindPersistentResources();
    }

    private uint TraceDimension(uint fullDimension)
    {
        return Math.Max((uint)MathF.Ceiling(fullDimension * _voxelGi.TraceResolutionScale), 1u);
    }

    private RenderTexture CreateTexture(uint width, uint height, string name)
    {
        return _rendering.CreateRenderTexture(
            _rendering.PreferredLightMapPass, width, height, name);
    }

    private void BindPersistentResources()
    {
        _copyMaterial.SetRenderTexture(ShaderResourceId.Texture, _pipeline.ForwardRenderTexture);

        _traceMaterial.SetBuffer("_ssrData", _dataBuffer);
        _traceMaterial.SetRenderTexture("_sceneColor", _sceneCopy);
        _traceMaterial.SetRenderTexture("_albedo", _pipeline.GBuffer, 0);
        _traceMaterial.SetRenderTexture("_normal", _pipeline.GBuffer, 1);
        _traceMaterial.SetRenderTexture("_mrAO", _pipeline.GBuffer, 2);
        _traceMaterial.SetRenderTextureDepth("_gbufferDepth", _pipeline.GBuffer);

        _resolveMaterial.SetBuffer("_ssrData", _dataBuffer);
        _resolveMaterial.SetRenderTexture("_reflectionRaw", _reflectionRaw);
        _resolveMaterial.SetRenderTexture("_reflectionHistory", _reflectionHistory[0]);
        _resolveMaterial.SetRenderTexture("_albedo", _pipeline.GBuffer, 0);
        _resolveMaterial.SetRenderTexture("_normal", _pipeline.GBuffer, 1);
        _resolveMaterial.SetRenderTextureDepth("_gbufferDepth", _pipeline.GBuffer);

        _compositeMaterial.SetBuffer("_ssrData", _dataBuffer);
        _compositeMaterial.SetRenderTexture("_sceneColor", _sceneCopy);
        _compositeMaterial.SetRenderTexture("_reflection", _reflectionHistory[1]);
        _compositeMaterial.SetRenderTexture("_albedo", _pipeline.GBuffer, 0);
        _compositeMaterial.SetRenderTexture("_normal", _pipeline.GBuffer, 1);
        _compositeMaterial.SetRenderTexture("_mrAO", _pipeline.GBuffer, 2);
        _compositeMaterial.SetRenderTextureDepth("_gbufferDepth", _pipeline.GBuffer);
    }

    private void EnsureTextures(uint width, uint height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        uint traceWidth = TraceDimension(width);
        uint traceHeight = TraceDimension(height);
        if (_sceneCopy.Width == width && _sceneCopy.Height == height
            && _reflectionRaw.Width == traceWidth && _reflectionRaw.Height == traceHeight)
        {
            return;
        }

        _sceneCopy.Dispose();
        _reflectionRaw.Dispose();
        _reflectionHistory[0].Dispose();
        _reflectionHistory[1].Dispose();
        _sceneCopy = CreateTexture(width, height, "ssr_scene_copy");
        _reflectionRaw = CreateTexture(traceWidth, traceHeight, "ssr_raw");
        _reflectionHistory[0] = CreateTexture(traceWidth, traceHeight, "ssr_history_a");
        _reflectionHistory[1] = CreateTexture(traceWidth, traceHeight, "ssr_history_b");
        _historyReadIndex = 0;
        _historyValid = false;
        BindPersistentResources();
    }

    /// <inheritdoc />
    public void OnRenderForward(GPUFrameBuffer target, GPUAttachmentLayout layout)
    {
        RenderTexture scene = _pipeline.ForwardRenderTexture;
        EnsureTextures(scene.Width, scene.Height);

        bool ssrOnly = _voxelGi.SsrOnly;
        if (ssrOnly != _lastSsrOnly)
        {
            _historyValid = false;
            _lastSsrOnly = ssrOnly;
        }

        Matrix4x4 viewProjection = _camera.Data.ViewProjectionMatrix;
        if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 invViewProjection))
        {
            invViewProjection = Matrix4x4.Identity;
        }

        SsrData data = new()
        {
            InvViewProjection = invViewProjection,
            ViewProjection = viewProjection,
            PreviousViewProjection = _historyValid ? _previousViewProjection : viewProjection,
            CameraPosition = new Vector4(_camera.Transform.Position, 1.0f),
            RenderSize = new Vector4(scene.Width, scene.Height,
                _reflectionRaw.Width, _reflectionRaw.Height),
            Params = new Vector4(_frameIndex, _historyValid ? 1.0f : 0.0f,
                (int)_voxelGi.DebugView, _pipeline.GiSpecularStrength),
            RayParams = new Vector4(MaxTraceDistance, RoughnessCutoff, 0.0f, 0.0f),
        };
        _dataBuffer.UpdateBuffer(data);

        // Preserve the completed HDR scene before overwriting the pipeline target.
        _copyMaterial.SetRenderTexture(ShaderResourceId.Texture, scene);
        _renderContext.Begin(_sceneCopy.FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _copyMaterial);
        _renderContext.End();

        _renderContext.Begin(_reflectionRaw.FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _traceMaterial);
        _renderContext.End();

        int historyWriteIndex = 1 - _historyReadIndex;
        _resolveMaterial.SetRenderTexture(
            "_reflectionHistory", _reflectionHistory[_historyReadIndex]);
        _renderContext.Begin(_reflectionHistory[historyWriteIndex].FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _resolveMaterial);
        _renderContext.End();

        _compositeMaterial.SetRenderTexture(
            "_reflection", _reflectionHistory[historyWriteIndex]);
        _renderContext.Begin(target);
        _renderContext.Draw(_fullScreenMesh, _compositeMaterial);
        _renderContext.End();

        _historyReadIndex = historyWriteIndex;
        _historyValid = true;
        _previousViewProjection = viewProjection;
        _frameIndex++;
    }

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        EnsureTextures(width, height);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _copyMaterial.Dispose();
            _traceMaterial.Dispose();
            _resolveMaterial.Dispose();
            _compositeMaterial.Dispose();
            _dataBuffer.Dispose();
            _sceneCopy.Dispose();
            _reflectionRaw.Dispose();
            _reflectionHistory[0].Dispose();
            _reflectionHistory[1].Dispose();
            _renderContext.Dispose();
        }
    }
}
