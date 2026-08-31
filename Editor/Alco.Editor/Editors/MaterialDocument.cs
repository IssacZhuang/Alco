using System.Numerics;
using System.Text.Json;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// Material asset document (<c>.amat</c>): a preview pane (the material's bound
/// textures; a surface-accurate compiled preview is a planned follow-up) plus a
/// parameters pane editing the name, texture slots and <see cref="ShaderValue"/>
/// parameter table. When the material names a surface, its texture slots and
/// parameter rows come from the surface's shader reflection (fixed names and types —
/// nothing to type in); materials without a surface keep free-form rows.
/// Edits happen on a detached copy deserialized from the file, so
/// the shared asset cache is never mutated; saving serializes through the material
/// loader's own JSON options.
/// </summary>
public sealed class MaterialDocument : AssetDocument
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _absolutePath = string.Empty;
    private readonly MaterialAsset _material;

    private string _name;
    private readonly List<TextureSlotRow> _textureSlots = new();
    private readonly List<ParameterRow> _parameters = new();
    private string _newParameterName = string.Empty;
    private int _newParameterKind;
    private string _saveError = string.Empty;

    /// <summary>Creates the document; throws when the file cannot be resolved or parsed.</summary>
    public MaterialDocument(EditorContext context, string assetPath) : base(context, assetPath)
    {
        _jsonOptions = AssetLoaderMaterialAsset.CreateJsonOptions(Context.AssetSystem, Context.RenderingSystem.ShaderSystem);

        if (!Context.Project.TryGetOwnedAbsolutePath(assetPath, out string? owned)
            && !Context.Project.TryGetReferencedAbsolutePath(assetPath, out owned))
        {
            throw new FileNotFoundException($"Cannot resolve {assetPath} to a mounted file.");
        }
        _absolutePath = owned;

        // Detached edit copy: parse the file directly instead of taking the shared
        // cached instance, so edits never leak into other consumers before a save.
        _material = JsonSerializer.Deserialize<MaterialAsset>(File.ReadAllText(_absolutePath), _jsonOptions)
            ?? throw new InvalidDataException($"Material asset '{assetPath}' is empty.");

        _name = _material.Name;
        if (_material.Surface is { } surface)
        {
            BuildReflectedRows(surface);
        }
        else
        {
            // No surface named (pipeline default, unknown to the editor): free-form rows.
            foreach (KeyValuePair<string, Texture2D> pair in _material.Textures)
            {
                _textureSlots.Add(new TextureSlotRow(pair.Key, pair.Value));
            }
            foreach (KeyValuePair<string, ShaderValue> pair in _material.Parameters)
            {
                _parameters.Add(new ParameterRow(pair.Key, pair.Value));
            }
        }
    }

    /// <summary>
    /// Builds the slot and parameter rows from the surface's reflection: one fixed row
    /// per declared texture slot and per <c>[MaterialParams]</c> member, then the
    /// asset's leftover bindings as removable orphan rows.
    /// </summary>
    private void BuildReflectedRows(ShaderLibrary surface)
    {
        ShaderLibraryReflection reflection = surface.Reflection;

        var knownSlots = new HashSet<string>(StringComparer.Ordinal);
        foreach (ShaderTextureSlot slot in reflection.TextureSlots)
        {
            knownSlots.Add(slot.Name);
            _material.Textures.TryGetValue(slot.Name, out Texture2D? texture);
            _textureSlots.Add(new TextureSlotRow(slot.Name, texture) { IsReflected = true });
        }
        foreach (KeyValuePair<string, Texture2D> pair in _material.Textures)
        {
            if (!knownSlots.Contains(pair.Key))
            {
                _textureSlots.Add(new TextureSlotRow(pair.Key, pair.Value));
            }
        }

        var knownParams = new HashSet<string>(StringComparer.Ordinal);
        foreach (ShaderUniformBlock block in reflection.UniformBlocks)
        {
            if (!block.Attributes.Contains(MaterialCompiler.ParamsMarkerAttribute))
            {
                continue;
            }
            foreach (ShaderUniformMember member in block.Members)
            {
                knownParams.Add(member.Name);
                bool hasExisting = _material.Parameters.TryGetValue(member.Name, out ShaderValue existing);
                if (member.ComponentCount != 16 && member.ElementCount > 1 && member.ComponentCount > 1)
                {
                    // Vector arrays cannot be authored; keep an existing value as a
                    // read-only row so saving preserves it, otherwise omit it entirely.
                    if (hasExisting)
                    {
                        _parameters.Add(new ParameterRow(member.Name, existing)
                        {
                            IsReflected = true,
                            Block = block.Name,
                            TypeHint = Describe(member),
                        });
                    }
                    continue;
                }

                ShaderValue value = hasExisting ? CoerceOrDefault(member, existing) : DefaultValue(member);
                _parameters.Add(new ParameterRow(member.Name, value)
                {
                    IsReflected = true,
                    Block = block.Name,
                    TypeHint = Describe(member),
                });
            }
        }
        foreach (KeyValuePair<string, ShaderValue> pair in _material.Parameters)
        {
            if (!knownParams.Contains(pair.Key))
            {
                _parameters.Add(new ParameterRow(pair.Key, pair.Value));
            }
        }
    }

    /// <summary>The default value of a reflected member (zero / identity).</summary>
    private static ShaderValue DefaultValue(ShaderUniformMember member)
    {
        if (member.ComponentCount == 16)
        {
            return ShaderValue.Matrix(Matrix4x4.Identity);
        }
        if (member.ElementCount > 1)
        {
            int count = (int)member.ElementCount;
            return member.ScalarType switch
            {
                ShaderUniformScalarType.Int32 or ShaderUniformScalarType.UInt32 => ShaderValue.Ints(new int[count]),
                ShaderUniformScalarType.Bool32 => ShaderValue.Bools(new bool[count]),
                _ => ShaderValue.Floats(new float[count]),
            };
        }
        return member.ScalarType switch
        {
            ShaderUniformScalarType.Int32 => (ShaderValue)0,
            ShaderUniformScalarType.UInt32 => (ShaderValue)0u,
            ShaderUniformScalarType.Bool32 => (ShaderValue)false,
            _ => member.ComponentCount switch
            {
                2 => (ShaderValue)Vector2.Zero,
                3 => (ShaderValue)Vector3.Zero,
                4 => (ShaderValue)Vector4.Zero,
                _ => (ShaderValue)0f,
            },
        };
    }

    /// <summary>
    /// Keeps an asset's existing value when the reflected member accepts it, mirroring
    /// the compiler's marshal rules (<see cref="MaterialCompiler"/>): float members take
    /// float/int/uint values by leading components (an authored <c>"#RRGGBB"</c> color
    /// keeps its rgb on a float3 member), int/uint members take integer values of the
    /// same element count. Anything else falls back to the member's default.
    /// </summary>
    private static ShaderValue CoerceOrDefault(ShaderUniformMember member, ShaderValue existing)
    {
        // Exact match keeps the value untouched.
        if (existing.Kind == (ShaderValueKind)member.ScalarType
            && existing.ComponentCount == member.ComponentCount
            && existing.ElementCount == (int)member.ElementCount)
        {
            return existing;
        }

        if (member.ScalarType == ShaderUniformScalarType.Float32
            && existing.Kind is ShaderValueKind.Float32 or ShaderValueKind.Int32 or ShaderValueKind.UInt32)
        {
            int components = member.ComponentCount;
            int elements = (int)member.ElementCount;
            if (components == 16)
            {
                // Matrix members admit exactly a matrix.
                return existing.ComponentCount == 16 && existing.ElementCount == 1 ? existing : DefaultValue(member);
            }
            ReadOnlySpan<float> flat = existing.Kind == ShaderValueKind.Float32
                ? existing.AsFloatList()
                : [existing.GetInt()];
            if (flat.Length == components * elements)
            {
                return existing; // exact flat fit (element-shaped or flat arrays)
            }
            if (elements == 1)
            {
                // Plain member: leading components land, the rest read zero.
                float[] image = new float[components];
                for (int i = 0; i < Math.Min(flat.Length, components); i++)
                {
                    image[i] = flat[i];
                }
                return ShaderValue.Floats(image, components);
            }
            return DefaultValue(member);
        }

        if (member.ScalarType is ShaderUniformScalarType.Int32 or ShaderUniformScalarType.UInt32
            && existing.Kind is ShaderValueKind.Int32 or ShaderValueKind.UInt32
            && existing.ElementCount == (int)member.ElementCount)
        {
            return existing;
        }

        return DefaultValue(member);
    }

    /// <summary>A member's type spelled the slang way (e.g. <c>float3</c>, <c>int[4]</c>).</summary>
    private static string Describe(ShaderUniformMember member)
    {
        string type = member.ScalarType switch
        {
            ShaderUniformScalarType.Int32 => "int",
            ShaderUniformScalarType.UInt32 => "uint",
            ShaderUniformScalarType.Bool32 => "bool",
            _ => member.ComponentCount == 16 ? "matrix"
                : member.ComponentCount > 1 ? $"float{member.ComponentCount}" : "float",
        };
        return member.ElementCount > 1 ? $"{type}[{member.ElementCount}]" : type;
    }

    /// <inheritdoc/>
    public override void Save()
    {
        if (IsReadOnly)
        {
            return;
        }

        // Rebuild the texture table, validating every path first.
        var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        foreach (TextureSlotRow row in _textureSlots)
        {
            string slot = row.Slot.Trim();
            string path = row.Path.Trim();
            if (slot.Length == 0 || path.Length == 0)
            {
                continue;
            }
            try
            {
                textures[slot] = Context.AssetSystem.Load<Texture2D>(path);
            }
            catch (Exception e)
            {
                _saveError = $"Texture '{path}' (slot '{slot}') failed to load: {e.Message}";
                return;
            }
        }

        var parameters = new Dictionary<string, ShaderValue>(StringComparer.Ordinal);
        foreach (ParameterRow row in _parameters)
        {
            string name = row.Name.Trim();
            if (name.Length > 0)
            {
                parameters[name] = row.Value;
            }
        }

        _material.Name = _name.Trim();
        _material.Version ??= MaterialAsset.FormatVersion;
        _material.Textures = textures;
        _material.Parameters = parameters;

        try
        {
            File.WriteAllText(_absolutePath, JsonSerializer.Serialize(_material, _jsonOptions));
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
        DrawToolbar();
        ImGui.Separator();

        float paramsWidth = 380f;
        if (ImGui.BeginChild("##preview", new Vector2(-paramsWidth, -1)))
        {
            DrawPreview();
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

        if (_saveError.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), _saveError);
        }
    }

    private void DrawPreview()
    {
        ImGui.SeparatorText("Textures");

        if (_textureSlots.Count == 0)
        {
            ImGui.TextDisabled("No texture slots bound.");
            return;
        }

        float available = ImGui.GetContentRegionAvail().X;
        foreach (TextureSlotRow row in _textureSlots)
        {
            if (row.Texture is { IsDisposed: false } texture)
            {
                float aspect = texture.Height == 0 ? 1f : (float)texture.Width / texture.Height;
                float width = MathF.Min(available, 256f);
                ImGui.Image(texture, new Vector2(width, width / aspect));
                ImGui.TextUnformatted($"{row.Slot}: {row.Path} ({texture.Width}x{texture.Height})");
            }
            else
            {
                ImGui.TextDisabled($"{row.Slot}: (unresolved)");
            }
            ImGui.Spacing();
        }
    }

    private void DrawParams()
    {
        ImGui.SeparatorText("Material");

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputText("Name", ref _name, 128))
        {
            MarkDirty();
        }

        ImGui.BeginDisabled(true);
        string surface = _material.Surface?.Name ?? "(pipeline default)";
        ImGui.InputText("Surface", ref surface, 256);
        ImGui.EndDisabled();
        if (_material.Surface == null)
        {
            ImGui.TextDisabled("No surface named — slots and parameters are free-form.");
        }

        DrawTextureSlots();
        DrawParameters();
    }

    private void DrawTextureSlots()
    {
        bool reflected = _material.Surface != null;
        ImGui.SeparatorText("Texture Slots");
        if (reflected && _textureSlots.Count == 0)
        {
            ImGui.TextDisabled("The surface declares no texture slots.");
        }

        int removeIndex = -1;
        bool orphanHeaderShown = false;
        for (int i = 0; i < _textureSlots.Count; i++)
        {
            TextureSlotRow row = _textureSlots[i];
            if (reflected && !row.IsReflected && !orphanHeaderShown)
            {
                ImGui.SeparatorText("Not In Surface");
                orphanHeaderShown = true;
            }

            ImGui.PushID(i);

            bool changed = false;
            ImGui.SetNextItemWidth(110f);
            if (row.IsReflected)
            {
                ImGui.BeginDisabled(true);
                string slot = row.Slot;
                ImGui.InputText("##slot", ref slot, 64);
                ImGui.EndDisabled();
            }
            else
            {
                changed = ImGui.InputText("##slot", ref row.Slot, 64);
            }
            ImGui.SameLine();

            ImGui.SetNextItemWidth(row.IsReflected ? -120f : -90f);
            changed |= row.PathPicker.Draw(Context, "##path", ref row.Path, typeof(Texture2D));

            if (!row.IsReflected)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("X"))
                {
                    removeIndex = i;
                }
            }

            // Validity / fallback hint after the row.
            string path = row.Path.Trim();
            if (path.Length > 0 && !Context.AssetSystem.IsFileExist(path))
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(missing)");
            }
            else if (row.IsReflected && path.Length == 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(fallback)");
            }

            if (changed)
            {
                row.RefreshTexture(Context);
                MarkDirty();
            }
            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            _textureSlots.RemoveAt(removeIndex);
            MarkDirty();
        }

        if (!reflected && ImGui.SmallButton("Add Slot"))
        {
            _textureSlots.Add(new TextureSlotRow("slot", null));
            MarkDirty();
        }
    }

    private void DrawParameters()
    {
        bool reflected = _material.Surface != null;

        int removeIndex = -1;
        string? lastHeader = null;
        for (int i = 0; i < _parameters.Count; i++)
        {
            ParameterRow row = _parameters[i];
            string header = row.IsReflected
                ? $"Parameters ({row.Block})"
                : reflected ? "Not In Surface" : $"Parameters ({_parameters.Count})";
            if (header != lastHeader)
            {
                ImGui.SeparatorText(header);
                lastHeader = header;
            }

            ImGui.PushID(i);

            ImGui.SetNextItemWidth(120f);
            ImGui.BeginDisabled(true); // renaming keys is done via add/remove
            ImGui.InputText("##name", ref row.Name, 128);
            ImGui.EndDisabled();
            if (row.IsReflected && row.TypeHint != null && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(row.TypeHint);
            }
            ImGui.SameLine();

            bool changed = DrawValueEditor(row);
            if (!row.IsReflected)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("X"))
                {
                    removeIndex = i;
                }
            }

            if (changed)
            {
                MarkDirty();
            }
            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            _parameters.RemoveAt(removeIndex);
            MarkDirty();
        }

        // Free-form materials (no surface) add parameters by hand; reflected
        // materials get one row per [MaterialParams] member automatically.
        if (reflected)
        {
            return;
        }

        // New parameter row.
        ImGui.SetNextItemWidth(120f);
        ImGui.InputText("##newname", ref _newParameterName, 128);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        string[] kinds = { "float", "float2", "float3", "float4", "int", "uint", "bool" };
        if (ImGui.BeginCombo("##newkind", kinds[_newParameterKind]))
        {
            for (int i = 0; i < kinds.Length; i++)
            {
                if (ImGui.Selectable(kinds[i], i == _newParameterKind))
                {
                    _newParameterKind = i;
                }
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Add") && _newParameterName.Trim().Length > 0)
        {
            ShaderValue value = _newParameterKind switch
            {
                1 => ShaderValue.Floats([0f, 0f], 2),
                2 => ShaderValue.Floats([0f, 0f, 0f], 3),
                3 => ShaderValue.Floats([0f, 0f, 0f, 0f], 4),
                4 => (ShaderValue)0,
                5 => (ShaderValue)0u,
                6 => (ShaderValue)false,
                _ => (ShaderValue)0f,
            };
            _parameters.Add(new ParameterRow(_newParameterName.Trim(), value));
            _newParameterName = string.Empty;
            MarkDirty();
        }
    }

    /// <summary>Draws the value editor for one parameter row (arrays/matrices are read-only).</summary>
    private static bool DrawValueEditor(ParameterRow row)
    {
        ShaderValue value = row.Value;
        if (value.ElementCount > 1 || value.ComponentCount == 16)
        {
            ImGui.TextDisabled($"{value} (not editable yet)");
            return false;
        }

        switch (value.Kind)
        {
            case ShaderValueKind.Float32:
            {
                switch (value.ComponentCount)
                {
                    case 1:
                    {
                        float v = value.GetFloats()[0];
                        if (ImGui.DragFloat("##value", ref v, 0.01f))
                        {
                            row.Value = ShaderValue.Floats([v], 1);
                            return true;
                        }
                        return false;
                    }
                    case 2:
                    {
                        Vector2 v = new(value.GetFloats()[0], value.GetFloats()[1]);
                        if (ImGui.DragFloat2("##value", ref v, 0.01f))
                        {
                            row.Value = ShaderValue.Floats([v.X, v.Y], 2);
                            return true;
                        }
                        return false;
                    }
                    case 3:
                    {
                        Vector3 v = new(value.GetFloats()[0], value.GetFloats()[1], value.GetFloats()[2]);
                        if (ImGui.DragFloat3("##value", ref v, 0.01f))
                        {
                            row.Value = ShaderValue.Floats([v.X, v.Y, v.Z], 3);
                            return true;
                        }
                        return false;
                    }
                    default:
                    {
                        Vector4 v = new(value.GetFloats()[0], value.GetFloats()[1], value.GetFloats()[2], value.GetFloats()[3]);
                        if (ImGui.DragFloat4("##value", ref v, 0.01f))
                        {
                            row.Value = ShaderValue.Floats([v.X, v.Y, v.Z, v.W], 4);
                            return true;
                        }
                        return false;
                    }
                }
            }
            case ShaderValueKind.Int32:
            {
                int v = value.GetInt();
                if (ImGui.DragInt("##value", ref v))
                {
                    row.Value = v;
                    return true;
                }
                return false;
            }
            case ShaderValueKind.UInt32:
            {
                int v = value.GetInt();
                if (ImGui.DragInt("##value", ref v, 0.1f, 0))
                {
                    row.Value = (ShaderValue)(uint)Math.Max(v, 0);
                    return true;
                }
                return false;
            }
            case ShaderValueKind.Bool32:
            {
                bool v = value.GetInt() != 0;
                if (ImGui.Checkbox("##value", ref v))
                {
                    row.Value = v;
                    return true;
                }
                return false;
            }
            default:
                ImGui.TextDisabled(value.ToString());
                return false;
        }
    }

    /// <summary>One editable texture slot row; the resolved texture is display-only.</summary>
    private sealed class TextureSlotRow
    {
        public string Slot;
        public string Path;
        public Texture2D? Texture;

        /// <summary>True when the row comes from surface reflection (fixed name, no delete).</summary>
        public bool IsReflected;

        /// <summary>Pickers must be one instance per field so each keeps its own popup state.</summary>
        public readonly AssetPicker PathPicker = new();

        public TextureSlotRow(string slot, Texture2D? texture)
        {
            Slot = slot;
            Path = texture?.Name ?? string.Empty;
            Texture = texture;
        }

        public void RefreshTexture(EditorContext context)
        {
            string path = Path.Trim();
            if (path.Length == 0 || !context.AssetSystem.IsFileExist(path))
            {
                Texture = null;
                return;
            }
            try
            {
                Texture = context.AssetSystem.Load<Texture2D>(path);
            }
            catch
            {
                Texture = null;
            }
        }
    }

    /// <summary>One editable parameter row.</summary>
    private sealed class ParameterRow
    {
        public string Name;
        public ShaderValue Value;

        /// <summary>True when the row comes from surface reflection (fixed name/type, no delete).</summary>
        public bool IsReflected;

        /// <summary>The owning <c>[MaterialParams]</c> block name (reflected rows only).</summary>
        public string? Block;

        /// <summary>The reflected slang type spelling, shown as a tooltip (reflected rows only).</summary>
        public string? TypeHint;

        public ParameterRow(string name, ShaderValue value)
        {
            Name = name;
            Value = value;
        }
    }
}
