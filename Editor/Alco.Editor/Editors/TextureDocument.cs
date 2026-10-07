using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.Graphics;
using Alco.ImGUI;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// Texture asset document: a preview pane plus an editor for the texture's
/// <see cref="Texture2DMeta"/> sidecar (<c>&lt;file&gt;.meta</c>) — filter/address modes,
/// premultiplied alpha, 9-slice padding and sprite rects. The image file itself is
/// never modified. The preview uses its own <see cref="Texture2D"/> instance (not the
/// shared asset cache) so it always reflects the meta currently being edited.
/// </summary>
public sealed class TextureDocument : AssetDocument
{
    /// <summary>
    /// On-disk meta format: PascalCase, case-insensitive read (matches
    /// <c>AssetLoaderMeta</c>/<c>TextureOptionCache</c>), null fields omitted so
    /// directory-cascade inheritance keeps working.
    /// </summary>
    internal static readonly JsonSerializerOptions MetaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new JsonConverterPadding() },
    };

    private readonly string? _absolutePath;
    private readonly byte[] _fileBytes;
    private Texture2D? _previewTexture;
    private float _zoom = 1f;

    private Texture2DMeta _meta;
    private readonly List<SpriteRow> _sprites = new();

    /// <summary>Creates the document; throws when the file cannot be resolved or decoded.</summary>
    public TextureDocument(EditorContext context, string assetPath) : base(context, assetPath)
    {
        if (Context.Project.TryGetOwnedAbsolutePath(assetPath, out string? absolute)
            || Context.Project.TryGetReferencedAbsolutePath(assetPath, out absolute))
        {
            _absolutePath = absolute;
            _fileBytes = File.ReadAllBytes(absolute);
            _meta = LoadMeta(MetaPath);
        }
        else if (Context.AssetSystem.TryLoadRaw(assetPath, out Alco.SafeMemoryHandle data))
        {
            // Engine built-in or packaged asset: no on-disk path to resolve. Read the
            // bytes and meta through the asset system instead; such assets are never
            // owned, so the document stays read-only and Save refuses to write.
            _fileBytes = data.AsSpan().ToArray();
            _meta = Context.AssetSystem.TryLoad(assetPath + FileExt_Meta, out Texture2DMeta? loaded, out _)
                ? loaded
                : new Texture2DMeta();
        }
        else
        {
            throw new FileNotFoundException($"Cannot resolve {assetPath} to a mounted file.");
        }

        foreach (KeyValuePair<string, Texture2DMeta.Rect> pair in _meta.Sprites)
        {
            _sprites.Add(new SpriteRow(pair.Key, pair.Value));
        }

        RebuildPreview();
    }

    // Only reached for project-resolvable files (ctor above; Save is read-only-guarded,
    // and non-read-only implies the owned branch resolved an absolute path).
    private string MetaPath => _absolutePath! + FileExt_Meta;

    private const string FileExt_Meta = ".meta";

    /// <inheritdoc/>
    public override void Save()
    {
        if (IsReadOnly)
        {
            return;
        }

        _meta.Sprites = new Dictionary<string, Texture2DMeta.Rect>();
        foreach (SpriteRow row in _sprites)
        {
            string name = row.Name.Trim();
            if (name.Length > 0)
            {
                _meta.Sprites[name] = new Texture2DMeta.Rect
                {
                    X = row.X,
                    Y = row.Y,
                    Width = row.Width,
                    Height = row.Height,
                };
            }
        }

        string json = JsonSerializer.Serialize(_meta, MetaJsonOptions);
        File.WriteAllText(MetaPath, json);

        // Evict the shared cache entries so the next load re-resolves the saved meta.
        // (Known limitation: AssetLoaderTexture2D's internal directory-option cache does
        // not observe the change; that is an engine-side follow-up.)
        Context.AssetSystem.Unload(AssetPath);
        Context.AssetSystem.Unload(AssetPath + FileExt_Meta);

        IsDirty = false;
        RebuildPreview();
    }

    /// <inheritdoc/>
    protected override void DrawContent()
    {
        DrawToolbar();
        ImGui.Separator();

        float paramsWidth = 320f;
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
        if (ImGui.Button("Apply to Preview"))
        {
            RebuildPreview();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(IsDirty ? "(modified)" : string.Empty);
    }

    private void DrawPreview()
    {
        if (_previewTexture == null || _previewTexture.IsDisposed)
        {
            ImGui.TextDisabled("No preview (decode failed).");
            return;
        }

        ImGui.Text($"{_previewTexture.Width} x {_previewTexture.Height}");
        ImGui.SetNextItemWidth(120f);
        ImGui.SliderFloat("Zoom", ref _zoom, 0.05f, 8f, "%.2fx");

        Vector2 size = new((int)_previewTexture.Width * _zoom, (int)_previewTexture.Height * _zoom);
        if (ImGui.BeginChild("##image", new Vector2(-1, -1), ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar))
        {
            ImGui.Image(_previewTexture, size);
        }
        ImGui.EndChild();
    }

    private void DrawParams()
    {
        ImGui.SeparatorText("Import Options (.meta)");

        FilterMode? filterMode = _meta.FilterMode;
        if (NullableEnumCombo("Filter Mode", ref filterMode))
        {
            _meta.FilterMode = filterMode;
            MarkDirty();
        }

        AddressMode? addressMode = _meta.AddressMode;
        if (NullableEnumCombo("Address Mode", ref addressMode))
        {
            _meta.AddressMode = addressMode;
            MarkDirty();
        }

        bool? premultiplyAlpha = _meta.PremultiplyAlpha;
        if (TriStateCombo("Premultiply Alpha", ref premultiplyAlpha))
        {
            _meta.PremultiplyAlpha = premultiplyAlpha;
            MarkDirty();
        }

        DrawSlicePadding();
        DrawSprites();
    }

    private void DrawSlicePadding()
    {
        bool enabled = _meta.SlicePadding.HasValue;
        if (ImGui.Checkbox("9-Slice Padding", ref enabled))
        {
            _meta.SlicePadding = enabled ? Alco.Padding.Zero : null;
            MarkDirty();
        }

        if (_meta.SlicePadding is { } padding)
        {
            float left = padding.Left, top = padding.Top, right = padding.Right, bottom = padding.Bottom;
            ImGui.Indent();
            if (ImGui.DragFloat("Left", ref left) | ImGui.DragFloat("Top", ref top)
                | ImGui.DragFloat("Right", ref right) | ImGui.DragFloat("Bottom", ref bottom))
            {
                _meta.SlicePadding = new Alco.Padding(left, top, right, bottom);
                MarkDirty();
            }
            ImGui.Unindent();
        }
    }

    private void DrawSprites()
    {
        ImGui.SeparatorText($"Sprites ({_sprites.Count})");

        for (int i = _sprites.Count - 1; i >= 0; i--)
        {
            SpriteRow row = _sprites[i];
            ImGui.PushID(i);

            ImGui.SetNextItemWidth(110f);
            bool changed = ImGui.InputText("##name", ref row.Name, 128);
            ImGui.SameLine();
            changed |= ImGui.DragInt("##x", ref row.X, 0.5f);
            ImGui.SameLine();
            changed |= ImGui.DragInt("##y", ref row.Y, 0.5f);
            ImGui.SameLine();
            changed |= ImGui.DragInt("##w", ref row.Width, 0.5f, 0);
            ImGui.SameLine();
            changed |= ImGui.DragInt("##h", ref row.Height, 0.5f, 0);
            ImGui.SameLine();
            if (ImGui.SmallButton("X"))
            {
                _sprites.RemoveAt(i);
                changed = true;
            }

            if (changed)
            {
                MarkDirty();
            }
            ImGui.PopID();
        }

        if (ImGui.SmallButton("Add Sprite"))
        {
            _sprites.Add(new SpriteRow($"sprite{_sprites.Count}", new Texture2DMeta.Rect()));
            MarkDirty();
        }
    }

    private void RebuildPreview()
    {
        _previewTexture?.Dispose();

        ImageLoadOption option = ImageLoadOption.Default with { Name = AssetPath };
        if (_meta.FilterMode.HasValue)
        {
            option = option with { FilterMode = _meta.FilterMode.Value };
        }
        if (_meta.AddressMode.HasValue)
        {
            option = option with { AddressMode = _meta.AddressMode.Value };
        }
        if (_meta.SlicePadding.HasValue)
        {
            option = option with { SlicePadding = _meta.SlicePadding.Value };
        }
        if (_meta.PremultiplyAlpha.HasValue)
        {
            option = option with { PremultiplyAlpha = _meta.PremultiplyAlpha.Value };
        }

        try
        {
            _previewTexture = Context.RenderingSystem.CreateTexture2DFromFile(_fileBytes, option);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to decode {AssetPath}:", e);
            _previewTexture = null;
        }
    }

    private static Texture2DMeta LoadMeta(string metaPath)
    {
        if (!File.Exists(metaPath))
        {
            return new Texture2DMeta();
        }
        try
        {
            return JsonSerializer.Deserialize<Texture2DMeta>(File.ReadAllText(metaPath), MetaJsonOptions)
                ?? new Texture2DMeta();
        }
        catch (JsonException e)
        {
            Log.Error($"Failed to parse {metaPath}:", e);
            return new Texture2DMeta();
        }
    }

    /// <summary>Nullable enum combo whose first entry ("Inherit") selects null.</summary>
    private static bool NullableEnumCombo<T>(string label, ref T? value) where T : struct, Enum
    {
        bool changed = false;
        if (ImGui.BeginCombo(label, value?.ToString() ?? "Inherit"))
        {
            if (ImGui.Selectable("Inherit (default)", !value.HasValue))
            {
                value = null;
                changed = true;
            }
            foreach (T option in Enum.GetValues<T>())
            {
                if (ImGui.Selectable(option.ToString(), value.HasValue && value.Value.Equals(option)))
                {
                    value = option;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }
        return changed;
    }

    /// <summary>Tri-state combo for a nullable bool: Inherit / False / True.</summary>
    private static bool TriStateCombo(string label, ref bool? value)
    {
        string preview = value?.ToString() ?? "Inherit";
        bool changed = false;
        if (ImGui.BeginCombo(label, preview))
        {
            if (ImGui.Selectable("Inherit (default)", !value.HasValue)) { value = null; changed = true; }
            if (ImGui.Selectable("False", value == false)) { value = false; changed = true; }
            if (ImGui.Selectable("True", value == true)) { value = true; changed = true; }
            ImGui.EndCombo();
        }
        return changed;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        _previewTexture?.Dispose();
        _previewTexture = null;
    }

    /// <summary>One editable sprite row (name + pixel rect).</summary>
    private sealed class SpriteRow
    {
        public string Name;
        public int X, Y, Width, Height;

        public SpriteRow(string name, Texture2DMeta.Rect rect)
        {
            Name = name;
            X = rect.X;
            Y = rect.Y;
            Width = rect.Width;
            Height = rect.Height;
        }
    }
}
