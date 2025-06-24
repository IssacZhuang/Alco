using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A high performance dynamic mesh renderer that manages multiple DynamicMesh instances.
/// When the current DynamicMesh runs out of capacity, it automatically creates new DynamicMesh instances.
/// <br/> Not thread safe but each thread can have its own renderer instance for multi-thread rendering.
/// </summary>
public unsafe sealed class DynamicMeshRenderer : AutoDisposable, ICommandListener
{
    private const uint DefaultVertexBufferSizePerChunk = 64 * 1024; // 64KB
    private const uint DefaultIndexBufferSizePerChunk = 16 * 1024; // 16KB

    private readonly RenderingSystem _renderingSystem;
    private readonly IRenderContext _renderContext;

    private readonly List<DynamicMesh> _dynamicMeshes;
    private readonly uint _vertexBufferSizePerChunk;
    private readonly uint _indexBufferSizePerChunk;

    private int _currentMeshIndex;

    /// <summary>
    /// Gets the number of DynamicMesh instances currently managed by this renderer.
    /// </summary>
    public int MeshCount => _dynamicMeshes.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicMeshRenderer"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system.</param>
    /// <param name="renderContext">The render context.</param>
    /// <param name="vertexBufferSize">The size of each vertex buffer in bytes. Default is 64KB.</param>
    /// <param name="indexBufferSize">The size of each index buffer in bytes. Default is 16KB.</param>
    /// <param name="name">The name of the renderer.</param>
    internal DynamicMeshRenderer(RenderingSystem renderingSystem, IRenderContext renderContext,
        uint vertexBufferSize = DefaultVertexBufferSizePerChunk, uint indexBufferSize = DefaultIndexBufferSizePerChunk, string name = "dynamic_mesh_renderer")
    {
        _renderingSystem = renderingSystem;
        _renderContext = renderContext;

        _vertexBufferSizePerChunk = vertexBufferSize;
        _indexBufferSizePerChunk = indexBufferSize;
        _dynamicMeshes = new List<DynamicMesh>();

        // Create the first DynamicMesh
        _dynamicMeshes.Add(CreateNewDynamicMesh(0));
        _currentMeshIndex = 0;

        _renderContext.AddListener(this);
    }

    /// <summary>
    /// Draws vertices and indices with 32-bit indices using the specified material.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The 32-bit indices.</param>
    /// <param name="material">The material to use for drawing.</param>
    public void Draw<TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, in Material material) where TVertex : unmanaged
    {
        var (mesh, subMeshIndex) = AddSubMeshInternal(vertices, indices);
        _renderContext.Draw(mesh, material, subMeshIndex);
    }

    /// <summary>
    /// Draws vertices and indices with 16-bit indices using the specified material.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The 16-bit indices.</param>
    /// <param name="material">The material to use for drawing.</param>
    public void Draw<TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<ushort> indices, in Material material) where TVertex : unmanaged
    {
        var (mesh, subMeshIndex) = AddSubMeshInternal(vertices, indices);
        _renderContext.Draw(mesh, material, subMeshIndex);
    }

    /// <summary>
    /// Draws vertices and indices with 32-bit indices using the specified material and constant data.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The 32-bit indices.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    public void DrawWithConstant<TVertex, T>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, in Material material, in T constant)
        where TVertex : unmanaged where T : unmanaged
    {
        var (mesh, subMeshIndex) = AddSubMeshInternal(vertices, indices);
        _renderContext.DrawWithConstant(mesh, material, constant, subMeshIndex);
    }

    /// <summary>
    /// Draws vertices and indices with 16-bit indices using the specified material and constant data.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The 16-bit indices.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    public void DrawWithConstant<TVertex, T>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<ushort> indices, in Material material, in T constant)
        where TVertex : unmanaged where T : unmanaged
    {
        var (mesh, subMeshIndex) = AddSubMeshInternal(vertices, indices);
        _renderContext.DrawWithConstant(mesh, material, constant, subMeshIndex);
    }

    /// <summary>
    /// Draws vertices and indices with 32-bit indices multiple times using the specified material.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The 32-bit indices.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    public void DrawInstanced<TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, in Material material, in uint instanceCount) where TVertex : unmanaged
    {
        var (mesh, subMeshIndex) = AddSubMeshInternal(vertices, indices);
        _renderContext.DrawInstanced(mesh, material, instanceCount, subMeshIndex);
    }

    /// <summary>
    /// Draws vertices and indices with 16-bit indices multiple times using the specified material.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The 16-bit indices.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    public void DrawInstanced<TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<ushort> indices, in Material material, in uint instanceCount) where TVertex : unmanaged
    {
        var (mesh, subMeshIndex) = AddSubMeshInternal(vertices, indices);
        _renderContext.DrawInstanced(mesh, material, instanceCount, subMeshIndex);
    }

    /// <summary>
    /// Draws vertices and indices with 32-bit indices multiple times using the specified material and constant data.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The 32-bit indices.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    public void DrawInstancedWithConstant<TVertex, T>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, in Material material, in uint instanceCount, in T constant)
        where TVertex : unmanaged where T : unmanaged
    {
        var (mesh, subMeshIndex) = AddSubMeshInternal(vertices, indices);
        _renderContext.DrawInstancedWithConstant(mesh, material, instanceCount, constant, subMeshIndex);
    }

    /// <summary>
    /// Draws vertices and indices with 16-bit indices multiple times using the specified material and constant data.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The 16-bit indices.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    public void DrawInstancedWithConstant<TVertex, T>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<ushort> indices, in Material material, in uint instanceCount, in T constant)
        where TVertex : unmanaged where T : unmanaged
    {
        var (mesh, subMeshIndex) = AddSubMeshInternal(vertices, indices);
        _renderContext.DrawInstancedWithConstant(mesh, material, instanceCount, constant, subMeshIndex);
    }

    /// <summary>
    /// Draws vertices and indices with 32-bit indices multiple times using the specified material and constant data, starting from a specific instance.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The 32-bit indices.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="instanceStart">The index of the first instance to draw.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    public void DrawInstancedWithConstant<TVertex, T>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, in Material material, in uint instanceCount, in uint instanceStart, in T constant)
        where TVertex : unmanaged where T : unmanaged
    {
        var (mesh, subMeshIndex) = AddSubMeshInternal(vertices, indices);
        _renderContext.DrawInstancedWithConstant(mesh, material, instanceCount, instanceStart, constant, subMeshIndex);
    }

    /// <summary>
    /// Draws vertices and indices with 16-bit indices multiple times using the specified material and constant data, starting from a specific instance.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <typeparam name="T">The type of the constant data.</typeparam>
    /// <param name="vertices">The vertices to draw.</param>
    /// <param name="indices">The 16-bit indices.</param>
    /// <param name="material">The material to use for drawing.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    /// <param name="instanceStart">The index of the first instance to draw.</param>
    /// <param name="constant">The constant data to push to the shader.</param>
    public void DrawInstancedWithConstant<TVertex, T>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<ushort> indices, in Material material, in uint instanceCount, in uint instanceStart, in T constant)
        where TVertex : unmanaged where T : unmanaged
    {
        var (mesh, subMeshIndex) = AddSubMeshInternal(vertices, indices);
        _renderContext.DrawInstancedWithConstant(mesh, material, instanceCount, instanceStart, constant, subMeshIndex);
    }

    void ICommandListener.OnCommandBegin()
    {
        _currentMeshIndex = 0;

        // Clear the first mesh for new frame
        if (_dynamicMeshes.Count > 0)
        {
            _dynamicMeshes[0].Clear();
        }
    }

    void ICommandListener.OnCommandEnd()
    {
        // Update all used meshes to GPU
        for (int i = 0; i <= _currentMeshIndex && i < _dynamicMeshes.Count; i++)
        {
            DynamicMesh mesh = _dynamicMeshes[i];
            if (mesh.SubMeshCount > 0)
            {
                mesh.UpdateBufferToGPU();
            }
        }
    }

    /// <summary>
    /// Adds a sub-mesh with 32-bit indices to the current DynamicMesh.
    /// If the current mesh doesn't have enough capacity, a new DynamicMesh will be created.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices of the sub-mesh.</param>
    /// <param name="indices">The 32-bit indices of the sub-mesh.</param>
    /// <returns>A tuple containing the DynamicMesh and the sub-mesh index.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (DynamicMesh mesh, int subMeshIndex) AddSubMeshInternal<TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices) where TVertex : unmanaged
    {
        DynamicMesh currentMesh = _dynamicMeshes[_currentMeshIndex];

        // Try to add to current mesh
        if (currentMesh.TryAddSubMesh(vertices, indices, out SubMeshData subMeshData))
        {
            return (currentMesh, currentMesh.SubMeshCount - 1);
        }

        // Current mesh is full, create a new one
        _currentMeshIndex++;
        if (_currentMeshIndex >= _dynamicMeshes.Count)
        {
            _dynamicMeshes.Add(CreateNewDynamicMesh(_currentMeshIndex));
        }

        // Add to new mesh
        DynamicMesh newMesh = _dynamicMeshes[_currentMeshIndex];
        newMesh.Clear(); // Clear in case we're reusing an existing mesh
        newMesh.AddSubMesh(vertices, indices);
        return (newMesh, 0);
    }

    /// <summary>
    /// Adds a sub-mesh with 16-bit indices to the current DynamicMesh.
    /// If the current mesh doesn't have enough capacity, a new DynamicMesh will be created.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices of the sub-mesh.</param>
    /// <param name="indices">The 16-bit indices of the sub-mesh.</param>
    /// <returns>A tuple containing the DynamicMesh and the sub-mesh index.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (DynamicMesh mesh, int subMeshIndex) AddSubMeshInternal<TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<ushort> indices) where TVertex : unmanaged
    {
        DynamicMesh currentMesh = _dynamicMeshes[_currentMeshIndex];

        // Try to add to current mesh
        if (currentMesh.TryAddSubMesh(vertices, indices, out SubMeshData subMeshData))
        {
            return (currentMesh, currentMesh.SubMeshCount - 1);
        }

        // Current mesh is full, create a new one
        _currentMeshIndex++;
        if (_currentMeshIndex >= _dynamicMeshes.Count)
        {
            _dynamicMeshes.Add(CreateNewDynamicMesh(_currentMeshIndex));
        }

        // Add to new mesh
        DynamicMesh newMesh = _dynamicMeshes[_currentMeshIndex];
        newMesh.Clear(); // Clear in case we're reusing an existing mesh
        newMesh.AddSubMesh(vertices, indices);
        return (newMesh, 0);
    }

    /// <summary>
    /// Creates a new DynamicMesh instance with the specified index.
    /// </summary>
    /// <param name="index">The index of the mesh for naming purposes.</param>
    /// <returns>A new DynamicMesh instance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private DynamicMesh CreateNewDynamicMesh(int index)
    {
        return _renderingSystem.CreateDynamicMesh(_vertexBufferSizePerChunk, _indexBufferSizePerChunk, $"dynamic_mesh_{index}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderContext.RemoveListener(this);

            // Dispose all DynamicMesh instances
            foreach (DynamicMesh mesh in _dynamicMeshes)
            {
                mesh.Dispose();
            }
            _dynamicMeshes.Clear();
        }
    }
}