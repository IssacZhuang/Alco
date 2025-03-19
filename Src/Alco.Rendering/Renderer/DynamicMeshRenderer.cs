namespace Alco.Rendering;

/// <summary>
/// Renderer for dynamic meshes that implements the ICommandListener interface.
/// </summary>
public class DynamicMeshRenderer : AutoDisposable, ICommandListener
{
    private readonly RenderContext _renderContext;
    private readonly DynamicMesh _mesh;


    /// <summary>
    /// Gets the name of the renderer.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicMeshRenderer"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system used to create the dynamic mesh.</param>
    /// <param name="renderContext">The render context used for drawing.</param>
    /// <param name="name">The name of the renderer.</param>
    internal DynamicMeshRenderer(RenderingSystem renderingSystem,RenderContext renderContext, string name)
    {
        _renderContext = renderContext;
        _mesh = renderingSystem.CreateDynamicMesh(name);
        Name = name;
    }

    /// <summary>
    /// Called when a command begins. Clears the mesh.
    /// </summary>
    void ICommandListener.OnCommandBegin()
    {
        _mesh.Clear();
    }

    /// <summary>
    /// Called when a command ends. Updates the mesh buffer to the GPU.
    /// </summary>
    void ICommandListener.OnCommandEnd()
    {
        _mesh.UpdateBufferToGPU();
    }

    /// <summary>
    /// Draws a mesh with the specified vertices, indices, and material.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The indices to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    public void Draw<TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, Material material) where TVertex : unmanaged
    {
        SubMeshData subMeshData = _mesh.AddSubMesh(vertices, indices);
        _renderContext.Draw(_mesh, material, subMeshData.Index);
    }

    /// <summary>
    /// Draws a mesh with the specified vertices, indices, material, and constant data.
    /// </summary>
    /// <typeparam name="TConstant">The constant data type.</typeparam>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The indices to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="constant">The constant data to use for drawing.</param>
    public void DrawWithConstant<TConstant, TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, Material material, TConstant constant) where TConstant : unmanaged where TVertex : unmanaged
    {
        SubMeshData subMeshData = _mesh.AddSubMesh(vertices, indices);
        _renderContext.DrawWithConstant(_mesh, material, constant, subMeshData.Index);
    }

    /// <summary>
    /// Draws instanced meshes with the specified vertices, indices, material, and instance count.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The indices to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    public void DrawInstanced<TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, Material material, uint instanceCount) where TVertex : unmanaged
    {
        SubMeshData subMeshData = _mesh.AddSubMesh(vertices, indices);
        _renderContext.DrawInstanced(_mesh, material, instanceCount, subMeshData.Index);
    }

    /// <summary>
    /// Draws instanced meshes with the specified vertices, indices, material, instance count, and constant data.
    /// </summary>
    /// <typeparam name="TConstant">The constant data type.</typeparam>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The indices to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="constant">The constant data to use for drawing.</param>
    public void DrawInstancedWithConstant<TConstant, TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, Material material, uint instanceCount, TConstant constant) where TConstant : unmanaged where TVertex : unmanaged
    {
        SubMeshData subMeshData = _mesh.AddSubMesh(vertices, indices);
        _renderContext.DrawInstancedWithConstant(_mesh, material, instanceCount, constant, subMeshData.Index);
    }

    /// <summary>
    /// Draws instanced meshes with the specified vertices, indices, material, instance count, instance start, and constant data.
    /// </summary>
    /// <typeparam name="TConstant">The constant data type.</typeparam>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The indices to draw.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="instanceStart">The starting instance index.</param>
    /// <param name="constant">The constant data to use for drawing.</param>
    public void DrawInstancedWithConstant<TConstant, TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, Material material, uint instanceCount, uint instanceStart, TConstant constant) where TConstant : unmanaged where TVertex : unmanaged
    {
        SubMeshData subMeshData = _mesh.AddSubMesh(vertices, indices);
        _renderContext.DrawInstancedWithConstant(_mesh, material, instanceCount, instanceStart, constant, subMeshData.Index);
    }




    /// <summary>
    /// Disposes the resources used by the renderer.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources, false otherwise.</param>
    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            _mesh.Dispose();
        }
    }
}


