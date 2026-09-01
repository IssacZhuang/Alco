using System.Numerics;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// A reusable offscreen preview viewport for asset editors: a small
/// <see cref="RenderPipeline"/> (HDR scene, a toolbar-switchable display
/// transform that defaults to the game's Neutral post chain, and a blit into
/// an RGBA8 target shown through <c>ImGui.Image</c>) with a built-in camera,
/// viewport input, and a world-space helper overlay (grid, axes and a scale bar).
/// <br/>The viewport does not know what it renders. The owning document hooks
/// the pipeline through delegates: <see cref="SceneContent"/> draws into the
/// scene pass, <see cref="RecordFrame"/> records per-frame GPU work ahead of it
/// (e.g. particle simulation), <see cref="OverlayExtras"/> draws extra
/// world-space markup, and <see cref="ToolbarLeading"/>/<see cref="ToolbarTrailing"/>
/// plus <see cref="StatusPrefix"/>/<see cref="StatusSuffix"/> extend the UI.
/// <br/>Use <see cref="PreviewViewport2D"/> or <see cref="PreviewViewport3D"/>;
/// the base class holds everything that is independent of the camera dimension.
/// </summary>
public abstract class PreviewViewport : AutoDisposable
{
    private readonly RenderPipeline _pipeline;
    private readonly RGNode_Tonemap _tonemap;
    private readonly RenderTexture _target;
    /// <summary>The smallest on-screen cell size before the grid falls back to a coarser decade.</summary>
    private const float MinGridCellPixels = 24f;

    /// <summary>The display-transform combo entries, in enum order.</summary>
    private static readonly TonemapType[] DisplayOperators =
    [
        TonemapType.Linear,
        TonemapType.Reinhard,
        TonemapType.Uncharted2,
        TonemapType.Filmic,
        TonemapType.ACES,
        TonemapType.Neutral,
        TonemapType.AgX,
    ];

    /// <summary>The finest grid step; finer measurement is the scale bar's job.</summary>
    private const float MinGridStep = 0.1f;

    private ColorFloat _background = new(0.15f, 0.15f, 0.15f, 1f);
    private bool _showGrid = true;
    private bool _showAxes = true;
    private bool _showRuler = true;
    private float _gridStep = 1f;

    /// <summary>Creates the pipeline, target and overlay plumbing.</summary>
    /// <param name="context">The editor context (engine services).</param>
    /// <param name="name">The base name for the pipeline and its resources.</param>
    protected PreviewViewport(EditorContext context, string name)
    {
        ArgumentNullException.ThrowIfNull(context);
        RenderingSystem rendering = context.RenderingSystem;
        BuiltInAssets builtIn = context.Engine.BuiltInAssets;

        _pipeline = new RenderPipeline(rendering, new RenderPipeline.Descriptor
        {
            SceneLayout = rendering.PreferredHDRPass,
            BlitShader = builtIn.Shader_Blit,
            Width = 512,
            Height = 288,
            Name = name,
        });
        _pipeline.ClearColor = _background;
        _pipeline.Use(new RGNode_Callback { Callback = renderContext => RecordFrame?.Invoke(renderContext) });
        _pipeline.Use(new SceneNode(this, _pipeline.Graph, _pipeline.Chain));
        _tonemap = new RGNode_Tonemap(
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
            });
        // Neutral with its default data is the game's post chain (Engine.cs uses
        // the same operator), so the preview shows authored colors the way they
        // present in game. The toolbar can switch operators for comparison.
        _tonemap.Operator = TonemapType.Neutral;
        _pipeline.Use(_tonemap);

        _target = rendering.CreateRenderTexture(rendering.PreferredRGBATexturePass, 512, 288, name + "_target");
    }

    /// <summary>The render target the pipeline draws into (for aspect ratios).</summary>
    protected RenderTexture Target => _target;

    /// <summary>The viewport clear color, editable in the toolbar.</summary>
    public ColorFloat Background
    {
        get => _background;
        set
        {
            _background = value;
            _pipeline.ClearColor = value;
        }
    }

    /// <summary>
    /// Records per-frame GPU work ahead of the scene pass (e.g. simulation).
    /// Runs inside the render graph; do not call ImGui here.
    /// </summary>
    public Action<RenderGraphContext>? RecordFrame { get; set; }

    /// <summary>Draws the scene content into the viewport's render pass.</summary>
    public Action<RenderPassScope>? SceneContent { get; set; }

    /// <summary>Draws extra toolbar widgets ahead of the view controls (e.g. transport).</summary>
    public Action? ToolbarLeading { get; set; }

    /// <summary>Draws extra toolbar widgets after the view controls (e.g. extra toggles).</summary>
    public Action? ToolbarTrailing { get; set; }

    /// <summary>
    /// Draws extra world-space overlay content over the viewport image. Receives
    /// the window draw list (already clipped to the viewport), the camera
    /// view-projection matrix and the image rectangle; use
    /// <see cref="WorldToScreen"/>/<see cref="DrawWorldLine"/> to project.
    /// </summary>
    public Action<ImDrawListPtr, Matrix4x4, Vector2, Vector2>? OverlayExtras { get; set; }

    /// <summary>Diagnostics shown before the camera readout in the status line.</summary>
    public Func<string>? StatusPrefix { get; set; }

    /// <summary>Diagnostics appended to the status line (e.g. "paused").</summary>
    public Func<string>? StatusSuffix { get; set; }

    /// <summary>A failure of the owner's content, shown in red under the viewport.</summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>The camera view-projection matrix used by the overlay projection.</summary>
    protected abstract Matrix4x4 ViewProjection { get; }

    /// <summary>The screen density at the camera's focus point, in pixels per world unit.</summary>
    protected abstract float PixelsPerUnit { get; }

    /// <summary>The camera readout in the status line (e.g. "zoom 1x", "dist 8").</summary>
    protected abstract string CameraStatus { get; }

    /// <summary>The input hint in the status line.</summary>
    protected abstract string InputHint { get; }

    /// <summary>Uploads the camera matrix to the GPU; called once per frame before rendering.</summary>
    protected abstract void CommitCamera();

    /// <summary>Applies a new target aspect ratio to the camera.</summary>
    protected abstract void OnTargetResize(uint pixelWidth, uint pixelHeight);

    /// <summary>Handles the mouse wheel over the viewport (zoom or dolly).</summary>
    protected abstract void OnWheel(float wheel);

    /// <summary>Handles a left-button drag over the viewport (pan or orbit).</summary>
    protected abstract void OnLeftDrag(Vector2 delta, Vector2 imageSize);

    /// <summary>Handles a middle-button drag over the viewport (3D pan); a no-op in 2D.</summary>
    protected virtual void OnMiddleDrag(Vector2 delta, Vector2 imageSize)
    {
    }

    /// <summary>Draws the world grid with the given 1-2-5 cell size.</summary>
    protected abstract void DrawGridLines(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, float step);

    /// <summary>Draws the world axes through the origin.</summary>
    protected abstract void DrawAxisLines(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, float step);

    /// <summary>Restores the default camera pose.</summary>
    public abstract void ResetCamera();

    /// <summary>Draws the toolbar, the viewport and the status line.</summary>
    public void Draw()
    {
        DrawToolbar();
        DrawViewport();
        DrawStatusLine();
    }

    private void DrawToolbar()
    {
        if (ToolbarLeading != null)
        {
            ToolbarLeading.Invoke();
            ImGui.SameLine();
        }
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
        ImGui.SetNextItemWidth(90f);
        if (ImGui.BeginCombo("##display_transform", DisplayOperatorLabel(_tonemap.Operator)))
        {
            foreach (TonemapType type in DisplayOperators)
            {
                if (ImGui.Selectable(DisplayOperatorLabel(type), _tonemap.Operator == type))
                {
                    _tonemap.Operator = type;
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Display transform. Neutral matches the game's post chain");
        }
        ImGui.SameLine();
        ImGui.Checkbox("Grid", ref _showGrid);
        ImGui.SameLine();
        ImGui.Checkbox("Axes", ref _showAxes);
        ImGui.SameLine();
        ImGui.Checkbox("Ruler", ref _showRuler);
        if (ToolbarTrailing != null)
        {
            ImGui.SameLine();
            ToolbarTrailing.Invoke();
        }
    }

    /// <summary>The toolbar label of a display-transform operator.</summary>
    private static string DisplayOperatorLabel(TonemapType type)
    {
        return type switch
        {
            TonemapType.Linear => "Linear",
            TonemapType.Reinhard => "Reinhard",
            TonemapType.Uncharted2 => "Uncharted 2",
            TonemapType.Filmic => "Filmic",
            TonemapType.ACES => "ACES",
            TonemapType.Neutral => "Neutral",
            TonemapType.AgX => "AgX",
            _ => type.ToString(),
        };
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
            OnTargetResize(pixelWidth, pixelHeight);
        }

        // Camera buffers upload their matrices lazily via FlushDirty, which only
        // runs when a material bind group reads the buffer. The preview's bind
        // groups are cached across frames, so a dirty camera matrix would never
        // reach the GPU and the scene would ignore camera moves. Upload it
        // explicitly each frame.
        CommitCamera();
        _pipeline.Render(_target.FrameBuffer);

        ImGui.Image(_target.ColorTextures[0], imageSize);

        // Viewport input overlay: the button must opt into middle-button
        // activation — by default ImGui widgets only respond to the left button.
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
            OnWheel(io.MouseWheel);
        }
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            OnLeftDrag(io.MouseDelta, imageSize);
        }
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
        {
            OnMiddleDrag(io.MouseDelta, imageSize);
        }

        DrawOverlay(imageMin, imageSize);

        if (Error.Length > 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), Error);
        }
    }

    /// <summary>Draws the helper overlay (grid, world axes, scale bar, extras) over the viewport image.</summary>
    private void DrawOverlay(Vector2 imageMin, Vector2 imageSize)
    {
        if (!_showGrid && !_showAxes && !_showRuler && OverlayExtras == null)
        {
            return;
        }

        Matrix4x4 viewProjection = ViewProjection;
        // The grid is a world-size ruler, not a screen-space pattern: cells keep
        // their true world size across a wide zoom range and only jump decades
        // when they would shrink below a readable pixel width.
        float pixelsPerUnit = PixelsPerUnit;
        _gridStep = PickAnchoredGridStep(pixelsPerUnit);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(imageMin, imageMin + imageSize, true);
        if (_showGrid)
        {
            DrawGridLines(drawList, viewProjection, imageMin, imageSize, _gridStep);
        }
        if (_showAxes)
        {
            DrawAxisLines(drawList, viewProjection, imageMin, imageSize, _gridStep);
        }
        if (_showRuler)
        {
            DrawScaleBar(drawList, imageMin, imageSize, pixelsPerUnit);
        }
        OverlayExtras?.Invoke(drawList, viewProjection, imageMin, imageSize);
        drawList.PopClipRect();
    }

    /// <summary>
    /// Snaps the grid step to the finest decade (0.1, 1, 10, ...) whose cells
    /// reach <see cref="MinGridCellPixels"/> on screen, anchoring one cell to a
    /// true world-unit size instead of resizing with the camera.
    /// </summary>
    private static float PickAnchoredGridStep(float pixelsPerUnit)
    {
        float step = MinGridStep;
        while (step * pixelsPerUnit < MinGridCellPixels)
        {
            step *= 10f;
        }
        return step;
    }

    /// <summary>Draws a bottom-left scale bar with a 1-2-5 world length close to 90 pixels.</summary>
    private void DrawScaleBar(ImDrawListPtr drawList, Vector2 imageMin, Vector2 imageSize, float pixelsPerUnit)
    {
        const float targetPixels = 90f;
        float length = PickGridStep(targetPixels / pixelsPerUnit);
        Vector2 start = new(imageMin.X + 12f, imageMin.Y + imageSize.Y - 14f);
        Vector2 end = start + new Vector2(length * pixelsPerUnit, 0f);
        uint color = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.55f));
        drawList.AddLine(start, end, color, 1.5f);
        drawList.AddLine(start, start + new Vector2(0f, -5f), color, 1.5f);
        drawList.AddLine(end, end + new Vector2(0f, -5f), color, 1.5f);
        string label = UnitsLabel(length);
        Vector2 textSize = ImGui.CalcTextSize(label);
        drawList.AddText(new Vector2((start.X + end.X - textSize.X) * 0.5f, start.Y - 6f - textSize.Y), color, label);
    }

    /// <summary>Formats a world-unit length for the grid readout and the scale bar ("0.5u").</summary>
    private static string UnitsLabel(float units)
    {
        return $"{units:0.##}u";
    }

    /// <summary>The diagnostics line under the viewport.</summary>
    private void DrawStatusLine()
    {
        // The camera readout doubles as an input diagnostic: it must change on
        // every wheel tick and drag.
        string prefix = StatusPrefix?.Invoke() ?? string.Empty;
        if (prefix.Length > 0)
        {
            prefix += " | ";
        }
        string camera = CameraStatus;
        if (_showGrid)
        {
            camera += $" | grid {UnitsLabel(_gridStep)}";
        }
        ImGui.TextDisabled($"{prefix}{camera} | {InputHint}{StatusSuffix?.Invoke() ?? string.Empty}");
    }

    /// <summary>Snaps a raw grid spacing to a 1-2-5 decade step.</summary>
    public static float PickGridStep(float raw)
    {
        float decade = MathF.Pow(10f, MathF.Floor(MathF.Log10(raw)));
        float scaled = raw / decade;
        return decade * (scaled < 1.5f ? 1f : scaled < 3.5f ? 2f : scaled < 7.5f ? 5f : 10f);
    }

    /// <summary>The tessellation segment count for a circle of the given pixel radius.</summary>
    public static int CircleSegments(float pixelRadius)
    {
        return Math.Clamp((int)(pixelRadius * 0.75f), 12, 64);
    }

    /// <summary>
    /// Draws a world-space line segment, clipped against the camera near plane in
    /// clip space first. Segments touching the space behind the camera otherwise
    /// project through a negative W and wrap around to a mirrored "vanishing
    /// point" on screen; fully hidden segments are dropped.
    /// </summary>
    public static void DrawWorldLine(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, Vector3 a, Vector3 b, uint color, float thickness = 1f)
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
    public static Vector2 WorldToScreen(in Vector3 world, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize)
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

    /// <summary>Draws a small fixed-size cross marking a point.</summary>
    public static void DrawScreenCross(ImDrawListPtr drawList, Vector2 center, uint color)
    {
        const float arm = 5f;
        drawList.AddLine(center + new Vector2(-arm, 0f), center + new Vector2(arm, 0f), color, 1.5f);
        drawList.AddLine(center + new Vector2(0f, -arm), center + new Vector2(0f, arm), color, 1.5f);
    }

    /// <summary>
    /// Draws a circle (or upper-half arc) of the given radius around the origin,
    /// tessellated in the plane perpendicular to <paramref name="planeAxis"/>
    /// (0 = YZ, 1 = XZ, 2 = XY) and projected to the viewport.
    /// </summary>
    public static void DrawProjectedCircle(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, float radius, int planeAxis, bool upperHalf, uint color)
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

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pipeline.Dispose();
            _target.Dispose();
        }
    }

    /// <summary>The scene content node the owner's <see cref="SceneContent"/> draws into.</summary>
    private sealed class SceneNode(PreviewViewport owner, RenderGraph graph, RenderChain chain)
        : RGNode_SceneContent(graph, chain)
    {
        protected override void OnRender(in RenderGraphContext context, GPUFrameBuffer target, GPUAttachmentLayout layout)
        {
            using (RenderPassScope pass = context.RenderContext.BeginPass(target))
            {
                owner.SceneContent?.Invoke(pass);
            }
        }
    }
}
