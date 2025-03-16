using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

public sealed unsafe class DynamicMesh : Mesh
{
    private List<SubMeshData> _subMeshes;

    internal DynamicMesh(GPUDevice device, uint size, uint indexCount, IndexFormat indexFormat, string name = "mesh") :
    base(device, size, indexCount, indexFormat, name)
    {
        _subMeshes = new List<SubMeshData>();
    }

    public override int SubMeshCount => _subMeshes.Count;

    public override SubMeshData GetSubMesh(int index)
    {
        return _subMeshes[index];
    }
}