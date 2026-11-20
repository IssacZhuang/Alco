// D3D12 device initialization, shared buffer descriptors, resource memory, queues, and timeline fences.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using D3D12Native = global::D3D12.D3D12Native;
using static D3D12.D3D12Native;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class D3D12Queue
{
    internal nint Native;
    internal int Type;
    internal nint Completion;
    internal ulong Value;
    internal ulong TimestampFrequency;
}

internal sealed unsafe class D3D12GpuHeapOwner : GPUHeapOwner
{
    internal required D3D12Device Device;
    internal nint Native;
    internal ulong Address;
    internal ulong Size;
    internal MemoryType Memory;

    internal override void Destroy()
    {
        if (ViewIndex != 0)
        {
            lock (Device.DescriptorLock)
            {
                Device.WriteBufferDescriptors(ViewIndex, 0, 1);
                Device.CopyBufferDescriptors(ViewIndex);
                Device.BufferViews!.Free(ViewIndex);
            }
        }
        Release(Native);
    }
}

internal sealed unsafe class D3D12TextureHeapOwner : GPUTextureHeapOwner
{
    internal required D3D12Device Device;
    internal nint Native;

    internal override void Destroy() => Release(Native);
}

internal sealed unsafe class D3D12TimelineSemaphore : GPUTimelineSemaphore
{
    internal required D3D12Device Owner;
    internal nint Native;

    /// <summary>The latest completed value signaled by the GPU.</summary>
    public override ulong CompletedValue => D3D12Interop.CompletedValue(Owner, Native);

    internal override void Wait(ulong value) => D3D12Interop.Wait(Owner, Native, value);

    /// <summary>Releases the semaphore.</summary>
    public override void Destroy() => Release(Native);
}

internal sealed unsafe partial class D3D12Device : GPUDevice
{
    internal nint Native;
    internal nint Factory;
    internal D3D12Queue[] Queues = [];
    internal nint RootSignature;
    internal uint ResourceDescriptorSize;
    internal uint SamplerDescriptorSize;
    internal uint RenderDescriptorSize;
    internal uint DepthDescriptorSize;
    internal bool PresentationEnabled;
    internal int SwapchainCount;
    internal BufferViewSlots? BufferViews;
    internal readonly Lock DescriptorLock = new();
    internal nint BufferDescriptors;
    internal nuint BufferDescriptorCpu;
    internal nint DefaultResourceHeap;
    internal nuint DefaultResourceCpu;
    internal nint DefaultSamplerHeap;
    internal D3D12TextureDescriptorHeap? ActiveTextureHeaps;
    private ulong* BufferViewAddresses;
    private nint InfoQueue;
    private uint MessageCookie;
    private bool MessageCallbackRegistered;
    private readonly FeatureFormatSupport[] FormatSupport = new FeatureFormatSupport[Backend.FormatCount];
    private readonly FeatureFormatSupport[] SampledFormatSupport = new FeatureFormatSupport[Backend.FormatCount];

    private D3D12Device() : base(GraphicsBackend.Dx12) { }

    internal static D3D12Device Create(DeviceDesc desc)
    {
        Debug.Assert(desc.DesiredQueueCount != 0);
        D3D12Device device = new() { PresentationEnabled = !desc.Headless, TimestampQueryCount = desc.TimestampQueryCount };
        try
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
                throw D3D12Interop.CreationFailure(0, "the D3D12 backend requires 64-bit Windows", Error.Unsupported);
            D3D12Interop.InitializeRuntime();
            Guid factoryIid = IidFactory6;
            nint factory = 0;
            D3D12Interop.Check(CreateFactory(0, &factoryIid, &factory), "CreateDXGIFactory2");
            device.Factory = factory;
            device.InitializeAdapter();
            device.InitializeValidation();
            device.RequireBaseline(desc.DesiredCopyQueueCount != 0 && desc.TimestampQueryCount != 0);
            device.InitializeFormats();
            device.ResourceDescriptorSize = GetDescriptorHandleIncrementSize(device.Native, 0);
            device.SamplerDescriptorSize = GetDescriptorHandleIncrementSize(device.Native, 1);
            device.RenderDescriptorSize = GetDescriptorHandleIncrementSize(device.Native, 2);
            device.DepthDescriptorSize = GetDescriptorHandleIncrementSize(device.Native, 3);
            device.InitializeDescriptorHeaps();
            device.BufferViewAddresses = (ulong*)NativeMemory.AllocZeroed(BufferViewSlots.Capacity, sizeof(ulong));
            device.BufferViews = new BufferViewSlots(device.BufferViewAddresses);
            device.InitializeRootSignature();
            device.InitializeQueues(desc);
            device.Capabilities.MaxPushDataSize = uint.MaxValue;
            device.Capabilities.TextureDescriptorHeapCapacity = D3D12Interop.MaxResourceDescriptors - D3D12Interop.TextureDescriptorOffset;
            device.Capabilities.SamplerDescriptorHeapCapacity = Backend.BindlessSamplerDescriptorCapacity;
            device.Capabilities.TextureHeapAlignment = D3D12Interop.TextureHeapAlignment;
            device.Capabilities.TimestampPeriodNs = desc.TimestampQueryCount == 0 ? 0 : 1_000_000_000.0f / device.Queues[0].TimestampFrequency;
            device.Capabilities.SubTexelPrecisionBits = 8;
            device.Capabilities.TextureCompressionBc = true;
            device.Capabilities.StorageInputOutput16 = true;
            device.Capabilities.IndirectMeshDraw = true;
            return device;
        }
        catch (GPUException)
        {
            device.Destroy();
            throw;
        }
        catch (DllNotFoundException exception)
        {
            device.Destroy();
            throw D3D12Interop.CreationFailure(0, exception.Message, Error.Unsupported);
        }
        catch (EntryPointNotFoundException exception)
        {
            device.Destroy();
            throw D3D12Interop.CreationFailure(0, exception.Message, Error.Unsupported);
        }
    }

    private void InitializeAdapter()
    {
        Guid adapterIid = IidAdapter1;
        Guid deviceIid = IidDevice10;
        for (uint index = 0; ; ++index)
        {
            nint candidate = 0;
            int result = EnumAdapterByGpuPreference(Factory, index, 2, &adapterIid, &candidate);
            if (result == unchecked((int)0x887a0002)) break;
            D3D12Interop.Check(result, "IDXGIFactory6.EnumAdapterByGpuPreference");
            try
            {
                AdapterDesc1 adapter;
                D3D12Interop.Check(GetAdapterDesc1(candidate, &adapter), "IDXGIAdapter1.GetDesc1");
                if ((adapter.Flags & 2) != 0) continue;
                nint native = 0;
                result = D3D12Native.CreateDevice(candidate, 0xc100, &deviceIid, &native);
                if (result >= 0)
                {
                    Native = native;
                    Capabilities.DeviceName = new string(adapter.Description);
                    return;
                }
                Release(native);
            }
            finally
            {
                Release(candidate);
            }
        }
        throw D3D12Interop.CreationFailure(0, "a hardware device with feature level 12.1 and ID3D12Device10 is required", Error.Unsupported);
    }

    private void InitializeValidation()
    {
        if (!D3D12Interop.ValidationEnabled) return;
        Guid infoIid = IidInfoQueue1;
        nint infoQueue = 0;
        D3D12Interop.Check(QueryInterface(Native, &infoIid, &infoQueue), "query ID3D12InfoQueue1 for validation");
        InfoQueue = infoQueue;
        uint cookie = 0;
        D3D12Interop.Check(RegisterMessageCallback(infoQueue, &D3D12Interop.ValidationMessage, &cookie), "register validation callback");
        MessageCookie = cookie;
        MessageCallbackRegistered = true;
    }

    private void RequireBaseline(bool copyQueueTimestamps)
    {
        FeatureOptions options;
        FeatureOptions3 options3;
        FeatureOptions4 options4;
        FeatureOptions7 options7;
        FeatureOptions12 options12;
        FeatureOptions13 options13;
        FeatureOptions14 options14;
        D3D12Interop.Check(CheckFeatureSupport(Native, 0, &options, (uint)sizeof(FeatureOptions)), "query D3D12 options");
        D3D12Interop.Check(CheckFeatureSupport(Native, 21, &options3, (uint)sizeof(FeatureOptions3)), "query D3D12 options 3");
        D3D12Interop.Check(CheckFeatureSupport(Native, 23, &options4, (uint)sizeof(FeatureOptions4)), "query D3D12 options 4");
        D3D12Interop.Check(CheckFeatureSupport(Native, 32, &options7, (uint)sizeof(FeatureOptions7)), "query D3D12 options 7");
        D3D12Interop.Check(CheckFeatureSupport(Native, 41, &options12, (uint)sizeof(FeatureOptions12)), "query D3D12 options 12");
        D3D12Interop.Check(CheckFeatureSupport(Native, 42, &options13, (uint)sizeof(FeatureOptions13)), "query D3D12 options 13");
        D3D12Interop.Check(CheckFeatureSupport(Native, 43, &options14, (uint)sizeof(FeatureOptions14)), "query D3D12 options 14");
        int shaderModel = 0x66;
        int rootVersion = 2;
        D3D12Interop.Check(CheckFeatureSupport(Native, 7, &shaderModel, sizeof(int)), "query shader model 6.6");
        D3D12Interop.Check(CheckFeatureSupport(Native, 12, &rootVersion, sizeof(int)), "query root signature version 1.1");
        if (options.ResourceBindingTier < 3 || options.ResourceHeapTier < 2 || options3.CastingFullyTypedFormatSupported == 0 ||
            options4.Native16BitShaderOpsSupported == 0 || options7.MeshShaderTier == 0 || options12.EnhancedBarriersSupported == 0 ||
            options13.UnrestrictedBufferTextureCopyPitchSupported == 0 || options14.IndependentFrontAndBackStencilRefMaskSupported == 0 ||
            shaderModel < 0x66 || rootVersion < 2)
        {
            throw D3D12Interop.CreationFailure(0,
                "requires resource binding tier 3, heap tier 2, typed format casting, native 16-bit shaders, mesh shaders, enhanced barriers, " +
                "unrestricted texture copy pitches, independent stencil references/masks, shader model 6.6, and root signatures 1.1", Error.Unsupported);
        }
        if (copyQueueTimestamps && options3.CopyQueueTimestampQueriesSupported == 0)
            throw D3D12Interop.CreationFailure(0, "timestamp queries on every requested queue class are required", Error.Unsupported);
    }

    private void InitializeFormats()
    {
        for (int index = 0; index < Backend.FormatCount; ++index)
        {
            Format format = (Format)index;
            int nativeFormat = D3D12Interop.Format(format);
            if (nativeFormat == 0) continue;
            FeatureFormatSupport support = new() { Format = nativeFormat };
            D3D12Interop.Check(CheckFeatureSupport(Native, 3, &support, (uint)sizeof(FeatureFormatSupport)), "query texture format support");
            FormatSupport[index] = support;
            int sampledFormat = D3D12Interop.ShaderResourceFormat(format, TextureAspect.Automatic);
            if (sampledFormat != nativeFormat)
            {
                support = new FeatureFormatSupport { Format = sampledFormat };
                D3D12Interop.Check(CheckFeatureSupport(Native, 3, &support, (uint)sizeof(FeatureFormatSupport)), "query depth/stencil sampling support");
            }
            SampledFormatSupport[index] = support;
        }
    }

    internal void InitializeDescriptorHeaps()
    {
        DescriptorHeapDesc desc = new() { NumDescriptors = D3D12Interop.TextureDescriptorOffset };
        nint source = 0;
        D3D12Interop.Check(CreateDescriptorHeap(Native, &desc, &source), "create shared CPU buffer descriptor heap");
        BufferDescriptors = source;
        BufferDescriptorCpu = GetCPUDescriptorHandleForHeapStart(source);
        desc.Flags = 1;
        nint shader = 0;
        D3D12Interop.Check(CreateDescriptorHeap(Native, &desc, &shader), "create default shader-visible buffer descriptor heap");
        DefaultResourceHeap = shader;
        DefaultResourceCpu = GetCPUDescriptorHandleForHeapStart(shader);
        desc.Type = 1;
        desc.NumDescriptors = 1;
        nint sampler = 0;
        D3D12Interop.Check(CreateDescriptorHeap(Native, &desc, &sampler), "create default shader-visible sampler heap");
        DefaultSamplerHeap = sampler;
        DefaultSamplerDesc samplerDesc = new()
        {
            Filter = 0x15,
            AddressU = 1,
            AddressV = 1,
            AddressW = 1,
            MaxAnisotropy = 1,
            ComparisonFunc = 1,
            MaxLOD = float.MaxValue,
        };
        CreateDefaultSampler(Native, &samplerDesc, GetCPUDescriptorHandleForHeapStart(sampler));
        lock (DescriptorLock)
        {
            WriteBufferDescriptors(0, 0, 1);
            nuint uavCpu = BufferDescriptorCpu + (nuint)D3D12Interop.BufferUavOffset * ResourceDescriptorSize;
            for (uint count = 1; count < D3D12Interop.BufferViewCapacity; count *= 2)
            {
                CopyDescriptorsSimple(Native, count, BufferDescriptorCpu + (nuint)count * ResourceDescriptorSize, BufferDescriptorCpu, 0);
                CopyDescriptorsSimple(Native, count, uavCpu + (nuint)count * ResourceDescriptorSize, uavCpu, 0);
            }
            CopyDescriptorsSimple(Native, D3D12Interop.TextureDescriptorOffset, DefaultResourceCpu, BufferDescriptorCpu, 0);
        }
    }

    internal void DestroyDescriptorHeaps()
    {
        Debug.Assert(ActiveTextureHeaps == null);
        Release(DefaultSamplerHeap);
        DefaultSamplerHeap = 0;
        Release(DefaultResourceHeap);
        Release(BufferDescriptors);
        DefaultResourceHeap = 0;
        BufferDescriptors = 0;
    }

    internal void WriteBufferDescriptors(uint index, nint resource, uint elements)
    {
        ShaderResourceViewDesc srv = new()
        {
            Format = 39,
            ViewDimension = 1,
            Shader4ComponentMapping = 5768,
            Buffer = new BufferSrv { NumElements = elements, Flags = 1 },
        };
        UnorderedAccessViewDesc uav = new()
        {
            Format = 39,
            ViewDimension = 1,
            Buffer = new BufferUav { NumElements = elements, Flags = 1 },
        };
        CreateShaderResourceView(Native, resource, &srv, BufferDescriptorCpu + (nuint)index * ResourceDescriptorSize);
        CreateUnorderedAccessView(Native, resource, 0, &uav,
            BufferDescriptorCpu + (nuint)(D3D12Interop.BufferUavOffset + index) * ResourceDescriptorSize);
    }

    private void InitializeRootSignature()
    {
        RootParameter1 parameter = new() { ParameterType = 3, Flags = 2 };
        VersionedRootSignatureDesc desc = new()
        {
            Version = 2,
            Desc1 = new RootSignatureDesc1 { NumParameters = 1, Parameters = &parameter, Flags = 0x400 | 0x800 },
        };
        nint blob = 0;
        nint errors = 0;
        try
        {
            int result = SerializeVersionedRootSignature(&desc, &blob, &errors);
            if (result < 0)
            {
                string diagnostic = errors != 0 ? Marshal.PtrToStringUTF8((nint)GetBlobBufferPointer(errors), (int)GetBlobBufferSize(errors)) ?? "" : "";
                throw D3D12Interop.CreationFailure(result, $"serialize bindless root signature: {diagnostic}");
            }
            nint signature = 0;
            D3D12Interop.Check(CreateRootSignature(Native, GetBlobBufferPointer(blob), GetBlobBufferSize(blob), &signature), "create bindless root signature");
            RootSignature = signature;
        }
        finally
        {
            Release(errors);
            Release(blob);
        }
    }

    private void InitializeQueues(DeviceDesc desc)
    {
        QueueCount = desc.DesiredQueueCount + desc.DesiredComputeQueueCount + desc.DesiredCopyQueueCount;
        Capabilities.GeneralQueueCount = desc.DesiredQueueCount;
        Capabilities.ComputeQueueCount = desc.DesiredComputeQueueCount;
        Capabilities.CopyQueueCount = desc.DesiredCopyQueueCount;
        Capabilities.QueueCount = QueueCount;
        Queues = new D3D12Queue[QueueCount];
        for (uint index = 0; index < QueueCount; ++index) Queues[index] = new D3D12Queue();
        for (uint index = 0; index < QueueCount; ++index)
        {
            D3D12Queue queue = Queues[index];
            queue.Type = index < desc.DesiredQueueCount ? 0 : index < desc.DesiredQueueCount + desc.DesiredComputeQueueCount ? 2 : 3;
            CommandQueueDesc queueDesc = new() { Type = queue.Type };
            nint native = 0;
            D3D12Interop.Check(CreateCommandQueue(Native, &queueDesc, &native), "create command queue");
            queue.Native = native;
            nint completion = 0;
            D3D12Interop.Check(CreateFence(Native, 0, &completion), "create queue completion fence");
            queue.Completion = completion;
            if (TimestampQueryCount != 0)
            {
                ulong frequency = 0;
                D3D12Interop.Check(GetTimestampFrequency(native, &frequency), "get queue timestamp frequency");
                queue.TimestampFrequency = frequency;
            }
        }
    }

    /// <summary>Releases the device after its resources have been destroyed and all submitted work has completed.</summary>
    public override void Destroy()
    {
        Debug.Assert(Volatile.Read(ref SwapchainCount) == 0);
        for (int index = 0; index < Queues.Length; ++index)
        {
            D3D12Queue queue = Queues[index];
            Debug.Assert(queue.Completion == 0 || D3D12Interop.CompletedValue(this, queue.Completion) >= queue.Value);
            queue.DestroyCommandStorage();
            Release(queue.Completion);
            Release(queue.Native);
        }
        Queues = [];
        Release(RootSignature);
        RootSignature = 0;
        BufferViews = null;
        NativeMemory.Free(BufferViewAddresses);
        BufferViewAddresses = null;
        DestroyDescriptorHeaps();
        if (MessageCallbackRegistered)
            D3D12Interop.CheckExecution(UnregisterMessageCallback(InfoQueue, MessageCookie), "unregister validation callback");
        Release(InfoQueue);
        InfoQueue = 0;
        MessageCallbackRegistered = false;
        Release(Native);
        Release(Factory);
        Native = 0;
        Factory = 0;
    }

    /// <summary>Allocates a linear GPU heap on this device.</summary>
    /// <param name="size">The heap size in bytes.</param>
    /// <param name="memory">The heap's memory type.</param>
    /// <returns>The allocated heap and its optional CPU mapping.</returns>
    public override GPUHeap AllocateGPUHeap(ulong size, MemoryType memory)
    {
        Debug.Assert(size != 0);
        if (memory != MemoryType.CpuVisible && memory != MemoryType.GpuOnly && memory != MemoryType.Readback)
            throw D3D12Interop.CreationFailure(0, "unsupported memory type", Error.Unsupported);
        HeapProperties properties = new()
        {
            Type = memory == MemoryType.GpuOnly ? 1 : 4,
            CPUPageProperty = memory == MemoryType.CpuVisible ? 2 : memory == MemoryType.Readback ? 3 : 0,
            MemoryPoolPreference = memory == MemoryType.GpuOnly ? 0 : 1,
            CreationNodeMask = 1,
            VisibleNodeMask = 1,
        };
        ResourceDesc1 desc = new()
        {
            Dimension = 1,
            Width = Backend.AlignUp(size, 4),
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            SampleDesc = new SampleDesc { Count = 1 },
            Layout = 1,
            Flags = ResourceFlagAllowUnorderedAccess,
        };
        nint resource = 0;
        D3D12Interop.Check(CreateCommittedResource3(Native, &properties, 0, &desc, -1, &resource), "create GPU heap buffer");
        D3D12GpuHeapOwner owner = new() { Device = this, Native = resource, Size = size, Memory = memory };
        try
        {
            byte* mapped = null;
            if (memory != MemoryType.GpuOnly)
            {
                D3D12Native.Range readRange = new() { End = memory == MemoryType.Readback ? (nuint)size : 0 };
                D3D12Interop.Check(Map(resource, 0, &readRange, (void**)&mapped), "map GPU heap buffer");
            }
            owner.Address = GetGPUVirtualAddress(resource);
            owner.ViewIndex = BufferViews!.Allocate(owner.Address);
            if (owner.ViewIndex == 0)
                throw D3D12Interop.CreationFailure(0, "65,535 backing-buffer view slots exhausted", Error.Unsupported);
            lock (DescriptorLock)
            {
                WriteBufferDescriptors(owner.ViewIndex, resource, (uint)(desc.Width / 4));
                CopyBufferDescriptors(owner.ViewIndex);
            }
            return new GPUHeap
            {
                Range = new GPUAllocation<byte> { Cpu = mapped, Buffer = owner, SizeBytes = size },
                Owner = owner,
            };
        }
        catch (GPUException)
        {
            owner.Destroy();
            throw;
        }
    }

    /// <summary>Allocates a dedicated texture-placement heap.</summary>
    /// <param name="byteCount">The heap size in bytes.</param>
    /// <returns>The allocated heap.</returns>
    public override GPUTextureHeap CreateTextureHeap(ulong byteCount)
    {
        // Multisampled placements require a 4 MB-aligned heap, and any heap that can hold one is at least that large.
        ulong alignment = byteCount >= D3D12Interop.MsaaTextureHeapAlignment ? D3D12Interop.MsaaTextureHeapAlignment : D3D12Interop.TextureHeapAlignment;
        HeapDesc desc = new()
        {
            SizeInBytes = Backend.AlignUp(byteCount, alignment),
            Properties = new HeapProperties { Type = 1, CreationNodeMask = 1, VisibleNodeMask = 1 },
            Alignment = alignment,
        };
        nint heap = 0;
        D3D12Interop.Check(CreateHeap(Native, &desc, &heap), "create texture placement heap");
        return new GPUTextureHeap { Size = byteCount, Owner = new D3D12TextureHeapOwner { Device = this, Native = heap } };
    }

    /// <summary>Returns a texture's required placement size and alignment, or zero when unsupported.</summary>
    /// <param name="desc">The texture description.</param>
    /// <returns>The required size and placement alignment.</returns>
    public override SizeAlign GetTextureSizeAlign(TextureDesc desc)
    {
        if (!Backend.SupportsTextureSamples(in desc) || !SupportsTextureFormat(desc.Format, desc.Usage)) return default;
        ResourceDesc native = D3D12Interop.TextureResourceDesc(desc);
        ResourceAllocationInfo info = GetResourceAllocationInfo(Native, 1, &native);
        return info.SizeInBytes == ulong.MaxValue ? default : new SizeAlign { Size = info.SizeInBytes, Align = info.Alignment };
    }

    /// <summary>Places a texture in a texture heap.</summary>
    /// <param name="desc">The texture description.</param>
    /// <param name="heap">The backing texture heap.</param>
    /// <param name="offset">The aligned placement offset in bytes.</param>
    /// <returns>The placed texture.</returns>
    public override GPUTexture CreateTexture(TextureDesc desc, GPUTextureHeap heap, ulong offset)
    {
        Debug.Assert(heap.Owner is D3D12TextureHeapOwner owner && owner.Device == this);
        Backend.RequireTextureSamples(in desc);
        if (!SupportsTextureFormat(desc.Format, desc.Usage))
            throw D3D12Interop.CreationFailure(0, $"texture format {desc.Format} does not support usage {desc.Usage}", Error.Unsupported);
        ResourceDesc1 native = D3D12Interop.TextureResourceDesc1(desc);
        nint resource = 0;
        D3D12Interop.Check(CreatePlacedResource2(Native, ((D3D12TextureHeapOwner)heap.Owner!).Native, offset, &native, 0, &resource),
            "create placed texture");
        return new D3D12Texture { Owner = this, Native = resource, Desc = desc };
    }

    /// <summary>Returns whether textures of the given format and usage can be created.</summary>
    /// <param name="format">The texture format.</param>
    /// <param name="usage">The requested usage.</param>
    /// <returns>Whether the format supports every requested use.</returns>
    public override bool SupportsTextureFormat(Format format, TextureUsage usage)
    {
        if ((uint)format >= Backend.FormatCount || D3D12Interop.Format(format) == 0) return false;
        TextureFormatInfo info = Gpu.GetTextureFormatInfo(format);
        FeatureFormatSupport support = FormatSupport[(int)format];
        if ((support.Support1 & 0x20) == 0) return false;
        if (info.Depth && info.Stencil && (usage & (TextureUsage.TransferSource | TextureUsage.TransferDestination)) != 0) return false;
        if (Backend.HasFlag(usage, TextureUsage.Sampled) && (SampledFormatSupport[(int)format].Support1 & 0x100) == 0) return false;
        if (Backend.HasFlag(usage, TextureUsage.Storage) &&
            ((support.Support1 & 0x2000000) == 0 || (support.Support2 & 0xc0) != 0xc0 || info.Depth || info.Stencil)) return false;
        if (Backend.HasFlag(usage, TextureUsage.ColorAttachment) && ((support.Support1 & 0x4000) == 0 || info.Depth || info.Stencil)) return false;
        if (Backend.HasFlag(usage, TextureUsage.DepthStencilAttachment) && ((support.Support1 & 0x10000) == 0 || !(info.Depth || info.Stencil))) return false;
        return true;
    }

    /// <summary>Blocks until every submitted command has completed.</summary>
    public override void WaitIdle()
    {
        for (int index = 0; index < Queues.Length; ++index)
        {
            D3D12Queue queue = Queues[index];
            D3D12Interop.CheckExecution(QueueSignal(queue.Native, queue.Completion, ++queue.Value), "signal queue idle fence");
        }
        for (int index = 0; index < Queues.Length; ++index)
        {
            D3D12Queue queue = Queues[index];
            D3D12Interop.Wait(this, queue.Completion, queue.Value);
        }
    }

    /// <summary>Creates a timeline semaphore.</summary>
    /// <param name="initialValue">The initial completed value.</param>
    /// <returns>The semaphore.</returns>
    public override GPUTimelineSemaphore CreateTimelineSemaphore(ulong initialValue = 0)
    {
        nint fence = 0;
        D3D12Interop.Check(CreateFence(Native, initialValue, &fence), "create timeline fence");
        return new D3D12TimelineSemaphore { Owner = this, Native = fence };
    }
}
