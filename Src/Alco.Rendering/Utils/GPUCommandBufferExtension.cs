using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Extension methods that bind meshes and pipelines to render passes and render bundles.
/// </summary>
public static class GPUCommandBufferExtension
{
    /// <summary>
    /// Binds the given sub-mesh's vertex and index buffers to the render pass.
    /// </summary>
    /// <param name="renderPass">The render pass.</param>
    /// <param name="mesh">The mesh to bind.</param>
    /// <param name="subMeshIndex">The sub-mesh index.</param>
    /// <returns>The sub-mesh index count.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SetMesh(this GPUCommandBuffer.RenderPass renderPass, Mesh mesh, int subMeshIndex = 0)
    {
        SubMeshData subMeshData = mesh.GetSubMesh(subMeshIndex);
        renderPass.SetVertexBuffer(0, mesh.VertexBuffer, subMeshData.VertexOffset, subMeshData.VertexSize);
        renderPass.SetIndexBuffer(mesh.IndexBuffer, subMeshData.IndexFormat, subMeshData.IndexOffset, subMeshData.IndexSize);
        return subMeshData.IndexCount;
    }

    /// <summary>
    /// Binds the pipeline stored in the given context to the render pass.
    /// </summary>
    /// <param name="renderPass">The render pass.</param>
    /// <param name="pipelineInfo">The pipeline context.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetPipeline(this GPUCommandBuffer.RenderPass renderPass, GraphicsPipelineContext pipelineInfo)
    {
        renderPass.SetPipeline(pipelineInfo.Pipeline!);
    }

    /// <summary>
    /// Binds the given sub-mesh's vertex and index buffers to the render bundle.
    /// </summary>
    /// <param name="command">The render bundle.</param>
    /// <param name="mesh">The mesh to bind.</param>
    /// <param name="subMeshIndex">The sub-mesh index.</param>
    /// <returns>The sub-mesh index count.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint SetMesh(this GPURenderBundle command, Mesh mesh, int subMeshIndex = 0)
    {
        SubMeshData subMeshData = mesh.GetSubMesh(subMeshIndex);
        command.SetVertexBuffer(0, mesh.VertexBuffer, subMeshData.VertexOffset, subMeshData.VertexSize);
        command.SetIndexBuffer(mesh.IndexBuffer, subMeshData.IndexFormat, subMeshData.IndexOffset, subMeshData.IndexSize);
        return subMeshData.IndexCount;
    }
    
    
}