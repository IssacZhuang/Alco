
using WebGPU;
using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

public class WebGPUGraphicsPipeline : GPUGraphicsPipeline
{
    private readonly string _name = "Unnamed Graphics Pipeline";
    public override string Name => _name;
    private readonly WGPURenderPipeline _pipeline;

    public unsafe WebGPUGraphicsPipeline(WGPUDevice nativeDevice, in GraphicsPipelineDescriptor descriptor)
    {
        _name = descriptor.Name;
        // Create shader modules
        if (!UtilsDescriptor.IsGraphicsShader(descriptor.ShaderStages, out ShaderStageSource vertex, out ShaderStageSource pixel))
        {
            throw new ArgumentException("The shader stages must contain a vertex and a pixel shader.");
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



        fixed (sbyte* pVertexEntry = vertex.EntryPoint.GetUtf8Span())
        fixed (sbyte* pPixelEntry = pixel.EntryPoint.GetUtf8Span())
        {
            WGPUBlendState blendState = new()
            {
                color = UtilsWebGPU.ConvertToWebGPU(descriptor.BlendState.Color),
                alpha = UtilsWebGPU.ConvertToWebGPU(descriptor.BlendState.Alpha),
            };

            // TODO: color targets
            WGPUFragmentState fragmentState = new()
            {
                module = pixelShader,
                entryPoint = pPixelEntry,
                targetCount = (uint)descriptor.ColorFormats.Length,
                targets = null,
                constantCount = 0,
                constants = null,
            };

            WGPUDepthStencilState depthStencilState = new()
            {
                nextInChain = null,
                format = UtilsWebGPU.PixelFormatToWebGPU(descriptor.DepthStencilFormat),
                depthWriteEnabled = descriptor.DepthStencilState.DepthWriteEnable,
                depthCompare = UtilsWebGPU.CompareFunctionToWebGPU(descriptor.DepthStencilState.DepthCompare),
            };

            WGPURenderPipelineDescriptor pipelineDescriptor = new()
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

            _pipeline = wgpuDeviceCreateRenderPipeline(nativeDevice, &pipelineDescriptor);
        }

        wgpuShaderModuleRelease(vertexShader);
        wgpuShaderModuleRelease(pixelShader);
    }

    protected override void Dispose(bool disposing)
    {
        wgpuRenderPipelineRelease(_pipeline);
    }
}