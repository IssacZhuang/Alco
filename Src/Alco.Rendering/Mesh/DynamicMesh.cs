using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

public sealed unsafe class DynamicMesh : Mesh
{
    private readonly List<SubMeshData> _subMeshes;

    public DynamicMesh(GPUDevice device, uint vertexBufferSize, uint indexBufferSize, string name = "mesh") : 
    base(device, vertexBufferSize, indexBufferSize, name)
    {
        _subMeshes = new List<SubMeshData>();
    }

    public override int SubMeshCount => _subMeshes.Count;


    

    public override SubMeshData GetSubMesh(int index)
    {
        return _subMeshes[index];
    }
}