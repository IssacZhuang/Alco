// D3D12 native descriptor layouts, resource-view calls, and pipeline-state streams.

using System.Runtime.InteropServices;

namespace D3D12;

internal static unsafe partial class D3D12Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceDescriptorHeapDesc
    {
        internal uint Type;
        internal uint NumDescriptors;
        internal uint Flags;
        internal uint NodeMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceMipSrv
    {
        internal uint MostDetailedMip;
        internal uint MipLevels;
        internal float ResourceMinLodClamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceArraySrv
    {
        internal uint MostDetailedMip;
        internal uint MipLevels;
        internal uint FirstArraySlice;
        internal uint ArraySize;
        internal float ResourceMinLodClamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceTexture2DSrv
    {
        internal uint MostDetailedMip;
        internal uint MipLevels;
        internal uint PlaneSlice;
        internal float ResourceMinLodClamp;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceTexture2DArraySrv
    {
        internal uint MostDetailedMip;
        internal uint MipLevels;
        internal uint FirstArraySlice;
        internal uint ArraySize;
        internal uint PlaneSlice;
        internal float ResourceMinLodClamp;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct ResourceShaderResourceViewDesc
    {
        [FieldOffset(0)] internal int Format;
        [FieldOffset(4)] internal uint ViewDimension;
        [FieldOffset(8)] internal uint Shader4ComponentMapping;
        [FieldOffset(16)] internal BufferSrv Buffer;
        [FieldOffset(16)] internal ResourceMipSrv Texture1D;
        [FieldOffset(16)] internal ResourceArraySrv Texture1DArray;
        [FieldOffset(16)] internal ResourceTexture2DSrv Texture2D;
        [FieldOffset(16)] internal ResourceTexture2DArraySrv Texture2DArray;
        [FieldOffset(16)] internal ResourceMipSrv Texture3D;
        [FieldOffset(16)] internal ResourceMipSrv TextureCube;
        [FieldOffset(16)] internal ResourceArraySrv TextureCubeArray;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceTexture2DUav
    {
        internal uint MipSlice;
        internal uint PlaneSlice;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceTexture1DArrayUav
    {
        internal uint MipSlice;
        internal uint FirstArraySlice;
        internal uint ArraySize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceArrayUav
    {
        internal uint MipSlice;
        internal uint FirstArraySlice;
        internal uint ArraySize;
        internal uint PlaneSlice;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceTexture3DUav
    {
        internal uint MipSlice;
        internal uint FirstWSlice;
        internal uint WSize;
    }

    // D3D12_TEX2DMS_ARRAY_* views select one multisampled array slice without mip fields.
    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceTexture2DMSArray
    {
        internal uint FirstArraySlice;
        internal uint ArraySize;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct ResourceUnorderedAccessViewDesc
    {
        [FieldOffset(0)] internal int Format;
        [FieldOffset(4)] internal uint ViewDimension;
        [FieldOffset(8)] internal BufferUav Buffer;
        [FieldOffset(8)] internal uint Texture1DMipSlice;
        [FieldOffset(8)] internal ResourceTexture1DArrayUav Texture1DArray;
        [FieldOffset(8)] internal ResourceTexture2DUav Texture2D;
        [FieldOffset(8)] internal ResourceArrayUav Texture2DArray;
        [FieldOffset(8)] internal ResourceTexture3DUav Texture3D;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceTexture1DArrayRtv
    {
        internal uint MipSlice;
        internal uint FirstArraySlice;
        internal uint ArraySize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceArrayRtv
    {
        internal uint MipSlice;
        internal uint FirstArraySlice;
        internal uint ArraySize;
        internal uint PlaneSlice;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceBufferRtv
    {
        internal ulong FirstElement;
        internal uint NumElements;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct ResourceRenderTargetViewDesc
    {
        [FieldOffset(0)] internal int Format;
        [FieldOffset(4)] internal uint ViewDimension;
        [FieldOffset(8)] internal ResourceBufferRtv Buffer;
        [FieldOffset(8)] internal uint Texture1DMipSlice;
        [FieldOffset(8)] internal ResourceTexture1DArrayRtv Texture1DArray;
        [FieldOffset(8)] internal ResourceTexture2DUav Texture2D;
        [FieldOffset(8)] internal ResourceArrayRtv Texture2DArray;
        [FieldOffset(8)] internal ResourceTexture3DUav Texture3D;
        [FieldOffset(8)] internal ResourceTexture2DMSArray Texture2DMSArray;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct ResourceDepthStencilViewDesc
    {
        [FieldOffset(0)] internal int Format;
        [FieldOffset(4)] internal uint ViewDimension;
        [FieldOffset(8)] internal uint Flags;
        [FieldOffset(12)] internal uint MipSlice;
        [FieldOffset(16)] internal uint FirstArraySlice;
        [FieldOffset(20)] internal uint ArraySize;
        [FieldOffset(12)] internal ResourceTexture2DMSArray Texture2DMSArray;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceSamplerDesc
    {
        internal uint Filter;
        internal uint AddressU;
        internal uint AddressV;
        internal uint AddressW;
        internal float MipLodBias;
        internal uint MaxAnisotropy;
        internal uint ComparisonFunc;
        internal fixed float BorderColor[4];
        internal float MinLod;
        internal float MaxLod;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineShaderBytecode
    {
        internal void* Code;
        internal nuint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineRenderTargetBlendDesc
    {
        internal int BlendEnable;
        internal int LogicOpEnable;
        internal uint SrcBlend;
        internal uint DestBlend;
        internal uint BlendOp;
        internal uint SrcBlendAlpha;
        internal uint DestBlendAlpha;
        internal uint BlendOpAlpha;
        internal uint LogicOp;
        internal byte RenderTargetWriteMask;
    }

    [System.Runtime.CompilerServices.InlineArray(8)]
    internal struct PipelineRenderTargetBlendArray
    {
        private PipelineRenderTargetBlendDesc Element;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineBlendDesc
    {
        internal int AlphaToCoverageEnable;
        internal int IndependentBlendEnable;
        internal PipelineRenderTargetBlendArray RenderTargets;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineRasterizerDesc1
    {
        internal uint FillMode;
        internal uint CullMode;
        internal int FrontCounterClockwise;
        internal float DepthBias;
        internal float DepthBiasClamp;
        internal float SlopeScaledDepthBias;
        internal int DepthClipEnable;
        internal int MultisampleEnable;
        internal int AntialiasedLineEnable;
        internal uint ForcedSampleCount;
        internal uint ConservativeRaster;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineStencilOpDesc1
    {
        internal uint StencilFailOp;
        internal uint StencilDepthFailOp;
        internal uint StencilPassOp;
        internal uint StencilFunc;
        internal byte StencilReadMask;
        internal byte StencilWriteMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineDepthStencilDesc2
    {
        internal int DepthEnable;
        internal uint DepthWriteMask;
        internal uint DepthFunc;
        internal int StencilEnable;
        internal PipelineStencilOpDesc1 FrontFace;
        internal PipelineStencilOpDesc1 BackFace;
        internal int DepthBoundsTestEnable;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineRootSubobject
    {
        internal uint Type;
        internal nint Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineShaderSubobject
    {
        internal uint Type;
        internal PipelineShaderBytecode Value;
    }

    // Native stream subobjects align the complete tag/payload pair to a pointer boundary.
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    internal struct PipelineUintSubobject
    {
        [FieldOffset(0)] private nint Alignment;
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(4)] internal uint Value;
    }

    [StructLayout(LayoutKind.Explicit, Size = 336)]
    internal struct PipelineBlendSubobject
    {
        [FieldOffset(0)] private nint Alignment;
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(4)] internal PipelineBlendDesc Value;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    internal struct PipelineRasterizerSubobject
    {
        [FieldOffset(0)] private nint Alignment;
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(4)] internal PipelineRasterizerDesc1 Value;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct PipelineDepthStencilSubobject
    {
        [FieldOffset(0)] private nint Alignment;
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(4)] internal PipelineDepthStencilDesc2 Value;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct PipelineFormatsSubobject
    {
        [FieldOffset(0)] private nint Alignment;
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(4)] internal fixed int Formats[8];
        [FieldOffset(36)] internal uint Count;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct PipelineSampleSubobject
    {
        [FieldOffset(0)] private nint Alignment;
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(4)] internal uint Count;
        [FieldOffset(8)] internal uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineOutputStream
    {
        internal PipelineBlendSubobject Blend;
        internal PipelineUintSubobject SampleMask;
        internal PipelineRasterizerSubobject Rasterizer;
        internal PipelineDepthStencilSubobject DepthStencil;
        internal PipelineUintSubobject Topology;
        internal PipelineFormatsSubobject Formats;
        internal PipelineUintSubobject DepthFormat;
        internal PipelineSampleSubobject Samples;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineGraphicsStream
    {
        internal PipelineRootSubobject Root;
        internal PipelineShaderSubobject Vertex;
        internal PipelineShaderSubobject Pixel;
        internal PipelineUintSubobject StripCut;
        internal PipelineOutputStream Output;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineMeshStream
    {
        internal PipelineRootSubobject Root;
        internal PipelineShaderSubobject Task;
        internal PipelineShaderSubobject Mesh;
        internal PipelineShaderSubobject Pixel;
        internal PipelineOutputStream Output;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineComputeStream
    {
        internal PipelineRootSubobject Root;
        internal PipelineShaderSubobject Compute;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PipelineStateStreamDesc
    {
        internal nuint SizeInBytes;
        internal void* Stream;
    }

    internal static int PipelineCreateState(nint device, void* stream, nuint size, out nint pipeline)
    {
        Guid iid = new("765a30f3-f624-4c6f-a828-ace948622445");
        PipelineStateStreamDesc desc = new() { SizeInBytes = size, Stream = stream };
        nint result = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, PipelineStateStreamDesc*, Guid*, nint*, int>)(*(void***)device)[47])
            (device, &desc, &iid, &result);
        pipeline = result;
        return hr;
    }

    internal static int ResourceCreateDescriptorHeap(nint device, ResourceDescriptorHeapDesc* desc, out nint heap)
    {
        Guid iid = new("8efb471d-616c-4f49-90f7-127bb763fa51");
        nint result = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, ResourceDescriptorHeapDesc*, Guid*, nint*, int>)(*(void***)device)[14])
            (device, desc, &iid, &result);
        heap = result;
        return hr;
    }

    internal static nuint ResourceGetCpuDescriptorHandle(nint heap)
    {
        nuint result = 0;
        ((delegate* unmanaged[Stdcall]<nint, nuint*, nuint*>)(*(void***)heap)[9])(heap, &result);
        return result;
    }

    internal static void ResourceCreateShaderResourceView(nint device, nint resource, ResourceShaderResourceViewDesc* desc, nuint handle)
    {
        ((delegate* unmanaged[Stdcall]<nint, nint, ResourceShaderResourceViewDesc*, nuint, void>)(*(void***)device)[18])
            (device, resource, desc, handle);
    }

    internal static void ResourceCreateUnorderedAccessView(nint device, nint resource, ResourceUnorderedAccessViewDesc* desc, nuint handle)
    {
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, ResourceUnorderedAccessViewDesc*, nuint, void>)(*(void***)device)[19])
            (device, resource, 0, desc, handle);
    }

    internal static void ResourceCreateRenderTargetView(nint device, nint resource, ResourceRenderTargetViewDesc* desc, nuint handle)
    {
        ((delegate* unmanaged[Stdcall]<nint, nint, ResourceRenderTargetViewDesc*, nuint, void>)(*(void***)device)[20])
            (device, resource, desc, handle);
    }

    internal static void ResourceCreateDepthStencilView(nint device, nint resource, ResourceDepthStencilViewDesc* desc, nuint handle)
    {
        ((delegate* unmanaged[Stdcall]<nint, nint, ResourceDepthStencilViewDesc*, nuint, void>)(*(void***)device)[21])
            (device, resource, desc, handle);
    }

    internal static void ResourceCreateSampler(nint device, ResourceSamplerDesc* desc, nuint handle)
    {
        ((delegate* unmanaged[Stdcall]<nint, ResourceSamplerDesc*, nuint, void>)(*(void***)device)[22])(device, desc, handle);
    }

    internal static void ResourceCopyDescriptors(nint device, uint count, nuint destination, nuint source, uint type)
    {
        ((delegate* unmanaged[Stdcall]<nint, uint, nuint, nuint, uint, void>)(*(void***)device)[24])(device, count, destination, source, type);
    }
}
