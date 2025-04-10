
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal sealed class WebGPUGraphicsPipeline : GPUPipeline
{
    #region Properties
    private readonly WGPURenderPipeline _graphicsipeline;
    private readonly ShaderStage _stages;

    #endregion

    #region Abstract Implementation

    protected override GPUDevice Device { get; }

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

    

    public unsafe WebGPUGraphicsPipeline(WebGPUDevice device, in GraphicsPipelineDescriptor descriptor): base(descriptor)
    {
        Device = device;

        WGPUDevice nativeDevice = device.Native;

        // === Create shader modules ===============================

        UtilsDescriptor.GetVertexAndPixelModules(descriptor.ShaderModules, out ShaderModule vertex, out ShaderModule pixel);

        _stages = ShaderStage.None;
        for (int i = 0; i < descriptor.ShaderModules.Length; i++)
        {
            _stages |= descriptor.ShaderModules[i].Stage;
        }

        WGPUShaderModule vertexShader = nativeDevice.CreateShaderModule(vertex);
        WGPUShaderModule pixelShader = nativeDevice.CreateShaderModule(pixel);
        //wgpuShaderModuleGetCompilationInfo(vertexShader, &ShaderCompileErrorCallback, 0);

        // === Create vertex layout ======================================


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

        for (int i = 0; i < vertexInputLayouts.Length; i++)
        {
            vertexBufferLayouts[i].attributes = vertexAttributes;

            for (int j = 0; j < vertexInputLayouts[i].Elements.Length; j++)
            {
                vertexAttributes[j] = UtilsWebGPU.ConvertToWebGPU(vertexInputLayouts[i].Elements[j]);
            }

            vertexAttributes += vertexInputLayouts[i].Elements.Length;
        }

        ReadOnlySpan<byte> vertexEntry = vertex.EntryPoint.GetUtf8Span();
        ReadOnlySpan<byte> pixelEntry = pixel.EntryPoint.GetUtf8Span();

        fixed (byte* pVertexEntry = vertexEntry)
        fixed (byte* pPixelEntry = pixelEntry)
        {


            // === Fragment state ======================================


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
                entryPoint = new WGPUStringView(pPixelEntry, pixelEntry.Length),
                targetCount = (uint)descriptor.ColorFormats.Length,
                targets = targets,
                constantCount = 0,
                constants = null,
            };


            // === Vertex state ======================================


            WGPUVertexState vertexState = new WGPUVertexState
            {
                module = vertexShader,
                entryPoint = new WGPUStringView(pVertexEntry, vertexEntry.Length),
                buffers = vertexBufferLayouts,
                bufferCount = (uint)vertexInputLayouts.Length,
                constantCount = 0,
                constants = null,
            };


            // === Create pipeline layout ======================================


            WGPUBindGroupLayout* bindGroupLayouts = stackalloc WGPUBindGroupLayout[descriptor.BindGroups.Length];

            for (int i = 0; i < descriptor.BindGroups.Length; i++)
            {
                bindGroupLayouts[i] = ((WebGPUBindGroup)descriptor.BindGroups[i]).Native;
            }

            WGPUPipelineLayoutDescriptor pipelineLayoutDescriptor = new WGPUPipelineLayoutDescriptor
            {
                nextInChain = null,
                label = WGPUStringView.Empty,
                bindGroupLayoutCount = (uint)descriptor.BindGroups.Length,
                bindGroupLayouts = bindGroupLayouts,
            };

            if (descriptor.PushConstantsRanges != null)
            {
                WGPUPushConstantRange* pushConstants = stackalloc WGPUPushConstantRange[descriptor.PushConstantsRanges.Length];
                for (int i = 0; i < descriptor.PushConstantsRanges.Length; i++)
                {
                    PushConstantsRange range = descriptor.PushConstantsRanges[i];
                    pushConstants[i] = new WGPUPushConstantRange
                    {

                        stages = UtilsWebGPU.ConvertShaderStage(range.Stage),
                        start = range.Start,
                        end = range.End
                    };
                }
                WGPUPipelineLayoutExtras extras = new WGPUPipelineLayoutExtras
                {
                    chain = new WGPUChainedStruct
                    {
                        sType = (WGPUSType)WGPUNativeSType.PipelineLayoutExtras,
                        next = null,
                    },
                    pushConstantRangeCount = (uint)descriptor.PushConstantsRanges.Length,
                    pushConstantRanges = pushConstants,
                };

                pipelineLayoutDescriptor.nextInChain = &extras.chain;
            }

            WGPUPipelineLayout pipelineLayout = wgpuDeviceCreatePipelineLayout(nativeDevice, &pipelineLayoutDescriptor);


            // === Primitive State ======================================


            WGPUPrimitiveState primitiveState = new WGPUPrimitiveState
            {
                topology = UtilsWebGPU.PrimitiveTopologyToWebGPU(descriptor.PrimitiveTopology),
                // TODO : strip index format
                stripIndexFormat = WGPUIndexFormat.Undefined,
                frontFace = UtilsWebGPU.FrontFaceToWebGPU(descriptor.RasterizerState.FrontFace),
                cullMode = UtilsWebGPU.CullModeToWebGPU(descriptor.RasterizerState.CullMode),
            };

            if (descriptor.PrimitiveTopology == PrimitiveTopology.TriangleStrip || descriptor.PrimitiveTopology == PrimitiveTopology.LineStrip)
            {
                primitiveState.stripIndexFormat = WGPUIndexFormat.Uint32;
            }

            if (descriptor.PrimitiveTopology == PrimitiveTopology.LineList)
            {
                primitiveState.cullMode = WGPUCullMode.None;
            }



            // === Create pipeline ======================================


            WGPURenderPipelineDescriptor pipelineDescriptor = new WGPURenderPipelineDescriptor()
            {
                vertex = vertexState,
                fragment = &fragmentState,
                layout = pipelineLayout,
                primitive = primitiveState,
                multisample = new WGPUMultisampleState
                {
                    count = 1,
                    mask = ~0u,
                    alphaToCoverageEnabled = false,
                },
            };


            // === Set depth stencil state if exist ======================================


            if (descriptor.DepthStencilFormat.HasValue)
            {
                WGPUDepthStencilState depthStencilState = new WGPUDepthStencilState()
                {
                    nextInChain = null,
                    format = UtilsWebGPU.PixelFormatToWebGPU(descriptor.DepthStencilFormat.Value),
                    depthWriteEnabled = descriptor.DepthStencilState.DepthWriteEnabled ? WGPUOptionalBool.True : WGPUOptionalBool.False,
                    depthCompare = UtilsWebGPU.CompareFunctionToWebGPU(descriptor.DepthStencilState.DepthCompare),
                    stencilFront = UtilsWebGPU.ConvertToWebGPU(descriptor.DepthStencilState.FrontFace),
                    stencilBack = UtilsWebGPU.ConvertToWebGPU(descriptor.DepthStencilState.BackFace),
                    stencilReadMask = descriptor.DepthStencilState.StencilReadMask,
                    stencilWriteMask = descriptor.DepthStencilState.StencilReadMask,
                };

                pipelineDescriptor.depthStencil = &depthStencilState;
            }

            _graphicsipeline = wgpuDeviceCreateRenderPipeline(nativeDevice, &pipelineDescriptor);

        }

        wgpuShaderModuleRelease(vertexShader);
        wgpuShaderModuleRelease(pixelShader);
    }

    #endregion
}