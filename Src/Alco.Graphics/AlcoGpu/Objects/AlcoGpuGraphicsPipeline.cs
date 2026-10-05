using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;
using static Alco.Graphics.InteropUtility;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Owns a native graphics pipeline and releases transient creation data on failure.</summary>
internal sealed unsafe class AlcoGpuGraphicsPipeline : GPUPipeline
{
    #region Properties
    private readonly AlcoHandle _pipeline;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        if (!_pipeline.IsNull)
        {
            uint status = AlcoGpuNative.PipelineDestroy(((AlcoGpuDevice)Device).Native, _pipeline);
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    #endregion

    #region AlcoGpu Implementation

    /// <summary>Gets the native graphics pipeline handle.</summary>
    public AlcoHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipeline;
    }

    internal AlcoGpuGraphicsPipeline(AlcoGpuDevice device, in GraphicsPipelineDescriptor descriptor) : base(descriptor)
    {
        Device = device;
        AlcoHandle nativeDevice = device.Native;

        // === Create transient shader modules ===============================

        DescriptorUtility.GetVertexAndPixelModules(descriptor.ShaderModules, out ShaderModule vertex, out ShaderModule pixel);

        AlcoHandle vertexShader = device.CreateShaderModule(vertex);
        AlcoHandle pixelShader = AlcoHandle.Null;
        AlcoVertexElement* vertexElements = null;

        try
        {
            pixelShader = device.CreateShaderModule(pixel);
            // === Vertex layouts ======================================

            VertexInputLayout[] vertexInputLayouts = descriptor.VertexInputLayouts;
            int vertexElementCount = 0;
            for (int i = 0; i < vertexInputLayouts.Length; i++)
            {
                vertexElementCount += vertexInputLayouts[i].Elements.Length;
            }

            // One contiguous block of attributes; each layout points into it.
            vertexElements = Alloc<AlcoVertexElement>(Math.Max(vertexElementCount, 1));
            AlcoVertexLayout* vertexBufferLayouts = stackalloc AlcoVertexLayout[vertexInputLayouts.Length];
            AlcoVertexElement* elementCursor = vertexElements;
            for (int i = 0; i < vertexInputLayouts.Length; i++)
            {
                vertexBufferLayouts[i] = new AlcoVertexLayout
                {
                    Stride = vertexInputLayouts[i].Stride,
                    StepMode = (uint)vertexInputLayouts[i].StepMode,
                    Elements = elementCursor,
                    ElementCount = (uint)vertexInputLayouts[i].Elements.Length,
                };

                for (int j = 0; j < vertexInputLayouts[i].Elements.Length; j++)
                {
                    elementCursor[j] = new AlcoVertexElement
                    {
                        Location = vertexInputLayouts[i].Elements[j].Location,
                        Offset = vertexInputLayouts[i].Elements[j].Offset,
                        Format = (uint)vertexInputLayouts[i].Elements[j].Format,
                    };
                }

                elementCursor += vertexInputLayouts[i].Elements.Length;
            }

            // === Bind group layouts ======================================

            AlcoHandle* bindGroupLayouts = stackalloc AlcoHandle[descriptor.BindGroups.Length];
            for (int i = 0; i < descriptor.BindGroups.Length; i++)
            {
                bindGroupLayouts[i] = ((AlcoGpuBindGroup)descriptor.BindGroups[i]).Native;
            }

            // === Color formats ======================================

            uint* colorFormats = stackalloc uint[descriptor.ColorFormats.Length];
            for (int i = 0; i < descriptor.ColorFormats.Length; i++)
            {
                colorFormats[i] = (uint)descriptor.ColorFormats[i];
            }

            // FragmentOutputCount follows the managed semantics: a value at or
            // above the target count writes all targets; the native side masks
            // the extra targets' color writes otherwise.
            uint fragmentOutputCount = descriptor.FragmentOutputCount >= descriptor.ColorFormats.Length
                ? AlcoGpuAbi.AlcoNone
                : (uint)descriptor.FragmentOutputCount;

            ReadOnlySpan<byte> vertexEntry = vertex.EntryPoint.Utf8Z();
            ReadOnlySpan<byte> pixelEntry = pixel.EntryPoint.Utf8Z();
            ReadOnlySpan<byte> nameSpan = Name.Utf8Z();

            fixed (byte* pVertexEntry = vertexEntry)
            fixed (byte* pPixelEntry = pixelEntry)
            fixed (byte* pName = nameSpan)
            {
                AlcoGraphicsPipelineDesc desc = new()
                {
                    BindGroupLayouts = bindGroupLayouts,
                    BindGroupLayoutCount = (uint)descriptor.BindGroups.Length,
                    VertexModule = vertexShader,
                    VertexEntry = pVertexEntry,
                    FragmentModule = pixelShader,
                    FragmentEntry = pPixelEntry,
                    VertexLayouts = vertexBufferLayouts,
                    VertexLayoutCount = (uint)vertexInputLayouts.Length,
                    FillMode = (uint)descriptor.RasterizerState.FillMode,
                    CullMode = (uint)descriptor.RasterizerState.CullMode,
                    FrontFace = (uint)descriptor.RasterizerState.FrontFace,
                    Blend = new AlcoBlendState
                    {
                        Color = ToAbi(descriptor.BlendState.Color),
                        Alpha = ToAbi(descriptor.BlendState.Alpha),
                    },
                    DepthStencil = new AlcoDepthStencilState
                    {
                        DepthWriteEnabled = descriptor.DepthStencilState.DepthWriteEnabled ? AlcoGpuAbi.AlcoTrue : AlcoGpuAbi.AlcoFalse,
                        DepthBoundsTestEnabled = descriptor.DepthStencilState.DepthBoundsTestEnabled ? AlcoGpuAbi.AlcoTrue : AlcoGpuAbi.AlcoFalse,
                        DepthCompare = (uint)descriptor.DepthStencilState.DepthCompare,
                        Front = ToAbi(descriptor.DepthStencilState.FrontFace),
                        Back = ToAbi(descriptor.DepthStencilState.BackFace),
                        StencilReadMask = descriptor.DepthStencilState.StencilReadMask,
                        StencilWriteMask = descriptor.DepthStencilState.StencilWriteMask,
                    },
                    DepthStencilFormat = descriptor.DepthStencilFormat.HasValue
                        ? (uint)descriptor.DepthStencilFormat.Value
                        : AlcoGpuAbi.AlcoNone,
                    Topology = (uint)descriptor.PrimitiveTopology,
                    ColorFormats = colorFormats,
                    ColorFormatCount = (uint)descriptor.ColorFormats.Length,
                    FragmentOutputCount = fragmentOutputCount,
                    ImmediateSize = descriptor.PushConstantsSize,
                    Name = pName,
                };

                uint status = AlcoGpuNative.GraphicsPipelineCreate(nativeDevice, in desc, out _pipeline);
                AlcoGpuMarshal.ThrowIfFailed(status);
            }
        }
        finally
        {
            Free(vertexElements);
            // The pipeline layout is built internally and dropped natively, so the
            // transient modules can be released right after creation.
            device.DestroyShaderModule(vertexShader);
            device.DestroyShaderModule(pixelShader);
        }
    }

    private static AlcoBlendComponent ToAbi(BlendComponent component)
    {
        return new AlcoBlendComponent
        {
            SrcFactor = (uint)component.SrcFactor,
            DstFactor = (uint)component.DstFactor,
            Operation = (uint)component.Operation,
        };
    }

    private static AlcoStencilFace ToAbi(StencilFaceState face)
    {
        return new AlcoStencilFace
        {
            Compare = (uint)face.Compare,
            StencilFailOp = (uint)face.StencilFailOperation,
            DepthFailOp = (uint)face.DepthFailOperation,
            PassOp = (uint)face.PassOperation,
        };
    }

    #endregion
}
