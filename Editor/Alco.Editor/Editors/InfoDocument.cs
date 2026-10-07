using System.Numerics;
using Alco.ImGUI;
using Alco.IO;

namespace Alco.Editor;

/// <summary>
/// Fallback document for asset types without a dedicated editor: shows the file's
/// path, its source (owned root or referenced entry) and size, plus a read-only text
/// preview for text-like formats.
/// </summary>
public sealed class InfoDocument : AssetDocument
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        FileExt.Text, FileExt.TextCSV, FileExt.TextJSON, FileExt.TextJSONC, FileExt.TextXML,
        FileExt.TextYAML, FileExt.TextYML, FileExt.TextMD, FileExt.TextINI, FileExt.TextTOML,
        FileExt.ShaderSlang, FileExt.ShaderGLSL, FileExt.ShaderWGSL, FileExt.Meta, ".alco",
    };

    private const int MaxPreviewBytes = 256 * 1024;

    private readonly string _absolutePath = string.Empty;
    private readonly long _fileSize;
    private string? _text;

    /// <summary>Creates the info document for the given asset-system-relative path.</summary>
    public InfoDocument(EditorContext context, string assetPath) : base(context, assetPath)
    {
        if (Context.Project.TryGetOwnedAbsolutePath(assetPath, out string? owned))
        {
            _absolutePath = owned;
        }
        else if (Context.Project.TryGetReferencedAbsolutePath(assetPath, out string? referenced))
        {
            _absolutePath = referenced;
        }

        if (_absolutePath.Length > 0 && File.Exists(_absolutePath))
        {
            _fileSize = new FileInfo(_absolutePath).Length;
            if (TextExtensions.Contains(Path.GetExtension(assetPath)))
            {
                try
                {
                    using FileStream stream = File.OpenRead(_absolutePath);
                    int length = (int)Math.Min(stream.Length, MaxPreviewBytes);
                    byte[] bytes = new byte[length];
                    int read = stream.Read(bytes, 0, length);
                    _text = System.Text.Encoding.UTF8.GetString(bytes, 0, read);
                    if (stream.Length > MaxPreviewBytes)
                    {
                        _text += "\n... (truncated)";
                    }
                }
                catch (Exception e)
                {
                    _text = $"(failed to read: {e.Message})";
                }
            }
        }
    }

    /// <inheritdoc/>
    protected override void DrawContent()
    {
        ImGui.Text($"Path: {AssetPath}");
        ImGui.Text($"Source: {(IsReadOnly ? "referenced (read-only)" : "project")}");
        if (_absolutePath.Length > 0)
        {
            ImGui.Text($"File: {_absolutePath}");
            ImGui.Text($"Size: {_fileSize:N0} bytes");
        }
        else
        {
            ImGui.TextDisabled("Not backed by a mounted file (package or missing entry).");
        }

        if (_text != null)
        {
            ImGui.Separator();
            string text = _text;
            ImGui.InputTextMultiline("##preview", ref text, (uint)text.Length,
                new Vector2(-1, -1), ImGuiInputTextFlags.ReadOnly);
        }
    }
}
