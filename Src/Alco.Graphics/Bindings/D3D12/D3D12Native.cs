// Minimal Windows D3D12 and DXGI device, allocation, descriptor, and fence interop.

using System.Runtime.InteropServices;

namespace D3D12;

internal static unsafe partial class D3D12Native
{
    internal const uint ResourceFlagAllowRenderTarget = 0x1;
    internal const uint ResourceFlagAllowDepthStencil = 0x2;
    internal const uint ResourceFlagAllowUnorderedAccess = 0x4;
    internal const uint ResourceFlagDenyShaderResource = 0x8;
    internal const uint ResourceFlagAllowSimultaneousAccess = 0x20;

    internal static readonly Guid IidDevice10 = new("517f8718-aa66-49f9-b02b-a7ab89c06031");
    internal static readonly Guid IidFactory6 = new("c1b6694f-ff09-44a9-b03c-77900a0a1d17");
    internal static readonly Guid IidAdapter1 = new("29038f61-3839-4626-91fd-086879011a05");
    internal static readonly Guid IidCommandQueue = new("0ec870a6-5d7e-4c22-8cfc-5baae07616ed");
    internal static readonly Guid IidDescriptorHeap = new("8efb471d-616c-4f49-90f7-127bb763fa51");
    internal static readonly Guid IidRootSignature = new("c54a6b66-72df-4ee8-8be5-a946a1429214");
    internal static readonly Guid IidResource = new("696442be-a72e-4059-bc79-5b5c98040fad");
    internal static readonly Guid IidHeap = new("6b3b2502-6e51-45b3-90ee-9884265e8df3");
    internal static readonly Guid IidFence = new("0a753dcf-c4d8-4b91-adf6-be5a60d95a76");
    internal static readonly Guid IidDebug = new("344488b7-6846-474b-b989-f027448245e0");
    internal static readonly Guid IidDebug1 = new("affaa4ca-63fe-4d8e-b8ad-159000af4304");
    internal static readonly Guid IidInfoQueue1 = new("2852dd88-b484-4c0c-b6b1-67168500e600");

    [LibraryImport("d3d12.dll", EntryPoint = "D3D12CreateDevice")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CreateDevice(nint adapter, int minimumFeatureLevel, Guid* iid, nint* device);

    [LibraryImport("d3d12.dll", EntryPoint = "D3D12GetDebugInterface")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetDebugInterface(Guid* iid, nint* debug);

    [LibraryImport("d3d12.dll", EntryPoint = "D3D12SerializeVersionedRootSignature")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int SerializeVersionedRootSignature(VersionedRootSignatureDesc* desc, nint* blob, nint* errorBlob);

    [LibraryImport("dxgi.dll", EntryPoint = "CreateDXGIFactory2")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int CreateFactory(uint flags, Guid* iid, nint* factory);

    internal static int QueryInterface(nint native, Guid* iid, nint* result) =>
        ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(void***)native)[0])(native, iid, result);

    internal static uint AddRef(nint native) => ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)native)[1])(native);

    internal static uint Release(nint native) => native == 0 ? 0 : ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)native)[2])(native);

    internal static int EnumAdapterByGpuPreference(nint factory, uint index, int preference, Guid* iid, nint* adapter) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, int, Guid*, nint*, int>)(*(void***)factory)[29])(factory, index, preference, iid, adapter);

    internal static int GetAdapterDesc1(nint adapter, AdapterDesc1* desc) =>
        ((delegate* unmanaged[Stdcall]<nint, AdapterDesc1*, int>)(*(void***)adapter)[10])(adapter, desc);

    internal static void EnableDebugLayer(nint debug) => ((delegate* unmanaged[Stdcall]<nint, void>)(*(void***)debug)[3])(debug);

    internal static void SetEnableGPUBasedValidation(nint debug, int enabled) =>
        ((delegate* unmanaged[Stdcall]<nint, int, void>)(*(void***)debug)[4])(debug, enabled);

    internal static int RegisterMessageCallback(nint queue, delegate* unmanaged[Stdcall]<int, int, int, byte*, void*, void> callback, uint* cookie) =>
        ((delegate* unmanaged[Stdcall]<nint, delegate* unmanaged[Stdcall]<int, int, int, byte*, void*, void>, uint, void*, uint*, int>)
            (*(void***)queue)[38])(queue, callback, 0, null, cookie);

    internal static int UnregisterMessageCallback(nint queue, uint cookie) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, int>)(*(void***)queue)[39])(queue, cookie);

    internal static int CreateCommandQueue(nint device, CommandQueueDesc* desc, nint* queue)
    {
        Guid iid = IidCommandQueue;
        return ((delegate* unmanaged[Stdcall]<nint, CommandQueueDesc*, Guid*, nint*, int>)(*(void***)device)[8])(device, desc, &iid, queue);
    }

    internal static int CheckFeatureSupport(nint device, int feature, void* data, uint size) =>
        ((delegate* unmanaged[Stdcall]<nint, int, void*, uint, int>)(*(void***)device)[13])(device, feature, data, size);

    internal static int CreateDescriptorHeap(nint device, DescriptorHeapDesc* desc, nint* heap)
    {
        Guid iid = IidDescriptorHeap;
        return ((delegate* unmanaged[Stdcall]<nint, DescriptorHeapDesc*, Guid*, nint*, int>)(*(void***)device)[14])(device, desc, &iid, heap);
    }

    internal static uint GetDescriptorHandleIncrementSize(nint device, int type) =>
        ((delegate* unmanaged[Stdcall]<nint, int, uint>)(*(void***)device)[15])(device, type);

    internal static int CreateRootSignature(nint device, void* bytes, nuint size, nint* signature)
    {
        Guid iid = IidRootSignature;
        return ((delegate* unmanaged[Stdcall]<nint, uint, void*, nuint, Guid*, nint*, int>)(*(void***)device)[16])
            (device, 0, bytes, size, &iid, signature);
    }

    internal static void CreateShaderResourceView(nint device, nint resource, ShaderResourceViewDesc* desc, nuint destination) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, ShaderResourceViewDesc*, nuint, void>)(*(void***)device)[18])(device, resource, desc, destination);

    internal static void CreateUnorderedAccessView(nint device, nint resource, nint counter, UnorderedAccessViewDesc* desc, nuint destination) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, UnorderedAccessViewDesc*, nuint, void>)(*(void***)device)[19])
            (device, resource, counter, desc, destination);

    internal static void CreateDefaultSampler(nint device, DefaultSamplerDesc* desc, nuint destination) =>
        ((delegate* unmanaged[Stdcall]<nint, DefaultSamplerDesc*, nuint, void>)(*(void***)device)[22])(device, desc, destination);

    internal static void CopyDescriptorsSimple(nint device, uint count, nuint destination, nuint source, int type) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, nuint, nuint, int, void>)(*(void***)device)[24])(device, count, destination, source, type);

    internal static ResourceAllocationInfo GetResourceAllocationInfo(nint device, uint count, ResourceDesc* descs)
    {
        ResourceAllocationInfo result;
        ((delegate* unmanaged[Stdcall]<nint, ResourceAllocationInfo*, uint, uint, ResourceDesc*, ResourceAllocationInfo*>)(*(void***)device)[25])
            (device, &result, 0, count, descs);
        return result;
    }

    internal static int CreateCommittedResource(nint device, HeapProperties* properties, uint heapFlags, ResourceDesc* desc,
        uint initialState, nint* resource)
    {
        Guid iid = IidResource;
        return ((delegate* unmanaged[Stdcall]<nint, HeapProperties*, uint, ResourceDesc*, uint, void*, Guid*, nint*, int>)(*(void***)device)[27])
            (device, properties, heapFlags, desc, initialState, null, &iid, resource);
    }

    internal static int CreateHeap(nint device, HeapDesc* desc, nint* heap)
    {
        Guid iid = IidHeap;
        return ((delegate* unmanaged[Stdcall]<nint, HeapDesc*, Guid*, nint*, int>)(*(void***)device)[28])(device, desc, &iid, heap);
    }

    internal static int CreateFence(nint device, ulong initialValue, nint* fence)
    {
        Guid iid = IidFence;
        return ((delegate* unmanaged[Stdcall]<nint, ulong, uint, Guid*, nint*, int>)(*(void***)device)[36])
            (device, initialValue, 0, &iid, fence);
    }

    internal static int GetDeviceRemovedReason(nint device) => ((delegate* unmanaged[Stdcall]<nint, int>)(*(void***)device)[37])(device);

    internal static void GetCopyableFootprints(nint device, ResourceDesc* desc, uint first, uint count, ulong offset,
        PlacedSubresourceFootprint* layouts, uint* rows, ulong* rowSizes, ulong* totalSize) =>
        ((delegate* unmanaged[Stdcall]<nint, ResourceDesc*, uint, uint, ulong, PlacedSubresourceFootprint*, uint*, ulong*, ulong*, void>)
            (*(void***)device)[38])(device, desc, first, count, offset, layouts, rows, rowSizes, totalSize);

    internal static int CreateCommittedResource3(nint device, HeapProperties* properties, uint heapFlags, ResourceDesc1* desc,
        int layout, nint* resource)
    {
        Guid iid = IidResource;
        return ((delegate* unmanaged[Stdcall]<nint, HeapProperties*, uint, ResourceDesc1*, int, void*, nint, uint, int*, Guid*, nint*, int>)
            (*(void***)device)[76])(device, properties, heapFlags, desc, layout, null, 0, 0, null, &iid, resource);
    }

    internal static int CreatePlacedResource2(nint device, nint heap, ulong offset, ResourceDesc1* desc, int layout, nint* resource)
    {
        Guid iid = IidResource;
        return ((delegate* unmanaged[Stdcall]<nint, nint, ulong, ResourceDesc1*, int, void*, uint, int*, Guid*, nint*, int>)(*(void***)device)[77])
            (device, heap, offset, desc, layout, null, 0, null, &iid, resource);
    }

    internal static nuint GetCPUDescriptorHandleForHeapStart(nint heap)
    {
        nuint result;
        ((delegate* unmanaged[Stdcall]<nint, nuint*, nuint*>)(*(void***)heap)[9])(heap, &result);
        return result;
    }

    internal static ulong GetGPUDescriptorHandleForHeapStart(nint heap)
    {
        ulong result;
        ((delegate* unmanaged[Stdcall]<nint, ulong*, ulong*>)(*(void***)heap)[10])(heap, &result);
        return result;
    }

    internal static int Map(nint resource, uint subresource, Range* readRange, void** data) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, Range*, void**, int>)(*(void***)resource)[8])(resource, subresource, readRange, data);

    internal static void Unmap(nint resource, uint subresource, Range* writtenRange) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, Range*, void>)(*(void***)resource)[9])(resource, subresource, writtenRange);

    internal static ulong GetGPUVirtualAddress(nint resource) => ((delegate* unmanaged[Stdcall]<nint, ulong>)(*(void***)resource)[11])(resource);

    internal static int QueueSignal(nint queue, nint fence, ulong value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, ulong, int>)(*(void***)queue)[14])(queue, fence, value);

    internal static int QueueWait(nint queue, nint fence, ulong value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, ulong, int>)(*(void***)queue)[15])(queue, fence, value);

    internal static int GetTimestampFrequency(nint queue, ulong* frequency) =>
        ((delegate* unmanaged[Stdcall]<nint, ulong*, int>)(*(void***)queue)[16])(queue, frequency);

    internal static ulong GetCompletedValue(nint fence) => ((delegate* unmanaged[Stdcall]<nint, ulong>)(*(void***)fence)[8])(fence);

    internal static int SetEventOnCompletion(nint fence, ulong value, nint handle) =>
        ((delegate* unmanaged[Stdcall]<nint, ulong, nint, int>)(*(void***)fence)[9])(fence, value, handle);

    internal static void* GetBlobBufferPointer(nint blob) => ((delegate* unmanaged[Stdcall]<nint, void*>)(*(void***)blob)[3])(blob);

    internal static nuint GetBlobBufferSize(nint blob) => ((delegate* unmanaged[Stdcall]<nint, nuint>)(*(void***)blob)[4])(blob);

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdapterDesc1
    {
        internal fixed char Description[128];
        internal uint VendorId;
        internal uint DeviceId;
        internal uint SubSysId;
        internal uint Revision;
        internal nuint DedicatedVideoMemory;
        internal nuint DedicatedSystemMemory;
        internal nuint SharedSystemMemory;
        internal ulong AdapterLuid;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandQueueDesc
    {
        internal int Type;
        internal int Priority;
        internal uint Flags;
        internal uint NodeMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DescriptorHeapDesc
    {
        internal int Type;
        internal uint NumDescriptors;
        internal uint Flags;
        internal uint NodeMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SampleDesc
    {
        internal uint Count;
        internal uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceDesc
    {
        internal int Dimension;
        internal ulong Alignment;
        internal ulong Width;
        internal uint Height;
        internal ushort DepthOrArraySize;
        internal ushort MipLevels;
        internal int Format;
        internal SampleDesc SampleDesc;
        internal int Layout;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceDesc1
    {
        internal int Dimension;
        internal ulong Alignment;
        internal ulong Width;
        internal uint Height;
        internal ushort DepthOrArraySize;
        internal ushort MipLevels;
        internal int Format;
        internal SampleDesc SampleDesc;
        internal int Layout;
        internal uint Flags;
        internal uint SamplerFeedbackMipRegionWidth;
        internal uint SamplerFeedbackMipRegionHeight;
        internal uint SamplerFeedbackMipRegionDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HeapProperties
    {
        internal int Type;
        internal int CPUPageProperty;
        internal int MemoryPoolPreference;
        internal uint CreationNodeMask;
        internal uint VisibleNodeMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HeapDesc
    {
        internal ulong SizeInBytes;
        internal HeapProperties Properties;
        internal ulong Alignment;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DefaultSamplerDesc
    {
        internal int Filter;
        internal int AddressU;
        internal int AddressV;
        internal int AddressW;
        internal float MipLODBias;
        internal uint MaxAnisotropy;
        internal int ComparisonFunc;
        internal fixed float BorderColor[4];
        internal float MinLOD;
        internal float MaxLOD;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ResourceAllocationInfo
    {
        internal ulong SizeInBytes;
        internal ulong Alignment;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Range
    {
        internal nuint Begin;
        internal nuint End;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SubresourceFootprint
    {
        internal int Format;
        internal uint Width;
        internal uint Height;
        internal uint Depth;
        internal uint RowPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PlacedSubresourceFootprint
    {
        internal ulong Offset;
        internal SubresourceFootprint Footprint;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferSrv
    {
        internal ulong FirstElement;
        internal uint NumElements;
        internal uint StructureByteStride;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct ShaderResourceViewDesc
    {
        [FieldOffset(0)] internal int Format;
        [FieldOffset(4)] internal int ViewDimension;
        [FieldOffset(8)] internal uint Shader4ComponentMapping;
        [FieldOffset(16)] internal BufferSrv Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferUav
    {
        internal ulong FirstElement;
        internal uint NumElements;
        internal uint StructureByteStride;
        internal ulong CounterOffsetInBytes;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct UnorderedAccessViewDesc
    {
        [FieldOffset(0)] internal int Format;
        [FieldOffset(4)] internal int ViewDimension;
        [FieldOffset(8)] internal BufferUav Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RootDescriptorTable1
    {
        internal uint NumDescriptorRanges;
        internal void* DescriptorRanges;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct RootParameter1
    {
        [FieldOffset(0)] internal int ParameterType;
        [FieldOffset(8)] internal RootDescriptorTable1 DescriptorTable;
        [FieldOffset(8)] internal uint ShaderRegister;
        [FieldOffset(12)] internal uint RegisterSpace;
        [FieldOffset(16)] internal uint Flags;
        [FieldOffset(24)] internal int ShaderVisibility;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RootSignatureDesc1
    {
        internal uint NumParameters;
        internal RootParameter1* Parameters;
        internal uint NumStaticSamplers;
        internal void* StaticSamplers;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    internal struct VersionedRootSignatureDesc
    {
        [FieldOffset(0)] internal int Version;
        [FieldOffset(8)] internal RootSignatureDesc1 Desc1;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FeatureOptions
    {
        internal int DoublePrecisionFloatShaderOps;
        internal int OutputMergerLogicOp;
        internal int MinPrecisionSupport;
        internal int TiledResourcesTier;
        internal int ResourceBindingTier;
        internal int PSSpecifiedStencilRefSupported;
        internal int TypedUAVLoadAdditionalFormats;
        internal int ROVsSupported;
        internal int ConservativeRasterizationTier;
        internal uint MaxGPUVirtualAddressBitsPerResource;
        internal int StandardSwizzle64KBSupported;
        internal int CrossNodeSharingTier;
        internal int CrossAdapterRowMajorTextureSupported;
        internal int VPAndRTArrayIndexFromAnyShaderFeedingRasterizerSupportedWithoutGSEmulation;
        internal int ResourceHeapTier;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FeatureOptions3
    {
        internal int CopyQueueTimestampQueriesSupported;
        internal int CastingFullyTypedFormatSupported;
        internal uint WriteBufferImmediateSupportFlags;
        internal int ViewInstancingTier;
        internal int BarycentricsSupported;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FeatureOptions4
    {
        internal int MSAA64KBAlignedTextureSupported;
        internal int SharedResourceCompatibilityTier;
        internal int Native16BitShaderOpsSupported;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FeatureOptions7
    {
        internal int MeshShaderTier;
        internal int SamplerFeedbackTier;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FeatureOptions12
    {
        internal int MSPrimitivesPipelineStatisticIncludesCulledPrimitives;
        internal int EnhancedBarriersSupported;
        internal int RelaxedFormatCastingSupported;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FeatureOptions13
    {
        internal int UnrestrictedBufferTextureCopyPitchSupported;
        internal int UnrestrictedVertexElementAlignmentSupported;
        internal int InvertedViewportHeightFlipsYSupported;
        internal int InvertedViewportDepthFlipsZSupported;
        internal int TextureCopyBetweenDimensionsSupported;
        internal int AlphaBlendFactorSupported;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FeatureOptions14
    {
        internal int AdvancedTextureOpsSupported;
        internal int WriteableMSAATexturesSupported;
        internal int IndependentFrontAndBackStencilRefMaskSupported;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FeatureFormatSupport
    {
        internal int Format;
        internal uint Support1;
        internal uint Support2;
    }
}
