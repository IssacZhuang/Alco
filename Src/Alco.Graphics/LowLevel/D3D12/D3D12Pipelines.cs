// D3D12 graphics, mesh, and compute pipelines with lock-free warmed depth/stencil variant lookup.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using static D3D12.D3D12Native;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class D3D12Pso : GPUPipeline
{
    internal required D3D12Device Owner;
    internal nint Native;
    internal bool Compute;
    internal bool Mesh;
    internal PrimitiveTopology Topology;
    internal uint NativeTopology;

    private struct Variant
    {
        internal ulong Key;
        internal nint Native;
    }

    private readonly Lock CacheLock = new();
    private Variant[] Variants = [];
    private int VariantCount;
    private byte* ShaderCode;
    private PipelineGraphicsStream Graphics;
    private PipelineMeshStream MeshPipeline;
    private bool HasDepth;
    private bool HasStencil;

    private PipelineShaderBytecode CopyShader(ReadOnlySpan<byte> code, ref nuint offset)
    {
        if (code.IsEmpty) return default;
        fixed (byte* source = code)
        {
            Debug.Assert(code.Length >= 32 && *(uint*)source == 0x43425844, "D3D12 shader stages require DXIL containers");
            Backend.NativeCopy(ShaderCode + offset, source, (nuint)code.Length);
        }
        PipelineShaderBytecode result = new() { Code = ShaderCode + offset, Length = (nuint)code.Length };
        offset += (nuint)code.Length;
        return result;
    }

    internal void InitializeGraphics(GraphicsPipelineDesc desc)
    {
        Debug.Assert(!desc.Vertex.Code.IsEmpty);
        HasDepth = desc.DepthFormat != Format.Undefined;
        HasStencil = desc.StencilFormat != Format.Undefined;
        ShaderCode = (byte*)NativeMemory.Alloc((nuint)desc.Vertex.Code.Length + (nuint)desc.Pixel.Code.Length);
        nuint offset = 0;
        Graphics = new PipelineGraphicsStream
        {
            Root = new PipelineRootSubobject { Value = Owner.RootSignature },
            Vertex = new PipelineShaderSubobject { Type = 1, Value = CopyShader(desc.Vertex.Code, ref offset) },
            Pixel = new PipelineShaderSubobject { Type = 2, Value = CopyShader(desc.Pixel.Code, ref offset) },
            StripCut = new PipelineUintSubobject { Type = 13 },
            Output = OutputStream(desc.ColorTargets, desc.DepthFormat, desc.StencilFormat, desc.Rasterization, desc.Topology, desc.SampleCount),
        };
        Native = GetPipeline(new DepthStencilState());
        if (Topology is PrimitiveTopology.LineStrip or PrimitiveTopology.TriangleStrip) GetPipeline(new DepthStencilState(), IndexType.UInt32);
    }

    internal void InitializeMesh(MeshPipelineDesc desc)
    {
        Debug.Assert(!desc.Mesh.Code.IsEmpty);
        HasDepth = desc.DepthFormat != Format.Undefined;
        HasStencil = desc.StencilFormat != Format.Undefined;
        ShaderCode = (byte*)NativeMemory.Alloc((nuint)desc.Task.Code.Length + (nuint)desc.Mesh.Code.Length + (nuint)desc.Pixel.Code.Length);
        nuint offset = 0;
        MeshPipeline = new PipelineMeshStream
        {
            Root = new PipelineRootSubobject { Value = Owner.RootSignature },
            Task = new PipelineShaderSubobject { Type = 24, Value = CopyShader(desc.Task.Code, ref offset) },
            Mesh = new PipelineShaderSubobject { Type = 25, Value = CopyShader(desc.Mesh.Code, ref offset) },
            Pixel = new PipelineShaderSubobject { Type = 2, Value = CopyShader(desc.Pixel.Code, ref offset) },
            Output = OutputStream(desc.ColorTargets, desc.DepthFormat, desc.StencilFormat, desc.Rasterization,
                PrimitiveTopology.TriangleList, desc.SampleCount),
        };
        Native = GetPipeline(new DepthStencilState());
    }

    internal void InitializeCompute(ShaderStage compute)
    {
        Debug.Assert(!compute.Code.IsEmpty);
        ShaderCode = (byte*)NativeMemory.Alloc((nuint)compute.Code.Length);
        nuint offset = 0;
        PipelineComputeStream stream = new()
        {
            Root = new PipelineRootSubobject { Value = Owner.RootSignature },
            Compute = new PipelineShaderSubobject { Type = 6, Value = CopyShader(compute.Code, ref offset) },
        };
        D3D12Interop.Check(PipelineCreateState(Owner.Native, &stream, (nuint)sizeof(PipelineComputeStream), out Native), "CreatePipelineState (compute)");
    }

    private static uint BlendFactor(BlendFactor factor, bool alpha = false) => factor switch
    {
        global::Alco.Graphics.BlendFactor.Zero => 1,
        global::Alco.Graphics.BlendFactor.One => 2,
        global::Alco.Graphics.BlendFactor.SourceColor => alpha ? 5u : 3u,
        global::Alco.Graphics.BlendFactor.OneMinusSourceColor => alpha ? 6u : 4u,
        global::Alco.Graphics.BlendFactor.SourceAlpha => 5,
        global::Alco.Graphics.BlendFactor.OneMinusSourceAlpha => 6,
        global::Alco.Graphics.BlendFactor.DestinationAlpha => 7,
        global::Alco.Graphics.BlendFactor.OneMinusDestinationAlpha => 8,
        global::Alco.Graphics.BlendFactor.DestinationColor => alpha ? 7u : 9u,
        global::Alco.Graphics.BlendFactor.OneMinusDestinationColor => alpha ? 8u : 10u,
        global::Alco.Graphics.BlendFactor.SourceAlphaSaturate => alpha ? 2u : 11u,
        _ => 1,
    };

    private static PipelineOutputStream OutputStream(ReadOnlySpan<ColorTargetDesc> targets, Format depth, Format stencil,
        RasterizationState rasterization, PrimitiveTopology topology, uint sampleCount)
    {
        Debug.Assert(targets.Length <= Backend.MaxColorAttachments);
        Debug.Assert(sampleCount is 1 or 2 or 4 or 8);
        int depthFormat = D3D12Interop.Format(depth);
        int stencilFormat = D3D12Interop.Format(stencil);
        if (depth != Format.Undefined && stencil != Format.Undefined && depthFormat != stencilFormat)
            throw D3D12Interop.CreationFailure(0, "pipelines require a shared native depth/stencil attachment format", Error.Unsupported);
        PipelineOutputStream result = new()
        {
            Blend = new PipelineBlendSubobject { Type = 8, Value = new PipelineBlendDesc { IndependentBlendEnable = 1 } },
            SampleMask = new PipelineUintSubobject { Type = 9, Value = uint.MaxValue },
            Rasterizer = new PipelineRasterizerSubobject
            {
                Type = 27,
                Value = new PipelineRasterizerDesc1
                {
                    FillMode = rasterization.Fill == FillMode.Wireframe ? 2u : 3u,
                    CullMode = rasterization.Cull == CullMode.None ? 1u : rasterization.Cull == CullMode.Clockwise ? 3u : 2u,
                    FrontCounterClockwise = 1,
                    DepthBias = rasterization.DepthBiasConstant,
                    DepthBiasClamp = rasterization.DepthBiasClamp,
                    SlopeScaledDepthBias = rasterization.DepthBiasSlope,
                    DepthClipEnable = 1,
                },
            },
            DepthStencil = new PipelineDepthStencilSubobject { Type = 26 },
            Topology = new PipelineUintSubobject
            {
                Type = 14,
                Value = topology == PrimitiveTopology.PointList ? 1u :
                    topology is PrimitiveTopology.LineList or PrimitiveTopology.LineStrip ? 2u : 3u,
            },
            Formats = new PipelineFormatsSubobject { Type = 15, Count = (uint)targets.Length },
            DepthFormat = new PipelineUintSubobject { Type = 16, Value = (uint)(depthFormat != 0 ? depthFormat : stencilFormat) },
            Samples = new PipelineSampleSubobject { Type = 17, Count = sampleCount },
        };
        for (int i = 0; i < 8; ++i)
        {
            ColorTargetDesc target = i < targets.Length ? targets[i] : new ColorTargetDesc();
            result.Blend.Value.RenderTargets[i] = new PipelineRenderTargetBlendDesc
            {
                BlendEnable = target.Blend.Enabled ? 1 : 0,
                SrcBlend = BlendFactor(target.Blend.Color.Source),
                DestBlend = BlendFactor(target.Blend.Color.Destination),
                BlendOp = (uint)target.Blend.Color.Operation + 1,
                SrcBlendAlpha = BlendFactor(target.Blend.Alpha.Source, true),
                DestBlendAlpha = BlendFactor(target.Blend.Alpha.Destination, true),
                BlendOpAlpha = (uint)target.Blend.Alpha.Operation + 1,
                RenderTargetWriteMask = target.WriteMask,
            };
            result.Formats.Formats[i] = D3D12Interop.Format(target.Format);
        }
        return result;
    }

    private ulong StateKey(DepthStencilState state, IndexType indices)
    {
        ulong key = 0;
        if (HasDepth && state.DepthTest) key = 1ul | (state.DepthWrite ? 2ul : 0ul) | (ulong)state.DepthCompare << 2;
        if (HasStencil && state.StencilTest)
        {
            key |= 1ul << 5 | (ulong)state.StencilReadMask << 6 | (ulong)state.StencilWriteMask << 14;
            key |= (ulong)state.Front.Compare << 22 | (ulong)state.Front.Fail << 25 | (ulong)state.Front.Pass << 28 | (ulong)state.Front.DepthFail << 31;
            key |= (ulong)state.Back.Compare << 34 | (ulong)state.Back.Fail << 37 | (ulong)state.Back.Pass << 40 | (ulong)state.Back.DepthFail << 43;
        }
        if (Topology is PrimitiveTopology.LineStrip or PrimitiveTopology.TriangleStrip && indices == IndexType.UInt32) key |= 1ul << 46;
        return key;
    }

    private static PipelineStencilOpDesc1 StencilDescriptor(StencilFaceState face, byte readMask, byte writeMask)
    {
        return new PipelineStencilOpDesc1
        {
            StencilFailOp = (uint)face.Fail + 1,
            StencilDepthFailOp = (uint)face.DepthFail + 1,
            StencilPassOp = (uint)face.Pass + 1,
            StencilFunc = (uint)face.Compare + 1,
            StencilReadMask = readMask,
            StencilWriteMask = writeMask,
        };
    }

    private nint CreateVariant(DepthStencilState state, IndexType indices)
    {
        bool depth = HasDepth && state.DepthTest;
        bool stencil = HasStencil && state.StencilTest;
        PipelineDepthStencilDesc2 depthStencil = new()
        {
            DepthEnable = depth ? 1 : 0,
            DepthWriteMask = depth && state.DepthWrite ? 1u : 0u,
            DepthFunc = depth ? (uint)state.DepthCompare + 1 : 8u,
            StencilEnable = stencil ? 1 : 0,
            FrontFace = StencilDescriptor(stencil ? state.Front : new StencilFaceState(), state.StencilReadMask, state.StencilWriteMask),
            BackFace = StencilDescriptor(stencil ? state.Back : new StencilFaceState(), state.StencilReadMask, state.StencilWriteMask),
        };
        nint result = 0;
        int hr;
        if (Mesh)
        {
            PipelineMeshStream stream = MeshPipeline;
            stream.Output.DepthStencil.Value = depthStencil;
            hr = PipelineCreateState(Owner.Native, &stream, (nuint)sizeof(PipelineMeshStream), out result);
        }
        else
        {
            PipelineGraphicsStream stream = Graphics;
            stream.Output.DepthStencil.Value = depthStencil;
            if (Topology is PrimitiveTopology.LineStrip or PrimitiveTopology.TriangleStrip) stream.StripCut.Value = indices == IndexType.UInt16 ? 1u : 2u;
            hr = PipelineCreateState(Owner.Native, &stream, (nuint)sizeof(PipelineGraphicsStream), out result);
        }
        if (hr < 0)
        {
            Release(result);
            D3D12Interop.Check(hr, Mesh ? "CreatePipelineState (mesh)" : "CreatePipelineState (graphics)");
        }
        return result;
    }

    internal nint GetPipeline(DepthStencilState state, IndexType indices = IndexType.UInt16)
    {
        if (Compute) return Native;
        ulong key = StateKey(state, indices);
        int count = Volatile.Read(ref VariantCount);
        Variant[] entries = Volatile.Read(ref Variants);
        for (int i = 0; i < count; ++i)
            if (entries[i].Key == key) return entries[i].Native;
        lock (CacheLock)
        {
            count = VariantCount;
            entries = Variants;
            for (int i = 0; i < count; ++i)
                if (entries[i].Key == key) return entries[i].Native;
            nint pipeline = CreateVariant(state, indices);
            if (count == entries.Length)
            {
                Variant[] replacement = new Variant[Math.Max(8, count * 2)];
                Array.Copy(entries, replacement, count);
                entries = replacement;
                Volatile.Write(ref Variants, entries);
            }
            entries[count] = new Variant { Key = key, Native = pipeline };
            Volatile.Write(ref VariantCount, count + 1);
            return pipeline;
        }
    }

    /// <summary>Releases every native pipeline variant after all recorded and executing uses have finished.</summary>
    public override void Destroy()
    {
        if (Compute) Release(Native);
        else
            for (int i = 0; i < VariantCount; ++i) Release(Variants[i].Native);
        NativeMemory.Free(ShaderCode);
    }
}

internal sealed unsafe partial class D3D12Device
{
    /// <summary>Builds a vertex pipeline with an optional pixel stage.</summary>
    /// <param name="desc">The shaders, attachment formats, and rasterization configuration.</param>
    /// <returns>The graphics pipeline.</returns>
    public override GPUPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        Backend.RequireRasterSamples(desc.SampleCount, desc.ColorTargets, desc.DepthFormat, desc.StencilFormat);
        D3D12Pso result = new()
        {
            Owner = this,
            Topology = desc.Topology,
            NativeTopology = desc.Topology switch
            {
                PrimitiveTopology.PointList => 1,
                PrimitiveTopology.LineList => 2,
                PrimitiveTopology.LineStrip => 3,
                PrimitiveTopology.TriangleStrip => 5,
                _ => 4,
            },
        };
        try
        {
            result.InitializeGraphics(desc);
            return result;
        }
        catch (GPUException)
        {
            result.Destroy();
            throw;
        }
    }

    /// <summary>Builds a mesh pipeline with optional amplification and pixel stages.</summary>
    /// <param name="desc">The shaders, attachment formats, and rasterization configuration.</param>
    /// <returns>The mesh pipeline.</returns>
    public override GPUPipeline CreateMeshPipeline(MeshPipelineDesc desc)
    {
        Backend.RequireRasterSamples(desc.SampleCount, desc.ColorTargets, desc.DepthFormat, desc.StencilFormat);
        D3D12Pso result = new() { Owner = this, Mesh = true, NativeTopology = 4 };
        try
        {
            result.InitializeMesh(desc);
            return result;
        }
        catch (GPUException)
        {
            result.Destroy();
            throw;
        }
    }

    /// <summary>Builds a compute pipeline from a DXIL shader.</summary>
    /// <param name="compute">The compute shader and its threadgroup dimensions.</param>
    /// <returns>The compute pipeline.</returns>
    public override GPUPipeline CreateComputePipeline(ShaderStage compute)
    {
        D3D12Pso result = new() { Owner = this, Compute = true };
        try
        {
            result.InitializeCompute(compute);
            return result;
        }
        catch (GPUException)
        {
            result.Destroy();
            throw;
        }
    }
}
