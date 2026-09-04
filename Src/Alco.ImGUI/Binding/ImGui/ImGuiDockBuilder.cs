using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.ImGUI;

/// <summary>
/// Manual binding for the ImGui internal DockBuilder API (docking branch of cimgui).
/// Used to build programmatic default dock layouts; the layout is persisted by ImGui
/// through its regular ini file afterwards.
/// <br/>
/// All functions must be called between <see cref="ImGui.NewFrame"/> and
/// <see cref="ImGui.EndFrame"/> (typical use: right before/after creating the dockspace).
/// </summary>
public static unsafe class ImGuiDockBuilder
{
    /// <summary>Hidden internal flag (1 &lt;&lt; 10): marks the node as a dockspace root.</summary>
    private const ImGuiDockNodeFlags DockSpaceFlag = (ImGuiDockNodeFlags)(1 << 10);

    /// <summary>
    /// Adds a fresh (empty) dock node as a dockspace root. Use <see cref="RemoveNode"/> first
    /// when rebuilding an existing dockspace id.
    /// </summary>
    /// <param name="dockspaceId">The dockspace id (usually <see cref="ImGui.GetID(string)"/> of the dockspace).</param>
    /// <param name="flags">Dock node flags; <see cref="DockSpaceFlag"/> is always applied.</param>
    /// <returns>The created node id.</returns>
    public static uint AddNode(uint dockspaceId, ImGuiDockNodeFlags flags)
    {
        return igDockBuilderAddNode(dockspaceId, flags | DockSpaceFlag);
    }

    /// <summary>Removes the node and all of its children (undocks nothing by itself).</summary>
    public static void RemoveNode(uint nodeId) => igDockBuilderRemoveNode(nodeId);

    /// <summary>Sets the position of a node (required for <see cref="ImGui.GetWindowPos"/>-based layouts).</summary>
    public static void SetNodePos(uint nodeId, Vector2 pos) => igDockBuilderSetNodePos(nodeId, pos);

    /// <summary>Sets the size of a node, used by subsequent <see cref="SplitNode"/> ratio splits.</summary>
    public static void SetNodeSize(uint nodeId, Vector2 size) => igDockBuilderSetNodeSize(nodeId, size);

    /// <summary>
    /// Splits a node in two along <paramref name="splitDir"/>. The child on
    /// <paramref name="splitDir"/> receives <paramref name="sizeRatioForNodeAtDir"/> of the
    /// parent extent; the opposite child receives the remainder.
    /// </summary>
    /// <param name="nodeId">Node to split.</param>
    /// <param name="splitDir">Direction of the first child.</param>
    /// <param name="sizeRatioForNodeAtDir">Ratio (0..1) allocated to the child at <paramref name="splitDir"/>.</param>
    /// <param name="idAtDir">Receives the node id at <paramref name="splitDir"/> (0 when absent).</param>
    /// <param name="idAtOppositeDir">Receives the node id opposite to <paramref name="splitDir"/> (0 when absent).</param>
    /// <returns>The updated node id.</returns>
    public static uint SplitNode(
        uint nodeId,
        ImGuiDir splitDir,
        float sizeRatioForNodeAtDir,
        out uint idAtDir,
        out uint idAtOppositeDir)
    {
        uint idAtDirValue = 0;
        uint idAtOppositeDirValue = 0;
        uint result = igDockBuilderSplitNode(
            nodeId,
            splitDir,
            sizeRatioForNodeAtDir,
            &idAtDirValue,
            &idAtOppositeDirValue);
        idAtDir = idAtDirValue;
        idAtOppositeDir = idAtOppositeDirValue;
        return result;
    }

    /// <summary>Docks the window with the given full name (as passed to <see cref="ImGui.Begin(string, ImGuiWindowFlags)"/>)
    /// into the node. Windows created later are matched by name when they first appear.</summary>
    public static void DockWindow(string windowName, uint nodeId)
    {
        int utf8NameByteCount = System.Text.Encoding.UTF8.GetByteCount(windowName);
        byte* utf8NameBytes;
        if (utf8NameByteCount > Util.StackAllocationSizeLimit)
        {
            utf8NameBytes = Util.Allocate(utf8NameByteCount + 1);
        }
        else
        {
            byte* stackPtr = stackalloc byte[utf8NameByteCount + 1];
            utf8NameBytes = stackPtr;
        }
        Util.GetUtf8(windowName, utf8NameBytes, utf8NameByteCount);

        igDockBuilderDockWindow(utf8NameBytes, nodeId);

        if (utf8NameByteCount > Util.StackAllocationSizeLimit)
        {
            Util.Free(utf8NameBytes);
        }
    }

    /// <summary>Commits the dock layout built since <see cref="RemoveNode"/>/<see cref="AddNode"/>.</summary>
    public static void Finish(uint rootNodeId) => igDockBuilderFinish(rootNodeId);

    /// <summary>Returns the central node id of the given node hierarchy (0 when none exists).</summary>
    public static uint GetCentralNode(uint nodeId)
    {
        void* node = igDockBuilderGetCentralNode(nodeId);
        // The native function returns ImGuiDockNode*; marshal the node id, which is the
        // first field of the struct. Declaring the return as uint would truncate the pointer.
        return node == null ? 0 : *(uint*)node;
    }

    /// <summary>Returns whether a node with the given id currently exists in the dock context.</summary>
    public static bool NodeExists(uint nodeId) => igDockBuilderGetNode(nodeId) != null;

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint igDockBuilderAddNode(uint dockspace_id, ImGuiDockNodeFlags flags);

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderRemoveNode(uint node_id);

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderSetNodePos(uint node_id, Vector2 pos);

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderSetNodeSize(uint node_id, Vector2 size);

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint igDockBuilderSplitNode(
        uint node_id,
        ImGuiDir split_dir,
        float size_ratio_for_node_at_dir,
        uint* out_id_at_dir,
        uint* out_id_at_opposite_dir);

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderDockWindow(byte* window_name, uint node_id);

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern void igDockBuilderFinish(uint node_id);

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern void* igDockBuilderGetCentralNode(uint node_id);

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    private static extern void* igDockBuilderGetNode(uint node_id);
}
