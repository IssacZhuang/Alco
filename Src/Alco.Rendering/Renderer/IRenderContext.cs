namespace Alco.Rendering;

public interface IRenderContext
{
    public void Draw(in Mesh mesh, in Material material, in int subMeshIndex = 0);
    public void DrawWithConstant<T>(in Mesh mesh, in Material material, in T constant, in int subMeshIndex = 0) where T : unmanaged;
    public void DrawInstanced(in Mesh mesh, in Material material, in uint instanceCount, in int subMeshIndex = 0);
    public void DrawInstancedWithConstant<T>(in Mesh mesh, in Material material, in uint instanceCount, in T constant, in int subMeshIndex = 0) where T : unmanaged;
    public void DrawInstancedWithConstant<T>(in Mesh mesh, in Material material, in uint instanceCount, in uint instanceStart, in T constant, in int subMeshIndex = 0) where T : unmanaged;

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

