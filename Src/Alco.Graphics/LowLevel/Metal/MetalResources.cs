// Metal backend part 2: texture heaps and textures, render views, descriptor heaps, pipelines, command pools, and the depth-state cache.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Mtl = Metal;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class MetalGpuHeapOwner : GPUHeapOwner
{
    internal MetalDevice Device = null!;
    internal Mtl.MTLHeap Heap;
    internal Mtl.MTLBuffer Buffer;
    internal ulong Address;

    internal override void Destroy()
    {
        using Mtl.NSAutoreleasePool pool = new();
        Device.BufferViews!.Free(ViewIndex);
        MetalBackend.RemoveResident(Device, Device.ShaderValidation ? (Mtl.MTLAllocation)(Mtl.MTLResource)Buffer : (Mtl.MTLAllocation)Heap);
        Buffer.Dispose();
        Heap.Dispose();
    }
}

internal sealed unsafe partial class MetalTextureHeapOwner : GPUTextureHeapOwner
{
    internal MetalDevice Device = null!;
    internal Mtl.MTLHeap Heap;

    internal override void Destroy()
    {
        using Mtl.NSAutoreleasePool pool = new();
        if (!Device.ShaderValidation) MetalBackend.RemoveResident(Device, Heap);
        Heap.Dispose();
    }
}

internal sealed unsafe partial class MetalTexture : GPUTexture
{
    internal MetalDevice Owner = null!;
    internal Mtl.MTLTexture Native;
    internal TextureDesc Desc;
    internal MetalSwapchain? Swapchain;

    /// <summary>Creates a render-target view.</summary>
    /// <param name="desc">The attachment mip level and slice.</param>
    /// <returns>The render view.</returns>
    public override GPURenderView CreateRenderView(RenderViewDesc desc)
    {
        using Mtl.NSAutoreleasePool pool = new();
        return new MetalRenderView
        {
            Owner = Owner,
            Source = this,
            Texture = new Mtl.MTLTexture(MetalInterop.retain(Native.NativePtr)),
            Swapchain = Swapchain,
            Mip = desc.MipLevel,
            Slice = desc.Slice,
        };
    }

    /// <summary>Releases the texture after every view and GPU use have finished.</summary>
    public override void Destroy()
    {
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(Swapchain == null, "swapchain textures are borrowed");
        if (Owner.ShaderValidation) MetalBackend.RemoveResident(Owner, Native);
        Native.Dispose();
    }
}

internal sealed unsafe partial class MetalRenderView : GPURenderView
{
    internal MetalDevice Owner = null!;
    internal required MetalTexture Source;
    internal Mtl.MTLTexture Texture;
    internal uint Mip;
    internal uint Slice;
    internal MetalSwapchain? Swapchain;
    internal bool SwapchainView;

    internal Extent2D Extent => new(Math.Max((uint)Texture.Width >> (int)Mip, 1u), Math.Max((uint)Texture.Height >> (int)Mip, 1u));

    /// <summary>Releases an owned view after every render pass use has finished; swapchain views are borrowed.</summary>
    public override void Destroy()
    {
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(!SwapchainView, "swapchain render views are borrowed");
        Texture.Dispose();
    }
}

internal sealed unsafe partial class MetalTextureDescriptorHeap : GPUTextureDescriptorHeap
{
    internal MetalDevice Owner = null!;
    internal Mtl.MTLTextureViewPool Pool;
    internal Mtl.MTLBuffer BaseBuffer;

    /// <summary>Releases the descriptor heap.</summary>
    public override void Destroy()
    {
        using Mtl.NSAutoreleasePool pool = new();
        MetalBackend.RemoveResident(Owner, BaseBuffer);
        BaseBuffer.Dispose();
        Pool.Dispose();
    }

    /// <summary>Binds a texture view into one heap slot.</summary>
    /// <param name="index">The destination slot.</param>
    /// <param name="texture">The owned texture to bind.</param>
    /// <param name="type">The descriptor access type.</param>
    /// <param name="desc">The texture view description.</param>
    public override void WriteTextureDescriptor(uint index, GPUTexture texture, TextureDescriptorType type, TextureDescriptorDesc desc)
    {
        MetalTexture metalTexture = (MetalTexture)texture;
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(index < Capacity && metalTexture.Owner == Owner && metalTexture.Swapchain == null);
        Mtl.MTLTextureViewDescriptor view = new();
        view.PixelFormat = MetalInterop.PixelFormat(desc.Format == Format.Undefined ? metalTexture.Desc.Format : desc.Format);
        if (desc.Aspect == TextureAspect.Stencil && metalTexture.Desc.Format == Format.D32FloatS8Uint) view.PixelFormat = Mtl.MTLPixelFormat.X32Stencil8;
        view.TextureType = metalTexture.Native.TextureType;
        if (type == TextureDescriptorType.Storage && (view.TextureType == Mtl.MTLTextureType.Cube || view.TextureType == Mtl.MTLTextureType.CubeArray))
            view.TextureType = Mtl.MTLTextureType.Type2DArray;
        view.LevelRange = new Mtl.NSRange { location = desc.BaseMip, length = desc.MipCount != 0 ? desc.MipCount : metalTexture.Desc.MipLevels - desc.BaseMip };
        view.SliceRange = new Mtl.NSRange
        {
            location = desc.BaseLayer,
            length = desc.LayerCount != 0 ? desc.LayerCount : metalTexture.Desc.LayerCount - desc.BaseLayer,
        };
        Mtl.MTLResourceID resource = Pool.SetTextureView(metalTexture.Native, view, index);
        Debug.Assert(resource._impl == Pool.BaseResourceID._impl + index);
        view.Dispose();
    }

    /// <summary>Copies heap slots into another heap.</summary>
    public override void CopyTextureDescriptors(uint sourceIndex, GPUTextureDescriptorHeap destination, uint destinationIndex, uint count)
    {
        MetalTextureDescriptorHeap destinationHeap = (MetalTextureDescriptorHeap)destination;
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(Owner == destinationHeap.Owner && sourceIndex + count <= Capacity && destinationIndex + count <= destinationHeap.Capacity);
        if (count == 0 || (this == destinationHeap && sourceIndex == destinationIndex)) return;
        if (this == destinationHeap && destinationIndex > sourceIndex && destinationIndex < sourceIndex + count)
        {
            for (uint i = count; i != 0; --i)
                destinationHeap.Pool.CopyResourceViewsFromPool(Pool, new Mtl.NSRange { location = sourceIndex + i - 1, length = 1 }, destinationIndex + i - 1);
        }
        else if (this == destinationHeap && sourceIndex > destinationIndex && sourceIndex < destinationIndex + count)
        {
            for (uint i = 0; i < count; ++i)
                destinationHeap.Pool.CopyResourceViewsFromPool(Pool, new Mtl.NSRange { location = sourceIndex + i, length = 1 }, destinationIndex + i);
        }
        else destinationHeap.Pool.CopyResourceViewsFromPool(Pool, new Mtl.NSRange { location = sourceIndex, length = count }, destinationIndex);
    }
}

internal sealed unsafe partial class MetalSamplerDescriptorHeap : GPUSamplerDescriptorHeap
{
    internal MetalDevice Owner = null!;
    internal Mtl.MTLBuffer Buffer;
    internal Mtl.MTLSamplerState[] Samplers = Array.Empty<Mtl.MTLSamplerState>();

    /// <summary>Releases the descriptor heap.</summary>
    public override void Destroy()
    {
        using Mtl.NSAutoreleasePool pool = new();
        for (uint i = 0; i < Capacity; ++i) Samplers[i].Dispose();
        MetalBackend.RemoveResident(Owner, Buffer);
        Buffer.Dispose();
    }

    /// <summary>Binds a sampler into one heap slot.</summary>
    public override void WriteSamplerDescriptor(uint index, SamplerDesc desc)
    {
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(index < Capacity);
        Mtl.MTLSamplerDescriptor sampler = new();
        sampler.MinFilter = desc.MinFilter == Filter.Linear ? Mtl.MTLSamplerMinMagFilter.Linear : Mtl.MTLSamplerMinMagFilter.Nearest;
        sampler.MagFilter = desc.MagFilter == Filter.Linear ? Mtl.MTLSamplerMinMagFilter.Linear : Mtl.MTLSamplerMinMagFilter.Nearest;
        sampler.MipFilter = desc.MipFilter == Filter.Linear ? Mtl.MTLSamplerMipFilter.Linear : Mtl.MTLSamplerMipFilter.Nearest;
        sampler.SAddressMode = MetalInterop.AddressMode(desc.AddressU);
        sampler.TAddressMode = MetalInterop.AddressMode(desc.AddressV);
        sampler.RAddressMode = MetalInterop.AddressMode(desc.AddressW);
        sampler.MaxAnisotropy = (uint)(desc.Anisotropic ? 4 : 1);
        sampler.CompareFunction = desc.CompareEnabled ? (Mtl.MTLCompareFunction)desc.Compare : Mtl.MTLCompareFunction.Never;
        sampler.SupportArgumentBuffers = true;
        Mtl.MTLSamplerState state = Owner.MtlDevice.NewSamplerState(sampler);
        sampler.Dispose();
        Debug.Assert(state.NativePtr != 0);
        Samplers[index].Dispose();
        Samplers[index] = state;
        ((Mtl.MTLResourceID*)Buffer.Contents)[index] = state.GpuResourceID;
    }

    /// <summary>Copies heap slots into another heap.</summary>
    public override void CopySamplerDescriptors(uint sourceIndex, GPUSamplerDescriptorHeap destination, uint destinationIndex, uint count)
    {
        MetalSamplerDescriptorHeap destinationHeap = (MetalSamplerDescriptorHeap)destination;
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(Owner == destinationHeap.Owner && sourceIndex + count <= Capacity && destinationIndex + count <= destinationHeap.Capacity);
        if (count == 0 || (this == destinationHeap && sourceIndex == destinationIndex)) return;
        bool backwards = this == destinationHeap && destinationIndex > sourceIndex;
        for (uint n = 0; n < count; ++n)
        {
            uint i = backwards ? count - 1 - n : n;
            Mtl.MTLSamplerState state = new(MetalInterop.retain(Samplers[sourceIndex + i].NativePtr));
            destinationHeap.Samplers[destinationIndex + i].Dispose();
            destinationHeap.Samplers[destinationIndex + i] = state;
            Mtl.MTLResourceID resourceId = state.NativePtr != 0 ? state.GpuResourceID : default;
            ((Mtl.MTLResourceID*)destinationHeap.Buffer.Contents)[destinationIndex + i] = resourceId;
        }
    }
}

internal sealed unsafe partial class MetalPso : GPUPipeline
{
    internal MetalDevice Owner = null!;
    internal Mtl.MTLRenderPipelineState Render;
    internal Mtl.MTLComputePipelineState Compute;
    internal RasterizationState Rasterization;
    internal Mtl.MTLPrimitiveType PrimitiveType;
    internal Mtl.MTLSize Threads;
    internal Mtl.MTLSize ObjectThreads;
    internal bool Mesh;

    /// <summary>Releases the pipeline after every recorded and executing use has finished.</summary>
    public override void Destroy()
    {
        using Mtl.NSAutoreleasePool pool = new();
        if (Render.NativePtr != 0) MetalBackend.RemoveResident(Owner, Render);
        if (Compute.NativePtr != 0) MetalBackend.RemoveResident(Owner, Compute);
        Render.Dispose();
        Compute.Dispose();
    }
}

internal sealed unsafe partial class MetalCommandPool : GPUCommandPool
{
    internal MetalDevice Owner = null!;
    internal MetalCommandBuffer? First;
    internal MetalCommandBuffer? Last;
    internal MetalCommandBuffer? NextBuffer;
    internal MetalQueueKind Kind;
    internal uint QueueIndex;
    internal DepthStateChunk DepthStates = null!;

    private MetalCommandBuffer CreateContext()
    {
        MetalDevice device = Owner;
        MetalCommandBuffer result = new() { Owner = device, Pool = this };
        try
        {
            result.NativeBuffers = new NativeCommandBuffer
            {
                Buffer = device.MtlDevice.NewCommandBuffer,
                Allocator = device.MtlDevice.NewCommandAllocator(),
            };
            result.Commands = result.NativeBuffers.Buffer;
            if (result.Commands.NativePtr == 0) throw MetalBackend.CreationFailure(default, "command context buffer");
            if (result.NativeBuffers.Allocator.NativePtr == 0) throw MetalBackend.CreationFailure(default, "command context allocator");
            using Mtl.MTL4ArgumentTableDescriptor arguments = new() { MaxBufferBindCount = 4, InitializeBindings = true };
            if (arguments.NativePtr == 0) throw MetalBackend.CreationFailure(default, "command argument table descriptor");
            Mtl.NSError error = default;
            result.Arguments = device.MtlDevice.NewArgumentTable(arguments, ref error);
            if (result.Arguments.NativePtr == 0) throw MetalBackend.CreationFailure(error, "command argument table");
            result.Pass = new Mtl.MTL4RenderPassDescriptor();
            if (result.Pass.NativePtr == 0) throw MetalBackend.CreationFailure(default, "command render pass descriptor");
            if (device.TimestampQueryCount != 0)
            {
                result.Timestamps = new MetalTimestampSlot[device.TimestampQueryCount];
                device.AllocateTimestamps(result.Timestamps);
            }
            return result;
        }
        catch (GPUException)
        {
            result.DestroyContext();
            throw;
        }
    }

    private static bool SameStencil(in StencilFaceState a, in StencilFaceState b) =>
        a.Compare == b.Compare && a.Fail == b.Fail && a.Pass == b.Pass && a.DepthFail == b.DepthFail;

    private static bool SameDepth(in DepthStencilState a, in DepthStencilState b) =>
        a.DepthTest == b.DepthTest && a.DepthWrite == b.DepthWrite && a.DepthCompare == b.DepthCompare && a.StencilTest == b.StencilTest &&
        a.StencilReadMask == b.StencilReadMask && a.StencilWriteMask == b.StencilWriteMask &&
        SameStencil(in a.Front, in b.Front) && SameStencil(in a.Back, in b.Back);

    private static void StencilDescriptor(Mtl.MTLStencilDescriptor descriptor, in StencilFaceState face, in DepthStencilState state)
    {
        descriptor.StencilCompareFunction = (Mtl.MTLCompareFunction)face.Compare;
        descriptor.StencilFailureOperation = (Mtl.MTLStencilOperation)face.Fail;
        descriptor.DepthFailureOperation = (Mtl.MTLStencilOperation)face.DepthFail;
        descriptor.DepthStencilPassOperation = (Mtl.MTLStencilOperation)face.Pass;
        descriptor.ReadMask = state.StencilReadMask;
        descriptor.WriteMask = state.StencilWriteMask;
    }

    internal Mtl.MTLDepthStencilState DepthState(in DepthStencilState state, bool creating = false)
    {
        MetalDevice device = Owner;
        ref DepthStateChunk chunk = ref DepthStates;
        while (chunk != null)
        {
            for (uint i = 0; i < chunk.Count; ++i)
                if (SameDepth(in chunk.Entries[(int)i].Desc, in state)) return chunk.Entries[(int)i].State;
            if (chunk.Count < 64) break;
            chunk = ref chunk.Next;
        }
        if (chunk == null) chunk = new DepthStateChunk();
        Mtl.MTLDepthStencilDescriptor descriptor = new();
        descriptor.DepthCompareFunction = state.DepthTest ? (Mtl.MTLCompareFunction)state.DepthCompare : Mtl.MTLCompareFunction.Always;
        descriptor.IsDepthWriteEnabled = state.DepthWrite && state.DepthTest;
        if (state.StencilTest)
        {
            Mtl.MTLStencilDescriptor front = new();
            Mtl.MTLStencilDescriptor back = new();
            StencilDescriptor(front, in state.Front, in state);
            StencilDescriptor(back, in state.Back, in state);
            descriptor.FrontFaceStencil = front;
            descriptor.BackFaceStencil = back;
            front.Dispose();
            back.Dispose();
        }
        Mtl.MTLDepthStencilState result = device.MtlDevice.NewDepthStencilState(descriptor);
        descriptor.Dispose();
        if (result.NativePtr == 0)
        {
            if (creating) throw MetalBackend.CreationFailure(default, "command pool depth state");
            Backend.RequireError(Error.DriverError, "Metal depth state");
        }
        chunk.Entries[chunk.Count++] = new MetalDepthStateEntry { Desc = state, State = result };
        return result;
    }

    /// <summary>Recycles every command buffer while leaving acquired images owned by their swapchains.</summary>
    public override void Reset()
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        for (MetalCommandBuffer? commands = First; commands != null; commands = commands.Next)
        {
            Debug.Assert(commands.Retirement.NativePtr == 0 || commands.Retirement.SignaledValue >= commands.RetirementValue);
            if (commands.AcquiredSwapchain != null)
            {
                commands.AcquiredSwapchain.AcquiringCommands = null;
                commands.AcquiredSwapchain = null;
            }
            if (commands.Render.NativePtr != 0) commands.EndRenderPass();
            commands.EndCompute();
            if (commands.Recording) commands.Commands.EndCommandBuffer();
            commands.Recording = false;
            commands.Ended = false;
            commands.Retirement = default;
            commands.RetirementValue = 0;
            commands.UsesSwapchain = false;
            for (NativeCommandBuffer native = commands.NativeBuffers; native != null; native = native.Next) native.Allocator.Reset();
        }
        NextBuffer = First;
    }

    /// <summary>Releases the pool and its command buffers.</summary>
    public override void Destroy()
    {
        using Mtl.NSAutoreleasePool autorelease = new();
        Reset();
        for (MetalCommandBuffer? commands = First; commands != null;)
        {
            MetalCommandBuffer? next = commands.Next;
            commands.DestroyContext();
            commands = next;
        }
        for (DepthStateChunk chunk = DepthStates; chunk != null;)
        {
            DepthStateChunk next = chunk.Next;
            for (uint i = 0; i < chunk.Count; ++i) chunk.Entries[(int)i].State.Dispose();
            chunk = next;
        }
    }
}

internal sealed unsafe partial class MetalCommandBuffer : GPUCommandBuffer
{
    internal MetalDevice Owner = null!;
    internal MetalCommandBuffer? Next;
    internal MetalCommandPool Pool = null!;
    internal Mtl.MTL4CommandBuffer Commands;
    internal NativeCommandBuffer NativeBuffers = null!;
    internal NativeCommandBuffer Native = null!;
    internal uint NativeCount;
    internal Mtl.MTL4ArgumentTable Arguments;
    internal ulong RootAddress;
    internal Mtl.MTL4ComputeCommandEncoder Compute;
    internal Mtl.MTL4RenderCommandEncoder Render;
    internal Mtl.MTL4RenderPassDescriptor Pass;
    private uint ColorResolveMask;
    internal MetalTimestampSlot[]? Timestamps;
    internal Mtl.MTLSharedEvent Retirement;
    internal ulong RetirementValue;
    internal bool Recording;
    internal bool Ended;
    internal bool RenderContinuation;
    internal bool UsesSwapchain;
    internal MetalSwapchain? AcquiredSwapchain;
    internal MetalPso? Pipeline;
}

internal sealed unsafe partial class MetalDevice : GPUDevice
{
    private static Mtl.MTLTextureDescriptor TextureDescriptor(in TextureDesc desc)
    {
        Mtl.MTLTextureDescriptor result = new();
        result.TextureType = desc.SampleCount > 1
            ? desc.Type == TextureType.TwoDArray ? Mtl.MTLTextureType.Type2DMultisampleArray : Mtl.MTLTextureType.Type2DMultisample
            : MetalInterop.MtlTextureType(desc.Type);
        result.PixelFormat = MetalInterop.PixelFormat(desc.Format);
        result.Width = desc.Extent.X;
        result.Height = desc.Extent.Y;
        result.Depth = desc.Extent.Z;
        result.MipmapLevelCount = desc.MipLevels;
        result.ArrayLength = desc.Type == TextureType.CubeArray ? desc.LayerCount / 6 :
                             desc.Type == TextureType.TwoDArray ? desc.LayerCount : 1;
        result.SampleCount = desc.SampleCount;
        result.StorageMode = Mtl.MTLStorageMode.Private;
        result.HazardTrackingMode = Mtl.MTLHazardTrackingMode.Untracked;
        result.Usage = Mtl.MTLTextureUsage.Unknown;
        if (Backend.HasFlag(desc.Usage, TextureUsage.Sampled)) result.Usage |= Mtl.MTLTextureUsage.ShaderRead;
        if (Backend.HasFlag(desc.Usage, TextureUsage.Storage)) result.Usage |= Mtl.MTLTextureUsage.ShaderRead | Mtl.MTLTextureUsage.ShaderWrite;
        if (Backend.HasFlag(desc.Usage, TextureUsage.ColorAttachment) || Backend.HasFlag(desc.Usage, TextureUsage.DepthStencilAttachment))
            result.Usage |= Mtl.MTLTextureUsage.RenderTarget;
        if (desc.MutableFormat || Gpu.GetTextureFormatInfo(desc.Format).Stencil) result.Usage |= Mtl.MTLTextureUsage.PixelFormatView;
        return result;
    }

    /// <summary>Returns the texture size and placement alignment in a texture heap.</summary>
    public override SizeAlign GetTextureSizeAlign(TextureDesc desc)
    {
        if (!Backend.SupportsTextureSamples(in desc)) return default;
        using Mtl.NSAutoreleasePool pool = new();
        if (!SupportsTextureFormat(desc.Format, desc.Usage))
        {
            MetalInterop.ReportError(default, "unsupported texture format or usage");
            return default;
        }
        Mtl.MTLTextureDescriptor descriptor = TextureDescriptor(desc);
        Mtl.MTLSizeAndAlign result = MtlDevice.HeapTextureSizeAndAlign(descriptor);
        descriptor.Dispose();
        if (result.size == 0 || result.align == 0)
        {
            MetalInterop.ReportError(default, "texture memory requirements");
            return default;
        }
        Debug.Assert(Capabilities.TextureHeapAlignment % result.align == 0);
        return new SizeAlign { Size = result.size, Align = result.align };
    }

    /// <summary>Places a texture in a heap.</summary>
    /// <param name="desc">The texture description.</param>
    /// <param name="heap">The backing texture heap.</param>
    /// <param name="offset">The aligned placement offset in bytes.</param>
    /// <returns>The texture.</returns>
    public override GPUTexture CreateTexture(TextureDesc desc, GPUTextureHeap heap, ulong offset)
    {
        Backend.RequireTextureSamples(in desc);
        if (!SupportsTextureFormat(desc.Format, desc.Usage))
            throw MetalBackend.CreationFailure(default, $"unsupported texture format {desc.Format} or usage {desc.Usage}", Error.Unsupported);
        MetalTextureHeapOwner owner = (MetalTextureHeapOwner)heap.Owner!;
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(heap.Owner != null && owner.Device == this);
        using Mtl.MTLTextureDescriptor descriptor = TextureDescriptor(desc);
        if (descriptor.NativePtr == 0) throw MetalBackend.CreationFailure(default, "texture descriptor");
        Mtl.MTLTexture texture = owner.Heap.NewTexture(descriptor, offset);
        if (texture.NativePtr == 0) throw MetalBackend.CreationFailure(default, "texture");
        if (ShaderValidation) MetalBackend.AddResident(this, texture);
        return new MetalTexture { Owner = this, Native = texture, Desc = desc };
    }

    /// <summary>Creates a bindless texture descriptor heap.</summary>
    /// <param name="capacity">The number of descriptor slots.</param>
    /// <returns>The descriptor heap.</returns>
    public override GPUTextureDescriptorHeap CreateTextureDescriptorHeap(uint capacity)
    {
        using Mtl.NSAutoreleasePool pool = new();
        using Mtl.MTLResourceViewPoolDescriptor viewPoolDescriptor = new() { ResourceViewCount = capacity };
        if (viewPoolDescriptor.NativePtr == 0) throw MetalBackend.CreationFailure(default, "texture descriptor heap descriptor");
        MetalTextureDescriptorHeap result = new() { Owner = this, Capacity = capacity };
        try
        {
            Mtl.NSError error = default;
            result.Pool = MtlDevice.NewTextureViewPool(viewPoolDescriptor, ref error);
            if (result.Pool.NativePtr == 0) throw MetalBackend.CreationFailure(error, "texture descriptor heap");
            Mtl.MTLResourceID baseId = result.Pool.BaseResourceID;
            result.BaseBuffer = MtlDevice.NewBuffer((nint)(&baseId), (ulong)sizeof(Mtl.MTLResourceID), Mtl.MTLResourceOptions.ResourceStorageModeShared);
            if (result.BaseBuffer.NativePtr == 0) throw MetalBackend.CreationFailure(default, "texture pool base");
        }
        catch (GPUException)
        {
            result.BaseBuffer.Dispose();
            result.Pool.Dispose();
            throw;
        }
        MetalBackend.AddResident(this, result.BaseBuffer);
        return result;
    }

    /// <summary>Creates a sampler descriptor heap.</summary>
    /// <param name="capacity">The number of descriptor slots.</param>
    /// <returns>The descriptor heap.</returns>
    public override GPUSamplerDescriptorHeap CreateSamplerDescriptorHeap(uint capacity)
    {
        using Mtl.NSAutoreleasePool pool = new();
        MetalSamplerDescriptorHeap result = new() { Owner = this, Capacity = capacity };
        result.Buffer = MtlDevice.NewBuffer((ulong)capacity * (ulong)sizeof(Mtl.MTLResourceID), Mtl.MTLResourceOptions.ResourceStorageModeShared);
        if (result.Buffer.NativePtr == 0) throw MetalBackend.CreationFailure(default, "sampler descriptor heap");
        result.Samplers = new Mtl.MTLSamplerState[capacity];
        NativeMemory.Fill((void*)result.Buffer.Contents, (nuint)(capacity * sizeof(Mtl.MTLResourceID)), 0);
        MetalBackend.AddResident(this, result.Buffer);
        return result;
    }

    private Mtl.MTL4LibraryFunctionDescriptor ShaderFunction(ShaderStage stage)
    {
        if (stage.Code.IsEmpty) return default;
        using Mtl.MTLLibrary library = ShaderLibrary(stage.Code, stage.EntryPoint);
        Mtl.MTL4LibraryFunctionDescriptor function = new() { Library = library, Name = stage.EntryPoint };
        if (function.NativePtr == 0) throw MetalBackend.CreationFailure(default, $"shader function descriptor for {stage.EntryPoint}");
        return function;
    }

    private static void ColorTargets(Mtl.MTL4RenderPipelineColorAttachmentDescriptorArray attachments, ReadOnlySpan<ColorTargetDesc> targets)
    {
        for (int i = 0; i < targets.Length; ++i)
        {
            Mtl.MTL4RenderPipelineColorAttachmentDescriptor attachment = attachments.Object((ulong)i);
            attachment.PixelFormat = MetalInterop.PixelFormat(targets[i].Format);
            attachment.WriteMask = (Mtl.MTLColorWriteMask)((((targets[i].WriteMask & 1) << 3) | ((targets[i].WriteMask & 2) << 1) |
                                                             ((targets[i].WriteMask & 4) >> 1) | ((targets[i].WriteMask & 8) >> 3)));
            attachment.BlendingState = targets[i].Blend.Enabled ? Mtl.MTL4BlendState.Enabled : Mtl.MTL4BlendState.Disabled;
            attachment.SourceRGBBlendFactor = MetalInterop.BlendFactor(targets[i].Blend.Color.Source);
            attachment.DestinationRGBBlendFactor = MetalInterop.BlendFactor(targets[i].Blend.Color.Destination);
            attachment.RgbBlendOperation = (Mtl.MTLBlendOperation)targets[i].Blend.Color.Operation;
            attachment.SourceAlphaBlendFactor = MetalInterop.BlendFactor(targets[i].Blend.Alpha.Source);
            attachment.DestinationAlphaBlendFactor = MetalInterop.BlendFactor(targets[i].Blend.Alpha.Destination);
            attachment.AlphaBlendOperation = (Mtl.MTLBlendOperation)targets[i].Blend.Alpha.Operation;
        }
    }

    /// <summary>Builds a vertex/pixel pipeline.</summary>
    /// <param name="desc">The shaders, output formats, rasterization state and vertex assembly mode.</param>
    /// <returns>The graphics pipeline.</returns>
    public override GPUPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        Backend.RequireRasterSamples(desc.SampleCount, desc.ColorTargets, desc.DepthFormat, desc.StencilFormat);
        Debug.Assert(!desc.Vertex.Code.IsEmpty, "Graphics PSOs require a vertex stage");
        using Mtl.NSAutoreleasePool pool = new();
        using Mtl.MTL4LibraryFunctionDescriptor vertex = ShaderFunction(desc.Vertex);
        using Mtl.MTL4LibraryFunctionDescriptor pixel = ShaderFunction(desc.Pixel);
        using Mtl.MTL4RenderPipelineDescriptor pipeline = new()
        {
            VertexFunctionDescriptor = vertex,
            FragmentFunctionDescriptor = pixel,
            InputPrimitiveTopology = MetalInterop.PrimitiveTopologyClass(desc.Topology),
            RasterSampleCount = desc.SampleCount,
        };
        if (pipeline.NativePtr == 0) throw MetalBackend.CreationFailure(default, "graphics pipeline descriptor");
        ColorTargets(pipeline.ColorAttachments, desc.ColorTargets);
        Mtl.NSError error = default;
        Mtl.MTLRenderPipelineState state = MtlCompiler.NewRenderPipelineState(pipeline, PipelineTaskOptions, ref error);
        if (state.NativePtr == 0) throw MetalBackend.CreationFailure(error, "graphics pipeline");
        if (PipelineSerializer.NativePtr != 0) Volatile.Write(ref PipelineCacheDirty, true);
        MetalBackend.AddResident(this, state);
        return new MetalPso
        {
            Owner = this,
            Render = state,
            Rasterization = desc.Rasterization,
            PrimitiveType = MetalInterop.PrimitiveType(desc.Topology),
        };
    }

    /// <summary>Builds a task/mesh/pixel pipeline.</summary>
    /// <param name="desc">The shaders, output formats, rasterization state and threadgroup dimensions.</param>
    /// <returns>The mesh pipeline.</returns>
    public override GPUPipeline CreateMeshPipeline(MeshPipelineDesc desc)
    {
        Backend.RequireRasterSamples(desc.SampleCount, desc.ColorTargets, desc.DepthFormat, desc.StencilFormat);
        Debug.Assert(!desc.Mesh.Code.IsEmpty, "Mesh PSOs require a mesh stage");
        using Mtl.NSAutoreleasePool pool = new();
        using Mtl.MTL4LibraryFunctionDescriptor task = ShaderFunction(desc.Task);
        using Mtl.MTL4LibraryFunctionDescriptor mesh = ShaderFunction(desc.Mesh);
        using Mtl.MTL4LibraryFunctionDescriptor pixel = ShaderFunction(desc.Pixel);
        Mtl.MTL4MeshRenderPipelineDescriptor pipeline = new()
        {
            ObjectFunctionDescriptor = task,
            MeshFunctionDescriptor = mesh,
            FragmentFunctionDescriptor = pixel,
            RequiredThreadsPerMeshThreadgroup = MetalInterop.MetalSize(desc.Mesh.ThreadgroupSize),
            RasterSampleCount = desc.SampleCount,
        };
        try
        {
            if (pipeline.NativePtr == 0) throw MetalBackend.CreationFailure(default, "mesh pipeline descriptor");
            if (task.NativePtr != 0) pipeline.RequiredThreadsPerObjectThreadgroup = MetalInterop.MetalSize(desc.Task.ThreadgroupSize);
            ColorTargets(pipeline.ColorAttachments, desc.ColorTargets);
            Mtl.NSError error = default;
            Mtl.MTLRenderPipelineState state = MtlCompiler.NewRenderPipelineState(pipeline, PipelineTaskOptions, ref error);
            if (state.NativePtr == 0) throw MetalBackend.CreationFailure(error, "mesh pipeline");
            if (PipelineSerializer.NativePtr != 0) Volatile.Write(ref PipelineCacheDirty, true);
            MetalBackend.AddResident(this, state);
            return new MetalPso
            {
                Owner = this,
                Render = state,
                Rasterization = desc.Rasterization,
                Threads = MetalInterop.MetalSize(desc.Mesh.ThreadgroupSize),
                ObjectThreads = MetalInterop.MetalSize(desc.Task.ThreadgroupSize),
                Mesh = true,
            };
        }
        finally
        {
            pipeline.Dispose();
        }
    }

    /// <summary>Builds a compute pipeline.</summary>
    /// <param name="compute">The compute shader and its threadgroup dimensions.</param>
    /// <returns>The compute pipeline.</returns>
    public override GPUPipeline CreateComputePipeline(ShaderStage compute)
    {
        Debug.Assert(!compute.Code.IsEmpty, "Compute PSOs require a compute stage");
        using Mtl.NSAutoreleasePool pool = new();
        using Mtl.MTL4LibraryFunctionDescriptor function = ShaderFunction(compute);
        using Mtl.MTL4ComputePipelineDescriptor descriptor = new()
        {
            ComputeFunctionDescriptor = function,
            RequiredThreadsPerThreadgroup = MetalInterop.MetalSize(compute.ThreadgroupSize),
        };
        if (descriptor.NativePtr == 0) throw MetalBackend.CreationFailure(default, "compute pipeline descriptor");
        Mtl.NSError error = default;
        Mtl.MTLComputePipelineState state = MtlCompiler.NewComputePipelineState(descriptor, PipelineTaskOptions, ref error);
        if (state.NativePtr == 0) throw MetalBackend.CreationFailure(error, "compute pipeline");
        if (PipelineSerializer.NativePtr != 0) Volatile.Write(ref PipelineCacheDirty, true);
        MetalBackend.AddResident(this, state);
        return new MetalPso { Owner = this, Compute = state, Threads = MetalInterop.MetalSize(compute.ThreadgroupSize) };
    }

    internal void AllocateTimestamps(MetalTimestampSlot[] slots)
    {
        uint allocated = 0;
        for (;;)
        {
            CounterPage head = Volatile.Read(ref CounterPages);
            uint published = 0;
            for (CounterPage page = head; page != null; page = page.Next)
            {
                ++published;
                for (uint word = 0; word < 64; ++word)
                {
                    ulong occupied = Volatile.Read(ref page.occupied[(int)word]);
                    while (occupied != ~0ul)
                    {
                        ulong available = ~occupied;
                        ulong claim = 0;
                        for (uint i = allocated; i < TimestampQueryCount && available != 0; ++i)
                        {
                            claim |= 1ul << BitOperations.TrailingZeroCount(available);
                            available &= available - 1;
                        }
                        ulong observed = Interlocked.CompareExchange(ref page.occupied[(int)word], occupied | claim, occupied);
                        if (observed != occupied)
                        {
                            occupied = observed;
                            continue;
                        }
                        while (claim != 0)
                        {
                            uint bit = (uint)BitOperations.TrailingZeroCount(claim);
                            slots[allocated++] = new MetalTimestampSlot { Heap = page.Heap, Page = page, Index = word * 64 + bit };
                            claim &= claim - 1;
                        }
                        if (allocated == TimestampQueryCount) return;
                        break;
                    }
                }
            }
            if (Volatile.Read(ref CounterPages) != head) continue;
            int pageCount = Volatile.Read(ref CounterPageCount);
            if (pageCount == 32)
            {
                if (published != 32) continue;
                throw MetalBackend.CreationFailure(default, "timestamp storage capacity exhausted");
            }
            if (Interlocked.CompareExchange(ref CounterPageCount, pageCount + 1, pageCount) != pageCount) continue;
            if (Volatile.Read(ref CounterPages) != head)
            {
                Interlocked.Decrement(ref CounterPageCount);
                continue;
            }
            CounterPage created = new();
            try
            {
                using Mtl.MTL4CounterHeapDescriptor counters = new() { Type = Mtl.MTL4CounterHeapType.Timestamp, Count = 4096 };
                if (counters.NativePtr == 0) throw MetalBackend.CreationFailure(default, "timestamp storage descriptor");
                Mtl.NSError error = default;
                created.Heap = MtlDevice.NewCounterHeap(counters, ref error);
                if (created.Heap.NativePtr == 0) throw MetalBackend.CreationFailure(error, "timestamp storage");
            }
            catch (GPUException)
            {
                created.Heap.Dispose();
                Interlocked.Decrement(ref CounterPageCount);
                throw;
            }
            // Reserve the count before creation and re-verify the page list; every published page was retained at creation.
            CounterPage observedHead = head;
            for (;;)
            {
                created.Next = observedHead;
                CounterPage exchanged = Interlocked.CompareExchange(ref CounterPages, created, observedHead);
                if (exchanged == observedHead) break;
                observedHead = exchanged;
            }
        }
    }

    /// <summary>Creates a command pool for the given queue.</summary>
    /// <param name="queueIndex">The queue the pool records commands for.</param>
    /// <returns>The command pool.</returns>
    public override GPUCommandPool CreateCommandPool(uint queueIndex)
    {
        using Mtl.NSAutoreleasePool pool = new();
        Debug.Assert(queueIndex < Capabilities.QueueCount);
        MetalCommandPool commandPool = new() { Owner = this, Kind = MtlQueues[queueIndex].Kind, QueueIndex = queueIndex };
        try
        {
            commandPool.DepthState(default, true);
            for (uint compare = 0; compare < 8; ++compare)
                for (uint write = 0; write < 2; ++write)
                    commandPool.DepthState(new DepthStencilState { DepthTest = true, DepthWrite = write != 0, DepthCompare = (CompareOp)compare }, true);
            return commandPool;
        }
        catch (GPUException)
        {
            commandPool.Destroy();
            throw;
        }
    }
}
