using System.Text;
using Alco.ImGUI;

namespace Alco.Editor;

/// <summary>
/// The editor-wide drag-and-drop payloads. Phase 1 carries one payload type: an asset
/// path dragged out of the asset browser (<see cref="SetAsset"/>) onto widgets that
/// reference assets (<see cref="TryAcceptAsset"/>), e.g. the <see cref="AssetPicker"/>
/// input field or a <see cref="PreviewViewport"/> image.
/// </summary>
public static class EditorDragDrop
{
    /// <summary>The ImGui payload type of an asset path drag.</summary>
    public const string AssetPayload = "ALCO_ASSET";

    /// <summary>
    /// Starts an asset-path drag from the last item when it is being dragged; a no-op
    /// otherwise. The payload holds the asset-system-relative path as UTF-8.
    /// </summary>
    /// <param name="assetName">The asset-system-relative path being dragged.</param>
    public static unsafe void SetAsset(string assetName)
    {
        ArgumentNullException.ThrowIfNull(assetName);
        if (!ImGui.BeginDragDropSource())
        {
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(assetName);
        Span<byte> utf8 = byteCount + 1 <= 512 ? stackalloc byte[byteCount + 1] : new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(assetName, utf8);
        utf8[byteCount] = 0;
        fixed (byte* data = utf8)
        {
            ImGui.SetDragDropPayload(AssetPayload, (IntPtr)data, (uint)(byteCount + 1));
        }
        ImGui.EndDragDropSource();
    }

    /// <summary>
    /// Accepts an asset-path drop onto the last item. Returns true (and the dropped
    /// path) only on delivery; hovering with a matching payload does not produce a
    /// value. A no-op returning false while the last item is disabled (e.g. a
    /// read-only document's controls), because ImGui then reports no drop target.
    /// </summary>
    /// <param name="assetName">The dropped asset-system-relative path.</param>
    public static unsafe bool TryAcceptAsset(out string assetName)
    {
        assetName = string.Empty;
        if (!ImGui.BeginDragDropTarget())
        {
            return false;
        }

        ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(AssetPayload);
        if (payload.NativePtr != null && payload.IsDelivery() && payload.DataSize > 1)
        {
            assetName = Encoding.UTF8.GetString((byte*)payload.Data, payload.DataSize - 1);
        }
        ImGui.EndDragDropTarget();
        return assetName.Length > 0;
    }
}
