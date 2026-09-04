using System.Numerics;
using System.Text.Json;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.IO;
using Alco.Particles;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// The parameter pane of <see cref="ParticleEffectDocument"/>: the effect header and
/// one collapsing section per emitter group exposing every asset field. Fields baked
/// into the GPU parameter record hot-apply to the preview (<see cref="OnLiveEdit"/>);
/// structural fields (capacity, timeline, bursts, behavior/material/texture, blend,
/// over-life tables, the group list itself) schedule a preview rebuild
/// (<see cref="OnStructuralEdit"/>).
/// </summary>
public sealed partial class ParticleEffectDocument
{
    /// <summary>Per-group widget state (pickers are one instance per field).</summary>
    private sealed class GroupUiState
    {
        public readonly AssetPicker TexturePicker = new();
        public readonly AssetPicker MaterialPicker = new();
        public string TexturePath = string.Empty;
        public string MaterialPath = string.Empty;
        public string ReferenceError = string.Empty;
    }

    private static readonly (string Name, BlendState State)[] s_blendPresets =
    [
        ("Opaque", BlendState.Opaque),
        ("AlphaBlend", BlendState.AlphaBlend),
        ("Additive", BlendState.Additive),
        ("PremultipliedAlpha", BlendState.PremultipliedAlpha),
        ("NonPremultipliedAlpha", BlendState.NonPremultipliedAlpha),
        ("Multiply", BlendState.Multiply),
        ("AlphaBlendNoAccumulation", BlendState.AlphaBlendNoAccumulation),
    ];

    private static readonly (string Name, DepthStencilState State)[] s_depthPresets =
    [
        ("None", DepthStencilState.None),
        ("Write", DepthStencilState.Write),
        ("Read", DepthStencilState.Read),
        ("WriteReverseZ", DepthStencilState.WriteReverseZ),
        ("ReadReverseZ", DepthStencilState.ReadReverseZ),
        ("Default", DepthStencilState.Default),
    ];

    private readonly Dictionary<ParticleGroupAsset, GroupUiState> _groupStates = new();

    /// <summary>
    /// The groups hidden in the preview, keyed by group object so the state
    /// survives reordering (stale entries from deleted groups are harmless).
    /// Editor-only state: never serialized into the asset file.
    /// </summary>
    private readonly HashSet<ParticleGroupAsset> _hiddenGroups = new();
    private string[]? _behaviorCandidates;

    /// <summary>The whole parameters pane: effect header, group editors, add button.</summary>
    private void DrawParams()
    {
        ImGui.SeparatorText("Effect");

        string name = _effect.Name;
        ImGui.SetNextItemWidth(-120f);
        if (ImGui.InputText("Name", ref name, 128))
        {
            _effect.Name = name;
            MarkDirty();
        }
        ImGui.TextDisabled($"Type: {(_is3D ? "3D" : "2D")} — {EffectGroupCount} group(s); rendered in list order");

        int count = EffectGroupCount;
        int removeGroup = -1;
        for (int i = 0; i < count; i++)
        {
            DrawGroup(i, GetGroup(i), ref removeGroup);
        }
        // Deferred until the loop ends: removing mid-iteration would shift the
        // remaining indices and throw inside this draw pass.
        if (removeGroup >= 0)
        {
            RemoveGroup(removeGroup);
        }

        if (ImGui.Button("Add Group"))
        {
            AddGroup();
            OnStructuralEdit();
        }
    }

    /// <summary>The group count of the edited effect (2D/3D agnostic).</summary>
    private int EffectGroupCount => _is3D
        ? ((ParticleEffect3DAsset)_effect).Groups.Count
        : ((ParticleEffect2DAsset)_effect).Groups.Count;

    /// <summary>Returns the edited group at <paramref name="index"/> as the shared base type.</summary>
    private ParticleGroupAsset GetGroup(int index) => _is3D
        ? ((ParticleEffect3DAsset)_effect).Groups[index]
        : ((ParticleEffect2DAsset)_effect).Groups[index];

    /// <summary>Adds a default group to the effect.</summary>
    private void AddGroup()
    {
        string name = $"Group {EffectGroupCount + 1}";
        if (_is3D)
        {
            ((ParticleEffect3DAsset)_effect).Groups.Add(new ParticleGroup3DAsset { Name = name });
        }
        else
        {
            ((ParticleEffect2DAsset)_effect).Groups.Add(new ParticleGroup2DAsset { Name = name });
        }
    }

    /// <summary>Duplicates a group (JSON roundtrip of the group object) after itself.</summary>
    private void DuplicateGroup(int index)
    {
        if (_is3D)
        {
            var groups = ((ParticleEffect3DAsset)_effect).Groups;
            string json = JsonSerializer.Serialize(groups[index], _jsonOptions);
            groups.Insert(index + 1, JsonSerializer.Deserialize<ParticleGroup3DAsset>(json, _jsonOptions)!);
        }
        else
        {
            var groups = ((ParticleEffect2DAsset)_effect).Groups;
            string json = JsonSerializer.Serialize(groups[index], _jsonOptions);
            groups.Insert(index + 1, JsonSerializer.Deserialize<ParticleGroup2DAsset>(json, _jsonOptions)!);
        }
    }

    /// <summary>One group editor: header buttons, then the parameter sections. A Delete
    /// click only records the index; the caller removes the group after the draw pass.</summary>
    private void DrawGroup(int index, ParticleGroupAsset group, ref int removeGroup)
    {
        GroupUiState state = GetGroupUiState(group);
        ImGui.PushID(index);

        // Editor-only visibility toggle on the header row: hides the group's
        // particles and shape outline in the preview without touching the asset.
        bool visible = !_hiddenGroups.Contains(group);
        if (ImGui.Checkbox("##visible", ref visible))
        {
            if (visible)
            {
                _hiddenGroups.Remove(group);
            }
            else
            {
                _hiddenGroups.Add(group);
            }
            _preview.SetGroupVisible(index, visible);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Show this group in the preview only (never saved to the asset).");
        }
        ImGui.SameLine();

        if (ImGui.CollapsingHeader($"{group.Name}##header", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Group list actions.
            if (ImGui.SmallButton("Up"))
            {
                MoveGroup(index, -1);
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Down"))
            {
                MoveGroup(index, +1);
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Duplicate"))
            {
                DuplicateGroup(index);
                OnStructuralEdit();
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(EffectGroupCount <= 1);
            if (ImGui.SmallButton("Delete"))
            {
                removeGroup = index;
            }
            ImGui.EndDisabled();

            DrawGroupName(group);
            DrawEmissionSection(index, group);
            DrawShapeSection(index, group);
            DrawMotionSection(index, group);
            DrawLifeAndSizeSection(index, group);
            DrawColorSection(index, group);
            DrawStretchSection(index, group);
            DrawFlipbookSection(index, group);
            DrawRenderingSection(index, group, state);
        }

        ImGui.PopID();
    }

    /// <summary>Moves a group within the list (no-op at the boundaries).</summary>
    private void MoveGroup(int index, int delta)
    {
        int target = index + delta;
        if (target < 0 || target >= EffectGroupCount)
        {
            return;
        }
        if (_is3D)
        {
            var groups = ((ParticleEffect3DAsset)_effect).Groups;
            (groups[index], groups[target]) = (groups[target], groups[index]);
        }
        else
        {
            var groups = ((ParticleEffect2DAsset)_effect).Groups;
            (groups[index], groups[target]) = (groups[target], groups[index]);
        }
        OnStructuralEdit();
    }

    /// <summary>Removes a group; the last remaining group cannot be deleted.</summary>
    private void RemoveGroup(int index)
    {
        if (EffectGroupCount <= 1)
        {
            return;
        }
        if (_is3D)
        {
            ((ParticleEffect3DAsset)_effect).Groups.RemoveAt(index);
        }
        else
        {
            ((ParticleEffect2DAsset)_effect).Groups.RemoveAt(index);
        }
        OnStructuralEdit();
    }

    /// <summary>The per-group widget state, created on first use.</summary>
    private GroupUiState GetGroupUiState(ParticleGroupAsset group)
    {
        if (!_groupStates.TryGetValue(group, out GroupUiState? state))
        {
            state = new GroupUiState
            {
                TexturePath = group.Texture?.Name ?? string.Empty,
                MaterialPath = group.Material?.Name ?? string.Empty,
            };
            _groupStates.Add(group, state);
        }
        return state;
    }

    /// <summary>The group name row (diagnostics only; no preview impact).</summary>
    private void DrawGroupName(ParticleGroupAsset group)
    {
        string name = group.Name;
        ImGui.SetNextItemWidth(-120f);
        if (ImGui.InputText("Group Name", ref name, 128))
        {
            group.Name = name;
            MarkDirty();
        }
    }

    /// <summary>Emission: rate (live), capacity, timeline and bursts (structural).</summary>
    private void DrawEmissionSection(int index, ParticleGroupAsset group)
    {
        ImGui.SeparatorText("Emission");

        float rate = group.EmissionRate;
        if (DragRow("Rate (per s)", ref rate, 0.5f, 0f, 100000f))
        {
            group.EmissionRate = rate;
            OnLiveEdit(index);
        }

        int maxParticles = group.MaxParticles;
        if (DragRow("Max Particles", ref maxParticles, 8, 1, 1 << 20))
        {
            group.MaxParticles = maxParticles;
            OnStructuralEdit();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Pool slice capacity; excess spawns overwrite the oldest particles.");
        }

        float duration = group.Duration;
        if (DragRow("Duration (s)", ref duration, 0.05f, 0f, 120f))
        {
            group.Duration = duration;
            OnStructuralEdit();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("0 = the emitter never stops emitting on its own.");
        }

        bool looping = group.Looping;
        if (ImGui.Checkbox("Looping", ref looping))
        {
            group.Looping = looping;
            OnStructuralEdit();
        }

        // Bursts (structural: they live on the CPU emission timeline).
        int removeBurst = -1;
        for (int b = 0; b < group.Bursts.Count; b++)
        {
            ParticleBurst burst = group.Bursts[b];
            ImGui.PushID(b);
            Caption("Burst");
            Caption("at");
            float time = burst.Time;
            ImGui.SetNextItemWidth(70f);
            if (ImGui.DragFloat("##time", ref time, 0.02f, 0f, 300f))
            {
                burst.Time = time;
                OnStructuralEdit();
            }
            ImGui.SameLine();
            Caption("min");
            int countMin = burst.CountMin;
            ImGui.SetNextItemWidth(60f);
            if (ImGui.DragInt("##countmin", ref countMin, 0.2f, 0, 100000))
            {
                burst.CountMin = countMin;
                OnStructuralEdit();
            }
            ImGui.SameLine();
            Caption("max");
            int countMax = burst.CountMax;
            ImGui.SetNextItemWidth(60f);
            if (ImGui.DragInt("##countmax", ref countMax, 0.2f, 0, 100000))
            {
                burst.CountMax = countMax;
                OnStructuralEdit();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("X"))
            {
                removeBurst = b;
            }
            ImGui.PopID();
        }
        if (removeBurst >= 0)
        {
            group.Bursts.RemoveAt(removeBurst);
            OnStructuralEdit();
        }
        if (ImGui.SmallButton("Add Burst"))
        {
            group.Bursts.Add(new ParticleBurst());
            OnStructuralEdit();
        }
    }

    /// <summary>Shape: the 2D/3D emission shape with per-type fields and the fixed
    /// position offset of the spawn point (all live).</summary>
    private void DrawShapeSection(int index, ParticleGroupAsset group)
    {
        ImGui.SeparatorText("Shape");

        if (group is ParticleGroup2DAsset group2D)
        {
            ParticleShape2D shape = group2D.Shape;
            ParticleShape2DType type = shape.Type;
            if (ImGui.Combo("Type", ref type))
            {
                shape.Type = type;
                OnLiveEdit(index);
            }
            if (shape.Type == ParticleShape2DType.Circle)
            {
                float radius = shape.Radius;
                if (DragRow("Radius", ref radius, 0.1f, 0f))
                {
                    shape.Radius = radius;
                    OnLiveEdit(index);
                }
                float inner = shape.InnerRadius;
                if (SliderRow("Inner Radius", ref inner, 0f, 1f))
                {
                    shape.InnerRadius = inner;
                    OnLiveEdit(index);
                }
            }
            else if (shape.Type == ParticleShape2DType.Box)
            {
                Vector2 extents = shape.Extents;
                if (DragRow("Extents", ref extents, 0.1f, 0f))
                {
                    shape.Extents = extents;
                    OnLiveEdit(index);
                }
            }
            Vector2 positionOffset = group2D.PositionOffset;
            if (DragRow("Position Offset", ref positionOffset, 0.02f))
            {
                group2D.PositionOffset = positionOffset;
                OnLiveEdit(index);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Fixed emitter-local offset of the spawn point; rotates with the emitter.");
            }
        }
        else
        {
            ParticleShape3D shape = ((ParticleGroup3DAsset)group).Shape;
            ParticleShape3DType type = shape.Type;
            if (ImGui.Combo("Type", ref type))
            {
                shape.Type = type;
                OnLiveEdit(index);
            }
            if (shape.Type is ParticleShape3DType.Sphere or ParticleShape3DType.Hemisphere)
            {
                float radius = shape.Radius;
                if (DragRow("Radius", ref radius, 0.05f, 0f))
                {
                    shape.Radius = radius;
                    OnLiveEdit(index);
                }
                float inner = shape.InnerRadius;
                if (SliderRow("Inner Radius", ref inner, 0f, 1f))
                {
                    shape.InnerRadius = inner;
                    OnLiveEdit(index);
                }
            }
            else if (shape.Type == ParticleShape3DType.Box)
            {
                Vector3 extents = shape.Extents;
                if (DragRow("Extents", ref extents, 0.05f, 0f))
                {
                    shape.Extents = extents;
                    OnLiveEdit(index);
                }
            }
            Vector3 positionOffset = ((ParticleGroup3DAsset)group).PositionOffset;
            if (DragRow("Position Offset", ref positionOffset, 0.02f))
            {
                ((ParticleGroup3DAsset)group).PositionOffset = positionOffset;
                OnLiveEdit(index);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Fixed emitter-local offset of the spawn point; rotates with the emitter.");
            }
        }
    }

    /// <summary>Motion: planar direction/speed, independent 2.5D height, gravity and drag (all live).</summary>
    private void DrawMotionSection(int index, ParticleGroupAsset group)
    {
        ImGui.SeparatorText("Motion");

        if (_is3D)
        {
            var group3D = (ParticleGroup3DAsset)group;
            ParticleDirectionMode mode = group3D.DirectionMode;
            if (ImGui.Combo("Direction Mode", ref mode))
            {
                group3D.DirectionMode = mode;
                OnLiveEdit(index);
            }
            Vector3 direction = group3D.Direction;
            if (DragRow("Direction", ref direction, 0.02f))
            {
                group3D.Direction = direction;
                OnLiveEdit(index);
            }
            float spread = group3D.SpreadAngle * math.RadToDeg;
            if (DragRow("Spread (deg)", ref spread, 0.5f, 0f, 360f))
            {
                group3D.SpreadAngle = spread * math.DegToRad;
                OnLiveEdit(index);
            }
        }
        else
        {
            var group2D = (ParticleGroup2DAsset)group;
            ParticleDirectionMode mode = group2D.DirectionMode;
            if (ImGui.Combo("Direction Mode", ref mode))
            {
                group2D.DirectionMode = mode;
                OnLiveEdit(index);
            }
            Vector2 direction = group2D.Direction;
            if (DragRow("Direction", ref direction, 0.02f))
            {
                group2D.Direction = direction;
                OnLiveEdit(index);
            }
            float spread = group2D.SpreadAngle * math.RadToDeg;
            if (DragRow("Spread (deg)", ref spread, 0.5f, 0f, 360f))
            {
                group2D.SpreadAngle = spread * math.DegToRad;
                OnLiveEdit(index);
            }
        }

        if (DrawRangeRow("Speed", group.Speed, out ParticleRange speed, 0.1f))
        {
            group.Speed = speed;
            OnLiveEdit(index);
        }

        if (_is3D)
        {
            Vector3 gravity = ((ParticleGroup3DAsset)group).Gravity;
            if (DragRow("Gravity", ref gravity, 0.05f))
            {
                ((ParticleGroup3DAsset)group).Gravity = gravity;
                OnLiveEdit(index);
            }
        }
        else
        {
            var group2D = (ParticleGroup2DAsset)group;
            Vector2 gravity = group2D.Gravity;
            if (DragRow("Gravity", ref gravity, 0.05f))
            {
                group2D.Gravity = gravity;
                OnLiveEdit(index);
            }
            if (DrawRangeRow("Start Height", group2D.StartHeight, out ParticleRange startHeight, 0.05f))
            {
                group2D.StartHeight = startHeight;
                OnLiveEdit(index);
            }
            if (DrawRangeRow("Height Velocity", group2D.HeightVelocity, out ParticleRange heightVelocity, 0.05f))
            {
                group2D.HeightVelocity = heightVelocity;
                OnLiveEdit(index);
            }
            float heightAcceleration = group2D.HeightAcceleration;
            if (DragRow("Height Acceleration", ref heightAcceleration, 0.05f))
            {
                group2D.HeightAcceleration = heightAcceleration;
                OnLiveEdit(index);
            }
        }

        float drag = group.Drag;
        if (DragRow("Drag", ref drag, 0.02f, 0f))
        {
            group.Drag = drag;
            OnLiveEdit(index);
        }
    }

    /// <summary>Lifetime, fades, size and the over-life size curve.</summary>
    private void DrawLifeAndSizeSection(int index, ParticleGroupAsset group)
    {
        ImGui.SeparatorText("Lifetime & Size");

        if (DrawRangeRow("Lifetime (s)", group.Lifetime, out ParticleRange lifetime, 0.05f))
        {
            group.Lifetime = lifetime;
            OnLiveEdit(index);
        }

        float fadeIn = group.FadeIn;
        if (SliderRow("Fade In", ref fadeIn, 0f, 1f))
        {
            group.FadeIn = fadeIn;
            OnLiveEdit(index);
        }
        float fadeOut = group.FadeOut;
        if (SliderRow("Fade Out", ref fadeOut, 0f, 1f))
        {
            group.FadeOut = fadeOut;
            OnLiveEdit(index);
        }

        if (_is3D)
        {
            var group3D = (ParticleGroup3DAsset)group;
            if (DrawRangeRow("Size", group3D.Size, out ParticleRange size, 0.02f))
            {
                group3D.Size = size;
                OnLiveEdit(index);
            }
        }
        else
        {
            var group2D = (ParticleGroup2DAsset)group;
            if (DrawRangeRow("Size", group2D.Size, out ParticleVector2Range size, 0.02f))
            {
                group2D.Size = size;
                OnLiveEdit(index);
            }
        }

        float endScale = group.EndScale;
        if (DragRow("End Scale", ref endScale, 0.01f, 0f))
        {
            group.EndScale = endScale;
            OnLiveEdit(index);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Ignored while a size curve is set.");
        }

        if (_is3D)
        {
            var group3D = (ParticleGroup3DAsset)group;
            DrawAngleRangeRow(index, "Start Rotation (deg)", group3D.StartRotation, v => group3D.StartRotation = v);
            DrawAngleRangeRow(index, "Angular Velocity (deg/s)", group3D.AngularVelocity, v => group3D.AngularVelocity = v);
        }
        else
        {
            var group2D = (ParticleGroup2DAsset)group;
            DrawAngleRangeRow(index, "Start Rotation (deg)", group2D.StartRotation, v => group2D.StartRotation = v);
            DrawAngleRangeRow(index, "Angular Velocity (deg/s)", group2D.AngularVelocity, v => group2D.AngularVelocity = v);
        }

        // Size curve (structural: the lookup texture rebakes).
        bool hasCurve = group.SizeCurve is { Count: > 0 };
        if (ImGui.Checkbox("Size Curve", ref hasCurve))
        {
            group.SizeCurve = hasCurve
                ? [new ParticleScalarKey { Time = 0f, Value = 1f }, new ParticleScalarKey { Time = 1f, Value = MathF.Max(group.EndScale, 0f) }]
                : null;
            OnStructuralEdit();
        }
        if (hasCurve && group.SizeCurve != null)
        {
            if (CurveEditor.Draw("##sizecurve", group.SizeCurve))
            {
                OnStructuralEdit();
            }
        }
    }

    /// <summary>Color: spawn range, end color, tint and the over-life gradient.</summary>
    private void DrawColorSection(int index, ParticleGroupAsset group)
    {
        ImGui.SeparatorText("Color");

        ColorFloat startMin = group.StartColor.Min;
        if (ImGui.ColorEdit4("Start Color Min", ref startMin))
        {
            group.StartColor = new ParticleColorRange { Min = startMin, Max = group.StartColor.Max };
            OnLiveEdit(index);
        }
        ColorFloat startMax = group.StartColor.Max;
        if (ImGui.ColorEdit4("Start Color Max", ref startMax))
        {
            group.StartColor = new ParticleColorRange { Min = group.StartColor.Min, Max = startMax };
            OnLiveEdit(index);
        }
        ColorFloat endColor = group.EndColor;
        if (ImGui.ColorEdit4("End Color", ref endColor))
        {
            group.EndColor = endColor;
            OnLiveEdit(index);
        }
        ColorFloat tint = group.Tint;
        if (ImGui.ColorEdit4("Tint", ref tint))
        {
            group.Tint = tint;
            OnLiveEdit(index);
        }

        // Color gradient (structural: the lookup texture rebakes).
        bool hasGradient = group.ColorGradient is { Count: > 0 };
        if (ImGui.Checkbox("Color Gradient", ref hasGradient))
        {
            group.ColorGradient = hasGradient
                ? [new ParticleColorKey { Time = 0f, Color = group.StartColor.Min }, new ParticleColorKey { Time = 1f, Color = group.EndColor }]
                : null;
            OnStructuralEdit();
        }
        if (hasGradient && group.ColorGradient != null)
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Replaces the start → end color lerp; fades still apply.");
            }
            if (GradientEditor.Draw("##colorgradient", group.ColorGradient))
            {
                OnStructuralEdit();
            }
        }
    }

    /// <summary>Velocity stretch (2D also requires rotation alignment; all live).</summary>
    private void DrawStretchSection(int index, ParticleGroupAsset group)
    {
        if (group is ParticleGroup2DAsset group2D)
        {
            ImGui.SeparatorText("Rotation & Stretch");

            bool align = group2D.AlignRotationToVelocity;
            if (ImGui.Checkbox("Align Rotation To Velocity", ref align))
            {
                group2D.AlignRotationToVelocity = align;
                OnLiveEdit(index);
            }

            bool stretch = group2D.VelocityStretch;
            ImGui.BeginDisabled(!group2D.AlignRotationToVelocity);
            if (ImGui.Checkbox("Velocity Stretch", ref stretch))
            {
                group2D.VelocityStretch = stretch;
                OnLiveEdit(index);
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("2D stretch requires Align Rotation To Velocity.");
            }

            float lengthScale = group2D.StretchLengthScale;
            if (DragRow("Stretch Length Scale", ref lengthScale, 0.02f, 0f))
            {
                group2D.StretchLengthScale = lengthScale;
                OnLiveEdit(index);
            }
            float speedScale = group2D.StretchSpeedScale;
            if (DragRow("Stretch Speed Scale", ref speedScale, 0.005f, 0f))
            {
                group2D.StretchSpeedScale = speedScale;
                OnLiveEdit(index);
            }
        }
        else
        {
            ImGui.SeparatorText("Stretch");

            var group3D = (ParticleGroup3DAsset)group;
            bool stretch = group3D.VelocityStretch;
            if (ImGui.Checkbox("Velocity Stretch", ref stretch))
            {
                group3D.VelocityStretch = stretch;
                OnLiveEdit(index);
            }
            float lengthScale = group3D.StretchLengthScale;
            if (DragRow("Stretch Length Scale", ref lengthScale, 0.02f, 0f))
            {
                group3D.StretchLengthScale = lengthScale;
                OnLiveEdit(index);
            }
            float speedScale = group3D.StretchSpeedScale;
            if (DragRow("Stretch Speed Scale", ref speedScale, 0.005f, 0f))
            {
                group3D.StretchSpeedScale = speedScale;
                OnLiveEdit(index);
            }
        }
    }

    /// <summary>Flipbook animation of the particle texture (all live).</summary>
    private void DrawFlipbookSection(int index, ParticleGroupAsset group)
    {
        bool enabled = group.Flipbook != null;
        if (ImGui.Checkbox("Flipbook", ref enabled))
        {
            group.Flipbook = enabled ? new ParticleFlipbook() : null;
            OnLiveEdit(index);
        }
        if (group.Flipbook == null)
        {
            return;
        }

        ParticleFlipbook flipbook = group.Flipbook!;
        ImGui.Indent();
        int rows = flipbook.Rows;
        if (DragRow("Rows", ref rows, 0.1f, 1, 64))
        {
            flipbook.Rows = rows;
            OnLiveEdit(index);
        }
        int cols = flipbook.Cols;
        if (DragRow("Columns", ref cols, 0.1f, 1, 64))
        {
            flipbook.Cols = cols;
            OnLiveEdit(index);
        }
        int framesPerAnim = flipbook.FramesPerAnim;
        if (DragRow("Frames/Anim", ref framesPerAnim, 0.1f, 0, flipbook.Rows * flipbook.Cols))
        {
            flipbook.FramesPerAnim = framesPerAnim;
            OnLiveEdit(index);
        }
        float cycles = flipbook.Cycles;
        if (DragRow("Cycles", ref cycles, 0.02f, 0f, 64f))
        {
            flipbook.Cycles = cycles;
            OnLiveEdit(index);
        }
        int frames = flipbook.Rows * flipbook.Cols;
        float averageLifetime = (group.Lifetime.Min + group.Lifetime.Max) * 0.5f;
        float animFrames = framesPerAnim > 0 ? framesPerAnim : frames;
        float effectiveFps = averageLifetime > 1e-5f ? animFrames * flipbook.Cycles / averageLifetime : 0f;
        string animHint = framesPerAnim > 0
            ? $"{frames / Math.Max(framesPerAnim, 1)} anims of {framesPerAnim} frames — one random anim per particle"
            : "the whole sheet is one animation";
        ImGui.TextDisabled($"{animHint}; {flipbook.Cycles:0.##}x, ~{effectiveFps:0.#} fps");
        bool reverse = flipbook.Reverse;
        if (ImGui.Checkbox("Reverse", ref reverse))
        {
            flipbook.Reverse = reverse;
            OnLiveEdit(index);
        }
        ImGui.Unindent();
    }

    /// <summary>Rendering: simulation space (live), blend/depth/material/texture/behavior (structural).</summary>
    private void DrawRenderingSection(int index, ParticleGroupAsset group, GroupUiState state)
    {
        ImGui.SeparatorText("Rendering");

        ParticleSimulationSpace space = group.SimulationSpace;
        if (ImGui.Combo("Simulation Space", ref space))
        {
            group.SimulationSpace = space;
            OnLiveEdit(index);
        }

        DrawBlendRow(group);
        DrawDepthRow(group);
        DrawMaterialRow(group, state);
        DrawTextureRow(group, state);
        DrawBehaviorRow(group, state);

        if (state.ReferenceError.Length > 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), state.ReferenceError);
        }
    }

    /// <summary>The blend preset combo; "(Default)" maps to null (alpha blend).</summary>
    private void DrawBlendRow(ParticleGroupAsset group)
    {
        int current = PresetIndex(group.Blend, s_blendPresets);
        if (DrawPresetCombo("Blend", current, s_blendPresets, out int selected))
        {
            group.Blend = selected < 0 ? null : s_blendPresets[selected].State;
            OnStructuralEdit();
        }
    }

    /// <summary>The depth preset combo; "(Default)" maps to null (pass default).</summary>
    private void DrawDepthRow(ParticleGroupAsset group)
    {
        int current = PresetIndex(group.Depth, s_depthPresets);
        if (DrawPresetCombo("Depth", current, s_depthPresets, out int selected))
        {
            group.Depth = selected < 0 ? null : s_depthPresets[selected].State;
            OnStructuralEdit();
        }
    }

    /// <summary>Finds the preset index matching a nullable state value; -1 = default, -2 = custom.</summary>
    private static int PresetIndex<TState>(TState? state, (string Name, TState State)[] presets) where TState : struct
    {
        if (state == null)
        {
            return -1;
        }
        for (int i = 0; i < presets.Length; i++)
        {
            if (presets[i].State.Equals(state.Value))
            {
                return i;
            }
        }
        return -2;
    }

    /// <summary>Draws a preset combo with a leading "(Default)" entry; outputs -1 for it.</summary>
    private static bool DrawPresetCombo<TState>(string label, int current, (string Name, TState State)[] presets, out int selected)
    {
        selected = current;
        string preview = current == -1 ? "(Default)" : current == -2 ? "(custom)" : presets[current].Name;
        if (!ImGui.BeginCombo(label, preview))
        {
            return false;
        }
        if (ImGui.Selectable("(Default)", current == -1))
        {
            selected = -1;
        }
        for (int i = 0; i < presets.Length; i++)
        {
            if (ImGui.Selectable(presets[i].Name, current == i))
            {
                selected = i;
            }
        }
        ImGui.EndCombo();
        return selected != current;
    }

    /// <summary>The material picker row; an empty path maps to null (default surface).</summary>
    private void DrawMaterialRow(ParticleGroupAsset group, GroupUiState state)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Material");
        ImGui.SameLine();
        if (state.MaterialPicker.Draw(Context, "##material", ref state.MaterialPath, typeof(MaterialAsset)))
        {
            state.ReferenceError = string.Empty;
            string path = state.MaterialPath.Trim();
            if (path.Length == 0)
            {
                group.Material = null;
                OnStructuralEdit();
            }
            else
            {
                try
                {
                    group.Material = Context.AssetSystem.Load<MaterialAsset>(path);
                    OnStructuralEdit();
                }
                catch (Exception e)
                {
                    state.ReferenceError = $"Material '{path}': {e.Message}";
                }
            }
        }
        if (group.Material != null && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Clear the path to use the default particle surface.");
        }
    }

    /// <summary>The texture picker row; an empty path keeps the material's own binding.</summary>
    private void DrawTextureRow(ParticleGroupAsset group, GroupUiState state)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Texture");
        ImGui.SameLine();
        if (state.TexturePicker.Draw(Context, "##texture", ref state.TexturePath, typeof(Texture2D)))
        {
            state.ReferenceError = string.Empty;
            string path = state.TexturePath.Trim();
            if (path.Length == 0)
            {
                group.Texture = null;
                OnStructuralEdit();
            }
            else
            {
                try
                {
                    group.Texture = Context.AssetSystem.Load<Texture2D>(path);
                    OnStructuralEdit();
                }
                catch (Exception e)
                {
                    state.ReferenceError = $"Texture '{path}': {e.Message}";
                }
            }
        }
    }

    /// <summary>The behavior module combo: default, the built-ins and every .slang file stem.</summary>
    private void DrawBehaviorRow(ParticleGroupAsset group, GroupUiState state)
    {
        string currentName = group.Behavior?.Name ?? string.Empty;
        string[] candidates = GetBehaviorCandidates(currentName);
        int currentIndex = currentName.Length == 0 ? 0 : Math.Max(Array.IndexOf(candidates, currentName), 0);

        if (!ImGui.BeginCombo("Behavior", currentIndex == 0 ? "(Default)" : candidates[currentIndex]))
        {
            return;
        }
        string? picked = null;
        bool clear = false;
        if (ImGui.Selectable("(Default)", currentIndex == 0))
        {
            clear = true;
        }
        for (int i = 1; i < candidates.Length; i++)
        {
            if (ImGui.Selectable(candidates[i], currentIndex == i))
            {
                picked = candidates[i];
            }
        }
        ImGui.EndCombo();

        if (clear)
        {
            group.Behavior = null;
            state.ReferenceError = string.Empty;
            OnStructuralEdit();
        }
        else if (picked != null)
        {
            try
            {
                group.Behavior = Context.RenderingSystem.ShaderSystem.GetLibrary(picked);
                state.ReferenceError = string.Empty;
                OnStructuralEdit();
            }
            catch (Exception e)
            {
                state.ReferenceError = $"Behavior '{picked}': {e.Message}";
            }
        }
    }

    /// <summary>The behavior module candidates: "(Default)" placeholder, the built-in
    /// defaults and the file stems of all .slang assets; the current value always present.</summary>
    private string[] GetBehaviorCandidates(string currentName)
    {
        if (_behaviorCandidates == null)
        {
            var stems = new SortedSet<string>(StringComparer.Ordinal)
            {
                _is3D ? ParticleAssetPipeline.DefaultBehavior3D : ParticleAssetPipeline.DefaultBehavior2D,
            };
            foreach (string assetPath in Context.AssetSystem.AllAssetNames)
            {
                if (assetPath.EndsWith(FileExt.ShaderSlang, StringComparison.OrdinalIgnoreCase))
                {
                    stems.Add(Path.GetFileNameWithoutExtension(assetPath));
                }
            }
            _behaviorCandidates = ["(Default)", .. stems];
        }
        if (currentName.Length > 0 && !_behaviorCandidates.Contains(currentName))
        {
            _behaviorCandidates = [.. _behaviorCandidates, currentName];
        }
        return _behaviorCandidates;
    }

    /// <summary>An angle range row (degrees in the UI, radians in the asset).</summary>
    private void DrawAngleRangeRow(int index, string label, ParticleRange radians, Action<ParticleRange> set)
    {
        ParticleRange degrees = new(radians.Min * math.RadToDeg, radians.Max * math.RadToDeg);
        if (DrawRangeRow(label, degrees, out ParticleRange edited, 0.5f))
        {
            set(new ParticleRange(edited.Min * math.DegToRad, edited.Max * math.DegToRad));
            OnLiveEdit(index);
        }
    }

    /// <summary>A labeled float drag row filling the remaining width.</summary>
    private static bool DragRow(string label, ref float value, float speed, float min = 0f, float max = 0f)
    {
        ImGui.SetNextItemWidth(-120f);
        return ImGui.DragFloat(label, ref value, speed, min, max);
    }

    /// <summary>A labeled int drag row filling the remaining width.</summary>
    private static bool DragRow(string label, ref int value, float speed, int min, int max)
    {
        ImGui.SetNextItemWidth(-120f);
        return ImGui.DragInt(label, ref value, speed, min, max);
    }

    /// <summary>A labeled Vector2 drag row filling the remaining width.</summary>
    private static bool DragRow(string label, ref Vector2 value, float speed, float min = 0f, float max = 0f)
    {
        ImGui.SetNextItemWidth(-120f);
        return ImGui.DragFloat2(label, ref value, speed, min, max);
    }

    /// <summary>A labeled Vector3 drag row filling the remaining width.</summary>
    private static bool DragRow(string label, ref Vector3 value, float speed, float min = 0f, float max = 0f)
    {
        ImGui.SetNextItemWidth(-120f);
        return ImGui.DragFloat3(label, ref value, speed, min, max);
    }

    /// <summary>A labeled float slider row filling the remaining width.</summary>
    private static bool SliderRow(string label, ref float value, float min, float max)
    {
        ImGui.SetNextItemWidth(-120f);
        return ImGui.SliderFloat(label, ref value, min, max);
    }

    /// <summary>A small caption vertically aligned with the following widget, on the same line.</summary>
    private static void Caption(string text)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(text);
        ImGui.SameLine();
    }

    /// <summary>A min/max pair of drag fields for a scalar range.</summary>
    private static bool DrawRangeRow(string label, ParticleRange range, out ParticleRange result, float speed)
    {
        float min = range.Min;
        float max = range.Max;
        ImGui.PushID(label);
        Caption(label);
        Caption("min");
        ImGui.SetNextItemWidth(90f);
        bool changed = ImGui.DragFloat("##min", ref min, speed, 0f, 0f);
        ImGui.SameLine();
        Caption("max");
        ImGui.SetNextItemWidth(90f);
        changed |= ImGui.DragFloat("##max", ref max, speed, 0f, 0f);
        ImGui.PopID();
        result = new ParticleRange(min, max);
        return changed;
    }

    /// <summary>A min/max pair of drag fields for a Vector2 range.</summary>
    private static bool DrawRangeRow(string label, ParticleVector2Range range, out ParticleVector2Range result, float speed)
    {
        Vector2 min = range.Min;
        Vector2 max = range.Max;
        ImGui.PushID(label);
        Caption(label);
        Caption("min");
        ImGui.SetNextItemWidth(140f);
        bool changed = ImGui.DragFloat2("##min", ref min, speed, 0f, 0f);
        ImGui.SameLine();
        Caption("max");
        ImGui.SetNextItemWidth(-1f);
        changed |= ImGui.DragFloat2("##max", ref max, speed, 0f, 0f);
        ImGui.PopID();
        result = new ParticleVector2Range { Min = min, Max = max };
        return changed;
    }
}
