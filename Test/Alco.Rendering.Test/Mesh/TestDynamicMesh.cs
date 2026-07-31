using System.Numerics;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Tests dynamic mesh staging-buffer uploads.
/// </summary>
[TestFixture]
public sealed class TestDynamicMesh
{
    private const uint VertexBytesPerTriangle = 3 * 2 * sizeof(float);
    private const uint IndexBytesPerTriangle = 3 * sizeof(ushort);

    /// <summary>
    /// Verifies UInt16 index data is uploaded safely for aligned and unaligned aggregate sizes.
    /// </summary>
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(19)]
    public void UpdateBufferToGPU_UInt16TriangleIndices_HandlesUploadAlignment(int triangleCount)
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        Vector2[] vertices =
        {
            new(-0.5f, 0.5f),
            new(0.5f, 0f),
            new(-0.5f, -0.5f),
        };
        ushort[] indices = { 0, 1, 2 };

        uint vertexBufferSize = checked((uint)triangleCount * VertexBytesPerTriangle);
        uint indexBufferSize = checked((uint)triangleCount * IndexBytesPerTriangle);
        using DynamicMesh mesh = host.RenderingSystem.CreateDynamicMesh(
            vertexBufferSize,
            indexBufferSize,
            "alignment_test_mesh");

        for (int i = 0; i < triangleCount; i++)
            mesh.AddSubMesh(vertices, indices);

        Assert.That(mesh.SubMeshCount, Is.EqualTo(triangleCount));
        Assert.DoesNotThrow(mesh.UpdateBufferToGPU);

        for (int i = 0; i < triangleCount; i++)
        {
            SubMeshData subMesh = mesh.GetSubMesh(i);
            Assert.That(subMesh.VertexOffset, Is.EqualTo((uint)i * VertexBytesPerTriangle));
            Assert.That(subMesh.VertexSize, Is.EqualTo(VertexBytesPerTriangle));
            Assert.That(subMesh.IndexOffset, Is.EqualTo((uint)i * IndexBytesPerTriangle));
            Assert.That(subMesh.IndexSize, Is.EqualTo(IndexBytesPerTriangle));
            Assert.That(subMesh.IndexCount, Is.EqualTo(3));
        }
    }

    /// <summary>
    /// Verifies unaligned vertex data uses the same safe upload path.
    /// </summary>
    [Test]
    public void UpdateBufferToGPU_UnalignedVertexData_HandlesUploadAlignment()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        using DynamicMesh mesh = host.RenderingSystem.CreateDynamicMesh(
            vertexBufferSize: 3,
            indexBufferSize: sizeof(uint),
            name: "unaligned_vertex_test_mesh");

        mesh.AddSubMesh(new byte[] { 1, 2, 3 }, new uint[] { 0 });

        Assert.DoesNotThrow(mesh.UpdateBufferToGPU);
        Assert.That(mesh.GetSubMesh(0).VertexSize, Is.EqualTo(3));
    }
}
