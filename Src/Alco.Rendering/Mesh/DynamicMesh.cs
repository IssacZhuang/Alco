using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

public sealed unsafe class DynamicMesh : Mesh
{
    private readonly List<SubMeshData> _subMeshes;
    public override int SubMeshCount => _subMeshes.Count;

    internal DynamicMesh(GPUDevice device, uint size, uint indexCount, IndexFormat indexFormat, string name = "mesh") :
    base(device, size, indexCount, indexFormat, name)
    {
        _subMeshes = new List<SubMeshData>();
        ResizeVertextBufferSoft(0);
        ResizeIndexBuffer(0, IndexFormat.UInt16);
    }

    

    public override SubMeshData GetSubMesh(int index)
    {
        return _subMeshes[index];
    }
}