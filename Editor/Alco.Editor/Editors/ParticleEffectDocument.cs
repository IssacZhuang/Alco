using System.ComponentModel;
using System.Numerics;
using System.Text.Json;
using Alco.ImGUI;
using Alco.LLM;
using Alco.Particles;

namespace Alco.Editor;

/// <summary>
/// Particle effect asset document (<c>.afx</c>): a live preview viewport on the left
/// and the full group parameter editor on the right, for both 2D and 3D effects.
/// Edits happen on a detached copy deserialized from the file (the shared asset cache
/// is never mutated); saving serializes through the particle loader's own JSON options.
/// <br/>Preview synchronization: fields baked into the GPU parameter record apply
/// live through <see cref="ParticleEffectInstance2D.SetGroupParams"/> without
/// restarting emission; structural edits (pool capacity, timeline, bursts, behavior,
/// material/texture, blend/depth, over-life tables, group list) rebuild the preview
/// instance from a JSON-roundtripped copy after a short debounce, so the system's
/// per-asset material and lookup-texture caches rebake against fresh object identities.
/// </summary>
public sealed partial class ParticleEffectDocument : AssetDocument
{
    /// <summary>The debounce delay between a structural edit and the preview rebuild.</summary>
    private const float StructuralRebuildDelay = 0.25f;

    private readonly JsonSerializerOptions _jsonOptions;
    private readonly JsonSerializerOptions _jsonWriteOptions;
    private readonly string _absolutePath = string.Empty;
    private readonly ParticleEffectAsset _effect;
    private readonly bool _is3D;
    private readonly ParticleEffectPreview _preview;

    private string _saveError = string.Empty;
    private string _roundtripError = string.Empty;
    private float? _structuralRebuildTimer;

    /// <summary>Creates the document; throws when the file cannot be resolved or parsed.</summary>
    public ParticleEffectDocument(EditorContext context, string assetPath) : base(context, assetPath)
    {
        _jsonOptions = AssetLoaderParticleEffect.CreateJsonOptions(Context.AssetSystem, Context.RenderingSystem.ShaderSystem);
        // Indented output keeps the hand-editable files diff-friendly.
        _jsonWriteOptions = new JsonSerializerOptions(_jsonOptions) { WriteIndented = true };

        if (!Context.Project.TryGetOwnedAbsolutePath(assetPath, out string? owned)
            && !Context.Project.TryGetReferencedAbsolutePath(assetPath, out owned))
        {
            throw new FileNotFoundException($"Cannot resolve {assetPath} to a mounted file.");
        }
        _absolutePath = owned;

        // Detached edit copy: parse the file directly instead of taking the shared
        // cached instance, so edits never leak into other consumers before a save.
        _effect = JsonSerializer.Deserialize<ParticleEffectAsset>(File.ReadAllText(_absolutePath), _jsonOptions)
            ?? throw new InvalidDataException($"Particle effect asset '{assetPath}' is empty.");
        _is3D = _effect is ParticleEffect3DAsset;

        _preview = new ParticleEffectPreview(context, _is3D);
        _preview.OverlaySource = _effect;
        RebuildPreview();
    }

    /// <summary>Whether the document edits a 3D effect (otherwise 2D).</summary>
    public bool Is3D => _is3D;

    /// <inheritdoc/>
    public override IEnumerable<object> CreateAgentTools()
    {
        yield return new ParticleEffectPreviewTools(this);
    }

    /// <inheritdoc/>
    public override void Save()
    {
        if (IsReadOnly)
        {
            return;
        }

        _effect.Version ??= ParticleEffectAsset.FormatVersion;
        try
        {
            File.WriteAllText(_absolutePath, JsonSerializer.Serialize(_effect, _jsonWriteOptions));
        }
        catch (Exception e)
        {
            _saveError = e.Message;
            return;
        }

        _saveError = string.Empty;
        IsDirty = false;

        // Evict the cached shared instance so the next load sees the saved file.
        Context.AssetSystem.Unload(AssetPath);
    }

    /// <inheritdoc/>
    protected override void DrawContent()
    {
        TickStructuralRebuild(ImGui.GetIO().DeltaTime);

        DrawToolbar();
        ImGui.Separator();

        const float paramsWidth = 420f;
        if (ImGui.BeginChild("##preview", new Vector2(-paramsWidth, -1)))
        {
            _preview.Draw();
        }
        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("##params", new Vector2(0, -1)))
        {
            ImGui.BeginDisabled(IsReadOnly);
            DrawParams();
            ImGui.EndDisabled();
        }
        ImGui.EndChild();
    }

    /// <summary>The save button plus the document-level error text.</summary>
    private void DrawToolbar()
    {
        ImGui.BeginDisabled(IsReadOnly || !IsDirty);
        if (ImGui.Button("Save"))
        {
            Save();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextUnformatted(IsDirty ? "(modified)" : string.Empty);

        string error = _saveError.Length > 0 ? _saveError : _roundtripError;
        if (error.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), error);
        }
    }

    /// <summary>Hot-applies an edited group's GPU-record fields to the live preview.</summary>
    /// <param name="groupIndex">The index of the edited group.</param>
    private void OnLiveEdit(int groupIndex)
    {
        MarkDirty();
        if (_is3D)
        {
            _preview.LiveUpdateGroup(groupIndex, ((ParticleEffect3DAsset)_effect).Groups[groupIndex]);
        }
        else
        {
            _preview.LiveUpdateGroup(groupIndex, ((ParticleEffect2DAsset)_effect).Groups[groupIndex]);
        }
    }

    /// <summary>Marks a structural edit: the preview instance rebuilds after the debounce.</summary>
    private void OnStructuralEdit()
    {
        MarkDirty();
        _structuralRebuildTimer = StructuralRebuildDelay;
    }

    /// <summary>Runs the pending structural rebuild once its debounce elapses.</summary>
    private void TickStructuralRebuild(float deltaTime)
    {
        if (_structuralRebuildTimer is not { } timer)
        {
            return;
        }
        timer -= deltaTime;
        if (timer > 0f)
        {
            _structuralRebuildTimer = timer;
            return;
        }
        _structuralRebuildTimer = null;
        RebuildPreview();
    }

    /// <summary>
    /// Rebuilds the preview instance from a JSON-roundtripped copy of the edit asset:
    /// fresh object identities make the particle system rebake its per-asset materials
    /// and over-life lookup textures. A roundtrip failure keeps the old preview.
    /// </summary>
    private void RebuildPreview()
    {
        try
        {
            string json = JsonSerializer.Serialize(_effect, _jsonOptions);
            ParticleEffectAsset fresh = JsonSerializer.Deserialize<ParticleEffectAsset>(json, _jsonOptions)!;
            _preview.SetEffect(fresh);
            _roundtripError = string.Empty;
        }
        catch (Exception e)
        {
            _roundtripError = $"Preview rebuild failed: {e.Message}";
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _preview.Dispose();
        }
    }

    /// <summary>
    /// Agent tools driving the document's live preview viewport, so agents can
    /// frame an effect deterministically and verify it through screenshots.
    /// </summary>
    [AgentTools]
    public sealed class ParticleEffectPreviewTools
    {
        private readonly ParticleEffectDocument _document;

        /// <summary>Creates the tool set for the given document.</summary>
        public ParticleEffectPreviewTools(ParticleEffectDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            _document = document;
        }

        [AgentFunction]
        [Description("Returns the particle effect preview camera state: the mode (2D/3D), and for 2D the zoom (1 = default, smaller zooms in) and view center, for 3D the orbit yaw/pitch (radians), distance and target.")]
        public string GetParticlePreviewCamera()
        {
            if (_document.Is3D)
            {
                (float yaw, float pitch, float distance, Vector3 target) = _document._preview.Camera3DState;
                return $"mode: 3D | yaw: {yaw:0.###} | pitch: {pitch:0.###} | distance: {distance:0.###} | target: ({target.X:0.###}, {target.Y:0.###}, {target.Z:0.###})";
            }
            (float zoom, Vector2 center) = _document._preview.Camera2DState;
            return $"mode: 2D | zoom: {zoom:0.###} | center: ({center.X:0.###}, {center.Y:0.###})";
        }

        [AgentFunction]
        [Description("Sets the particle effect preview camera. 2D effects accept zoom (1 = default, smaller zooms in) and centerX/centerY; 3D effects accept yaw/pitch (radians), distance and targetX/Y/Z. Omitted parameters keep their current values.")]
        public string SetParticlePreviewCamera(
            [Description("2D zoom multiplier: 1 = default view, 0.5 = twice as close.")] float? zoom = null,
            [Description("2D view center X.")] float? centerX = null,
            [Description("2D view center Y.")] float? centerY = null,
            [Description("3D orbit yaw in radians.")] float? yaw = null,
            [Description("3D orbit pitch in radians.")] float? pitch = null,
            [Description("3D orbit distance.")] float? distance = null,
            [Description("3D orbit target X.")] float? targetX = null,
            [Description("3D orbit target Y.")] float? targetY = null,
            [Description("3D orbit target Z.")] float? targetZ = null)
        {
            if (_document.Is3D)
            {
                (float currentYaw, float currentPitch, float currentDistance, Vector3 currentTarget) = _document._preview.Camera3DState;
                _document._preview.SetCamera3DState(
                    yaw ?? currentYaw,
                    pitch ?? currentPitch,
                    distance ?? currentDistance,
                    new Vector3(targetX ?? currentTarget.X, targetY ?? currentTarget.Y, targetZ ?? currentTarget.Z));
            }
            else
            {
                (float currentZoom, Vector2 currentCenter) = _document._preview.Camera2DState;
                _document._preview.SetCamera2DState(
                    zoom ?? currentZoom,
                    new Vector2(centerX ?? currentCenter.X, centerY ?? currentCenter.Y));
            }
            return GetParticlePreviewCamera();
        }
    }
}
