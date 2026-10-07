using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;
using static Alco.Graphics.InteropUtility;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Owns a native graphics pipeline and releases transient creation data on failure.</summary>
internal sealed unsafe class AlcoGpuGraphicsPipeline : GPUPipeline
{
    #region Properties
    private AlcoGPU.GraphicsPipelineHandle _pipeline;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        try
        {
            AlcoGPU.GraphicsPipelineHandle handle = _pipeline;
            _pipeline = AlcoGPU.GraphicsPipelineHandle.Null;
            if (!handle.IsNull)
            {
                try
                {
                    AlcoGpuNative.GraphicsPipelineDestroy(handle);
                }
                finally
                {
                }
            }
        }
        finally
        {
            GC.KeepAlive(this);
        }
    }

    #endregion

    #region AlcoGpu Implementation

    /// <summary>Gets the native graphics pipeline handle.</summary>
    public AlcoGPU.GraphicsPipelineHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipeline;
    }

    internal AlcoGpuGraphicsPipeline(AlcoGpuDevice device, in GraphicsPipelineDescriptor descriptor) : base(descriptor)
    {
        try
        {
            Device = device;
            AlcoGPU.DeviceHandle nativeDevice = device.Native;

            // === Create transient shader modules ===============================

            DescriptorUtility.GetVertexAndPixelModules(descriptor.ShaderModules, out ShaderModule vertex, out ShaderModule pixel);

            AlcoGPU.ShaderModuleHandle vertexShader = device.CreateShaderModule(vertex);
            AlcoGPU.ShaderModuleHandle pixelShader = AlcoGPU.ShaderModuleHandle.Null;
            AlcoGPU.VertexElement* vertexElements = null;

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
                vertexElements = Alloc<AlcoGPU.VertexElement>(Math.Max(vertexElementCount, 1));
                Span<AlcoGPU.VertexLayout> vertexLayoutStorage = vertexInputLayouts.Length <= 32
                    ? stackalloc AlcoGPU.VertexLayout[vertexInputLayouts.Length] : new AlcoGPU.VertexLayout[vertexInputLayouts.Length];
                AlcoGPU.VertexElement* elementCursor = vertexElements;
                for (int i = 0; i < vertexInputLayouts.Length; i++)
                {
                    vertexLayoutStorage[i] = new AlcoGPU.VertexLayout
                    {
                        Stride = vertexInputLayouts[i].Stride,
                        StepMode = vertexInputLayouts[i].StepMode,
                        Elements = elementCursor,
                        ElementCount = (uint)vertexInputLayouts[i].Elements.Length,
                    };

                    for (int j = 0; j < vertexInputLayouts[i].Elements.Length; j++)
                    {
                        elementCursor[j] = new AlcoGPU.VertexElement
                        {
                            Location = vertexInputLayouts[i].Elements[j].Location,
                            Offset = vertexInputLayouts[i].Elements[j].Offset,
                            Format = vertexInputLayouts[i].Elements[j].Format,
                        };
                    }

                    elementCursor += vertexInputLayouts[i].Elements.Length;
                }

                // === Bind group layouts ======================================

                GPUBindGroup[] bindGroups = descriptor.BindGroups;
                Span<AlcoGPU.BindGroupLayoutHandle> bindGroupLayoutStorage = bindGroups.Length <= 64
                    ? stackalloc AlcoGPU.BindGroupLayoutHandle[bindGroups.Length] : new AlcoGPU.BindGroupLayoutHandle[bindGroups.Length];
                for (int i = 0; i < bindGroups.Length; i++)
                {
                    bindGroupLayoutStorage[i] = ((AlcoGpuBindGroup)bindGroups[i]).Native;
                }

                // === Color formats ======================================

                Span<uint> colorFormatStorage = descriptor.ColorFormats.Length <= 64
                    ? stackalloc uint[descriptor.ColorFormats.Length] : new uint[descriptor.ColorFormats.Length];
                for (int i = 0; i < descriptor.ColorFormats.Length; i++)
                {
                    colorFormatStorage[i] = (uint)descriptor.ColorFormats[i];
                }

                // FragmentOutputCount follows the managed semantics: a value at or
                // above the target count writes all targets; the native side masks
                // the extra targets' color writes otherwise.
                uint fragmentOutputCount = descriptor.FragmentOutputCount >= descriptor.ColorFormats.Length
                    ? AlcoGPU.None
                    : (uint)descriptor.FragmentOutputCount;

                ReadOnlySpan<byte> vertexEntry = vertex.EntryPoint.Utf8Z();
                ReadOnlySpan<byte> pixelEntry = pixel.EntryPoint.Utf8Z();
                ReadOnlySpan<byte> nameSpan = Name.Utf8Z();

                fixed (byte* pVertexEntry = vertexEntry)
                fixed (byte* pPixelEntry = pixelEntry)
                fixed (byte* pName = nameSpan)
                fixed (AlcoGPU.VertexLayout* vertexBufferLayouts = vertexLayoutStorage)
                fixed (AlcoGPU.BindGroupLayoutHandle* bindGroupLayouts = bindGroupLayoutStorage)
                fixed (uint* colorFormats = colorFormatStorage)
                {
                    AlcoGPU.GraphicsPipelineDesc desc = new()
                    {
                        BindGroupLayouts = bindGroupLayouts,
                        BindGroupLayoutCount = (uint)bindGroups.Length,
                        VertexModule = vertexShader,
                        VertexEntry = pVertexEntry,
                        FragmentModule = pixelShader,
                        FragmentEntry = pPixelEntry,
                        VertexLayouts = vertexBufferLayouts,
                        VertexLayoutCount = (uint)vertexInputLayouts.Length,
                        FillMode = descriptor.RasterizerState.FillMode,
                        CullMode = descriptor.RasterizerState.CullMode,
                        FrontFace = descriptor.RasterizerState.FrontFace,
                        Blend = new AlcoGPU.BlendState
                        {
                            Color = ToAbi(descriptor.BlendState.Color),
                            Alpha = ToAbi(descriptor.BlendState.Alpha),
                        },
                        DepthStencil = new AlcoGPU.DepthStencilState
                        {
                            DepthWriteEnabled = descriptor.DepthStencilState.DepthWriteEnabled ? AlcoGPU.True : AlcoGPU.False,
                            DepthBoundsTestEnabled = descriptor.DepthStencilState.DepthBoundsTestEnabled ? AlcoGPU.True : AlcoGPU.False,
                            DepthCompare = descriptor.DepthStencilState.DepthCompare,
                            Front = ToAbi(descriptor.DepthStencilState.FrontFace),
                            Back = ToAbi(descriptor.DepthStencilState.BackFace),
                            StencilReadMask = descriptor.DepthStencilState.StencilReadMask,
                            StencilWriteMask = descriptor.DepthStencilState.StencilWriteMask,
                        },
                        DepthStencilFormat = descriptor.DepthStencilFormat.HasValue
                            ? (uint)descriptor.DepthStencilFormat.Value
                            : AlcoGPU.None,
                        Topology = descriptor.PrimitiveTopology,
                        ColorFormats = colorFormats,
                        ColorFormatCount = (uint)descriptor.ColorFormats.Length,
                        FragmentOutputCount = fragmentOutputCount,
                        ImmediateSize = descriptor.PushConstantsSize,
                        Name = pName,
                    };

                    AlcoGpuNative.GraphicsPipelineCreate(nativeDevice, in desc, out _pipeline);
                    GC.KeepAlive(bindGroups);
                }
            }
            finally
            {
                Free(vertexElements);
                // The pipeline layout is built internally and dropped natively, so the
                // transient modules can be released right after creation.
                try
                {
                    device.DestroyShaderModule(vertexShader);
                }
                finally
                {
                    device.DestroyShaderModule(pixelShader);
                }
            }

        }
        catch
        {
            try { Destroy(false); }
            catch { /* Preserve the construction failure. */ }
            throw;
        }
        finally
        {
            GC.KeepAlive(this);
            GC.KeepAlive(device);
            GC.KeepAlive(descriptor.BindGroups);
        }
    }

    private static AlcoGPU.BlendComponent ToAbi(BlendComponent component)
    {
        return new AlcoGPU.BlendComponent
        {
            SrcFactor = component.SrcFactor,
            DstFactor = component.DstFactor,
            Operation = component.Operation,
        };
    }

    private static AlcoGPU.StencilFace ToAbi(StencilFaceState face)
    {
        return new AlcoGPU.StencilFace
        {
            Compare = face.Compare,
            StencilFailOp = face.StencilFailOperation,
            DepthFailOp = face.DepthFailOperation,
            PassOp = face.PassOperation,
        };
    }

    #endregion
}
