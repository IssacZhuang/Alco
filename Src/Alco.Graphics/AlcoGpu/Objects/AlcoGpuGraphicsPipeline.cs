using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;
using static Alco.Graphics.InteropUtility;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Owns a native graphics pipeline and releases transient creation data on failure.</summary>
internal sealed unsafe class AlcoGpuGraphicsPipeline : GPUPipeline
{
    #region Properties
    private AlcoGpuAbi.GraphicsPipelineHandle _pipeline;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        try
        {
            AlcoGpuAbi.GraphicsPipelineHandle handle = _pipeline;
            _pipeline = AlcoGpuAbi.GraphicsPipelineHandle.Null;
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
    public AlcoGpuAbi.GraphicsPipelineHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipeline;
    }

    internal AlcoGpuGraphicsPipeline(AlcoGpuDevice device, in GraphicsPipelineDescriptor descriptor) : base(descriptor)
    {
        try
        {
            Device = device;
            AlcoGpuAbi.DeviceHandle nativeDevice = device.Native;

            // === Create transient shader modules ===============================

            DescriptorUtility.GetVertexAndPixelModules(descriptor.ShaderModules, out ShaderModule vertex, out ShaderModule pixel);

            AlcoGpuAbi.ShaderModuleHandle vertexShader = device.CreateShaderModule(vertex);
            AlcoGpuAbi.ShaderModuleHandle pixelShader = AlcoGpuAbi.ShaderModuleHandle.Null;
            AlcoGpuAbi.VertexElement* vertexElements = null;

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
                vertexElements = Alloc<AlcoGpuAbi.VertexElement>(Math.Max(vertexElementCount, 1));
                Span<AlcoGpuAbi.VertexLayout> vertexLayoutStorage = vertexInputLayouts.Length <= 32
                    ? stackalloc AlcoGpuAbi.VertexLayout[vertexInputLayouts.Length] : new AlcoGpuAbi.VertexLayout[vertexInputLayouts.Length];
                AlcoGpuAbi.VertexElement* elementCursor = vertexElements;
                for (int i = 0; i < vertexInputLayouts.Length; i++)
                {
                    vertexLayoutStorage[i] = new AlcoGpuAbi.VertexLayout
                    {
                        Stride = vertexInputLayouts[i].Stride,
                        StepMode = (uint)vertexInputLayouts[i].StepMode,
                        Elements = elementCursor,
                        ElementCount = (uint)vertexInputLayouts[i].Elements.Length,
                    };

                    for (int j = 0; j < vertexInputLayouts[i].Elements.Length; j++)
                    {
                        elementCursor[j] = new AlcoGpuAbi.VertexElement
                        {
                            Location = vertexInputLayouts[i].Elements[j].Location,
                            Offset = vertexInputLayouts[i].Elements[j].Offset,
                            Format = (uint)vertexInputLayouts[i].Elements[j].Format,
                        };
                    }

                    elementCursor += vertexInputLayouts[i].Elements.Length;
                }

                // === Bind group layouts ======================================

                GPUBindGroup[] bindGroups = descriptor.BindGroups;
                Span<AlcoGpuAbi.BindGroupLayoutHandle> bindGroupLayoutStorage = bindGroups.Length <= 64
                    ? stackalloc AlcoGpuAbi.BindGroupLayoutHandle[bindGroups.Length] : new AlcoGpuAbi.BindGroupLayoutHandle[bindGroups.Length];
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
                    ? AlcoGpuAbi.None
                    : (uint)descriptor.FragmentOutputCount;

                ReadOnlySpan<byte> vertexEntry = vertex.EntryPoint.Utf8Z();
                ReadOnlySpan<byte> pixelEntry = pixel.EntryPoint.Utf8Z();
                ReadOnlySpan<byte> nameSpan = Name.Utf8Z();

                fixed (byte* pVertexEntry = vertexEntry)
                fixed (byte* pPixelEntry = pixelEntry)
                fixed (byte* pName = nameSpan)
                fixed (AlcoGpuAbi.VertexLayout* vertexBufferLayouts = vertexLayoutStorage)
                fixed (AlcoGpuAbi.BindGroupLayoutHandle* bindGroupLayouts = bindGroupLayoutStorage)
                fixed (uint* colorFormats = colorFormatStorage)
                {
                    AlcoGpuAbi.GraphicsPipelineDesc desc = new()
                    {
                        BindGroupLayouts = bindGroupLayouts,
                        BindGroupLayoutCount = (uint)bindGroups.Length,
                        VertexModule = vertexShader,
                        VertexEntry = pVertexEntry,
                        FragmentModule = pixelShader,
                        FragmentEntry = pPixelEntry,
                        VertexLayouts = vertexBufferLayouts,
                        VertexLayoutCount = (uint)vertexInputLayouts.Length,
                        FillMode = (uint)descriptor.RasterizerState.FillMode,
                        CullMode = (uint)descriptor.RasterizerState.CullMode,
                        FrontFace = (uint)descriptor.RasterizerState.FrontFace,
                        Blend = new AlcoGpuAbi.BlendState
                        {
                            Color = ToAbi(descriptor.BlendState.Color),
                            Alpha = ToAbi(descriptor.BlendState.Alpha),
                        },
                        DepthStencil = new AlcoGpuAbi.DepthStencilState
                        {
                            DepthWriteEnabled = descriptor.DepthStencilState.DepthWriteEnabled ? AlcoGpuAbi.True : AlcoGpuAbi.False,
                            DepthBoundsTestEnabled = descriptor.DepthStencilState.DepthBoundsTestEnabled ? AlcoGpuAbi.True : AlcoGpuAbi.False,
                            DepthCompare = (uint)descriptor.DepthStencilState.DepthCompare,
                            Front = ToAbi(descriptor.DepthStencilState.FrontFace),
                            Back = ToAbi(descriptor.DepthStencilState.BackFace),
                            StencilReadMask = descriptor.DepthStencilState.StencilReadMask,
                            StencilWriteMask = descriptor.DepthStencilState.StencilWriteMask,
                        },
                        DepthStencilFormat = descriptor.DepthStencilFormat.HasValue
                            ? (uint)descriptor.DepthStencilFormat.Value
                            : AlcoGpuAbi.None,
                        Topology = (uint)descriptor.PrimitiveTopology,
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

    private static AlcoGpuAbi.BlendComponent ToAbi(BlendComponent component)
    {
        return new AlcoGpuAbi.BlendComponent
        {
            SrcFactor = (uint)component.SrcFactor,
            DstFactor = (uint)component.DstFactor,
            Operation = (uint)component.Operation,
        };
    }

    private static AlcoGpuAbi.StencilFace ToAbi(StencilFaceState face)
    {
        return new AlcoGpuAbi.StencilFace
        {
            Compare = (uint)face.Compare,
            StencilFailOp = (uint)face.StencilFailOperation,
            DepthFailOp = (uint)face.DepthFailOperation,
            PassOp = (uint)face.PassOperation,
        };
    }

    #endregion
}
