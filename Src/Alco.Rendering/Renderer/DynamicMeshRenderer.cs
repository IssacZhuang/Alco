
namespace Alco.Rendering;

public class DynamicMeshRenderer : AutoDisposable, ICommandListener
{
    private readonly RenderContext _renderContext;
    private readonly DynamicMesh _mesh;


    public string Name { get; }

    public DynamicMeshRenderer(RenderingSystem renderingSystem,RenderContext renderContext, string name)
    {
        _renderContext = renderContext;
        _mesh = renderingSystem.CreateDynamicMesh(name);
        Name = name;
    }

    void ICommandListener.OnCommandBegin()
    {
        _mesh.Clear();
    }

    void ICommandListener.OnCommandEnd()
    {
        _mesh.UpdateBufferToGPU();
    }

    public void Draw<TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, Material material) where TVertex : unmanaged
    {
        SubMeshData subMeshData = _mesh.AddSubMesh(vertices, indices);
        _renderContext.Draw(_mesh, material, subMeshData.Index);
    }

    public void DrawWithConstant<TConstant, TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, Material material, TConstant constant) where TConstant : unmanaged where TVertex : unmanaged
    {
        SubMeshData subMeshData = _mesh.AddSubMesh(vertices, indices);
        _renderContext.DrawWithConstant(_mesh, material, constant, subMeshData.Index);
    }

    public void DrawInstanced<TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, Material material, uint instanceCount) where TVertex : unmanaged
    {
        SubMeshData subMeshData = _mesh.AddSubMesh(vertices, indices);
        _renderContext.DrawInstanced(_mesh, material, instanceCount, subMeshData.Index);
    }

    public void DrawInstancedWithConstant<TConstant, TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, Material material, uint instanceCount, TConstant constant) where TConstant : unmanaged where TVertex : unmanaged
    {
        SubMeshData subMeshData = _mesh.AddSubMesh(vertices, indices);
        _renderContext.DrawInstancedWithConstant(_mesh, material, instanceCount, constant, subMeshData.Index);
    }

    public void DrawInstancedWithConstant<TConstant, TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices, Material material, uint instanceCount, uint instanceStart, TConstant constant) where TConstant : unmanaged where TVertex : unmanaged
    {
        SubMeshData subMeshData = _mesh.AddSubMesh(vertices, indices);
        _renderContext.DrawInstancedWithConstant(_mesh, material, instanceCount, instanceStart, constant, subMeshData.Index);
    }


    
    

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            _mesh.Dispose();
        }
    }
}


