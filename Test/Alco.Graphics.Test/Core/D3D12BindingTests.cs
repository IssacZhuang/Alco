using Alco.Graphics;
using Alco.Graphics.LowLevel;
// CPU regressions for the independent D3D12 binding assembly, Windows SDK 26100 layouts, and COM argument forwarding.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static D3D12.D3D12Native;

internal static unsafe class D3D12BindingTests
{
    private const ulong CpuHandle = 0x1234_5678_9ABC_DEF0;
    private const ulong GpuHandle = 0xFEDC_BA98_7654_3210;

    internal static void Run(Action<bool, string> check)
    {
        Console.WriteLine("D3D12 bindings (CPU):");
        Assembly binding = typeof(D3D12.D3D12Native).Assembly;
        check(binding == typeof(Alco.Graphics.LowLevel.GPUDevice).Assembly,
            "D3D12 bindings live inside the Alco.Graphics assembly");
        MethodInfo? createDeviceMethod = typeof(D3D12.D3D12Native).GetMethod("CreateDevice", BindingFlags.Static | BindingFlags.NonPublic);
        check(createDeviceMethod?.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()?.Paths == DllImportSearchPath.System32,
            "native system imports use System32 rather than application-local managed assemblies");

        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
        {
            Console.WriteLine("  [SKIP] Windows x64/ARM64 SDK layout and COM ABI checks");
            return;
        }

        RunResourceLayouts(check);
        RunPipelineLayouts(check);
        RunCommandLayouts(check);
        RunComCalls(check);
    }

    private static void RunResourceLayouts(Action<bool, string> check)
    {
        check(sizeof(ResourceDesc) == 56 && (int)Marshal.OffsetOf<ResourceDesc>(nameof(ResourceDesc.Alignment)) == 8 &&
            (int)Marshal.OffsetOf<ResourceDesc>(nameof(ResourceDesc.Width)) == 16 &&
            (int)Marshal.OffsetOf<ResourceDesc>(nameof(ResourceDesc.SampleDesc)) == 36 &&
            (int)Marshal.OffsetOf<ResourceDesc>(nameof(ResourceDesc.Flags)) == 48, "ResourceDesc retains its 56-byte SDK layout");
        check(sizeof(ResourceDesc1) == 64 && (int)Marshal.OffsetOf<ResourceDesc1>(nameof(ResourceDesc1.Width)) == 16 &&
            (int)Marshal.OffsetOf<ResourceDesc1>(nameof(ResourceDesc1.SamplerFeedbackMipRegionWidth)) == 52 &&
            (int)Marshal.OffsetOf<ResourceDesc1>(nameof(ResourceDesc1.SamplerFeedbackMipRegionHeight)) == 56 &&
            (int)Marshal.OffsetOf<ResourceDesc1>(nameof(ResourceDesc1.SamplerFeedbackMipRegionDepth)) == 60,
            "ResourceDesc1 retains its 64-byte SDK layout and sampler-feedback region");
        check(sizeof(HeapProperties) == 20 && (int)Marshal.OffsetOf<HeapProperties>(nameof(HeapProperties.VisibleNodeMask)) == 16,
            "HeapProperties retains its 20-byte SDK layout");
        check(sizeof(HeapDesc) == 48 && (int)Marshal.OffsetOf<HeapDesc>(nameof(HeapDesc.Properties)) == 8 &&
            (int)Marshal.OffsetOf<HeapDesc>(nameof(HeapDesc.Alignment)) == 32 && (int)Marshal.OffsetOf<HeapDesc>(nameof(HeapDesc.Flags)) == 40,
            "HeapDesc pads its 20-byte properties before alignment and flags");
        check(sizeof(ResourceAllocationInfo) == 16 &&
            (int)Marshal.OffsetOf<ResourceAllocationInfo>(nameof(ResourceAllocationInfo.Alignment)) == 8,
            "ResourceAllocationInfo retains two 64-bit fields");
        check(sizeof(BufferSrv) == 24 && (int)Marshal.OffsetOf<BufferSrv>(nameof(BufferSrv.Flags)) == 16 &&
            sizeof(ShaderResourceViewDesc) == 40 && (int)Marshal.OffsetOf<ShaderResourceViewDesc>(nameof(ShaderResourceViewDesc.Buffer)) == 16,
            "buffer SRVs retain the 24-byte union member at descriptor offset 16");
        check(sizeof(BufferUav) == 32 && (int)Marshal.OffsetOf<BufferUav>(nameof(BufferUav.CounterOffsetInBytes)) == 16 &&
            (int)Marshal.OffsetOf<BufferUav>(nameof(BufferUav.Flags)) == 24 && sizeof(UnorderedAccessViewDesc) == 40 &&
            (int)Marshal.OffsetOf<UnorderedAccessViewDesc>(nameof(UnorderedAccessViewDesc.Buffer)) == 8,
            "buffer UAVs retain the 32-byte union member at descriptor offset 8");
        check(sizeof(ResourceShaderResourceViewDesc) == 40 &&
            (int)Marshal.OffsetOf<ResourceShaderResourceViewDesc>(nameof(ResourceShaderResourceViewDesc.Buffer)) == 16 &&
            (int)Marshal.OffsetOf<ResourceShaderResourceViewDesc>(nameof(ResourceShaderResourceViewDesc.Shader4ComponentMapping)) == 8 &&
            (int)Marshal.OffsetOf<ResourceShaderResourceViewDesc>(nameof(ResourceShaderResourceViewDesc.Texture2D)) == 16 &&
            (int)Marshal.OffsetOf<ResourceShaderResourceViewDesc>(nameof(ResourceShaderResourceViewDesc.Texture2DArray)) == 16,
            "resource SRVs retain their 40-byte layout and union at offset 16");
        check(sizeof(ResourceUnorderedAccessViewDesc) == 40 &&
            (int)Marshal.OffsetOf<ResourceUnorderedAccessViewDesc>(nameof(ResourceUnorderedAccessViewDesc.Buffer)) == 8 &&
            (int)Marshal.OffsetOf<ResourceUnorderedAccessViewDesc>(nameof(ResourceUnorderedAccessViewDesc.Texture2D)) == 8 &&
            (int)Marshal.OffsetOf<ResourceUnorderedAccessViewDesc>(nameof(ResourceUnorderedAccessViewDesc.Texture3D)) == 8,
            "resource UAVs retain their 40-byte layout and union at offset 8");
        check(sizeof(ResourceRenderTargetViewDesc) == 24 &&
            (int)Marshal.OffsetOf<ResourceRenderTargetViewDesc>(nameof(ResourceRenderTargetViewDesc.Buffer)) == 8 &&
            (int)Marshal.OffsetOf<ResourceRenderTargetViewDesc>(nameof(ResourceRenderTargetViewDesc.Texture2D)) == 8 &&
            (int)Marshal.OffsetOf<ResourceRenderTargetViewDesc>(nameof(ResourceRenderTargetViewDesc.Texture3D)) == 8,
            "resource RTVs retain their 24-byte layout and union at offset 8");
        check(sizeof(ResourceTexture1DArrayUav) == 12 && sizeof(ResourceTexture1DArrayRtv) == 12 &&
            (int)Marshal.OffsetOf<ResourceTexture1DArrayUav>(nameof(ResourceTexture1DArrayUav.ArraySize)) == 8 &&
            (int)Marshal.OffsetOf<ResourceTexture1DArrayRtv>(nameof(ResourceTexture1DArrayRtv.ArraySize)) == 8,
            "1D-array UAV and RTV members retain their 12-byte layout without a plane slice");
        CheckAlignment<ResourceShaderResourceViewDesc>(check);
        CheckAlignment<ResourceUnorderedAccessViewDesc>(check);
        CheckAlignment<ResourceRenderTargetViewDesc>(check);
        check(sizeof(RootDescriptorTable1) == 16 &&
            (int)Marshal.OffsetOf<RootDescriptorTable1>(nameof(RootDescriptorTable1.DescriptorRanges)) == 8 &&
            sizeof(RootParameter1) == 32 && (int)Marshal.OffsetOf<RootParameter1>(nameof(RootParameter1.ShaderRegister)) == 8 &&
            (int)Marshal.OffsetOf<RootParameter1>(nameof(RootParameter1.DescriptorTable)) == 8 &&
            (int)Marshal.OffsetOf<RootParameter1>(nameof(RootParameter1.Flags)) == 16 &&
            (int)Marshal.OffsetOf<RootParameter1>(nameof(RootParameter1.ShaderVisibility)) == 24,
            "RootParameter1 retains its 32-byte layout, union at 8, and visibility at 24");
        CheckAlignment<RootParameter1>(check);
        check(sizeof(RootSignatureDesc1) == 40 && (int)Marshal.OffsetOf<RootSignatureDesc1>(nameof(RootSignatureDesc1.Parameters)) == 8 &&
            (int)Marshal.OffsetOf<RootSignatureDesc1>(nameof(RootSignatureDesc1.StaticSamplers)) == 24 &&
            (int)Marshal.OffsetOf<RootSignatureDesc1>(nameof(RootSignatureDesc1.Flags)) == 32,
            "RootSignatureDesc1 retains its 40-byte pointer-aligned layout");
        check(sizeof(VersionedRootSignatureDesc) == 48 &&
            (int)Marshal.OffsetOf<VersionedRootSignatureDesc>(nameof(VersionedRootSignatureDesc.Desc1)) == 8,
            "VersionedRootSignatureDesc retains its 48-byte layout and union at offset 8");
    }

    private static void RunPipelineLayouts(Action<bool, string> check)
    {
        check(sizeof(PipelineRenderTargetBlendDesc) == 40 &&
            (int)Marshal.OffsetOf<PipelineRenderTargetBlendDesc>(nameof(PipelineRenderTargetBlendDesc.RenderTargetWriteMask)) == 36 &&
            sizeof(PipelineBlendDesc) == 328 && (int)Marshal.OffsetOf<PipelineBlendDesc>(nameof(PipelineBlendDesc.RenderTargets)) == 8,
            "PipelineBlendDesc retains eight 40-byte render-target descriptions in 328 bytes");
        check(sizeof(PipelineRasterizerDesc1) == 44 &&
            (int)Marshal.OffsetOf<PipelineRasterizerDesc1>(nameof(PipelineRasterizerDesc1.DepthBias)) == 12 &&
            (int)Marshal.OffsetOf<PipelineRasterizerDesc1>(nameof(PipelineRasterizerDesc1.ConservativeRaster)) == 40,
            "PipelineRasterizerDesc1 retains its 44-byte SDK layout");
        check(sizeof(PipelineStencilOpDesc1) == 20 && sizeof(PipelineDepthStencilDesc2) == 60 &&
            (int)Marshal.OffsetOf<PipelineDepthStencilDesc2>(nameof(PipelineDepthStencilDesc2.FrontFace)) == 16 &&
            (int)Marshal.OffsetOf<PipelineDepthStencilDesc2>(nameof(PipelineDepthStencilDesc2.BackFace)) == 36 &&
            (int)Marshal.OffsetOf<PipelineDepthStencilDesc2>(nameof(PipelineDepthStencilDesc2.DepthBoundsTestEnable)) == 56,
            "PipelineDepthStencilDesc2 retains its 60-byte layout with independent stencil masks");
        check(sizeof(PipelineRootSubobject) == 16 && (int)Marshal.OffsetOf<PipelineRootSubobject>(nameof(PipelineRootSubobject.Value)) == 8,
            "pipeline root subobjects occupy 16 bytes with their pointer at offset 8");
        check(sizeof(PipelineShaderSubobject) == 24 && (int)Marshal.OffsetOf<PipelineShaderSubobject>(nameof(PipelineShaderSubobject.Value)) == 8,
            "pipeline shader subobjects occupy 24 bytes with bytecode at offset 8");
        check(sizeof(PipelineUintSubobject) == 8 && (int)Marshal.OffsetOf<PipelineUintSubobject>(nameof(PipelineUintSubobject.Value)) == 4,
            "pipeline uint subobjects occupy 8 bytes with their value at offset 4");
        check(sizeof(PipelineBlendSubobject) == 336 && (int)Marshal.OffsetOf<PipelineBlendSubobject>(nameof(PipelineBlendSubobject.Value)) == 4,
            "pipeline blend subobjects occupy 336 bytes with their value at offset 4");
        check(sizeof(PipelineRasterizerSubobject) == 48 &&
            (int)Marshal.OffsetOf<PipelineRasterizerSubobject>(nameof(PipelineRasterizerSubobject.Value)) == 4,
            "pipeline rasterizer subobjects occupy 48 bytes with their value at offset 4");
        check(sizeof(PipelineDepthStencilSubobject) == 64 &&
            (int)Marshal.OffsetOf<PipelineDepthStencilSubobject>(nameof(PipelineDepthStencilSubobject.Value)) == 4,
            "pipeline depth/stencil subobjects occupy 64 bytes with their value at offset 4");
        check(sizeof(PipelineFormatsSubobject) == 40 &&
            (int)Marshal.OffsetOf<PipelineFormatsSubobject>(nameof(PipelineFormatsSubobject.Formats)) == 4 &&
            (int)Marshal.OffsetOf<PipelineFormatsSubobject>(nameof(PipelineFormatsSubobject.Count)) == 36,
            "pipeline format subobjects occupy 40 bytes with eight formats before the count");
        check(sizeof(PipelineSampleSubobject) == 16 && (int)Marshal.OffsetOf<PipelineSampleSubobject>(nameof(PipelineSampleSubobject.Count)) == 4 &&
            (int)Marshal.OffsetOf<PipelineSampleSubobject>(nameof(PipelineSampleSubobject.Quality)) == 8,
            "pipeline sample subobjects occupy 16 bytes with count and quality at offsets 4 and 8");
        CheckAlignment<PipelineRootSubobject>(check);
        CheckAlignment<PipelineShaderSubobject>(check);
        CheckAlignment<PipelineUintSubobject>(check);
        CheckAlignment<PipelineBlendSubobject>(check);
        CheckAlignment<PipelineRasterizerSubobject>(check);
        CheckAlignment<PipelineDepthStencilSubobject>(check);
        CheckAlignment<PipelineFormatsSubobject>(check);
        CheckAlignment<PipelineSampleSubobject>(check);

        check(sizeof(PipelineOutputStream) == 528 &&
            sizeof(PipelineOutputStream) == sizeof(PipelineBlendSubobject) + 3 * sizeof(PipelineUintSubobject) +
                sizeof(PipelineRasterizerSubobject) + sizeof(PipelineDepthStencilSubobject) + sizeof(PipelineFormatsSubobject) +
                sizeof(PipelineSampleSubobject) &&
            (int)Marshal.OffsetOf<PipelineOutputStream>(nameof(PipelineOutputStream.SampleMask)) == 336 &&
            (int)Marshal.OffsetOf<PipelineOutputStream>(nameof(PipelineOutputStream.Rasterizer)) == 344 &&
            (int)Marshal.OffsetOf<PipelineOutputStream>(nameof(PipelineOutputStream.DepthStencil)) == 392 &&
            (int)Marshal.OffsetOf<PipelineOutputStream>(nameof(PipelineOutputStream.Topology)) == 456 &&
            (int)Marshal.OffsetOf<PipelineOutputStream>(nameof(PipelineOutputStream.Formats)) == 464 &&
            (int)Marshal.OffsetOf<PipelineOutputStream>(nameof(PipelineOutputStream.DepthFormat)) == 504 &&
            (int)Marshal.OffsetOf<PipelineOutputStream>(nameof(PipelineOutputStream.Samples)) == 512,
            "pipeline output streams concatenate eight aligned subobjects into 528 bytes");
        check(sizeof(PipelineGraphicsStream) == 600 &&
            sizeof(PipelineGraphicsStream) == sizeof(PipelineRootSubobject) + 2 * sizeof(PipelineShaderSubobject) +
                sizeof(PipelineUintSubobject) + sizeof(PipelineOutputStream) &&
            (int)Marshal.OffsetOf<PipelineGraphicsStream>(nameof(PipelineGraphicsStream.Vertex)) == 16 &&
            (int)Marshal.OffsetOf<PipelineGraphicsStream>(nameof(PipelineGraphicsStream.Pixel)) == 40 &&
            (int)Marshal.OffsetOf<PipelineGraphicsStream>(nameof(PipelineGraphicsStream.StripCut)) == 64 &&
            (int)Marshal.OffsetOf<PipelineGraphicsStream>(nameof(PipelineGraphicsStream.Output)) == 72,
            "graphics streams concatenate root, two shaders, strip-cut, and output into 600 bytes");
        check(sizeof(PipelineMeshStream) == 616 &&
            sizeof(PipelineMeshStream) == sizeof(PipelineRootSubobject) + 3 * sizeof(PipelineShaderSubobject) + sizeof(PipelineOutputStream) &&
            (int)Marshal.OffsetOf<PipelineMeshStream>(nameof(PipelineMeshStream.Task)) == 16 &&
            (int)Marshal.OffsetOf<PipelineMeshStream>(nameof(PipelineMeshStream.Mesh)) == 40 &&
            (int)Marshal.OffsetOf<PipelineMeshStream>(nameof(PipelineMeshStream.Pixel)) == 64 &&
            (int)Marshal.OffsetOf<PipelineMeshStream>(nameof(PipelineMeshStream.Output)) == 88,
            "mesh streams concatenate root, three shaders, and output into 616 bytes");
        check(sizeof(PipelineComputeStream) == 40 &&
            sizeof(PipelineComputeStream) == sizeof(PipelineRootSubobject) + sizeof(PipelineShaderSubobject) &&
            (int)Marshal.OffsetOf<PipelineComputeStream>(nameof(PipelineComputeStream.Compute)) == 16,
            "compute streams concatenate root and shader into 40 bytes");
        CheckAlignment<PipelineOutputStream>(check);
        CheckAlignment<PipelineGraphicsStream>(check);
        CheckAlignment<PipelineMeshStream>(check);
        CheckAlignment<PipelineComputeStream>(check);
    }

    private static void RunCommandLayouts(Action<bool, string> check)
    {
        check(sizeof(CommandTextureBarrier) == 64 && (int)Marshal.OffsetOf<CommandTextureBarrier>(nameof(CommandTextureBarrier.Resource)) == 24 &&
            (int)Marshal.OffsetOf<CommandTextureBarrier>(nameof(CommandTextureBarrier.Subresources)) == 32 &&
            (int)Marshal.OffsetOf<CommandTextureBarrier>(nameof(CommandTextureBarrier.Flags)) == 56,
            "texture barriers retain their 64-byte SDK layout");
        check(sizeof(CommandBeginningAccess) == 24 && (int)Marshal.OffsetOf<CommandBeginningAccess>(nameof(CommandBeginningAccess.Format)) == 4 &&
            (int)Marshal.OffsetOf<CommandBeginningAccess>(nameof(CommandBeginningAccess.Color)) == 8 &&
            (int)Marshal.OffsetOf<CommandBeginningAccess>(nameof(CommandBeginningAccess.Depth)) == 8 &&
            (int)Marshal.OffsetOf<CommandBeginningAccess>(nameof(CommandBeginningAccess.Stencil)) == 12,
            "render-pass beginning access retains its 24-byte clear-value union layout");
        check(sizeof(CommandResolveSubresource) == 32 &&
            (int)Marshal.OffsetOf<CommandResolveSubresource>(nameof(CommandResolveSubresource.SourceRect)) == 16 &&
            typeof(CommandResolveSubresource).GetField(nameof(CommandResolveSubresource.SourceRect),
                BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType == typeof(CommandRect),
            "render-pass resolve subresources retain their 32-byte SDK layout and inline rectangle at offset 16");
        check(sizeof(CommandEndingResolve) == 48 &&
            (int)Marshal.OffsetOf<CommandEndingResolve>(nameof(CommandEndingResolve.SubresourceCount)) == 16 &&
            (int)Marshal.OffsetOf<CommandEndingResolve>(nameof(CommandEndingResolve.Subresources)) == 24 &&
            (int)Marshal.OffsetOf<CommandEndingResolve>(nameof(CommandEndingResolve.PreserveSource)) == 40 &&
            sizeof(CommandEndingAccess) == 56 && (int)Marshal.OffsetOf<CommandEndingAccess>(nameof(CommandEndingAccess.Type)) == 0 &&
            (int)Marshal.OffsetOf<CommandEndingAccess>(nameof(CommandEndingAccess.Resolve)) == 8,
            "render-pass ending access retains its 56-byte SDK layout and union at offset 8");
        CheckAlignment<CommandEndingAccess>(check);
        check(sizeof(CommandRenderTarget) == 88 && (int)Marshal.OffsetOf<CommandRenderTarget>(nameof(CommandRenderTarget.Beginning)) == 8 &&
            (int)Marshal.OffsetOf<CommandRenderTarget>(nameof(CommandRenderTarget.Ending)) == 32,
            "render-pass render targets retain their 88-byte layout");
        check(sizeof(CommandDepthStencil) == 168 && (int)Marshal.OffsetOf<CommandDepthStencil>(nameof(CommandDepthStencil.DepthBeginning)) == 8 &&
            (int)Marshal.OffsetOf<CommandDepthStencil>(nameof(CommandDepthStencil.StencilBeginning)) == 32 &&
            (int)Marshal.OffsetOf<CommandDepthStencil>(nameof(CommandDepthStencil.DepthEnding)) == 56 &&
            (int)Marshal.OffsetOf<CommandDepthStencil>(nameof(CommandDepthStencil.StencilEnding)) == 112,
            "render-pass depth/stencil descriptions retain their 168-byte layout");
        check(sizeof(CommandCopyLocation) == 48 && (int)Marshal.OffsetOf<CommandCopyLocation>(nameof(CommandCopyLocation.Type)) == 8 &&
            (int)Marshal.OffsetOf<CommandCopyLocation>(nameof(CommandCopyLocation.Placed)) == 16 &&
            (int)Marshal.OffsetOf<CommandCopyLocation>(nameof(CommandCopyLocation.Subresource)) == 16,
            "texture copy locations retain their 48-byte layout and union at offset 16");
        check(sizeof(SwapchainDesc1) == 48 && (int)Marshal.OffsetOf<SwapchainDesc1>(nameof(SwapchainDesc1.Height)) == 4 &&
            (int)Marshal.OffsetOf<SwapchainDesc1>(nameof(SwapchainDesc1.SampleCount)) == 16 &&
            (int)Marshal.OffsetOf<SwapchainDesc1>(nameof(SwapchainDesc1.BufferCount)) == 28 &&
            (int)Marshal.OffsetOf<SwapchainDesc1>(nameof(SwapchainDesc1.Flags)) == 44, "SwapchainDesc1 retains its 48-byte DXGI layout");
    }

    private static void CheckAlignment<T>(Action<bool, string> check) where T : unmanaged
    {
        AlignmentProbe<T> probe = new();
        check((byte*)&probe.Value - (byte*)&probe == 8 && sizeof(AlignmentProbe<T>) == sizeof(T) + 8,
            $"{typeof(T).Name} preserves native 8-byte alignment when embedded after a byte");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AlignmentProbe<T> where T : unmanaged
    {
        internal byte Prefix;
        internal T Value;
    }

    private static void RunComCalls(Action<bool, string> check)
    {
        void** vtable = stackalloc void*[80];
        NativeMemory.Clear(vtable, (nuint)(80 * sizeof(void*)));
        vtable[9] = (void*)(delegate* unmanaged[Stdcall]<nint, nuint*, nuint*>)&CaptureCpuHandle;
        vtable[10] = (void*)(delegate* unmanaged[Stdcall]<nint, ulong*, ulong*>)&CaptureGpuHandle;
        vtable[25] = (void*)(delegate* unmanaged[Stdcall]<nint, ResourceAllocationInfo*, uint, uint, ResourceDesc*, ResourceAllocationInfo*>)
            &CaptureAllocationInfo;
        vtable[14] = (void*)(delegate* unmanaged[Stdcall]<nint, uint, uint, uint, void>)&CaptureDispatch;
        vtable[79] = (void*)(delegate* unmanaged[Stdcall]<nint, uint, uint, uint, void>)&CaptureDispatchMesh;
        vtable[76] = (void*)(delegate* unmanaged[Stdcall]<nint, HeapProperties*, uint, ResourceDesc1*, int, void*, nint, uint, int*, Guid*, nint*, int>)
            &CaptureCommittedResource;
        vtable[13] = (void*)(delegate* unmanaged[Stdcall]<nint, uint, uint, uint, uint, uint, int>)&CaptureResize;

        FakeCom heap = new() { Vtable = vtable };
        nuint cpu = GetCPUDescriptorHandleForHeapStart((nint)(&heap));
        check(heap.Calls == 1 && heap.ArgumentsMatch && cpu == unchecked((nuint)CpuHandle),
            "CPU heap-start slot 9 receives this and an explicit hidden-result pointer");
        cpu = ResourceGetCpuDescriptorHandle((nint)(&heap));
        check(heap.Calls == 2 && heap.ArgumentsMatch && cpu == unchecked((nuint)CpuHandle),
            "resource CPU heap-start wrapper preserves the hidden-result ABI");
        cpu = SwapchainRenderHeapStart((nint)(&heap));
        check(heap.Calls == 3 && heap.ArgumentsMatch && cpu == unchecked((nuint)CpuHandle),
            "swapchain CPU heap-start wrapper preserves the hidden-result ABI");
        ulong gpu = GetGPUDescriptorHandleForHeapStart((nint)(&heap));
        check(heap.Calls == 4 && heap.ArgumentsMatch && gpu == GpuHandle,
            "GPU heap-start slot 10 receives this and an explicit hidden-result pointer");

        ResourceDesc* descs = stackalloc ResourceDesc[2];
        FakeCom device = new() { Vtable = vtable, First = descs };
        ResourceAllocationInfo info = GetResourceAllocationInfo((nint)(&device), 2, descs);
        check(device.Calls == 1 && device.ArgumentsMatch && info.SizeInBytes == 0x1_2345_6789 && info.Alignment == 0x1_0000,
            "allocation-info slot 25 receives hidden-result storage, node mask, count, and descriptor pointer");

        FakeCom commands = new() { Vtable = vtable };
        CommandDispatch((nint)(&commands), 7, 0x1234, 0x8000_0001);
        check(commands.Calls == 1 && commands.ArgumentsMatch, "dispatch slot 14 receives scalar x/y/z unchanged");
        CommandDispatchMesh((nint)(&commands), 11, 37, 0xFEDC_BA98);
        check(commands.Calls == 2 && commands.ArgumentsMatch, "mesh dispatch slot 79 receives scalar x/y/z unchanged");

        HeapProperties properties = new() { Type = 1 };
        ResourceDesc1 desc = new() { Dimension = 3, Width = 73, Height = 41 };
        device.First = &properties;
        device.Second = &desc;
        nint resource = 0;
        int status = CreateCommittedResource3((nint)(&device), &properties, 0x200, &desc, 7, &resource);
        check(device.Calls == 2 && device.ArgumentsMatch && status == 0 && resource == (nint)0x5678,
            "CreateCommittedResource3 slot 76 forwards its inputs, null optional arguments, resource IID, and result");
        device.Status = unchecked((int)0x80070057);
        resource = (nint)0x9999;
        status = CreateCommittedResource3((nint)(&device), &properties, 0x200, &desc, 7, &resource);
        check(device.Calls == 3 && device.ArgumentsMatch && status == unchecked((int)0x80070057) && resource == 0,
            "CreateCommittedResource3 preserves a failing HRESULT and the native null result");

        FakeCom swapchain = new() { Vtable = vtable };
        status = SwapchainResizeBuffers((nint)(&swapchain), 3, 1234, 5678, 28, 0x800);
        check(swapchain.Calls == 1 && swapchain.ArgumentsMatch && status == unchecked((int)0x887A0001),
            "ResizeBuffers slot 13 forwards scalar dimensions and preserves the failing HRESULT");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FakeCom
    {
        internal void** Vtable;
        internal int Calls;
        internal bool ArgumentsMatch;
        internal void* First;
        internal void* Second;
        internal int Status;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nuint* CaptureCpuHandle(nint native, nuint* result)
    {
        FakeCom* capture = (FakeCom*)native;
        ++capture->Calls;
        capture->ArgumentsMatch = result != null;
        if (result != null) *result = unchecked((nuint)CpuHandle);
        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static ulong* CaptureGpuHandle(nint native, ulong* result)
    {
        FakeCom* capture = (FakeCom*)native;
        ++capture->Calls;
        capture->ArgumentsMatch = result != null;
        if (result != null) *result = GpuHandle;
        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static ResourceAllocationInfo* CaptureAllocationInfo(nint native, ResourceAllocationInfo* result, uint mask, uint count,
        ResourceDesc* descs)
    {
        FakeCom* capture = (FakeCom*)native;
        ++capture->Calls;
        capture->ArgumentsMatch = result != null && mask == 0 && count == 2 && descs == capture->First;
        if (result != null) *result = new() { SizeInBytes = 0x1_2345_6789, Alignment = 0x1_0000 };
        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void CaptureDispatch(nint native, uint x, uint y, uint z)
    {
        FakeCom* capture = (FakeCom*)native;
        ++capture->Calls;
        capture->ArgumentsMatch = x == 7 && y == 0x1234 && z == 0x8000_0001;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void CaptureDispatchMesh(nint native, uint x, uint y, uint z)
    {
        FakeCom* capture = (FakeCom*)native;
        ++capture->Calls;
        capture->ArgumentsMatch = x == 11 && y == 37 && z == 0xFEDC_BA98;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CaptureCommittedResource(nint native, HeapProperties* properties, uint flags, ResourceDesc1* desc, int layout,
        void* clear, nint protectedSession, uint formatCount, int* formats, Guid* iid, nint* resource)
    {
        FakeCom* capture = (FakeCom*)native;
        ++capture->Calls;
        capture->ArgumentsMatch = properties == capture->First && desc == capture->Second && flags == 0x200 && layout == 7 && clear == null &&
            protectedSession == 0 && formatCount == 0 && formats == null && iid != null &&
            *iid == new Guid("696442be-a72e-4059-bc79-5b5c98040fad") && resource != null;
        if (resource != null) *resource = capture->Status < 0 ? 0 : (nint)0x5678;
        return capture->Status;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CaptureResize(nint native, uint count, uint width, uint height, uint format, uint flags)
    {
        FakeCom* capture = (FakeCom*)native;
        ++capture->Calls;
        capture->ArgumentsMatch = count == 3 && width == 1234 && height == 5678 && format == 28 && flags == 0x800;
        return unchecked((int)0x887A0001);
    }
}
