using System.Numerics;
using System.Text.Json;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>Material asset document (<c>.amat</c>) with a texture preview pane and an inspector editing the name, texture slots, shader parameters, and specializations. Edits happen on a detached copy, so the shared asset cache is never mutated.</summary>
public sealed class MaterialDocument : AssetDocument
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _absolutePath = string.Empty;
    private readonly MaterialAsset _material;
    private readonly bool _hasReflectedParams;

    private string _name;
    private readonly List<TextureSlotRow> _textureSlots = new();
    private readonly List<ParameterRow> _parameters = new();
    private readonly List<SpecializationRow> _specializations = new();
    private string _newParameterName = string.Empty;
    private int _newParameterKind;
    private string _newSpecializationName = string.Empty;
    private int _newSpecializationKind;
    private string _saveError = string.Empty;

    /// <summary>Creates the document; throws when the file cannot be resolved or parsed.</summary>
    public MaterialDocument(EditorContext context, string assetPath) : base(context, assetPath)
    {
        _jsonOptions = AssetLoaderMaterialAsset.CreateJsonOptions(Context.AssetSystem);

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

        ShaderTypeLayout? reflectedParams = TryResolveParamsType(_material.Surface);
        if (reflectedParams != null)
        {
            _hasReflectedParams = true;
            BuildReflectedRows(reflectedParams);
        }
        else
        {
            foreach (KeyValuePair<string, Image> pair in _material.Textures)
            {
                _textureSlots.Add(new TextureSlotRow(pair.Key, pair.Value));
            }
            foreach (KeyValuePair<string, ShaderValue> pair in _material.Parameters)
            {
                _parameters.Add(new ParameterRow(pair.Key, pair.Value));
            }
        }

        foreach (KeyValuePair<string, ShaderValue> pair in _material.Specializations)
        {
            _specializations.Add(new SpecializationRow(pair.Key, pair.Value));
        }
    }

    private ShaderTypeLayout? TryResolveParamsType(string? surfaceName)
    {
        if (string.IsNullOrWhiteSpace(surfaceName))
        {
            return null;
        }

        try
        {
            ShaderModule module = Context.RenderingSystem.Shaders.Compiler.LoadModule(surfaceName);
            IReadOnlyList<ShaderTypeLayout> types = module.GetTypes();

            // Standalone passes wrap material parameters inside [GPURoot] struct's Material field.
            for (int i = 0; i < types.Count; i++)
            {
                ShaderTypeLayout type = types[i];
                if (type.Kind != ShaderTypeKind.Struct || !HasAttribute(type, "GPURoot"))
                {
                    continue;
                }

                ShaderFieldLayout? matField = type.FindField("Material");
                if (matField != null && matField.Type.Kind == ShaderTypeKind.Struct)
                {
                    return matField.Type;
                }
            }

            // Composed surface modules declare a <X>Params struct (typedef Params).
            ShaderTypeLayout? candidate = null;
            for (int i = 0; i < types.Count; i++)
            {
                ShaderTypeLayout type = types[i];
                if (type.Kind != ShaderTypeKind.Struct || string.IsNullOrEmpty(type.TypeName))
                {
                    continue;
                }

                string name = type.TypeName;
                if (name.Contains("Root", StringComparison.Ordinal)
                    || name.Contains("Pass", StringComparison.Ordinal)
                    || name.Contains("Input", StringComparison.Ordinal)
                    || name.Contains("Desc", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(name, "Params", StringComparison.Ordinal)
                    || string.Equals(name, "MaterialParams", StringComparison.Ordinal)
                    || name.EndsWith("Params", StringComparison.Ordinal))
                {
                    candidate = type;
                }
            }

            return candidate;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasAttribute(ShaderTypeLayout type, string attributeName)
    {
        for (int i = 0; i < type.Attributes.Count; i++)
        {
            if (string.Equals(type.Attributes[i].Name, attributeName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsPaddingName(string name)
    {
        return name.StartsWith("_pad", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("pad", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("padding", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the slot and parameter rows from the surface's reflected params struct,
    /// followed by any leftover asset bindings as removable orphan rows.
    /// </summary>
    private void BuildReflectedRows(ShaderTypeLayout paramsType)
    {
        var knownSlots = new HashSet<string>(StringComparer.Ordinal);
        var knownParams = new HashSet<string>(StringComparer.Ordinal);
        CollectReflectedFields(paramsType, paramsType.TypeName ?? "Params", string.Empty, knownSlots, knownParams);

        foreach (KeyValuePair<string, Image> pair in _material.Textures)
        {
            if (!knownSlots.Contains(pair.Key))
            {
                _textureSlots.Add(new TextureSlotRow(pair.Key, pair.Value));
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

    private void CollectReflectedFields(
        ShaderTypeLayout structType,
        string blockName,
        string prefix,
        HashSet<string> knownSlots,
        HashSet<string> knownParams)
    {
        for (int i = 0; i < structType.Fields.Count; i++)
        {
            ShaderFieldLayout field = structType.Fields[i];
            string fullName = prefix.Length == 0 ? field.Name : string.Concat(prefix, ".", field.Name);

            if (field.Semantic == ShaderFieldSemantic.TextureId)
            {
                knownSlots.Add(fullName);
                _material.Textures.TryGetValue(fullName, out Image? texture);
                _textureSlots.Add(new TextureSlotRow(fullName, texture) { IsReflected = true });
                continue;
            }

            if (field.Semantic != ShaderFieldSemantic.Data)
            {
                continue;
            }

            if (field.Type.Kind == ShaderTypeKind.Struct)
            {
                CollectReflectedFields(field.Type, blockName, fullName, knownSlots, knownParams);
                continue;
            }

            bool hasExisting = _material.Parameters.TryGetValue(fullName, out ShaderValue existing);
            if (!hasExisting && IsPaddingName(field.Name))
            {
                continue;
            }

            knownParams.Add(fullName);
            if (!CanEditReflectedType(field.Type))
            {
                if (hasExisting)
                {
                    _parameters.Add(new ParameterRow(fullName, existing)
                    {
                        IsReflected = true,
                        Block = blockName,
                        TypeHint = Describe(field.Type),
                    });
                }
                continue;
            }

            ShaderValue value = hasExisting ? CoerceOrDefault(field.Type, existing) : DefaultValue(field.Type);
            _parameters.Add(new ParameterRow(fullName, value)
            {
                IsReflected = true,
                Block = blockName,
                TypeHint = Describe(field.Type),
            });
        }
    }

    private static bool CanEditReflectedType(ShaderTypeLayout type)
    {
        if (type.Kind == ShaderTypeKind.Array)
        {
            ShaderTypeLayout? elem = type.ElementType;
            return elem != null && elem.Kind == ShaderTypeKind.Scalar && type.ElementCount > 0;
        }
        return type.Kind is ShaderTypeKind.Scalar or ShaderTypeKind.Enum or ShaderTypeKind.Vector or ShaderTypeKind.Matrix;
    }

    /// <summary>The default value of a reflected field type (zero / identity).</summary>
    private static ShaderValue DefaultValue(ShaderTypeLayout type)
    {
        if (type.Kind == ShaderTypeKind.Matrix)
        {
            return Matrix4x4.Identity;
        }
        if (type.Kind == ShaderTypeKind.Array && type.ElementType is { } elem && type.ElementCount > 0)
        {
            int count = (int)type.ElementCount;
            return elem.ScalarType switch
            {
                ShaderScalarType.Int8 or ShaderScalarType.Int16 or ShaderScalarType.Int32 or ShaderScalarType.Int64
                    or ShaderScalarType.UInt8 or ShaderScalarType.UInt16 or ShaderScalarType.UInt32 or ShaderScalarType.UInt64
                    or ShaderScalarType.Bool => ShaderValue.Ints(new int[count]),
                _ => ShaderValue.Floats(new float[count]),
            };
        }
        if (type.Kind == ShaderTypeKind.Vector)
        {
            return type.VectorLength switch
            {
                2 => Vector2.Zero,
                3 => Vector3.Zero,
                4 => Vector4.Zero,
                _ => ShaderValue.Floats(new float[Math.Max(1, (int)type.VectorLength)], Math.Max(1, (int)type.VectorLength)),
            };
        }
        return type.ScalarType switch
        {
            ShaderScalarType.Int8 or ShaderScalarType.Int16 or ShaderScalarType.Int32 or ShaderScalarType.Int64 => 0,
            ShaderScalarType.UInt8 or ShaderScalarType.UInt16 or ShaderScalarType.UInt32 or ShaderScalarType.UInt64 => 0u,
            ShaderScalarType.Bool => false,
            _ => 0f,
        };
    }

    /// <summary>Keeps an asset's existing value when the reflected field accepts it and otherwise falls back to the field's default.</summary>
    private static ShaderValue CoerceOrDefault(ShaderTypeLayout type, ShaderValue existing)
    {
        if (type.Kind == ShaderTypeKind.Matrix)
        {
            return existing.ComponentCount == 16 && existing.ElementCount == 1 ? existing : DefaultValue(type);
        }

        if (type.Kind == ShaderTypeKind.Vector)
        {
            int len = type.VectorLength;
            if (existing.Kind == ShaderValueKind.Float32 && existing.ComponentCount == len && existing.ElementCount == 1)
            {
                return existing;
            }
            if (existing.Kind is ShaderValueKind.Float32 or ShaderValueKind.Int32 or ShaderValueKind.UInt32)
            {
                ReadOnlySpan<float> flat = existing.Kind == ShaderValueKind.Float32
                    ? existing.GetFloats()
                    : [existing.GetFloat()];
                float[] padded = new float[len];
                for (int i = 0; i < Math.Min(flat.Length, len); i++)
                {
                    padded[i] = flat[i];
                }
                return ShaderValue.Floats(padded, len);
            }
            return DefaultValue(type);
        }

        if (type.Kind == ShaderTypeKind.Array)
        {
            return existing.ElementCount * existing.ComponentCount == (int)type.ElementCount
                ? existing
                : DefaultValue(type);
        }

        if (type.Kind is ShaderTypeKind.Scalar or ShaderTypeKind.Enum && existing.ElementCount == 1 && existing.ComponentCount == 1)
        {
            return existing;
        }

        return DefaultValue(type);
    }

    /// <summary>A field's type spelled the slang way (e.g. <c>float3</c>, <c>int[4]</c>).</summary>
    private static string Describe(ShaderTypeLayout type)
    {
        if (type.Kind == ShaderTypeKind.Array && type.ElementType is { } elem)
        {
            return $"{Describe(elem)}[{type.ElementCount}]";
        }
        if (type.Kind == ShaderTypeKind.Matrix)
        {
            return $"float{type.MatrixRows}x{type.MatrixColumns}";
        }
        string scalar = type.ScalarType switch
        {
            ShaderScalarType.Int8 or ShaderScalarType.Int16 or ShaderScalarType.Int32 or ShaderScalarType.Int64 => "int",
            ShaderScalarType.UInt8 or ShaderScalarType.UInt16 or ShaderScalarType.UInt32 or ShaderScalarType.UInt64 => "uint",
            ShaderScalarType.Bool => "bool",
            _ => "float",
        };
        return type.Kind == ShaderTypeKind.Vector ? $"{scalar}{type.VectorLength}" : scalar;
    }

    /// <inheritdoc/>
    public override void Save()
    {
        if (IsReadOnly)
        {
            return;
        }

        // Rebuild the texture table, validating every path first.
        var textures = new Dictionary<string, Image>(StringComparer.Ordinal);
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
                textures[slot] = Context.AssetSystem.Load<Image>(path);
            }
            catch (Exception e)
            {
                _saveError = $"Image '{path}' (slot '{slot}') failed to load: {e.Message}";
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

        var specializations = new Dictionary<string, ShaderValue>(StringComparer.Ordinal);
        foreach (SpecializationRow row in _specializations)
        {
            string name = row.Name.Trim();
            if (name.Length > 0)
            {
                specializations[name] = row.Value;
            }
        }

        _material.Name = _name.Trim();
        _material.Version ??= MaterialAsset.FormatVersion;
        _material.Textures = textures;
        _material.Parameters = parameters;
        _material.Specializations = specializations;

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

        if (ImGui.BeginChild("##preview", new Vector2(0, -1)))
        {
            DrawPreview();
        }
        ImGui.EndChild();
    }

    /// <inheritdoc/>
    protected override void DrawInspectorContent()
    {
        DrawSaveError();
        DrawParams();
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
            DrawSaveError();
        }
    }

    private void DrawSaveError()
    {
        if (_saveError.Length > 0)
        {
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
            if (row.Texture is { IsDisposed: false } image && !image.Texture.IsDisposed)
            {
                float aspect = image.Height == 0 ? 1f : (float)image.Width / image.Height;
                float width = MathF.Min(available, 256f);
                ImGui.Image(image.Texture, new Vector2(width, width / aspect));
                ImGui.TextUnformatted($"{row.Slot}: {row.Path} ({image.Width}x{image.Height})");
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

        ImGui.SetNextItemWidth(-ImGui.CalcTextSize("Name").X - ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGui.InputText("Name", ref _name, 128))
        {
            MarkDirty();
        }

        ImGui.BeginDisabled(true);
        string surface = _material.Surface ?? "(pipeline default)";
        ImGui.InputText("Surface", ref surface, 256);
        ImGui.EndDisabled();
        if (!_hasReflectedParams)
        {
            ImGui.TextDisabled(_material.Surface == null
                ? "No surface named — slots and parameters are free-form."
                : "Surface params not reflected — slots and parameters are free-form.");
        }

        DrawTextureSlots();
        DrawParameters();
        DrawSpecializations();
    }

    private void DrawTextureSlots()
    {
        ImGui.SeparatorText("Texture Slots");
        if (_hasReflectedParams && _textureSlots.Count == 0)
        {
            ImGui.TextDisabled("The surface declares no texture slots.");
        }

        int removeIndex = -1;
        bool orphanHeaderShown = false;
        for (int i = 0; i < _textureSlots.Count; i++)
        {
            TextureSlotRow row = _textureSlots[i];
            if (_hasReflectedParams && !row.IsReflected && !orphanHeaderShown)
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
            changed |= row.PathPicker.Draw(Context, "##path", ref row.Path, typeof(Image));

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

        if (!_hasReflectedParams && ImGui.SmallButton("Add Slot"))
        {
            _textureSlots.Add(new TextureSlotRow("slot", null));
            MarkDirty();
        }
    }

    private void DrawParameters()
    {
        int removeIndex = -1;
        string? lastHeader = null;
        for (int i = 0; i < _parameters.Count; i++)
        {
            ParameterRow row = _parameters[i];
            string header = row.IsReflected
                ? $"Parameters ({row.Block})"
                : _hasReflectedParams ? "Not In Surface" : $"Parameters ({_parameters.Count})";
            if (header != lastHeader)
            {
                ImGui.SeparatorText(header);
                lastHeader = header;
            }

            ImGui.PushID(i);

            ImGui.SetNextItemWidth(120f);
            ImGui.BeginDisabled(true);
            ImGui.InputText("##name", ref row.Name, 128);
            ImGui.EndDisabled();
            if (row.IsReflected && row.TypeHint != null && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(row.TypeHint);
            }
            ImGui.SameLine();

            ShaderValue editedValue = row.Value;
            bool changed = DrawValueEditor(ref editedValue);
            if (changed)
            {
                row.Value = editedValue;
            }

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

        if (_hasReflectedParams)
        {
            return;
        }

        if (lastHeader == null)
        {
            ImGui.SeparatorText("Parameters (0)");
        }

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
                4 => 0,
                5 => 0u,
                6 => false,
                _ => 0f,
            };
            _parameters.Add(new ParameterRow(_newParameterName.Trim(), value));
            _newParameterName = string.Empty;
            MarkDirty();
        }
    }

    private void DrawSpecializations()
    {
        ImGui.SeparatorText($"Specializations ({_specializations.Count})");
        int removeIndex = -1;

        for (int i = 0; i < _specializations.Count; i++)
        {
            SpecializationRow row = _specializations[i];
            ImGui.PushID("spec" + i);
            ImGui.SetNextItemWidth(120f);
            ImGui.BeginDisabled(true);
            ImGui.InputText("##specname", ref row.Name, 128);
            ImGui.EndDisabled();
            ImGui.SameLine();

            ShaderValue edited = row.Value;
            if (DrawValueEditor(ref edited))
            {
                row.Value = edited;
                MarkDirty();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("X"))
            {
                removeIndex = i;
            }
            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            _specializations.RemoveAt(removeIndex);
            MarkDirty();
        }

        ImGui.SetNextItemWidth(120f);
        ImGui.InputText("##newspecname", ref _newSpecializationName, 128);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90f);
        string[] kinds = { "bool", "int", "uint", "float" };
        if (ImGui.BeginCombo("##newspeckind", kinds[_newSpecializationKind]))
        {
            for (int i = 0; i < kinds.Length; i++)
            {
                if (ImGui.Selectable(kinds[i], i == _newSpecializationKind))
                {
                    _newSpecializationKind = i;
                }
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Add##spec") && _newSpecializationName.Trim().Length > 0)
        {
            ShaderValue value = _newSpecializationKind switch
            {
                1 => 0,
                2 => 0u,
                3 => 0f,
                _ => false,
            };
            _specializations.Add(new SpecializationRow(_newSpecializationName.Trim(), value));
            _newSpecializationName = string.Empty;
            MarkDirty();
        }
    }

    /// <summary>Draws the value editor for one shader value (arrays/matrices are read-only).</summary>
    private static bool DrawValueEditor(ref ShaderValue value)
    {
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
                            value = ShaderValue.Floats([v], 1);
                            return true;
                        }
                        return false;
                    }
                    case 2:
                    {
                        Vector2 v = new(value.GetFloats()[0], value.GetFloats()[1]);
                        if (ImGui.DragFloat2("##value", ref v, 0.01f))
                        {
                            value = ShaderValue.Floats([v.X, v.Y], 2);
                            return true;
                        }
                        return false;
                    }
                    case 3:
                    {
                        Vector3 v = new(value.GetFloats()[0], value.GetFloats()[1], value.GetFloats()[2]);
                        if (ImGui.DragFloat3("##value", ref v, 0.01f))
                        {
                            value = ShaderValue.Floats([v.X, v.Y, v.Z], 3);
                            return true;
                        }
                        return false;
                    }
                    default:
                    {
                        Vector4 v = new(value.GetFloats()[0], value.GetFloats()[1], value.GetFloats()[2], value.GetFloats()[3]);
                        if (ImGui.DragFloat4("##value", ref v, 0.01f))
                        {
                            value = ShaderValue.Floats([v.X, v.Y, v.Z, v.W], 4);
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
                    value = v;
                    return true;
                }
                return false;
            }
            case ShaderValueKind.UInt32:
            {
                int v = (int)value.GetUInt();
                if (ImGui.DragInt("##value", ref v, 0.1f, 0))
                {
                    value = (uint)Math.Max(v, 0);
                    return true;
                }
                return false;
            }
            case ShaderValueKind.Bool32:
            {
                bool v = value.GetInt() != 0;
                if (ImGui.Checkbox("##value", ref v))
                {
                    value = v;
                    return true;
                }
                return false;
            }
            default:
                ImGui.TextDisabled(value.ToString() ?? string.Empty);
                return false;
        }
    }

    /// <summary>One editable texture slot row; the resolved image is display-only.</summary>
    private sealed class TextureSlotRow
    {
        public string Slot;
        public string Path;
        public Image? Texture;

        /// <summary>True when the row comes from surface reflection (fixed name, no delete).</summary>
        public bool IsReflected;

        /// <summary>Pickers must be one instance per field so each keeps its own popup state.</summary>
        public readonly AssetPicker PathPicker = new();

        /// <summary>Creates a texture-slot row with an optional resolved preview image.</summary>
        public TextureSlotRow(string slot, Image? texture)
        {
            Slot = slot;
            Path = texture?.Name ?? string.Empty;
            Texture = texture;
        }

        /// <summary>Refreshes the resolved preview image from the current asset path.</summary>
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
                Texture = context.AssetSystem.Load<Image>(path);
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

        /// <summary>The owning params struct name (reflected rows only).</summary>
        public string? Block;

        /// <summary>The reflected slang type spelling, shown as a tooltip (reflected rows only).</summary>
        public string? TypeHint;

        /// <summary>Creates an editable material parameter row.</summary>
        public ParameterRow(string name, ShaderValue value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>One editable shader specialization argument row.</summary>
    private sealed class SpecializationRow(string name, ShaderValue value)
    {
        public string Name = name;
        public ShaderValue Value = value;
    }
}
