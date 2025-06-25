using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

public static class GPUCommandBufferExtension
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GPUCommandBuffer.RenderPass BeginRender(this GPUCommandBuffer command, IRenderTarget renderTarget)
    {
        return command.BeginRender(renderTarget.RenderTexture.FrameBuffer);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SetMesh(this GPUCommandBuffer.RenderPass renderPass, Mesh mesh, int subMeshIndex = 0)
    {
        SubMeshData subMeshData = mesh.GetSubMesh(subMeshIndex);
        renderPass.SetVertexBuffer(0, mesh.VertexBuffer, subMeshData.VertexOffset, subMeshData.VertexSize);
        renderPass.SetIndexBuffer(mesh.IndexBuffer, subMeshData.IndexFormat, subMeshData.IndexOffset, subMeshData.IndexSize);
        return subMeshData.IndexCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetPipeline(this GPUCommandBuffer.RenderPass renderPass, GraphicsPipelineContext pipelineInfo)
    {
        renderPass.SetPipeline(pipelineInfo.Pipeline!);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SetMesh(this GPURenderBundle command, Mesh mesh, int subMeshIndex = 0)
    {
        SubMeshData subMeshData = mesh.GetSubMesh(subMeshIndex);
        command.SetVertexBuffer(0, mesh.VertexBuffer, subMeshData.VertexOffset, subMeshData.VertexSize);
        command.SetIndexBuffer(mesh.IndexBuffer, subMeshData.IndexFormat, subMeshData.IndexOffset, subMeshData.IndexSize);
        return subMeshData.IndexCount;
    }
    
    
}