using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

public static class GPUCommandBufferExtension
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetFrameBuffer(this GPUCommandBuffer command, IRenderTarget renderTarget)
    {
        command.SetFrameBuffer(renderTarget.RenderTexture.FrameBuffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetFrameBuffer(this GPUCommandBuffer command, RenderTexture renderTexture)
    {
        command.SetFrameBuffer(renderTexture.FrameBuffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetGraphicsPipeline(this GPUCommandBuffer command, GraphicsPipelineContext pipelineInfo)
    {
        command.SetGraphicsPipeline(pipelineInfo.Pipeline!);
    }

    /// <summary>
    /// Set the mesh and return the index count of the sub mesh.
    /// </summary>
    /// <param name="command">The command buffer to set the mesh on. </param>
    /// <param name="mesh"> The mesh to set. </param>
    /// <param name="subMeshIndex"> The index of the sub mesh to set. </param>
    /// <returns> The index count of the sub mesh. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SetMesh(this GPUCommandBuffer command, Mesh mesh, int subMeshIndex = 0)
    {
        SubMeshData subMeshData = mesh.GetSubMesh(subMeshIndex);
        command.SetVertexBuffer(0, mesh.VertexBuffer, subMeshData.VertexOffset, subMeshData.VertexSize);
        command.SetIndexBuffer(mesh.IndexBuffer, subMeshData.IndexFormat, subMeshData.IndexOffset, subMeshData.IndexSize);
        return subMeshData.IndexCount;
    }
    
    
}