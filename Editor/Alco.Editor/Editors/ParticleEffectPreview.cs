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
/// <br/>The viewport owns its camera: 2D pans (left drag) and zooms (wheel) with a
/// default view about four world units wide; 3D orbits (left drag), dollies (wheel)
/// and pans (middle drag) around the orbit target. Transport controls
/// (pause, stop, restart, time scale) ride the simulation callback's delta time;
/// pausing passes 0, which freezes the simulation but keeps pool bookkeeping alive.
/// <br/>A helper overlay (world grid, axes, per-group emitter shape outlines from
/// <see cref="OverlaySource"/>) draws over the image through the window draw list.
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

    // Helper overlay toggles (grid, world axes, per-group emitter shapes).
    private bool _showGrid = true;
    private bool _showAxes = true;
    private bool _showShapes = true;

    /// <summary>Per-group overlay outline colors, cycled by group index.</summary>
    private static readonly Vector4[] ShapePalette =
    [
        new(1f, 0.8f, 0.2f, 1f),
        new(0.3f, 0.9f, 1f, 1f),
        new(1f, 0.45f, 0.8f, 1f),
        new(0.6f, 1f, 0.35f, 1f),
        new(1f, 0.6f, 0.25f, 1f),
        new(0.8f, 0.6f, 1f, 1f),
    ];

    // 2D camera state: position plus a zoom multiplier over the base view width.
    private const float BaseViewWidth2D = 4f;
    private float _zoom2D = 1f;

    // 3D orbit camera state around the orbit target.
    private float _orbitYaw = MathF.PI;
    private float _orbitPitch = 0.26f;
    private float _orbitDistance = 8f;
    private Vector3 _orbitTarget = Vector3.Zero;

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
            _camera2D = rendering.CreateCamera2D(BaseViewWidth2D, BaseViewWidth2D * 9f / 16f, 100f, "particle_preview_2d");
            _system2D = new GpuParticleSystem2D(rendering) { Camera = _camera2D };
        }
    }

    /// <summary>Whether the simulation is frozen (the instance timeline still exists).</summary>
    public bool IsPaused => _paused;

    /// <summary>The failure of the last effect rebuild, or empty when the preview is live.</summary>
    public string Error => _error;

    /// <summary>
    /// The document's edit asset, read per frame to draw the emitter shape overlays.
    /// Set once by the owning document; edits mutate this object in place, so the
    /// overlay always reflects the current parameters (live or not yet rebuilt).
    /// </summary>
    public ParticleEffectAsset? OverlaySource { get; set; }

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

        ImGui.SameLine();
        ImGui.Checkbox("Grid", ref _showGrid);
        ImGui.SameLine();
        ImGui.Checkbox("Axes", ref _showAxes);
        ImGui.SameLine();
        ImGui.Checkbox("Shapes", ref _showShapes);
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

        // Camera2DBuffer uploads its matrix lazily via FlushDirty, which only
        // runs when a material bind group reads the buffer. The preview's bind
        // groups are cached across frames, so a dirty 2D camera matrix would
        // never reach the GPU and particles would ignore zoom/pan. Upload it
        // explicitly each frame (the 3D path does the same in UpdateCamera3D).
        _camera2D?.UpdateMatrixToGPU();
        _pipeline.Render(_target.FrameBuffer);

        ImGui.Image(_target.ColorTextures[0], imageSize);

        // Viewport input overlay: drag pans (2D) / orbits (3D), wheel zooms.
        // The button must opt into middle-button activation: by default ImGui
        // widgets only respond to the left button.
        Vector2 imageMin = ImGui.GetItemRectMin();
        ImGui.SetCursorScreenPos(imageMin);
        ImGui.InvisibleButton("##viewport_input", imageSize, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonMiddle);
        ImGuiIOPtr io = ImGui.GetIO();
        // Rect hit-test fallback on top of the item hover: an occluded or
        // mis-layered button must never silently kill wheel zooming.
        Vector2 mousePos = io.MousePos;
        bool viewportHovered = ImGui.IsItemHovered()
            || (mousePos.X >= imageMin.X && mousePos.X < imageMin.X + imageSize.X
                && mousePos.Y >= imageMin.Y && mousePos.Y < imageMin.Y + imageSize.Y);
        if (viewportHovered && io.MouseWheel != 0f)
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
                // Orbit: dragging right swings the camera to its left around the
                // target (the scene appears to rotate right), matching DCC tools.
                _orbitYaw += delta.X * 0.008f;
                _orbitPitch = Math.Clamp(_orbitPitch + delta.Y * 0.008f, -1.5f, 1.5f);
                UpdateCamera3D();
            }
            else
            {
                // Grab-style pan: the content follows the mouse. World Y points up
                // but screen Y points down, so the vertical component is flipped.
                Vector2 worldPerPixel = _camera2D!.ViewSize / imageSize;
                _camera2D.Position -= new Vector2(delta.X, -delta.Y) * worldPerPixel;
            }
        }
        if (_is3D && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
        {
            // Pan: slide the orbit target in the camera's right/up plane so the
            // content follows the mouse, matching the 2D drag convention.
            Vector2 delta = io.MouseDelta;
            float worldPerPixel = 2f * _orbitDistance * MathF.Tan(_camera3D!.FieldOfView * 0.5f) / imageSize.Y;
            Vector3 position = GetOrbitPosition();
            (Vector3 right, Vector3 up, _) = ComputeCameraBasis(position, _orbitTarget);
            _orbitTarget += -right * (delta.X * worldPerPixel) + up * (delta.Y * worldPerPixel);
            UpdateCamera3D();
        }

        DrawOverlay(imageMin, imageSize);

        if (_error.Length > 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), _error);
        }
    }

    /// <summary>
    /// Draws the helper overlay (grid, world axes, emitter shapes) over the viewport
    /// image, in world space projected through the preview camera. 2D shapes live on
    /// the Z = 0 plane; the 3D grid lies on the XY ground plane (Z+ up).
    /// </summary>
    private void DrawOverlay(Vector2 imageMin, Vector2 imageSize)
    {
        if (!_showGrid && !_showAxes && !_showShapes)
        {
            return;
        }

        Matrix4x4 viewProjection = _is3D
            ? _camera3D!.Data.ViewProjectionMatrix
            : _camera2D!.Data.ViewProjectionMatrix;
        // Adaptive 1-2-5 spacing, roughly ten cells across the view.
        float step = PickGridStep((_is3D ? _orbitDistance : _camera2D!.ViewSize.X) / 10f);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(imageMin, imageMin + imageSize, true);
        if (_showGrid)
        {
            DrawGrid(drawList, viewProjection, imageMin, imageSize, step);
        }
        if (_showAxes)
        {
            DrawAxes(drawList, viewProjection, imageMin, imageSize, step);
        }
        if (_showShapes && OverlaySource != null)
        {
            DrawEmitterShapes(drawList, viewProjection, imageMin, imageSize);
        }
        drawList.PopClipRect();
    }

    /// <summary>Draws the world grid with the given cell size.</summary>
    private void DrawGrid(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, float step)
    {
        uint color = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f));
        if (_is3D)
        {
            // Fixed extent around the origin on the ground plane (Z = 0).
            float extent = step * 10f;
            for (float f = -extent; f <= extent; f += step)
            {
                DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
                    new Vector3(-extent, f, 0f), new Vector3(extent, f, 0f), color);
                DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
                    new Vector3(f, -extent, 0f), new Vector3(f, extent, 0f), color);
            }
            return;
        }

        // Cover the visible world rectangle.
        Vector2 center = _camera2D!.Position;
        Vector2 half = _camera2D.ViewSize * 0.5f;
        for (float x = MathF.Floor((center.X - half.X) / step) * step; x <= center.X + half.X; x += step)
        {
            DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
                new Vector3(x, center.Y - half.Y, 0f), new Vector3(x, center.Y + half.Y, 0f), color);
        }
        for (float y = MathF.Floor((center.Y - half.Y) / step) * step; y <= center.Y + half.Y; y += step)
        {
            DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
                new Vector3(center.X - half.X, y, 0f), new Vector3(center.X + half.X, y, 0f), color);
        }
    }

    /// <summary>Draws the world axes through the origin: X red, Y green, Z blue.</summary>
    private void DrawAxes(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, float step)
    {
        uint xColor = ImGui.GetColorU32(new Vector4(1f, 0.3f, 0.3f, 0.8f));
        uint yColor = ImGui.GetColorU32(new Vector4(0.35f, 0.9f, 0.35f, 0.8f));
        const float thickness = 1.5f;
        if (_is3D)
        {
            float extent = step * 10f;
            DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
                new Vector3(-extent, 0f, 0f), new Vector3(extent, 0f, 0f), xColor, thickness);
            DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
                new Vector3(0f, -extent, 0f), new Vector3(0f, extent, 0f), yColor, thickness);
            // Z+ up: only the positive half pokes out of the ground plane grid.
            uint zColor = ImGui.GetColorU32(new Vector4(0.35f, 0.5f, 1f, 0.8f));
            DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
                Vector3.Zero, new Vector3(0f, 0f, extent), zColor, thickness);
            return;
        }

        Vector2 center = _camera2D!.Position;
        Vector2 half = _camera2D.ViewSize * 0.5f;
        DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
            new Vector3(center.X - half.X, 0f, 0f), new Vector3(center.X + half.X, 0f, 0f), xColor, thickness);
        DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
            new Vector3(0f, center.Y - half.Y, 0f), new Vector3(0f, center.Y + half.Y, 0f), yColor, thickness);
    }

    /// <summary>Draws every group's emitter shape outline, colored by group index.</summary>
    private void DrawEmitterShapes(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize)
    {
        if (_is3D)
        {
            List<ParticleGroup3DAsset> groups = ((ParticleEffect3DAsset)OverlaySource!).Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                DrawShape3D(drawList, viewProjection, imageMin, imageSize, groups[i].Shape, ShapeColor(i));
            }
            return;
        }

        List<ParticleGroup2DAsset> groups2D = ((ParticleEffect2DAsset)OverlaySource!).Groups;
        for (int i = 0; i < groups2D.Count; i++)
        {
            DrawShape2D(drawList, viewProjection, imageMin, imageSize, groups2D[i].Shape, ShapeColor(i));
        }
    }

    /// <summary>Draws one 2D emitter shape (point cross, circle, or box) at the origin.</summary>
    private void DrawShape2D(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, ParticleShape2D shape, uint color)
    {
        switch (shape.Type)
        {
            case ParticleShape2DType.Point:
                DrawScreenCross(drawList, WorldToScreen(Vector3.Zero, viewProjection, imageMin, imageSize), color);
                break;
            case ParticleShape2DType.Circle:
                Vector2 center = WorldToScreen(Vector3.Zero, viewProjection, imageMin, imageSize);
                float pixelsPerUnit = imageSize.X / _camera2D!.ViewSize.X;
                float radius = Math.Max(shape.Radius * pixelsPerUnit, 0f);
                drawList.AddCircle(center, radius, color, CircleSegments(radius), 1.5f);
                if (shape.InnerRadius > 0.001f)
                {
                    float inner = radius * shape.InnerRadius;
                    drawList.AddCircle(center, inner, color, CircleSegments(inner), 1.5f);
                }
                break;
            case ParticleShape2DType.Box:
                Vector2 a = WorldToScreen(new Vector3(-shape.Extents.X, -shape.Extents.Y, 0f), viewProjection, imageMin, imageSize);
                Vector2 b = WorldToScreen(new Vector3(shape.Extents.X, shape.Extents.Y, 0f), viewProjection, imageMin, imageSize);
                drawList.AddRect(Vector2.Min(a, b), Vector2.Max(a, b), color, 0f, ImDrawFlags.None, 1.5f);
                break;
        }
    }

    /// <summary>Draws one 3D emitter shape (point cross, sphere, hemisphere, or box) at the origin.</summary>
    private void DrawShape3D(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, ParticleShape3D shape, uint color)
    {
        switch (shape.Type)
        {
            case ParticleShape3DType.Point:
                DrawScreenCross(drawList, WorldToScreen(Vector3.Zero, viewProjection, imageMin, imageSize), color);
                break;
            case ParticleShape3DType.Sphere:
                DrawProjectedCircle(drawList, viewProjection, imageMin, imageSize, shape.Radius, 0, false, color);
                DrawProjectedCircle(drawList, viewProjection, imageMin, imageSize, shape.Radius, 1, false, color);
                DrawProjectedCircle(drawList, viewProjection, imageMin, imageSize, shape.Radius, 2, false, color);
                break;
            case ParticleShape3DType.Hemisphere:
                // Base circle on the ground plane plus two meridian arcs above it.
                DrawProjectedCircle(drawList, viewProjection, imageMin, imageSize, shape.Radius, 2, false, color);
                DrawProjectedCircle(drawList, viewProjection, imageMin, imageSize, shape.Radius, 0, true, color);
                DrawProjectedCircle(drawList, viewProjection, imageMin, imageSize, shape.Radius, 1, true, color);
                break;
            case ParticleShape3DType.Box:
                Span<Vector3> corners = stackalloc Vector3[8];
                for (int i = 0; i < 8; i++)
                {
                    corners[i] = new Vector3(
                        (i & 1) == 0 ? -shape.Extents.X : shape.Extents.X,
                        (i & 2) == 0 ? -shape.Extents.Y : shape.Extents.Y,
                        (i & 4) == 0 ? -shape.Extents.Z : shape.Extents.Z);
                }
                ReadOnlySpan<int> edges = [0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7];
                for (int i = 0; i < edges.Length; i += 2)
                {
                    DrawWorldLine(drawList, viewProjection, imageMin, imageSize, corners[edges[i]], corners[edges[i + 1]], color, 1.5f);
                }
                break;
        }
    }

    /// <summary>
    /// Draws a circle (or upper-half arc) of the given radius around the origin,
    /// tessellated in the plane perpendicular to <paramref name="planeAxis"/>
    /// (0 = YZ, 1 = XZ, 2 = XY) and projected to the viewport.
    /// </summary>
    private void DrawProjectedCircle(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, float radius, int planeAxis, bool upperHalf, uint color)
    {
        const int segments = 40;
        int count = upperHalf ? segments / 2 : segments;
        Vector3 previous = default;
        for (int i = 0; i <= count; i++)
        {
            float t = i * (upperHalf ? MathF.PI : MathF.Tau) / count;
            float cos = MathF.Cos(t) * radius;
            float sin = MathF.Sin(t) * radius;
            Vector3 point = planeAxis switch
            {
                0 => new Vector3(0f, cos, sin),
                1 => new Vector3(cos, 0f, sin),
                _ => new Vector3(cos, sin, 0f),
            };
            if (i > 0)
            {
                DrawWorldLine(drawList, viewProjection, imageMin, imageSize, previous, point, color, 1.5f);
            }
            previous = point;
        }
    }

    /// <summary>Draws a small fixed-size cross marking a point emitter.</summary>
    private static void DrawScreenCross(ImDrawListPtr drawList, Vector2 center, uint color)
    {
        const float arm = 5f;
        drawList.AddLine(center + new Vector2(-arm, 0f), center + new Vector2(arm, 0f), color, 1.5f);
        drawList.AddLine(center + new Vector2(0f, -arm), center + new Vector2(0f, arm), color, 1.5f);
    }

    /// <summary>The overlay outline color of the group at the given index.</summary>
    private static uint ShapeColor(int groupIndex)
    {
        return ImGui.GetColorU32(ShapePalette[groupIndex % ShapePalette.Length]);
    }

    /// <summary>Snaps a raw grid spacing to a 1-2-5 decade step.</summary>
    private static float PickGridStep(float raw)
    {
        float decade = MathF.Pow(10f, MathF.Floor(MathF.Log10(raw)));
        float scaled = raw / decade;
        return decade * (scaled < 1.5f ? 1f : scaled < 3.5f ? 2f : scaled < 7.5f ? 5f : 10f);
    }

    /// <summary>The tessellation segment count for a circle of the given pixel radius.</summary>
    private static int CircleSegments(float pixelRadius)
    {
        return Math.Clamp((int)(pixelRadius * 0.75f), 12, 64);
    }

    /// <summary>
    /// Draws a world-space line segment, clipped against the camera near plane in
    /// clip space first. Segments touching the space behind the camera otherwise
    /// project through a negative W and wrap around to a mirrored "vanishing
    /// point" on screen; fully hidden segments are dropped.
    /// </summary>
    private static void DrawWorldLine(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, Vector3 a, Vector3 b, uint color, float thickness = 1f)
    {
        Vector4 clipA = Vector4.Transform(a, viewProjection);
        Vector4 clipB = Vector4.Transform(b, viewProjection);
        const float nearW = 0.01f;
        if (clipA.W < nearW && clipB.W < nearW)
        {
            return;
        }
        if (clipA.W < nearW)
        {
            clipA = Vector4.Lerp(clipA, clipB, (nearW - clipA.W) / (clipB.W - clipA.W));
        }
        else if (clipB.W < nearW)
        {
            clipB = Vector4.Lerp(clipA, clipB, (nearW - clipA.W) / (clipB.W - clipA.W));
        }
        drawList.AddLine(ClipToScreen(clipA, imageMin, imageSize), ClipToScreen(clipB, imageMin, imageSize), color, thickness);
    }

    /// <summary>
    /// Projects a world point to viewport pixels with the engine's Y-flip screen
    /// convention (same as <c>GizmoMath.WorldToScreen</c>, which is engine-internal).
    /// The point must be in front of the camera (positive clip W).
    /// </summary>
    private static Vector2 WorldToScreen(in Vector3 world, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize)
    {
        return ClipToScreen(Vector4.Transform(world, viewProjection), imageMin, imageSize);
    }

    /// <summary>Maps a clip-space position (positive W) to viewport pixels, Y flipped.</summary>
    private static Vector2 ClipToScreen(in Vector4 clip, Vector2 imageMin, Vector2 imageSize)
    {
        float invW = 1f / clip.W;
        return new Vector2(
            (clip.X * invW * 0.5f + 0.5f) * imageSize.X + imageMin.X,
            (1f - (clip.Y * invW * 0.5f + 0.5f)) * imageSize.Y + imageMin.Y);
    }

    /// <summary>The 2D camera state: the zoom multiplier over the base view width and the view center.</summary>
    public (float Zoom, Vector2 Position) Camera2DState => (_zoom2D, _camera2D?.Position ?? Vector2.Zero);

    /// <summary>The 3D orbit camera state: yaw, pitch, distance and orbit target.</summary>
    public (float Yaw, float Pitch, float Distance, Vector3 Target) Camera3DState => (_orbitYaw, _orbitPitch, _orbitDistance, _orbitTarget);

    /// <summary>Sets the 2D camera zoom and center, applying the viewport's zoom clamps.</summary>
    /// <param name="zoom">The zoom multiplier over the base view width (smaller zooms in).</param>
    /// <param name="position">The world-space view center.</param>
    public void SetCamera2DState(float zoom, Vector2 position)
    {
        if (_is3D)
        {
            throw new InvalidOperationException("The preview renders a 3D effect.");
        }
        _zoom2D = Math.Clamp(zoom, 0.05f, 40f);
        _camera2D!.Position = position;
        UpdateCamera2D();
    }

    /// <summary>Sets the 3D orbit camera, applying the viewport's pitch/distance clamps.</summary>
    /// <param name="yaw">The orbit yaw in radians.</param>
    /// <param name="pitch">The orbit pitch in radians.</param>
    /// <param name="distance">The orbit distance.</param>
    /// <param name="target">The orbit target.</param>
    public void SetCamera3DState(float yaw, float pitch, float distance, Vector3 target)
    {
        if (!_is3D)
        {
            throw new InvalidOperationException("The preview renders a 2D effect.");
        }
        _orbitYaw = yaw;
        _orbitPitch = Math.Clamp(pitch, -1.5f, 1.5f);
        _orbitDistance = Math.Clamp(distance, 1f, 200f);
        _orbitTarget = target;
        UpdateCamera3D();
    }

    /// <summary>The diagnostics line under the viewport.</summary>
    private void DrawStatusLine()
    {
        int groups = _instance2D?.GroupCount ?? _instance3D?.GroupCount ?? 0;
        float time = _instance2D?.Time ?? _instance3D?.Time ?? 0f;
        // The zoom readout doubles as a wheel-input diagnostic: it must change on every scroll.
        string camera = _is3D ? $"dist {_orbitDistance:0.##}" : $"zoom {_zoom2D:0.##}x";
        ImGui.TextDisabled(
            $"t={time:0.00}s | {groups} group(s) | {camera} | {(_is3D ? "3D — left-drag orbits, middle-drag pans, wheel zooms" : "2D — left-drag pans, wheel zooms")}{(_paused ? " | paused" : string.Empty)}");
    }

    /// <summary>Restores the default camera pose.</summary>
    private void ResetCamera()
    {
        if (_is3D)
        {
            _orbitYaw = MathF.PI;
            _orbitPitch = 0.26f;
            _orbitDistance = 8f;
            _orbitTarget = Vector3.Zero;
            UpdateCamera3D();
        }
        else
        {
            _zoom2D = 1f;
            _camera2D!.Position = Vector2.Zero;
            UpdateCamera2D();
        }
    }

    /// <summary>Applies the zoom to the 2D camera, keeping the default view width.</summary>
    private void UpdateCamera2D()
    {
        float aspect = _target.Height == 0 ? 16f / 9f : (float)_target.Width / _target.Height;
        float width = BaseViewWidth2D * _zoom2D;
        _camera2D!.ViewSize = new Vector2(width, width / aspect);
    }

    /// <summary>The 3D camera position on its orbit around the orbit target.</summary>
    private Vector3 GetOrbitPosition()
    {
        return _orbitTarget + new Vector3(
            _orbitDistance * MathF.Cos(_orbitPitch) * MathF.Cos(_orbitYaw),
            _orbitDistance * MathF.Cos(_orbitPitch) * MathF.Sin(_orbitYaw),
            _orbitDistance * MathF.Sin(_orbitPitch));
    }

    /// <summary>
    /// Builds the camera basis for the engine convention: forward = +X, up = +Z
    /// (Transform3D.LookAt aims +Z instead, so the rotation is built manually).
    /// </summary>
    private static (Vector3 Right, Vector3 Up, Vector3 Forward) ComputeCameraBasis(Vector3 position, Vector3 target)
    {
        Vector3 forward = Vector3.Normalize(target - position);
        Vector3 up = Vector3.Normalize(Vector3.UnitZ - forward * Vector3.Dot(forward, Vector3.UnitZ));
        return (Vector3.Cross(up, forward), up, forward);
    }

    /// <summary>Repositions the 3D camera on its orbit around the orbit target.</summary>
    private void UpdateCamera3D()
    {
        Vector3 position = GetOrbitPosition();
        (Vector3 right, Vector3 up, Vector3 forward) = ComputeCameraBasis(position, _orbitTarget);
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
