namespace Alco.Graphics;

// Reflection-driven pipeline creation. The bind group layouts, vertex input
// layouts, push-constant size and fragment output count are derived from a
// linked shader program's ShaderReflection instead of being hand-written.

public abstract partial class GPUDevice
{
    /// <summary>
    /// Creates a GPU graphics pipeline from a linked shader program's reflection: bind group
    /// layouts, push-constant size, fragment output count and (unless overridden) the vertex
    /// input layouts are all derived from <paramref name="reflection"/>. The transient bind
    /// group layouts consumed by pipeline creation are released before returning.
    /// </summary>
    /// <param name="reflection">The reflection of the linked shader program providing the pipeline interface.</param>
    /// <param name="vertexShader">The compiled vertex shader module.</param>
    /// <param name="fragmentShader">The compiled fragment shader module.</param>
    /// <param name="attachmentLayout">The attachment layout the pipeline renders into; color and depth formats are extracted from it.</param>
    /// <param name="rasterizerState">The rasterizer state.</param>
    /// <param name="blendState">The blend state.</param>
    /// <param name="depthStencilState">The depth-stencil state.</param>
    /// <param name="primitiveTopology">The primitive topology.</param>
    /// <param name="vertexInputLayouts">Custom vertex input layouts replacing the reflected ones, or <c>null</c> to use <paramref name="reflection"/>'s layouts.</param>
    /// <param name="name">The debug name of the pipeline.</param>
    /// <returns>The created GPU graphics pipeline.</returns>
    public GPUPipeline CreateGraphicsPipeline(
        in ShaderReflection reflection,
        ShaderModule vertexShader,
        ShaderModule fragmentShader,
        GPUAttachmentLayout attachmentLayout,
        RasterizerState rasterizerState,
        BlendState blendState,
        DepthStencilState depthStencilState,
        PrimitiveTopology primitiveTopology,
        IReadOnlyList<VertexInputLayout>? vertexInputLayouts = null,
        string name = "unnamed_graphics_pipeline")
    {
        GPUBindGroup[] bindGroups = new GPUBindGroup[reflection.BindGroups.Count];
        try
        {
            for (int i = 0; i < bindGroups.Length; i++)
            {
                bindGroups[i] = CreateBindGroup(reflection.BindGroups[i].ToDescriptor(name));
            }

            PixelFormat[] colors = new PixelFormat[attachmentLayout.Colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = attachmentLayout.Colors[i].Format;
            }

            IReadOnlyList<VertexInputLayout> reflectedLayouts = vertexInputLayouts ?? reflection.VertexLayouts;
            VertexInputLayout[] vertexLayouts = new VertexInputLayout[reflectedLayouts.Count];
            for (int i = 0; i < vertexLayouts.Length; i++)
            {
                vertexLayouts[i] = reflectedLayouts[i];
            }

            GraphicsPipelineDescriptor descriptor = new(
                bindGroups,
                new ShaderModule[] { vertexShader, fragmentShader },
                vertexLayouts,
                rasterizerState,
                blendState,
                depthStencilState,
                primitiveTopology,
                colors,
                attachmentLayout.Depth?.Format,
                (uint)reflection.PushConstantsSize,
                name)
            {
                FragmentOutputCount = reflection.FragmentOutputCount,
            };

            return CreateGraphicsPipeline(descriptor);
        }
        finally
        {
            for (int i = 0; i < bindGroups.Length; i++)
            {
                bindGroups[i].Dispose();
            }
        }
    }

    /// <summary>
    /// Creates a GPU compute pipeline from a linked shader program's reflection: bind group
    /// layouts and the push-constant size are derived from <paramref name="reflection"/>. The
    /// transient bind group layouts consumed by pipeline creation are released before returning.
    /// </summary>
    /// <param name="reflection">The reflection of the linked shader program providing the pipeline interface.</param>
    /// <param name="computeShader">The compiled compute shader module.</param>
    /// <param name="name">The debug name of the pipeline.</param>
    /// <returns>The created GPU compute pipeline.</returns>
    public GPUPipeline CreateComputePipeline(
        in ShaderReflection reflection,
        ShaderModule computeShader,
        string name = "unnamed_compute_pipeline")
    {
        GPUBindGroup[] bindGroups = new GPUBindGroup[reflection.BindGroups.Count];
        try
        {
            for (int i = 0; i < bindGroups.Length; i++)
            {
                bindGroups[i] = CreateBindGroup(reflection.BindGroups[i].ToDescriptor(name));
            }

            ComputePipelineDescriptor descriptor = new(
                computeShader,
                bindGroups,
                (uint)reflection.PushConstantsSize,
                name);

            return CreateComputePipeline(descriptor);
        }
        finally
        {
            for (int i = 0; i < bindGroups.Length; i++)
            {
                bindGroups[i].Dispose();
            }
        }
    }
}
