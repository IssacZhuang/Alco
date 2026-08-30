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
/// parameter table. Edits happen on a detached copy deserialized from the file, so
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
        foreach (KeyValuePair<string, Texture2D> pair in _material.Textures)
        {
            _textureSlots.Add(new TextureSlotRow(pair.Key, pair.Value));
        }
        foreach (KeyValuePair<string, ShaderValue> pair in _material.Parameters)
        {
            _parameters.Add(new ParameterRow(pair.Key, pair.Value));
        }
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

        DrawTextureSlots();
        DrawParameters();
    }

    private void DrawTextureSlots()
    {
        ImGui.SeparatorText("Texture Slots");

        for (int i = _textureSlots.Count - 1; i >= 0; i--)
        {
            TextureSlotRow row = _textureSlots[i];
            ImGui.PushID(i);

            ImGui.SetNextItemWidth(100f);
            bool changed = ImGui.InputText("##slot", ref row.Slot, 64);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-90f);
            changed |= row.PathPicker.Draw(Context, "##path", ref row.Path, typeof(Texture2D));
            ImGui.SameLine();
            if (ImGui.SmallButton("X"))
            {
                _textureSlots.RemoveAt(i);
                changed = true;
            }

            // Validity hint under the row.
            string path = row.Path.Trim();
            if (path.Length > 0 && !Context.AssetSystem.IsFileExist(path))
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(missing)");
            }

            if (changed)
            {
                row.RefreshTexture(Context);
                MarkDirty();
            }
            ImGui.PopID();
        }

        if (ImGui.SmallButton("Add Slot"))
        {
            _textureSlots.Add(new TextureSlotRow("slot", null));
            MarkDirty();
        }
    }

    private void DrawParameters()
    {
        ImGui.SeparatorText($"Parameters ({_parameters.Count})");

        for (int i = _parameters.Count - 1; i >= 0; i--)
        {
            ParameterRow row = _parameters[i];
            ImGui.PushID(i);

            ImGui.SetNextItemWidth(120f);
            ImGui.BeginDisabled(true); // renaming keys is done via add/remove
            ImGui.InputText("##name", ref row.Name, 128);
            ImGui.EndDisabled();
            ImGui.SameLine();

            bool changed = DrawValueEditor(row);
            ImGui.SameLine();
            if (ImGui.SmallButton("X"))
            {
                _parameters.RemoveAt(i);
                changed = true;
            }

            if (changed)
            {
                MarkDirty();
            }
            ImGui.PopID();
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

        public ParameterRow(string name, ShaderValue value)
        {
            Name = name;
            Value = value;
        }
    }
}
