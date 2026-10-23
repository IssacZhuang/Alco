namespace Alco.Rendering;

/// <summary>
/// A context that records draw commands into an open render pass.
/// </summary>
public interface IRenderContext
{
    /// <summary>Draws a mesh with the given material.</summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw.</param>
    public void Draw(in Mesh mesh, in GraphicsMaterial material, in int subMeshIndex = 0);
    /// <summary>Draws a mesh with the given material and push constants.</summary>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="constant">The constant data pushed to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw.</param>
    public void DrawWithConstant<T>(in Mesh mesh, in GraphicsMaterial material, in T constant, in int subMeshIndex = 0) where T : unmanaged;
    /// <summary>Draws a mesh multiple times with the given material.</summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw.</param>
    public void DrawInstanced(in Mesh mesh, in GraphicsMaterial material, in uint instanceCount, in int subMeshIndex = 0);
    /// <summary>Draws a mesh multiple times with the given material, starting from the specified instance.</summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="instanceStartIndex">The index of the first instance to draw.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw.</param>
    public void DrawInstanced(in Mesh mesh, in GraphicsMaterial material, in uint instanceCount, in uint instanceStartIndex, in int subMeshIndex = 0);
    /// <summary>Draws a mesh multiple times with the given material and push constants.</summary>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="constant">The constant data pushed to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw.</param>
    public void DrawInstancedWithConstant<T>(in Mesh mesh, in GraphicsMaterial material, in uint instanceCount, in T constant, in int subMeshIndex = 0) where T : unmanaged;
    /// <summary>Draws a mesh multiple times with the given material and push constants, starting from the specified instance.</summary>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="instanceStart">The index of the first instance to draw.</param>
    /// <param name="constant">The constant data pushed to the shader.</param>
    /// <param name="subMeshIndex">The index of the sub-mesh to draw.</param>
    public void DrawInstancedWithConstant<T>(in Mesh mesh, in GraphicsMaterial material, in uint instanceCount, in uint instanceStart, in T constant, in int subMeshIndex = 0) where T : unmanaged;

    /// <summary>
    /// Draws a mesh with draw arguments read from an indirect buffer plus push constants.
    /// The record must follow the <see cref="Alco.Graphics.IndexedIndirectData"/> layout.
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

