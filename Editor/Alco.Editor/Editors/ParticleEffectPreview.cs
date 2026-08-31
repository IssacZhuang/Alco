using System.Numerics;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.Particles;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// The live preview viewport of the particle effect document: a small offscreen
/// <see cref="RenderPipeline"/> (HDR scene + ACES tonemap + blit into an RGBA8
/// target shown through <c>ImGui.Image</c>) running one instance of the edited
/// effect on a private <see cref="GpuParticleSystem2D"/>/<see cref="GpuParticleSystem3D"/>.
/// <br/>The viewport owns its camera: 2D pans (drag) and zooms (wheel), 3D orbits
/// (drag) and dollies (wheel) around the effect at the origin. Transport controls
/// (pause, stop, restart, time scale) ride the simulation callback's delta time;
/// pausing passes 0, which freezes the simulation but keeps pool bookkeeping alive.
/// </summary>
public sealed class ParticleEffectPreview : AutoDisposable
{
    /// <summary>The deterministic instance seed, so restarts replay identically.</summary>
    private const int PreviewSeed = 1;

    private readonly bool _is3D;
    private readonly RenderPipeline _pipeline;
    private readonly RenderTexture _target;
    private readonly GpuParticleSystem2D? _system2D;
    private readonly GpuParticleSystem3D? _system3D;
    private readonly Camera2DBuffer? _camera2D;
    private readonly CameraPerspectiveBuffer? _camera3D;

    private ParticleEffectInstance2D? _instance2D;
    private ParticleEffectInstance3D? _instance3D;
    private string _error = string.Empty;

    private bool _paused;
    private float _timeScale = 1f;
    private ColorFloat _background = new(0.13f, 0.13f, 0.13f, 1f);

    // 2D camera state: position plus a zoom multiplier over the base view height.
    private const float BaseViewHeight2D = 36f;
    private float _zoom2D = 1f;

    // 3D orbit camera state around the origin.
    private float _orbitYaw = MathF.PI;
    private float _orbitPitch = 0.26f;
    private float _orbitDistance = 8f;

    /// <summary>Creates the preview for the given effect dimension.</summary>
    /// <param name="context">The editor context (engine services).</param>
    /// <param name="is3D">True to preview 3D effects, false for 2D.</param>
    public ParticleEffectPreview(EditorContext context, bool is3D)
    {
        ArgumentNullException.ThrowIfNull(context);
        _is3D = is3D;
        RenderingSystem rendering = context.RenderingSystem;
        BuiltInAssets builtIn = context.Engine.BuiltInAssets;

        _pipeline = new RenderPipeline(rendering, new RenderPipeline.Descriptor
        {
            SceneLayout = rendering.PreferredHDRPass,
            BlitShader = builtIn.Shader_Blit,
            Width = 512,
            Height = 288,
            Name = "particle_preview",
        });
        _pipeline.ClearColor = _background;
        _pipeline.Use(new RGNode_Callback { Callback = OnSimulate });
        _pipeline.Use(new SceneNode(this, _pipeline.Graph, _pipeline.Chain));
        var tonemap = new RGNode_Tonemap(
            rendering,
            _pipeline.Graph,
            _pipeline.Chain,
            _pipeline.PostProcessLayout,
            new RGNode_Tonemap.Descriptor
            {
                BlitShader = builtIn.Shader_Blit,
                ReinhardShader = builtIn.Shader_ReinhardLuminanceTonemap,
                Uncharted2Shader = builtIn.Shader_Uncharted2Tonemap,
                FilmicShader = builtIn.Shader_FilmicTonemap,
                AcesShader = builtIn.Shader_AcesTonemap,
                NeutralShader = builtIn.Shader_NeutralTonemap,
                AgxShader = builtIn.Shader_AgxTonemap,
            })
        { Operator = TonemapType.ACES };
        // Linear-to-sRGB: the ACES default gamma of 1 leaves the frame in linear space.
        ACESTonemapData aces = tonemap.ACESData;
        aces.Gamma = 2.2f;
        tonemap.ACESData = aces;
        _pipeline.Use(tonemap);

        _target = rendering.CreateRenderTexture(rendering.PreferredRGBATexturePass, 512, 288, "particle_preview_target");

        if (is3D)
        {
            _camera3D = rendering.CreateCameraPerspective(0.9f, 16f / 9f, 0.1f, 300f, "particle_preview_3d");
            _system3D = new GpuParticleSystem3D(rendering)
            {
                Camera = _camera3D,
                // The preview pipeline clears depth to 1 with a plain (non-reversed) projection.
                DepthStencilState = DepthStencilState.Read,
            };
            UpdateCamera3D();
        }
        else
        {
            _camera2D = rendering.CreateCamera2D(BaseViewHeight2D * 16f / 9f, BaseViewHeight2D, 100f, "particle_preview_2d");
            _system2D = new GpuParticleSystem2D(rendering) { Camera = _camera2D };
        }
    }

    /// <summary>Whether the simulation is frozen (the instance timeline still exists).</summary>
    public bool IsPaused => _paused;

    /// <summary>The failure of the last effect rebuild, or empty when the preview is live.</summary>
    public string Error => _error;

    /// <summary>Replaces the previewed effect instance (the asset must match the preview's dimension).</summary>
    /// <param name="effect">A fresh effect asset object (never the document's edit copy).</param>
    public void SetEffect(ParticleEffectAsset effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _instance2D?.Dispose();
        _instance2D = null;
        _instance3D?.Dispose();
        _instance3D = null;
        _error = string.Empty;
        try
        {
            if (_is3D)
            {
                _instance3D = _system3D!.CreateInstance((ParticleEffect3DAsset)effect, Transform3D.Identity, PreviewSeed);
            }
            else
            {
                _instance2D = _system2D!.CreateInstance((ParticleEffect2DAsset)effect, Transform2D.Identity, PreviewSeed);
            }
        }
        catch (Exception e)
        {
            // Bad references (behavior module, material, texture) must not crash the
            // editor: keep the empty preview and show the error over the viewport.
            _error = e.Message;
        }
    }

    /// <summary>
    /// Hot-applies the edited static fields of one 2D group to the live instance
    /// (no respawn); a no-op while the preview has no live instance.
    /// </summary>
    public void LiveUpdateGroup(int groupIndex, ParticleGroup2DAsset group)
    {
        if (_instance2D == null || groupIndex >= _instance2D.GroupCount)
        {
            return;
        }
        _instance2D.SetGroupEmissionRate(groupIndex, group.EmissionRate);
        _instance2D.SetGroupParams(groupIndex, EmitterParams2D.FromAsset(group, _system2D!.QuadMesh.GetSubMesh(0).IndexCount));
    }

    /// <summary>The 3D counterpart of <see cref="LiveUpdateGroup(int, ParticleGroup2DAsset)"/>.</summary>
    public void LiveUpdateGroup(int groupIndex, ParticleGroup3DAsset group)
    {
        if (_instance3D == null || groupIndex >= _instance3D.GroupCount)
        {
            return;
        }
        _instance3D.SetGroupEmissionRate(groupIndex, group.EmissionRate);
        _instance3D.SetGroupParams(groupIndex, EmitterParams3D.FromAsset(group, _system3D!.QuadMesh.GetSubMesh(0).IndexCount));
    }

    /// <summary>Draws the transport toolbar, the viewport and the status line.</summary>
    public void Draw()
    {
        DrawToolbar();
        DrawViewport();
        DrawStatusLine();
    }

    /// <summary>The transport row: pause/resume, stop, restart, time scale, background, camera reset.</summary>
    private void DrawToolbar()
    {
        if (ImGui.Button(_paused ? "Resume" : "Pause"))
        {
            _paused = !_paused;
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(_instance2D == null && _instance3D == null);
        if (ImGui.Button("Stop"))
        {
            _instance2D?.Stop();
            _instance3D?.Stop();
        }
        ImGui.SameLine();
        if (ImGui.Button("Restart"))
        {
            _instance2D?.Restart();
            _instance3D?.Restart();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(110f);
        ImGui.SliderFloat("##timescale", ref _timeScale, 0.05f, 4f, "speed %.2fx");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        if (ImGui.ColorEdit3("##background", ref _background, ImGuiColorEditFlags.NoInputs))
        {
            _pipeline.ClearColor = _background;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Viewport background");
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset Camera"))
        {
            ResetCamera();
        }
    }

    /// <summary>Renders the frame into the target and draws it, handling viewport input.</summary>
    private void DrawViewport()
    {
        Vector2 available = ImGui.GetContentRegionAvail();
        float width = Math.Max(available.X, 64f);
        float height = Math.Max(width * 9f / 16f, 64f);
        // Leave a line for the status text below.
        height = Math.Min(height, Math.Max(available.Y - ImGui.GetTextLineHeightWithSpacing(), 64f));
        Vector2 imageSize = new(width, height);

        uint pixelWidth = (uint)Math.Max((int)imageSize.X, 8);
        uint pixelHeight = (uint)Math.Max((int)imageSize.Y, 8);
        if (pixelWidth != _target.Width || pixelHeight != _target.Height)
        {
            _pipeline.Resize(pixelWidth, pixelHeight);
            _target.Resize(pixelWidth, pixelHeight);
            if (_is3D)
            {
                _camera3D!.AspectRatio = (float)pixelWidth / pixelHeight;
            }
            else
            {
                UpdateCamera2D();
            }
        }

        _pipeline.Render(_target.FrameBuffer);

        ImGui.Image(_target.ColorTextures[0], imageSize);

        // Viewport input overlay: drag pans (2D) / orbits (3D), wheel zooms.
        Vector2 imageMin = ImGui.GetItemRectMin();
        ImGui.SetCursorScreenPos(imageMin);
        ImGui.InvisibleButton("##viewport_input", imageSize);
        ImGuiIOPtr io = ImGui.GetIO();
        if (ImGui.IsItemHovered() && io.MouseWheel != 0f)
        {
            if (_is3D)
            {
                _orbitDistance = Math.Clamp(_orbitDistance * MathF.Pow(0.9f, io.MouseWheel), 1f, 200f);
                UpdateCamera3D();
            }
            else
            {
                _zoom2D = Math.Clamp(_zoom2D * MathF.Pow(0.9f, io.MouseWheel), 0.05f, 40f);
                UpdateCamera2D();
            }
        }
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            Vector2 delta = io.MouseDelta;
            if (_is3D)
            {
                _orbitYaw -= delta.X * 0.008f;
                _orbitPitch = Math.Clamp(_orbitPitch + delta.Y * 0.008f, -1.5f, 1.5f);
                UpdateCamera3D();
            }
            else
            {
                Vector2 worldPerPixel = _camera2D!.ViewSize / imageSize;
                _camera2D.Position -= delta * worldPerPixel;
            }
        }

        if (_error.Length > 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), _error);
        }
    }

    /// <summary>The diagnostics line under the viewport.</summary>
    private void DrawStatusLine()
    {
        int groups = _instance2D?.GroupCount ?? _instance3D?.GroupCount ?? 0;
        float time = _instance2D?.Time ?? _instance3D?.Time ?? 0f;
        ImGui.TextDisabled(
            $"t={time:0.00}s | {groups} group(s) | {(_is3D ? "3D — drag orbits, wheel dollies" : "2D — drag pans, wheel zooms")}{(_paused ? " | paused" : string.Empty)}");
    }

    /// <summary>Restores the default camera pose.</summary>
    private void ResetCamera()
    {
        if (_is3D)
        {
            _orbitYaw = MathF.PI;
            _orbitPitch = 0.26f;
            _orbitDistance = 8f;
            UpdateCamera3D();
        }
        else
        {
            _zoom2D = 1f;
            _camera2D!.Position = Vector2.Zero;
            UpdateCamera2D();
        }
    }

    /// <summary>Applies the zoom to the 2D camera, keeping the viewport aspect.</summary>
    private void UpdateCamera2D()
    {
        float aspect = _target.Height == 0 ? 16f / 9f : (float)_target.Width / _target.Height;
        _camera2D!.ViewSize = new Vector2(BaseViewHeight2D * _zoom2D * aspect, BaseViewHeight2D * _zoom2D);
    }

    /// <summary>Repositions the 3D camera on its orbit around the origin.</summary>
    private void UpdateCamera3D()
    {
        Vector3 position = new(
            _orbitDistance * MathF.Cos(_orbitPitch) * MathF.Cos(_orbitYaw),
            _orbitDistance * MathF.Cos(_orbitPitch) * MathF.Sin(_orbitYaw),
            _orbitDistance * MathF.Sin(_orbitPitch));
        // Engine camera convention: forward = +X, up = +Z (Transform3D.LookAt
        // aims +Z instead, so build the rotation here).
        Vector3 forward = Vector3.Normalize(-position);
        Vector3 up = Vector3.Normalize(Vector3.UnitZ - forward * Vector3.Dot(forward, Vector3.UnitZ));
        Vector3 right = Vector3.Cross(up, forward);
        Matrix4x4 rotation = new(
            forward.X, forward.Y, forward.Z, 0,
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            0, 0, 0, 1);
        _camera3D!.Transform.Position = position;
        _camera3D!.Transform.Rotation = Quaternion.CreateFromRotationMatrix(rotation);
        // Mutating through the Transform ref does not flag the buffer dirty.
        _camera3D!.UpdateMatrixToGPU();
    }

    /// <summary>The simulation step recorded ahead of the scene pass each frame.</summary>
    private void OnSimulate(RenderGraphContext context)
    {
        float delta = _paused ? 0f : context.DeltaTime * _timeScale;
        if (_is3D)
        {
            _system3D!.RecordSimulation(context.RenderContext.CommandBuffer, delta);
        }
        else
        {
            _system2D!.RecordSimulation(context.RenderContext.CommandBuffer, delta);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _instance2D?.Dispose();
            _instance3D?.Dispose();
            _system2D?.Dispose();
            _system3D?.Dispose();
            _pipeline.Dispose();
            _target.Dispose();
            _camera2D?.Dispose();
            _camera3D?.Dispose();
        }
    }

    /// <summary>The scene content node the preview's particles draw into.</summary>
    private sealed class SceneNode(ParticleEffectPreview owner, RenderGraph graph, RenderChain chain)
        : RGNode_SceneContent(graph, chain)
    {
        protected override void OnRender(in RenderGraphContext context, GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            using (RenderPassScope pass = context.RenderContext.BeginPass(target))
            {
                if (owner._is3D)
                {
                    owner._system3D!.Render(pass);
                }
                else
                {
                    owner._system2D!.Render(pass);
                }
            }
        }
    }
}
