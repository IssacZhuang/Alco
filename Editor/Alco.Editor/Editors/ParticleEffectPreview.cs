using System.Numerics;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.Particles;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// The live preview of the particle effect document: runs one instance of the
/// edited effect on a private <see cref="GpuParticleSystem2D"/>/<see cref="GpuParticleSystem3D"/>
/// inside a <see cref="PreviewViewport"/> (which owns the pipeline, camera,
/// viewport input and the grid/axes overlay).
/// <br/>This class adds the particle-specific parts on top of the viewport:
/// the effect instance lifecycle (<see cref="SetEffect"/>,
/// <see cref="LiveUpdateGroup(int, ParticleGroup2DAsset)"/>), the transport
/// controls (pause, stop, restart, time scale — pausing records a 0 delta,
/// which freezes the simulation but keeps pool bookkeeping alive), the
/// per-group emitter shape outlines read from <see cref="OverlaySource"/>,
/// and the editor-only per-group visibility toggles
/// (<see cref="SetGroupVisible"/>, never serialized into the asset).
/// </summary>
public sealed class ParticleEffectPreview : AutoDisposable
{
    /// <summary>The deterministic instance seed, so restarts replay identically.</summary>
    private const int PreviewSeed = 1;

    private readonly bool _is3D;
    private readonly PreviewViewport _viewport;
    private readonly PreviewViewport2D? _viewport2D;
    private readonly PreviewViewport3D? _viewport3D;
    private readonly GpuParticleSystem2D? _system2D;
    private readonly GpuParticleSystem3D? _system3D;

    private ParticleEffectInstance2D? _instance2D;
    private ParticleEffectInstance3D? _instance3D;

    private bool _paused;
    private float _timeScale = 1f;
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

    /// <summary>Creates the preview for the given effect dimension.</summary>
    /// <param name="context">The editor context (engine services).</param>
    /// <param name="is3D">True to preview 3D effects, false for 2D.</param>
    public ParticleEffectPreview(EditorContext context, bool is3D)
    {
        ArgumentNullException.ThrowIfNull(context);
        _is3D = is3D;
        RenderingSystem rendering = context.RenderingSystem;

        if (is3D)
        {
            _viewport3D = new PreviewViewport3D(context, "particle_preview");
            _system3D = new GpuParticleSystem3D(rendering)
            {
                Camera = _viewport3D.Camera,
                // The preview pipeline clears depth to 1 with a plain (non-reversed) projection.
                DepthStencilState = DepthStencilState.Read,
            };
        }
        else
        {
            _viewport2D = new PreviewViewport2D(context, "particle_preview");
            _system2D = new GpuParticleSystem2D(rendering) { Camera = _viewport2D.Camera };
        }
        _viewport = (PreviewViewport?)_viewport2D ?? _viewport3D!;
        _viewport.RecordFrame = OnSimulate;
        _viewport.SceneContent = RenderSceneContent;
        _viewport.ToolbarLeading = DrawTransportControls;
        _viewport.ToolbarTrailing = DrawShapesToggle;
        _viewport.OverlayExtras = DrawEmitterShapes;
        _viewport.StatusPrefix = GetStatusPrefix;
        _viewport.StatusSuffix = GetStatusSuffix;
    }

    /// <summary>Whether the simulation is frozen (the instance timeline still exists).</summary>
    public bool IsPaused => _paused;

    /// <summary>Restarts the live effect instance (the toolbar button and the Space shortcut).</summary>
    public void Restart()
    {
        _instance2D?.Restart();
        _instance3D?.Restart();
    }

    /// <summary>The failure of the last effect rebuild, or empty when the preview is live.</summary>
    public string Error => _viewport.Error;

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
        _viewport.Error = string.Empty;
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
            _viewport.Error = e.Message;
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

    /// <summary>Whether a group is visible in the preview (true when there is no live instance).</summary>
    /// <param name="groupIndex">The group index (0 .. group count - 1).</param>
    private bool IsGroupVisible(int groupIndex) => _instance2D?.IsGroupVisible(groupIndex)
        ?? _instance3D?.IsGroupVisible(groupIndex)
        ?? true;

    /// <summary>
    /// Shows or hides a group in the preview. Preview-only state on the live
    /// instance — the asset is never affected; the owning document re-applies the
    /// toggles after preview rebuilds.
    /// </summary>
    /// <param name="groupIndex">The group index (0 .. group count - 1).</param>
    /// <param name="visible">True to draw and simulate the group.</param>
    public void SetGroupVisible(int groupIndex, bool visible)
    {
        _instance2D?.SetGroupVisible(groupIndex, visible);
        _instance3D?.SetGroupVisible(groupIndex, visible);
    }

    /// <summary>Draws the transport toolbar, the viewport and the status line.</summary>
    public void Draw()
    {
        _viewport.Draw();
    }

    /// <summary>The 2D camera state: the zoom multiplier over the base view width and the view center.</summary>
    public (float Zoom, Vector2 Position) Camera2DState => _viewport2D?.CameraState ?? (1f, Vector2.Zero);

    /// <summary>The 3D orbit camera state: yaw, pitch, distance and orbit target.</summary>
    public (float Yaw, float Pitch, float Distance, Vector3 Target) Camera3DState =>
        _viewport3D?.CameraState ?? (MathF.PI, 0.26f, 8f, Vector3.Zero);

    /// <summary>Sets the 2D camera zoom and center, applying the viewport's zoom clamps.</summary>
    /// <param name="zoom">The zoom multiplier over the base view width (smaller zooms in).</param>
    /// <param name="position">The world-space view center.</param>
    public void SetCamera2DState(float zoom, Vector2 position)
    {
        if (_viewport2D == null)
        {
            throw new InvalidOperationException("The preview renders a 3D effect.");
        }
        _viewport2D.SetCameraState(zoom, position);
    }

    /// <summary>Sets the 3D orbit camera, applying the viewport's pitch/distance clamps.</summary>
    /// <param name="yaw">The orbit yaw in radians.</param>
    /// <param name="pitch">The orbit pitch in radians.</param>
    /// <param name="distance">The orbit distance.</param>
    /// <param name="target">The orbit target.</param>
    public void SetCamera3DState(float yaw, float pitch, float distance, Vector3 target)
    {
        if (_viewport3D == null)
        {
            throw new InvalidOperationException("The preview renders a 2D effect.");
        }
        _viewport3D.SetCameraState(yaw, pitch, distance, target);
    }

    /// <summary>The transport toolbar section ahead of the viewport's camera controls.</summary>
    private void DrawTransportControls()
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
            Restart();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Restart the effect (Space)");
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110f);
        ImGui.SliderFloat("##timescale", ref _timeScale, 0.05f, 4f, "speed %.2fx");
    }

    /// <summary>The emitter shape overlay toggle after the viewport's camera controls.</summary>
    private void DrawShapesToggle()
    {
        ImGui.Checkbox("Shapes", ref _showShapes);
    }

    /// <summary>The leading diagnostics: simulation time and group count.</summary>
    private string GetStatusPrefix()
    {
        int groups = _instance2D?.GroupCount ?? _instance3D?.GroupCount ?? 0;
        float time = _instance2D?.Time ?? _instance3D?.Time ?? 0f;
        return $"t={time:0.00}s | {groups} group(s)";
    }

    /// <summary>The trailing diagnostics: the paused marker.</summary>
    private string GetStatusSuffix()
    {
        return _paused ? " | paused" : string.Empty;
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

    /// <summary>Draws the particle systems into the viewport's scene pass.</summary>
    private void RenderSceneContent(RenderPassScope pass)
    {
        if (_is3D)
        {
            _system3D!.Render(pass);
        }
        else
        {
            _system2D!.Render(pass);
        }
    }

    /// <summary>
    /// Draws every group's emitter shape outline, colored by group index. 2D
    /// shapes live on the Z = 0 plane.
    /// </summary>
    private void DrawEmitterShapes(ImDrawListPtr drawList, Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize)
    {
        if (!_showShapes || OverlaySource == null)
        {
            return;
        }

        if (_is3D)
        {
            List<ParticleGroup3DAsset> groups = ((ParticleEffect3DAsset)OverlaySource).Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                if (!IsGroupVisible(i))
                {
                    continue;
                }
                DrawShape3D(drawList, viewProjection, imageMin, imageSize, groups[i].Shape, ShapeColor(i));
            }
            return;
        }

        List<ParticleGroup2DAsset> groups2D = ((ParticleEffect2DAsset)OverlaySource).Groups;
        for (int i = 0; i < groups2D.Count; i++)
        {
            if (!IsGroupVisible(i))
            {
                continue;
            }
            DrawShape2D(drawList, viewProjection, imageMin, imageSize, groups2D[i].Shape, ShapeColor(i));
        }
    }

    /// <summary>Draws one 2D emitter shape (point cross, circle, or box) at the origin.</summary>
    private void DrawShape2D(ImDrawListPtr drawList, Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, ParticleShape2D shape, uint color)
    {
        switch (shape.Type)
        {
            case ParticleShape2DType.Point:
                PreviewViewport.DrawScreenCross(drawList, PreviewViewport.WorldToScreen(Vector3.Zero, viewProjection, imageMin, imageSize), color);
                break;
            case ParticleShape2DType.Circle:
                Vector2 center = PreviewViewport.WorldToScreen(Vector3.Zero, viewProjection, imageMin, imageSize);
                float pixelsPerUnit = imageSize.X / _viewport2D!.Camera.ViewSize.X;
                float radius = Math.Max(shape.Radius * pixelsPerUnit, 0f);
                drawList.AddCircle(center, radius, color, PreviewViewport.CircleSegments(radius), 1.5f);
                if (shape.InnerRadius > 0.001f)
                {
                    float inner = radius * shape.InnerRadius;
                    drawList.AddCircle(center, inner, color, PreviewViewport.CircleSegments(inner), 1.5f);
                }
                break;
            case ParticleShape2DType.Box:
                Vector2 a = PreviewViewport.WorldToScreen(new Vector3(-shape.Extents.X, -shape.Extents.Y, 0f), viewProjection, imageMin, imageSize);
                Vector2 b = PreviewViewport.WorldToScreen(new Vector3(shape.Extents.X, shape.Extents.Y, 0f), viewProjection, imageMin, imageSize);
                drawList.AddRect(Vector2.Min(a, b), Vector2.Max(a, b), color, 0f, ImDrawFlags.None, 1.5f);
                break;
        }
    }

    /// <summary>Draws one 3D emitter shape (point cross, sphere, hemisphere, or box) at the origin.</summary>
    private void DrawShape3D(ImDrawListPtr drawList, Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, ParticleShape3D shape, uint color)
    {
        switch (shape.Type)
        {
            case ParticleShape3DType.Point:
                PreviewViewport.DrawScreenCross(drawList, PreviewViewport.WorldToScreen(Vector3.Zero, viewProjection, imageMin, imageSize), color);
                break;
            case ParticleShape3DType.Sphere:
                PreviewViewport.DrawProjectedCircle(drawList, viewProjection, imageMin, imageSize, shape.Radius, 0, false, color);
                PreviewViewport.DrawProjectedCircle(drawList, viewProjection, imageMin, imageSize, shape.Radius, 1, false, color);
                PreviewViewport.DrawProjectedCircle(drawList, viewProjection, imageMin, imageSize, shape.Radius, 2, false, color);
                break;
            case ParticleShape3DType.Hemisphere:
                // Base circle on the ground plane plus two meridian arcs above it.
                PreviewViewport.DrawProjectedCircle(drawList, viewProjection, imageMin, imageSize, shape.Radius, 2, false, color);
                PreviewViewport.DrawProjectedCircle(drawList, viewProjection, imageMin, imageSize, shape.Radius, 0, true, color);
                PreviewViewport.DrawProjectedCircle(drawList, viewProjection, imageMin, imageSize, shape.Radius, 1, true, color);
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
                    PreviewViewport.DrawWorldLine(drawList, viewProjection, imageMin, imageSize, corners[edges[i]], corners[edges[i + 1]], color, 1.5f);
                }
                break;
        }
    }

    /// <summary>The overlay outline color of the group at the given index.</summary>
    private static uint ShapeColor(int groupIndex)
    {
        return ImGui.GetColorU32(ShapePalette[groupIndex % ShapePalette.Length]);
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
            _viewport.Dispose();
        }
    }
}
