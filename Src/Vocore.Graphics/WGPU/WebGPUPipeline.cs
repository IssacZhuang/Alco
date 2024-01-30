
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUGraphicsPipeline : GPUPipeline
{
    #region Properties
    private readonly WGPURenderPipeline _graphicsipeline;
    private readonly ShaderStage _stages;

    #endregion

    #region Abstract Implementation

    public override string Name { get; }

    public override ShaderStage Stages
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _stages;
    }

    protected override void Dispose(bool disposing)
    {
        wgpuRenderPipelineRelease(_graphicsipeline);
    }

    #endregion

    #region WebGPU Implementation

    public WGPURenderPipeline Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _graphicsipeline;
    }

    public unsafe WebGPUGraphicsPipeline(WGPUDevice nativeDevice, in GraphicsPipelineDescriptor descriptor)
    {
        Name = descriptor.Name;
        // Create shader modules
        if (!UtilsDescriptor.IsGraphicsShader(descriptor.ShaderStages, out ShaderStageSource vertex, out ShaderStageSource pixel))
        {
            throw new ArgumentException("The shader stages must contain a vertex and a pixel shader when creating a graphics pipeline");
        }

        _stages = ShaderStage.None;
        for (int i = 0; i < descriptor.ShaderStages.Length; i++)
        {
            _stages |= descriptor.ShaderStages[i].Stage;
        }

        WGPUShaderModule vertexShader = nativeDevice.CreateShaderModule(vertex);
        WGPUShaderModule pixelShader = nativeDevice.CreateShaderModule(pixel);

        // Parse vertex input layouts
        VertexInputLayout[] vertexInputLayouts = descriptor.VertexInputLayouts;
        int vertexElementCount = 0;

        // !! memory alloc attention
        WGPUVertexBufferLayout* vertexBufferLayouts = stackalloc WGPUVertexBufferLayout[vertexInputLayouts.Length];

        for (int i = 0; i < vertexInputLayouts.Length; i++)
        {
            vertexElementCount += vertexInputLayouts[i].Elements.Length;
            vertexBufferLayouts[i] = new WGPUVertexBufferLayout()
            {
                arrayStride = vertexInputLayouts[i].Stride,
                stepMode = UtilsWebGPU.VertexStepModeToWebGPU(vertexInputLayouts[i].StepMode),
                attributeCount = (uint)vertexInputLayouts[i].Elements.Length,
                attributes = null,
            };
        }

        // !! memory alloc attention
        WGPUVertexAttribute* vertexAttributes = stackalloc WGPUVertexAttribute[vertexElementCount];

        for (int i = 0; i < vertexInputLayouts.Length; i++)
        {
            vertexBufferLayouts[i].attributes = vertexAttributes;

            for (int j = 0; j < vertexInputLayouts[i].Elements.Length; j++)
            {
                vertexAttributes[j] = UtilsWebGPU.ConvertToWebGPU(vertexInputLayouts[i].Elements[j]);
            }

            vertexAttributes += vertexInputLayouts[i].Elements.Length;
        }

        // TODO: bind groups/pipline layout
        fixed (sbyte* pVertexEntry = vertex.EntryPoint.GetUtf8Span())
        fixed (sbyte* pPixelEntry = pixel.EntryPoint.GetUtf8Span())
        {
            WGPUBlendState blendState = new()
            {
                color = UtilsWebGPU.ConvertToWebGPU(descriptor.BlendState.Color),
                alpha = UtilsWebGPU.ConvertToWebGPU(descriptor.BlendState.Alpha),
            };

            // !! memory alloc attention
            WGPUColorTargetState* targets = stackalloc WGPUColorTargetState[descriptor.ColorFormats.Length];

            for (int i = 0; i < descriptor.ColorFormats.Length; i++)
            {
                targets[i] = new WGPUColorTargetState()
                {
                    format = UtilsWebGPU.PixelFormatToWebGPU(descriptor.ColorFormats[i]),
                    blend = &blendState,
                    writeMask = WGPUColorWriteMask.All,
                };
            }

            // TODO: color targets
            WGPUFragmentState fragmentState = new WGPUFragmentState()
            {
                module = pixelShader,
                entryPoint = pPixelEntry,
                targetCount = (uint)descriptor.ColorFormats.Length,
                targets = targets,
                constantCount = 0,
                constants = null,
            };

            WGPUDepthStencilState depthStencilState = new WGPUDepthStencilState()
            {
                nextInChain = null,
                format = UtilsWebGPU.PixelFormatToWebGPU(descriptor.DepthStencilFormat),
                depthWriteEnabled = descriptor.DepthStencilState.DepthWriteEnabled,
                depthCompare = UtilsWebGPU.CompareFunctionToWebGPU(descriptor.DepthStencilState.DepthCompare),
            };

            WGPURenderPipelineDescriptor pipelineDescriptor = new WGPURenderPipelineDescriptor()
            {
                vertex = new WGPUVertexState
                {
                    module = vertexShader,
                    entryPoint = pVertexEntry,
                    buffers = vertexBufferLayouts,
                    bufferCount = (uint)vertexInputLayouts.Length,
                    constantCount = 0,
                    constants = null,
                },
                fragment = &fragmentState,
                multisample = new WGPUMultisampleState
                {
                    count = 1,
                    mask = ~0u,
                    alphaToCoverageEnabled = false,
                },
            };

            _graphicsipeline = wgpuDeviceCreateRenderPipeline(nativeDevice, &pipelineDescriptor);
        }

        wgpuShaderModuleRelease(vertexShader);
        wgpuShaderModuleRelease(pixelShader);
    }

    #endregion
}