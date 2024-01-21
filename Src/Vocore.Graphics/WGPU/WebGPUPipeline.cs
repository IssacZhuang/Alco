
using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUGraphicsPipeline : GPUPipeline
{
    public override string Name { get; }
    private readonly WGPURenderPipeline _graphicsipeline;
    private readonly ShaderStage _stages;

    public WGPURenderPipeline Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _graphicsipeline;
    }

    public override ShaderStage Stages
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _stages;
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

        WGPUVertexAttribute* vertexAttributes = stackalloc WGPUVertexAttribute[vertexElementCount];
        WGPUVertexAttribute* vertexAttributePtr = vertexAttributes;

        for (int i = 0; i < vertexInputLayouts.Length; i++)
        {
            vertexBufferLayouts[i].attributes = vertexAttributePtr;

            for (int j = 0; j < vertexInputLayouts[i].Elements.Length; j++)
            {
                vertexAttributePtr[j] = UtilsWebGPU.ConvertToWebGPU(vertexInputLayouts[i].Elements[j]);
            }

            vertexAttributePtr += vertexInputLayouts[i].Elements.Length;
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

            // TODO: color targets
            WGPUFragmentState fragmentState = new WGPUFragmentState()
            {
                module = pixelShader,
                entryPoint = pPixelEntry,
                targetCount = (uint)descriptor.ColorFormats.Length,
                targets = null,
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

    protected override void Dispose(bool disposing)
    {
        wgpuRenderPipelineRelease(_graphicsipeline);
    }
}