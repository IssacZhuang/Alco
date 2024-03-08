using Vocore.Graphics;

namespace Vocore.Rendering;

public interface IMesh
{
    GPUBuffer VertexBuffer { get; }
    GPUBuffer IndexBuffer { get; }
    IndexFormat IndexFormat { get; }
    uint IndexCount { get; }

    uint SubMeshCount { get; }
    SubMeshData GetSubMesh(int index);
}

