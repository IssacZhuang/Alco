namespace Alco.Rendering;

public interface IRenderContext
{
    public void Draw(in Mesh mesh, in GraphicsMaterial material, in int subMeshIndex = 0);
    public void DrawWithConstant<T>(in Mesh mesh, in GraphicsMaterial material, in T constant, in int subMeshIndex = 0) where T : unmanaged;
    public void DrawInstanced(in Mesh mesh, in GraphicsMaterial material, in uint instanceCount, in int subMeshIndex = 0);
    public void DrawInstanced(in Mesh mesh, in GraphicsMaterial material, in uint instanceCount, in uint instanceStartIndex, in int subMeshIndex = 0);
    public void DrawInstancedWithConstant<T>(in Mesh mesh, in GraphicsMaterial material, in uint instanceCount, in T constant, in int subMeshIndex = 0) where T : unmanaged;
    public void DrawInstancedWithConstant<T>(in Mesh mesh, in GraphicsMaterial material, in uint instanceCount, in uint instanceStart, in T constant, in int subMeshIndex = 0) where T : unmanaged;

    /// <summary>
    /// Draws a mesh with the draw arguments (index count, instance count, first
    /// instance) read from an indirect buffer plus push constants. The record at
    /// <paramref name="indirectOffset"/> must follow the
    /// <see cref="Alco.Graphics.IndexedIndirectData"/> layout; the shader still
    /// fetches instance data by instance id, offset by the record's firstInstance
    /// field. Available while recording render bundles, so bundles recorded against
    /// a persistent indirect buffer replay whatever the buffer holds at execute time.
    /// </summary>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw (vertex/index buffers are bound, the index count comes from the indirect record).</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="indirectBuffer">The buffer holding the indirect draw record.</param>
    /// <param name="indirectOffset">The byte offset of the record in the indirect buffer.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw. Default is 0.</param>
    public void DrawIndexedIndirect<T>(in Mesh mesh, in GraphicsMaterial material, GraphicsBuffer indirectBuffer, uint indirectOffset, in T constant, in int subMeshIndex = 0) where T : unmanaged;

    /// <summary>
    /// Adds a command listener to the render context.
    /// </summary>
    /// <param name="listener">The listener to add.</param>
    public void AddListener(ICommandListener listener);

    /// <summary>
    /// Removes a command listener from the render context.
    /// </summary>
    /// <param name="listener">The listener to remove.</param>
    public void RemoveListener(ICommandListener listener);
}

