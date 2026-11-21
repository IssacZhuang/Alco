namespace Alco.GUI;

/// <summary>Per-frame batching, clipping, and font atlas upload statistics for a <see cref="Canvas"/>.</summary>
/// <param name="QuadCount">Total content quads emitted during the frame (excluding mask quads).</param>
/// <param name="ContentDraws">Number of instanced quad content draw calls recorded.</param>
/// <param name="MaskDraws">Number of stencil push and pop draw calls recorded.</param>
/// <param name="MeshDraws">Number of custom triangulated mesh draw calls recorded.</param>
/// <param name="ScissorChanges">Number of scissor state changes emitted across the render pass.</param>
/// <param name="StencilMaskCount">Number of active (non-elided) stencil masks pushed during traversal.</param>
/// <param name="ScissorMaskCount">Number of axis-aligned untextured masks handled via scissor fast-path.</param>
/// <param name="ElidedMaskCount">Number of stencil masks elided because they had no visible content inside them.</param>
/// <param name="FontGlyphsUploaded">Number of new SDF glyphs rasterized and queued for upload this frame.</param>
/// <param name="FontPageCount">Total GPU atlas pages currently allocated in the canvas's <see cref="Alco.Rendering.GlyphCache"/>.</param>
/// <param name="FontAtlasBytes">Total GPU atlas bytes occupied by the canvas's <see cref="Alco.Rendering.GlyphCache"/>.</param>
/// <param name="SplitDraws">Whether debug draw splitting was enabled for this frame.</param>
public readonly record struct UIFrameStats(
    int QuadCount,
    int ContentDraws,
    int MaskDraws,
    int MeshDraws,
    int ScissorChanges,
    int StencilMaskCount,
    int ScissorMaskCount,
    int ElidedMaskCount,
    int FontGlyphsUploaded,
    int FontPageCount,
    long FontAtlasBytes,
    bool SplitDraws);
