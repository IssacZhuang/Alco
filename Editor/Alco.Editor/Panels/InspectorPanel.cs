using Alco.ImGUI;

namespace Alco.Editor;

/// <summary>Displays editable parameters of the active document independently of its preview.</summary>
public sealed class InspectorPanel
{
    private readonly DocumentManager _documents;

    /// <summary>Creates an inspector that follows the document manager's active asset.</summary>
    public InspectorPanel(DocumentManager documents)
    {
        _documents = documents;
    }

    /// <summary>Draws the active asset's parameters inside the workspace's Inspector tab.</summary>
    public void DrawContent()
    {
        AssetDocument? document = _documents.ActiveDocument;
        if (document == null)
        {
            ImGui.TextDisabled("No asset selected.");
            ImGui.TextWrapped("Open an asset in the Asset Browser to inspect its parameters.");
            return;
        }
        ImGui.TextWrapped(document.AssetPath);
        ImGui.BeginDisabled(document.IsReadOnly || !document.IsDirty);
        if (ImGui.Button("Save"))
        {
            _documents.SaveActive();
        }
        ImGui.EndDisabled();
        if (document.IsDirty)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Unsaved changes");
        }
        ImGui.Separator();
        document.DrawInspector();
    }
}
