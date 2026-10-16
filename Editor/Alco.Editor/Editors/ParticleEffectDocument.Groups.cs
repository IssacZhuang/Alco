using System.Numerics;
using System.Text.Json;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.IO;
using Alco.Effects;
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

    private readonly Dictionary<ParticleGroup, GroupUiState> _groupStates = new();

    /// <summary>
    /// The groups hidden in the preview, keyed by group object so the state
    /// survives reordering (stale entries from deleted groups are harmless).
    /// Editor-only state: never serialized into the asset file.
    /// </summary>
    private readonly HashSet<ParticleGroup> _hiddenGroups = new();
    private string[]? _behaviorCandidates;

    /// <summary>The whole parameters pane: effect header, group editors, add button.</summary>
    private void DrawParams()
    {
        ImGui.SeparatorText("Effect");

        using (var effect = new PropertyTable("effect"))
        {
            string name = _effect.Name;
            if (effect.TextField("Name", ref name, 128))
            {
                _effect.Name = name;
                MarkDirty();
            }
        }
        int count = EffectGroupCount;
        var info = new FixedString64("Type: ");
        info.Append(_is3D ? "3D" : "2D");
        info.Append(" — ");
        info.Append(count);
        info.Append(" group(s); rendered in list order");
        ImGui.TextDisabled(info);

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
        ? ((ParticleEffect3D)_effect).Groups.Count
        : ((ParticleEffect2D)_effect).Groups.Count;

    /// <summary>Returns the edited group at <paramref name="index"/> as the shared base type.</summary>
    private ParticleGroup GetGroup(int index) => _is3D
        ? ((ParticleEffect3D)_effect).Groups[index]
        : ((ParticleEffect2D)_effect).Groups[index];

    /// <summary>Adds a default group to the effect.</summary>
    private void AddGroup()
    {
        string name = $"Group {EffectGroupCount + 1}";
        if (_is3D)
        {
            ((ParticleEffect3D)_effect).Groups.Add(new ParticleGroup3D { Name = name });
        }
        else
        {
            ((ParticleEffect2D)_effect).Groups.Add(new ParticleGroup2D { Name = name });
        }
    }

    /// <summary>Duplicates a group (JSON roundtrip of the group object) after itself.</summary>
    private void DuplicateGroup(int index)
    {
        if (_is3D)
        {
            var groups = ((ParticleEffect3D)_effect).Groups;
            string json = JsonSerializer.Serialize(groups[index], _jsonOptions);
            groups.Insert(index + 1, JsonSerializer.Deserialize<ParticleGroup3D>(json, _jsonOptions)!);
        }
        else
        {
            var groups = ((ParticleEffect2D)_effect).Groups;
            string json = JsonSerializer.Serialize(groups[index], _jsonOptions);
            groups.Insert(index + 1, JsonSerializer.Deserialize<ParticleGroup2D>(json, _jsonOptions)!);
        }
    }

    /// <summary>One group editor: the header row (visibility checkbox, collapsing header
    /// and right-aligned ASCII actions) then the parameter sections. A Delete click only
    /// records the index; the caller removes the group after the draw pass.</summary>
    private void DrawGroup(int index, ParticleGroup group, ref int removeGroup)
    {
        GroupUiState state = GetGroupUiState(group);
        ImGui.PushID(index);
        float rowTopY = ImGui.GetCursorPosY();
        float rowLeftX = ImGui.GetCursorPosX();

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

        // The header claims the whole row width; the actions sit on its right end as
        // uniform square icon buttons (side = the row's frame height) with a tight
        // gap, so the cluster reads as one toolbar. SetNextItemAllowOverlap makes
        // the header yield hover and clicks to the buttons, which are submitted
        // after it over the same rect.
        const float actionGap = 2f;
        float iconSize = ImGui.GetFrameHeight();
        float actionsWidth = (iconSize * 4f) + (actionGap * 3f);
        float rowRightX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemAllowOverlap();
        var header = new FixedString256(group.Name);
        header.Append("##header");
        bool open = ImGui.CollapsingHeader(header);

        // Group list actions, right-aligned on the header row.
        ImGui.SameLine();
        ImGui.SetCursorPosX(rowRightX - actionsWidth);
        ImGui.BeginDisabled(index == 0);
        if (GroupActionButton(GroupActionIcon.MoveUp, new Vector2(iconSize)))
        {
            MoveGroup(index, -1);
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip("Move up");
        }
        ImGui.SameLine(0, actionGap);
        ImGui.BeginDisabled(index == EffectGroupCount - 1);
        if (GroupActionButton(GroupActionIcon.MoveDown, new Vector2(iconSize)))
        {
            MoveGroup(index, +1);
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip("Move down");
        }
        ImGui.SameLine(0, actionGap);
        if (GroupActionButton(GroupActionIcon.Duplicate, new Vector2(iconSize)))
        {
            DuplicateGroup(index);
            OnStructuralEdit();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Duplicate");
        }
        ImGui.SameLine(0, actionGap);
        ImGui.BeginDisabled(EffectGroupCount <= 1);
        if (GroupActionButton(GroupActionIcon.Delete, new Vector2(iconSize)))
        {
            removeGroup = index;
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip("Delete");
        }

        // Advance below the header row explicitly: the buttons end at the right edge
        // and NewLine() from there would insert an extra blank line between groups.
        ImGui.SetCursorPosX(rowLeftX);
        ImGui.SetCursorPosY(rowTopY + iconSize + ImGui.GetStyle().ItemSpacing.Y);
        if (open)
        {
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

    /// <summary>Vector glyphs for <see cref="GroupActionButton"/>.</summary>
    private enum GroupActionIcon
    {
        MoveUp,
        MoveDown,
        Duplicate,
        Delete,
    }

    /// <summary>A square button whose glyph is stroked as vector lines instead of font
    /// text: font ink for these small glyphs sits at inconsistent heights, so drawing
    /// them manually keeps every icon optically centered. Must run inside the
    /// caller's disabled scope — the glyph uses the style's text color and dims
    /// with the frame.</summary>
    private static bool GroupActionButton(GroupActionIcon icon, Vector2 size)
    {
        // Fixed ids: building them from the enum would allocate every frame.
        string id = icon switch
        {
            GroupActionIcon.MoveUp => "##moveUp",
            GroupActionIcon.MoveDown => "##moveDown",
            GroupActionIcon.Duplicate => "##duplicate",
            _ => "##delete",
        };
        bool clicked = ImGui.Button(id, size);
        Vector2 center = ImGui.GetItemRectMin() + (size * 0.5f);
        float half = size.X * 0.2f; // glyph half-extent inside the button
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        uint color = ImGui.GetColorU32(ImGuiCol.Text);
        const float thickness = 1.5f;
        switch (icon)
        {
            case GroupActionIcon.MoveUp:
                drawList.AddLine(new Vector2(center.X - half, center.Y + (half * 0.5f)), new Vector2(center.X, center.Y - (half * 0.5f)), color, thickness);
                drawList.AddLine(new Vector2(center.X, center.Y - (half * 0.5f)), new Vector2(center.X + half, center.Y + (half * 0.5f)), color, thickness);
                break;
            case GroupActionIcon.MoveDown:
                drawList.AddLine(new Vector2(center.X - half, center.Y - (half * 0.5f)), new Vector2(center.X, center.Y + (half * 0.5f)), color, thickness);
                drawList.AddLine(new Vector2(center.X, center.Y + (half * 0.5f)), new Vector2(center.X + half, center.Y - (half * 0.5f)), color, thickness);
                break;
            case GroupActionIcon.Duplicate:
            {
                // Conventional "duplicate" glyph: a full sheet in front with the
                // top-left corner of a second sheet peeking out behind it.
                Vector2 frontMin = center - new Vector2(half * 0.35f);
                Vector2 frontMax = center + new Vector2(half);
                Vector2 shift = new Vector2(-half * 0.65f); // back sheet offset
                Vector2 backMin = frontMin + shift;
                drawList.AddLine(backMin, new Vector2(frontMax.X + shift.X, backMin.Y), color, thickness);
                drawList.AddLine(backMin, new Vector2(backMin.X, frontMax.Y + shift.Y), color, thickness);
                drawList.AddLine(frontMin, new Vector2(frontMax.X, frontMin.Y), color, thickness);
                drawList.AddLine(new Vector2(frontMax.X, frontMin.Y), frontMax, color, thickness);
                drawList.AddLine(frontMax, new Vector2(frontMin.X, frontMax.Y), color, thickness);
                drawList.AddLine(new Vector2(frontMin.X, frontMax.Y), frontMin, color, thickness);
                break;
            }
            case GroupActionIcon.Delete:
                drawList.AddLine(new Vector2(center.X - half, center.Y - half), new Vector2(center.X + half, center.Y + half), color, thickness);
                drawList.AddLine(new Vector2(center.X - half, center.Y + half), new Vector2(center.X + half, center.Y - half), color, thickness);
                break;
        }
        return clicked;
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
            var groups = ((ParticleEffect3D)_effect).Groups;
            (groups[index], groups[target]) = (groups[target], groups[index]);
        }
        else
        {
            var groups = ((ParticleEffect2D)_effect).Groups;
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
            ((ParticleEffect3D)_effect).Groups.RemoveAt(index);
        }
        else
        {
            ((ParticleEffect2D)_effect).Groups.RemoveAt(index);
        }
        OnStructuralEdit();
    }

    /// <summary>The per-group widget state, created on first use.</summary>
    private GroupUiState GetGroupUiState(ParticleGroup group)
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
    private void DrawGroupName(ParticleGroup group)
    {
        string name = group.Name;
        using var table = new PropertyTable("groupname");
        if (table.TextField("Group Name", ref name, 128))
        {
            group.Name = name;
            MarkDirty();
        }
    }

    /// <summary>Emission: rate (live), capacity, timeline and bursts (structural).</summary>
    private void DrawEmissionSection(int index, ParticleGroup group)
    {
        ImGui.SeparatorText("Emission");
        using var table = new PropertyTable("emission");

        float rate = group.EmissionRate;
        if (table.Drag("Rate (per s)", ref rate, 0.5f, 0f, 100000f))
        {
            group.EmissionRate = rate;
            OnLiveEdit(index);
        }

        int maxParticles = group.MaxParticles;
        if (table.Drag("Max Particles", ref maxParticles, 8, 1, 1 << 20))
        {
            group.MaxParticles = maxParticles;
            OnStructuralEdit();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Pool slice capacity; excess spawns overwrite the oldest particles.");
        }

        float duration = group.Duration;
        if (table.Drag("Duration (s)", ref duration, 0.05f, 0f, 120f))
        {
            group.Duration = duration;
            OnStructuralEdit();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("0 = the emitter never stops emitting on its own.");
        }

        bool looping = group.Looping;
        if (table.Checkbox("Looping", ref looping))
        {
            group.Looping = looping;
            OnStructuralEdit();
        }

        // Bursts (structural: they live on the CPU emission timeline).
        int removeBurst = -1;
        for (int b = 0; b < group.Bursts.Count; b++)
        {
            var burstLabel = new FixedString32("Burst ");
            burstLabel.Append(b + 1);
            DrawBurstRow(table, group.Bursts[b], burstLabel, b, ref removeBurst);
        }
        if (removeBurst >= 0)
        {
            group.Bursts.RemoveAt(removeBurst);
            OnStructuralEdit();
        }
        table.BeginCell(string.Empty);
        if (ImGui.SmallButton("Add Burst"))
        {
            group.Bursts.Add(new ParticleBurst());
            OnStructuralEdit();
        }
        table.EndCell();
    }

    /// <summary>One burst row: the time and count fields flow onto extra lines as
    /// whole groups when the value column is too narrow; the trailing square button
    /// removes the burst (recorded into <paramref name="removeBurst"/> for the caller
    /// to apply after the draw pass).</summary>
    private void DrawBurstRow(PropertyTable table, ParticleBurst burst, ReadOnlySpan<char> label, int burstIndex, ref int removeBurst)
    {
        ImGui.PushID(burstIndex);
        table.BeginCell(label);

        float spacing = ImGui.GetStyle().ItemSpacing.X;
        const float timeWidth = 84f;
        const float countWidth = 68f;
        float removeWidth = ImGui.GetFrameHeight() + spacing;

        PropertyTable.WrapWhenFull(
            PropertyTable.CaptionWidth("at") + timeWidth + spacing + PropertyTable.CaptionWidth("min") + countWidth);
        PropertyTable.Caption("at");
        float time = burst.Time;
        ImGui.SetNextItemWidth(timeWidth);
        if (ImGui.DragFloat("##time", ref time, 0.02f, 0f, 300f))
        {
            burst.Time = time;
            OnStructuralEdit();
        }
        PropertyTable.Caption("min");
        int countMin = burst.CountMin;
        ImGui.SetNextItemWidth(countWidth);
        if (ImGui.DragInt("##countmin", ref countMin, 0.2f, 0, 100000))
        {
            burst.CountMin = countMin;
            OnStructuralEdit();
        }
        PropertyTable.WrapWhenFull(PropertyTable.CaptionWidth("max") + countWidth + removeWidth);
        PropertyTable.Caption("max");
        int countMax = burst.CountMax;
        ImGui.SetNextItemWidth(countWidth);
        if (ImGui.DragInt("##countmax", ref countMax, 0.2f, 0, 100000))
        {
            burst.CountMax = countMax;
            OnStructuralEdit();
        }
        ImGui.SameLine(0, spacing);
        if (ImGui.Button("X##remove", new Vector2(ImGui.GetFrameHeight())))
        {
            removeBurst = burstIndex;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Remove this burst");
        }

        table.EndCell();
        ImGui.PopID();
    }

    /// <summary>Shape: the 2D/3D emission shape with per-type fields and the fixed
    /// position offset of the spawn point (all live).</summary>
    private void DrawShapeSection(int index, ParticleGroup group)
    {
        ImGui.SeparatorText("Shape");
        using var table = new PropertyTable("shape");

        if (group is ParticleGroup2D group2D)
        {
            ParticleShape2D shape = group2D.Shape;
            ParticleShape2DType type = shape.Type;
            if (table.Combo("Type", ref type))
            {
                shape.Type = type;
                OnLiveEdit(index);
            }
            if (shape.Type == ParticleShape2DType.Circle)
            {
                float radius = shape.Radius;
                if (table.Drag("Radius", ref radius, 0.1f, 0f))
                {
                    shape.Radius = radius;
                    OnLiveEdit(index);
                }
                float inner = shape.InnerRadius;
                if (table.Slider("Inner Radius", ref inner, 0f, 1f))
                {
                    shape.InnerRadius = inner;
                    OnLiveEdit(index);
                }
            }
            else if (shape.Type == ParticleShape2DType.Box)
            {
                Vector2 extents = shape.Extents;
                if (table.Drag("Extents", ref extents, 0.1f, 0f))
                {
                    shape.Extents = extents;
                    OnLiveEdit(index);
                }
            }
            Vector2 positionOffset = group2D.PositionOffset;
            if (table.Drag("Position Offset", ref positionOffset, 0.02f))
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
            ParticleShape3D shape = ((ParticleGroup3D)group).Shape;
            ParticleShape3DType type = shape.Type;
            if (table.Combo("Type", ref type))
            {
                shape.Type = type;
                OnLiveEdit(index);
            }
            if (shape.Type is ParticleShape3DType.Sphere or ParticleShape3DType.Hemisphere)
            {
                float radius = shape.Radius;
                if (table.Drag("Radius", ref radius, 0.05f, 0f))
                {
                    shape.Radius = radius;
                    OnLiveEdit(index);
                }
                float inner = shape.InnerRadius;
                if (table.Slider("Inner Radius", ref inner, 0f, 1f))
                {
                    shape.InnerRadius = inner;
                    OnLiveEdit(index);
                }
            }
            else if (shape.Type == ParticleShape3DType.Box)
            {
                Vector3 extents = shape.Extents;
                if (table.Drag("Extents", ref extents, 0.05f, 0f))
                {
                    shape.Extents = extents;
                    OnLiveEdit(index);
                }
            }
            Vector3 positionOffset = ((ParticleGroup3D)group).PositionOffset;
            if (table.Drag("Position Offset", ref positionOffset, 0.02f))
            {
                ((ParticleGroup3D)group).PositionOffset = positionOffset;
                OnLiveEdit(index);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Fixed emitter-local offset of the spawn point; rotates with the emitter.");
            }
        }
    }

    /// <summary>Motion: planar direction/speed, independent 2.5D height, gravity and drag (all live).</summary>
    private void DrawMotionSection(int index, ParticleGroup group)
    {
        ImGui.SeparatorText("Motion");
        using var table = new PropertyTable("motion");

        if (_is3D)
        {
            var group3D = (ParticleGroup3D)group;
            ParticleDirectionMode mode = group3D.DirectionMode;
            if (table.Combo("Direction Mode", ref mode))
            {
                group3D.DirectionMode = mode;
                OnLiveEdit(index);
            }
            Vector3 direction = group3D.Direction;
            if (table.Drag("Direction", ref direction, 0.02f))
            {
                group3D.Direction = direction;
                OnLiveEdit(index);
            }
            float spread = group3D.SpreadAngle * math.RadToDeg;
            if (table.Drag("Spread (deg)", ref spread, 0.5f, 0f, 360f))
            {
                group3D.SpreadAngle = spread * math.DegToRad;
                OnLiveEdit(index);
            }
        }
        else
        {
            var group2D = (ParticleGroup2D)group;
            ParticleDirectionMode mode = group2D.DirectionMode;
            if (table.Combo("Direction Mode", ref mode))
            {
                group2D.DirectionMode = mode;
                OnLiveEdit(index);
            }
            Vector2 direction = group2D.Direction;
            if (table.Drag("Direction", ref direction, 0.02f))
            {
                group2D.Direction = direction;
                OnLiveEdit(index);
            }
            float spread = group2D.SpreadAngle * math.RadToDeg;
            if (table.Drag("Spread (deg)", ref spread, 0.5f, 0f, 360f))
            {
                group2D.SpreadAngle = spread * math.DegToRad;
                OnLiveEdit(index);
            }
        }

        if (RangeRow(table, "Speed", group.Speed, 0.1f, out ParticleRange speed))
        {
            group.Speed = speed;
            OnLiveEdit(index);
        }

        if (_is3D)
        {
            Vector3 gravity = ((ParticleGroup3D)group).Gravity;
            if (table.Drag("Gravity", ref gravity, 0.05f))
            {
                ((ParticleGroup3D)group).Gravity = gravity;
                OnLiveEdit(index);
            }
        }
        else
        {
            var group2D = (ParticleGroup2D)group;
            Vector2 gravity = group2D.Gravity;
            if (table.Drag("Gravity", ref gravity, 0.05f))
            {
                group2D.Gravity = gravity;
                OnLiveEdit(index);
            }
            if (RangeRow(table, "Start Height", group2D.StartHeight, 0.05f, out ParticleRange startHeight))
            {
                group2D.StartHeight = startHeight;
                OnLiveEdit(index);
            }
            if (RangeRow(table, "Height Velocity", group2D.HeightVelocity, 0.05f, out ParticleRange heightVelocity))
            {
                group2D.HeightVelocity = heightVelocity;
                OnLiveEdit(index);
            }
            float heightAcceleration = group2D.HeightAcceleration;
            if (table.Drag("Height Acceleration", ref heightAcceleration, 0.05f))
            {
                group2D.HeightAcceleration = heightAcceleration;
                OnLiveEdit(index);
            }
        }

        float drag = group.Drag;
        if (table.Drag("Drag", ref drag, 0.02f, 0f))
        {
            group.Drag = drag;
            OnLiveEdit(index);
        }
    }

    /// <summary>Lifetime, fades, size and the over-life size curve.</summary>
    private void DrawLifeAndSizeSection(int index, ParticleGroup group)
    {
        ImGui.SeparatorText("Lifetime & Size");
        bool hasCurve = group.SizeCurve is { Count: > 0 };
        using (var table = new PropertyTable("lifesize"))
        {
            if (RangeRow(table, "Lifetime (s)", group.Lifetime, 0.05f, out ParticleRange lifetime))
            {
                group.Lifetime = lifetime;
                OnLiveEdit(index);
            }

            float fadeIn = group.FadeIn;
            if (table.Slider("Fade In", ref fadeIn, 0f, 1f))
            {
                group.FadeIn = fadeIn;
                OnLiveEdit(index);
            }
            float fadeOut = group.FadeOut;
            if (table.Slider("Fade Out", ref fadeOut, 0f, 1f))
            {
                group.FadeOut = fadeOut;
                OnLiveEdit(index);
            }

            if (_is3D)
            {
                var group3D = (ParticleGroup3D)group;
                if (RangeRow(table, "Size", group3D.Size, 0.02f, out ParticleRange size))
                {
                    group3D.Size = size;
                    OnLiveEdit(index);
                }
            }
            else
            {
                var group2D = (ParticleGroup2D)group;
                Vector2 sizeMin = group2D.Size.Min;
                Vector2 sizeMax = group2D.Size.Max;
                if (table.Range("Size", ref sizeMin, ref sizeMax, 0.02f))
                {
                    group2D.Size = new ParticleVector2Range { Min = sizeMin, Max = sizeMax };
                    OnLiveEdit(index);
                }
            }

            float endScale = group.EndScale;
            if (table.Drag("End Scale", ref endScale, 0.01f, 0f))
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
                var group3D = (ParticleGroup3D)group;
                if (AngleRangeRow(table, "Start Rotation (deg)", group3D.StartRotation, out ParticleRange startRotation))
                {
                    group3D.StartRotation = startRotation;
                    OnLiveEdit(index);
                }
                if (AngleRangeRow(table, "Angular Velocity (deg/s)", group3D.AngularVelocity, out ParticleRange angularVelocity))
                {
                    group3D.AngularVelocity = angularVelocity;
                    OnLiveEdit(index);
                }
            }
            else
            {
                var group2D = (ParticleGroup2D)group;
                if (AngleRangeRow(table, "Start Rotation (deg)", group2D.StartRotation, out ParticleRange startRotation))
                {
                    group2D.StartRotation = startRotation;
                    OnLiveEdit(index);
                }
                if (AngleRangeRow(table, "Angular Velocity (deg/s)", group2D.AngularVelocity, out ParticleRange angularVelocity))
                {
                    group2D.AngularVelocity = angularVelocity;
                    OnLiveEdit(index);
                }
            }

            // Size curve (structural: the lookup texture rebakes).
            if (table.Checkbox("Size Curve", ref hasCurve))
            {
                group.SizeCurve = hasCurve
                    ? [new ParticleScalarKey { Time = 0f, Value = 1f }, new ParticleScalarKey { Time = 1f, Value = MathF.Max(group.EndScale, 0f) }]
                    : null;
                OnStructuralEdit();
            }
        }

        if (hasCurve && group.SizeCurve != null)
        {
            // Full width below the rows: dragging keys benefits from the extra width.
            if (CurveEditor.Draw("##sizecurve", group.SizeCurve))
            {
                OnStructuralEdit();
            }
        }
    }

    /// <summary>Color: spawn range, end color, tint and the over-life gradient.</summary>
    private void DrawColorSection(int index, ParticleGroup group)
    {
        ImGui.SeparatorText("Color");
        bool hasGradient = group.ColorGradient is { Count: > 0 };
        using (var table = new PropertyTable("color"))
        {
            ColorFloat startMin = group.StartColor.Min;
            if (table.Color("Start Color Min", ref startMin))
            {
                group.StartColor = new ParticleColorRange { Min = startMin, Max = group.StartColor.Max };
                OnLiveEdit(index);
            }
            ColorFloat startMax = group.StartColor.Max;
            if (table.Color("Start Color Max", ref startMax))
            {
                group.StartColor = new ParticleColorRange { Min = group.StartColor.Min, Max = startMax };
                OnLiveEdit(index);
            }
            ColorFloat endColor = group.EndColor;
            if (table.Color("End Color", ref endColor))
            {
                group.EndColor = endColor;
                OnLiveEdit(index);
            }
            ColorFloat tint = group.Tint;
            if (table.Color("Tint", ref tint))
            {
                group.Tint = tint;
                OnLiveEdit(index);
            }

            // Color gradient (structural: the lookup texture rebakes).
            if (table.Checkbox("Color Gradient", ref hasGradient))
            {
                group.ColorGradient = hasGradient
                    ? [new ParticleColorKey { Time = 0f, Color = group.StartColor.Min }, new ParticleColorKey { Time = 1f, Color = group.EndColor }]
                    : null;
                OnStructuralEdit();
            }
            if (hasGradient && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Replaces the start → end color lerp; fades still apply.");
            }
        }

        if (hasGradient && group.ColorGradient != null)
        {
            // Full width below the rows: dragging keys benefits from the extra width.
            if (GradientEditor.Draw("##colorgradient", group.ColorGradient))
            {
                OnStructuralEdit();
            }
        }
    }

    /// <summary>Velocity stretch (2D also requires rotation alignment; all live).</summary>
    private void DrawStretchSection(int index, ParticleGroup group)
    {
        if (group is ParticleGroup2D)
        {
            ImGui.SeparatorText("Rotation & Stretch");
        }
        else
        {
            ImGui.SeparatorText("Stretch");
        }
        using var table = new PropertyTable("stretch");

        if (group is ParticleGroup2D group2D)
        {
            bool align = group2D.AlignRotationToVelocity;
            if (table.Checkbox("Align Rotation To Velocity", ref align))
            {
                group2D.AlignRotationToVelocity = align;
                OnLiveEdit(index);
            }

            bool stretch = group2D.VelocityStretch;
            ImGui.BeginDisabled(!group2D.AlignRotationToVelocity);
            if (table.Checkbox("Velocity Stretch", ref stretch))
            {
                group2D.VelocityStretch = stretch;
                OnLiveEdit(index);
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("2D stretch requires Align Rotation To Velocity.");
            }

            float lengthScale = group2D.StretchLengthScale;
            if (table.Drag("Stretch Length Scale", ref lengthScale, 0.02f, 0f))
            {
                group2D.StretchLengthScale = lengthScale;
                OnLiveEdit(index);
            }
            float speedScale = group2D.StretchSpeedScale;
            if (table.Drag("Stretch Speed Scale", ref speedScale, 0.005f, 0f))
            {
                group2D.StretchSpeedScale = speedScale;
                OnLiveEdit(index);
            }
        }
        else
        {
            ImGui.SeparatorText("Stretch");

            var group3D = (ParticleGroup3D)group;
            bool stretch = group3D.VelocityStretch;
            if (table.Checkbox("Velocity Stretch", ref stretch))
            {
                group3D.VelocityStretch = stretch;
                OnLiveEdit(index);
            }
            float lengthScale = group3D.StretchLengthScale;
            if (table.Drag("Stretch Length Scale", ref lengthScale, 0.02f, 0f))
            {
                group3D.StretchLengthScale = lengthScale;
                OnLiveEdit(index);
            }
            float speedScale = group3D.StretchSpeedScale;
            if (table.Drag("Stretch Speed Scale", ref speedScale, 0.005f, 0f))
            {
                group3D.StretchSpeedScale = speedScale;
                OnLiveEdit(index);
            }
        }
    }

    /// <summary>Flipbook animation of the particle texture (all live).</summary>
    private void DrawFlipbookSection(int index, ParticleGroup group)
    {
        bool enabled = group.Flipbook != null;
        using (var table = new PropertyTable("flipbook"))
        {
            if (table.Checkbox("Flipbook", ref enabled))
            {
                group.Flipbook = enabled ? new ParticleFlipbook() : null;
                OnLiveEdit(index);
            }
            if (group.Flipbook == null)
            {
                return;
            }

            ParticleFlipbook flipbook = group.Flipbook!;
            int rows = flipbook.Rows;
            if (table.Drag("Rows", ref rows, 0.1f, 1, 64))
            {
                flipbook.Rows = rows;
                OnLiveEdit(index);
            }
            int cols = flipbook.Cols;
            if (table.Drag("Columns", ref cols, 0.1f, 1, 64))
            {
                flipbook.Cols = cols;
                OnLiveEdit(index);
            }
            int framesPerAnim = flipbook.FramesPerAnim;
            if (table.Drag("Frames/Anim", ref framesPerAnim, 0.1f, 0, flipbook.Rows * flipbook.Cols))
            {
                flipbook.FramesPerAnim = framesPerAnim;
                OnLiveEdit(index);
            }
            float cycles = flipbook.Cycles;
            if (table.Drag("Cycles", ref cycles, 0.02f, 0f, 64f))
            {
                flipbook.Cycles = cycles;
                OnLiveEdit(index);
            }

            // The usage hint wraps inside the value column, aligned with the fields.
            int frames = flipbook.Rows * flipbook.Cols;
            float averageLifetime = (group.Lifetime.Min + group.Lifetime.Max) * 0.5f;
            float animFrames = framesPerAnim > 0 ? framesPerAnim : frames;
            float effectiveFps = averageLifetime > 1e-5f ? animFrames * flipbook.Cycles / averageLifetime : 0f;
            var hint = new FixedString128();
            if (framesPerAnim > 0)
            {
                hint.Append(frames / Math.Max(framesPerAnim, 1));
                hint.Append(" anims of ");
                hint.Append(framesPerAnim);
                hint.Append(" frames — one random anim per particle");
            }
            else
            {
                hint.Append("the whole sheet is one animation");
            }
            hint.Append("; ");
            hint.Append(flipbook.Cycles, 2);
            hint.Append("x, ~");
            hint.Append(effectiveFps, 1);
            hint.Append(" fps");
            table.BeginCell(string.Empty);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
            ImGui.TextDisabled(hint);
            ImGui.PopTextWrapPos();
            table.EndCell();

            bool reverse = flipbook.Reverse;
            if (table.Checkbox("Reverse", ref reverse))
            {
                flipbook.Reverse = reverse;
                OnLiveEdit(index);
            }
        }
    }

    /// <summary>Rendering: simulation space (live), blend/depth/material/texture/behavior (structural).</summary>
    private void DrawRenderingSection(int index, ParticleGroup group, GroupUiState state)
    {
        ImGui.SeparatorText("Rendering");
        using (var table = new PropertyTable("rendering"))
        {
            ParticleSimulationSpace space = group.SimulationSpace;
            if (table.Combo("Simulation Space", ref space))
            {
                group.SimulationSpace = space;
                OnLiveEdit(index);
            }

            DrawBlendRow(table, group);
            DrawDepthRow(table, group);
            DrawMaterialRow(table, group, state);
            DrawTextureRow(table, group, state);
            DrawBehaviorRow(table, group, state);
        }

        if (state.ReferenceError.Length > 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), state.ReferenceError);
        }
    }

    /// <summary>The blend preset combo; "(Default)" maps to null (alpha blend).</summary>
    private void DrawBlendRow(PropertyTable table, ParticleGroup group)
    {
        int current = PresetIndex(group.Blend, s_blendPresets);
        table.BeginCell("Blend");
        if (DrawPresetCombo(current, s_blendPresets, out int selected))
        {
            group.Blend = selected < 0 ? null : s_blendPresets[selected].State;
            OnStructuralEdit();
        }
        table.EndCell();
    }

    /// <summary>The depth preset combo; "(Default)" maps to null (pass default).</summary>
    private void DrawDepthRow(PropertyTable table, ParticleGroup group)
    {
        int current = PresetIndex(group.Depth, s_depthPresets);
        table.BeginCell("Depth");
        if (DrawPresetCombo(current, s_depthPresets, out int selected))
        {
            group.Depth = selected < 0 ? null : s_depthPresets[selected].State;
            OnStructuralEdit();
        }
        table.EndCell();
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

    /// <summary>Draws a preset combo filling the current value cell, with a leading
    /// "(Default)" entry; outputs -1 for it.</summary>
    private static bool DrawPresetCombo<TState>(int current, (string Name, TState State)[] presets, out int selected)
    {
        selected = current;
        string preview = current == -1 ? "(Default)" : current == -2 ? "(custom)" : presets[current].Name;
        ImGui.SetNextItemWidth(-1f);
        if (!ImGui.BeginCombo("##value", preview))
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
    private void DrawMaterialRow(PropertyTable table, ParticleGroup group, GroupUiState state)
    {
        table.BeginCell("Material");
        // Reserve the trailing browse button so the input fills the rest of the cell.
        ImGui.SetNextItemWidth(-(ImGui.CalcTextSize("...").X + (ImGui.GetStyle().FramePadding.X * 4f) + ImGui.GetStyle().ItemSpacing.X));
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
        table.EndCell();
    }

    /// <summary>The texture picker row; an empty path keeps the material's own binding.</summary>
    private void DrawTextureRow(PropertyTable table, ParticleGroup group, GroupUiState state)
    {
        table.BeginCell("Texture");
        // Reserve the trailing browse button so the input fills the rest of the cell.
        ImGui.SetNextItemWidth(-(ImGui.CalcTextSize("...").X + (ImGui.GetStyle().FramePadding.X * 4f) + ImGui.GetStyle().ItemSpacing.X));
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
        table.EndCell();
    }

    /// <summary>The behavior module combo: default, the built-ins and every .slang file stem.</summary>
    private void DrawBehaviorRow(PropertyTable table, ParticleGroup group, GroupUiState state)
    {
        string currentName = group.Behavior?.Name ?? string.Empty;
        string[] candidates = GetBehaviorCandidates(currentName);
        int currentIndex = currentName.Length == 0 ? 0 : Math.Max(Array.IndexOf(candidates, currentName), 0);

        table.BeginCell("Behavior");
        ImGui.SetNextItemWidth(-1f);
        if (!ImGui.BeginCombo("##value", currentIndex == 0 ? "(Default)" : candidates[currentIndex]))
        {
            table.EndCell();
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
        table.EndCell();

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
    private static bool AngleRangeRow(PropertyTable table, string label, ParticleRange radians, out ParticleRange result)
    {
        bool changed = RangeRow(table, label, new ParticleRange(radians.Min * math.RadToDeg, radians.Max * math.RadToDeg), 0.5f, out ParticleRange degrees);
        result = new ParticleRange(degrees.Min * math.DegToRad, degrees.Max * math.DegToRad);
        return changed;
    }

    /// <summary>A scalar range row over a <see cref="ParticleRange"/> value.</summary>
    private static bool RangeRow(PropertyTable table, string label, ParticleRange range, float speed, out ParticleRange result)
    {
        float min = range.Min;
        float max = range.Max;
        bool changed = table.Range(label, ref min, ref max, speed);
        result = new ParticleRange(min, max);
        return changed;
    }
}
