// D3D12 textures, attachment views, and CPU-sourced bindless descriptor heaps.

using System.Diagnostics;
using static D3D12.D3D12Native;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class D3D12Texture : GPUTexture
{
    internal required D3D12Device Owner;
    internal nint Native;
    internal TextureDesc Desc;
    internal D3D12Swapchain? Swapchain;

    /// <summary>Creates an owned attachment view of one mip level and slice.</summary>
    /// <param name="desc">The attachment mip level and slice.</param>
    /// <returns>The render view.</returns>
    public override GPURenderView CreateRenderView(RenderViewDesc desc)
    {
        TextureFormatInfo format = Gpu.GetTextureFormatInfo(Desc.Format);
        bool depth = format.Depth || format.Stencil;
        Debug.Assert(desc.MipLevel < Desc.MipLevels);
        Debug.Assert(desc.Slice < (Desc.Type == TextureType.ThreeD ? Math.Max(1u, Desc.Extent.Z >> (int)desc.MipLevel) : Desc.LayerCount));
        ResourceDescriptorHeapDesc heapDesc = new() { Type = depth ? 3u : 2u, NumDescriptors = 1 };
        nint heap = 0;
        try
        {
            D3D12Interop.Check(ResourceCreateDescriptorHeap(Owner.Native, &heapDesc, out heap), "CreateDescriptorHeap (render view)");
            nuint handle = ResourceGetCpuDescriptorHandle(heap);
            if (depth)
            {
                ResourceDepthStencilViewDesc view = new()
                {
                    Format = D3D12Interop.Format(Desc.Format),
                    ViewDimension = Desc.SampleCount > 1 ? (Desc.LayerCount > 1 ? 6u : 5u) :
                        Desc.Type == TextureType.OneD ? (Desc.LayerCount > 1 ? 2u : 1u) : (Desc.LayerCount > 1 ? 4u : 3u),
                    MipSlice = desc.MipLevel,
                    FirstArraySlice = desc.Slice,
                    ArraySize = 1,
                };
                if (Desc.SampleCount > 1 && Desc.LayerCount > 1)
                    view.Texture2DMSArray = new ResourceTexture2DMSArray { FirstArraySlice = desc.Slice, ArraySize = 1 };
                Debug.Assert(Desc.Type != TextureType.ThreeD);
                ResourceCreateDepthStencilView(Owner.Native, Native, &view, handle);
            }
            else
            {
                ResourceRenderTargetViewDesc view = new() { Format = D3D12Interop.Format(Desc.Format) };
                if (Desc.SampleCount > 1)
                {
                    view.ViewDimension = Desc.LayerCount > 1 ? 7u : 6u;
                    if (Desc.LayerCount > 1)
                        view.Texture2DMSArray = new ResourceTexture2DMSArray { FirstArraySlice = desc.Slice, ArraySize = 1 };
                }
                else if (Desc.Type == TextureType.ThreeD)
                {
                    view.ViewDimension = 8;
                    view.Texture3D = new ResourceTexture3DUav { MipSlice = desc.MipLevel, FirstWSlice = desc.Slice, WSize = 1 };
                }
                else if (Desc.Type == TextureType.OneD)
                {
                    view.ViewDimension = Desc.LayerCount > 1 ? 3u : 2u;
                    view.Texture1DArray = new ResourceTexture1DArrayRtv { MipSlice = desc.MipLevel, FirstArraySlice = desc.Slice, ArraySize = 1 };
                }
                else if (Desc.LayerCount > 1)
                {
                    view.ViewDimension = 5;
                    view.Texture2DArray = new ResourceArrayRtv { MipSlice = desc.MipLevel, FirstArraySlice = desc.Slice, ArraySize = 1 };
                }
                else
                {
                    view.ViewDimension = 4;
                    view.Texture2D = new ResourceTexture2DUav { MipSlice = desc.MipLevel };
                }
                ResourceCreateRenderTargetView(Owner.Native, Native, &view, handle);
            }
            D3D12RenderView result = new()
            {
                Owner = Owner,
                Texture = this,
                DescriptorHeap = heap,
                Handle = handle,
                Mip = desc.MipLevel,
                Slice = desc.Slice,
                Width = Math.Max(1u, Desc.Extent.X >> (int)desc.MipLevel),
                Height = Math.Max(1u, Desc.Extent.Y >> (int)desc.MipLevel),
                IsDepth = depth,
            };
            AddRef(Native);
            return result;
        }
        catch (GPUException)
        {
            Release(heap);
            throw;
        }
    }

    /// <summary>Releases an owned texture after every view and GPU use have finished.</summary>
    public override void Destroy()
    {
        Debug.Assert(Swapchain == null, "swapchain textures are borrowed");
        Release(Native);
    }
}

internal sealed unsafe partial class D3D12RenderView : GPURenderView
{
    internal required D3D12Device Owner;
    internal required D3D12Texture Texture;
    internal nint DescriptorHeap;
    internal nuint Handle;
    internal uint Mip;
    internal uint Slice;
    internal uint Width;
    internal uint Height;
    internal bool SwapchainView;
    internal bool IsDepth;

    /// <summary>The pixel format of the underlying attachment texture.</summary>
    public override Format Format => Texture.Desc.Format;

    /// <summary>The sample count of the underlying attachment texture.</summary>
    public override uint SampleCount => Texture.Desc.SampleCount == 0 ? 1u : Texture.Desc.SampleCount;

    /// <summary>Releases an owned attachment view after every render pass use has finished.</summary>
    public override void Destroy()
    {
        Debug.Assert(!SwapchainView, "swapchain render views are borrowed");
        Release(DescriptorHeap);
        Release(Texture.Native);
    }
}

internal sealed unsafe partial class D3D12TextureDescriptorHeap : GPUTextureDescriptorHeap
{
    internal const uint BufferPrefixCount = 131072;
    internal const uint MaximumCapacity = 1_000_000 - BufferPrefixCount;
    internal required D3D12Device Owner;
    internal nint Native;
    internal nint Source;
    internal nuint Cpu;
    internal nuint ShaderCpu;
    internal D3D12TextureDescriptorHeap? Next;
    internal D3D12TextureDescriptorHeap? Previous;

    /// <summary>Binds a sampled or storage texture view into one heap slot.</summary>
    /// <param name="index">The destination slot.</param>
    /// <param name="texture">The referenced texture.</param>
    /// <param name="type">The descriptor access type.</param>
    /// <param name="desc">The texture view description.</param>
    public override void WriteTextureDescriptor(uint index, GPUTexture texture, TextureDescriptorType type, TextureDescriptorDesc desc)
    {
        D3D12Texture resource = (D3D12Texture)texture;
        Debug.Assert(index < Capacity && resource.Owner == Owner && resource.Swapchain == null && resource.Desc.SampleCount == 1);
        TextureDesc textureDesc = resource.Desc;
        Format format = desc.Format == Format.Undefined ? textureDesc.Format : desc.Format;
        uint mipCount = desc.MipCount != 0 ? desc.MipCount : textureDesc.MipLevels - desc.BaseMip;
        uint layerCount = desc.LayerCount != 0 ? desc.LayerCount : textureDesc.LayerCount - desc.BaseLayer;
        uint plane = D3D12Interop.TexturePlane(format, desc.Aspect);
        nuint handle = Cpu + (nuint)index * Owner.ResourceDescriptorSize;
        if (type == TextureDescriptorType.Sampled)
        {
            int nativeFormat = D3D12Interop.ShaderResourceFormat(format, desc.Aspect);
            ResourceShaderResourceViewDesc view = new()
            {
                Format = nativeFormat,
                // DXGI stencil-plane views expose stencil in G rather than R.
                Shader4ComponentMapping = nativeFormat is 22 or 47 ? 6945u : 5768u,
            };
            switch (textureDesc.Type)
            {
                case TextureType.OneD:
                    view.ViewDimension = 2;
                    view.Texture1D = new ResourceMipSrv { MostDetailedMip = desc.BaseMip, MipLevels = mipCount };
                    break;
                case TextureType.TwoD:
                    view.ViewDimension = 4;
                    view.Texture2D = new ResourceTexture2DSrv { MostDetailedMip = desc.BaseMip, MipLevels = mipCount, PlaneSlice = plane };
                    break;
                case TextureType.TwoDArray:
                    view.ViewDimension = 5;
                    view.Texture2DArray = new ResourceTexture2DArraySrv
                    {
                        MostDetailedMip = desc.BaseMip,
                        MipLevels = mipCount,
                        FirstArraySlice = desc.BaseLayer,
                        ArraySize = layerCount,
                        PlaneSlice = plane,
                    };
                    break;
                case TextureType.ThreeD:
                    view.ViewDimension = 8;
                    view.Texture3D = new ResourceMipSrv { MostDetailedMip = desc.BaseMip, MipLevels = mipCount };
                    break;
                case TextureType.Cube:
                    view.ViewDimension = 9;
                    view.TextureCube = new ResourceMipSrv { MostDetailedMip = desc.BaseMip, MipLevels = mipCount };
                    break;
                case TextureType.CubeArray:
                    view.ViewDimension = 10;
                    view.TextureCubeArray = new ResourceArraySrv
                    {
                        MostDetailedMip = desc.BaseMip,
                        MipLevels = mipCount,
                        FirstArraySlice = desc.BaseLayer,
                        ArraySize = layerCount / 6,
                    };
                    break;
            }
            ResourceCreateShaderResourceView(Owner.Native, resource.Native, &view, handle);
        }
        else
        {
            Debug.Assert(mipCount == 1);
            ResourceUnorderedAccessViewDesc view = new() { Format = D3D12Interop.Format(format) };
            switch (textureDesc.Type)
            {
                case TextureType.OneD:
                    view.ViewDimension = 2;
                    view.Texture1DMipSlice = desc.BaseMip;
                    break;
                case TextureType.TwoD:
                    view.ViewDimension = 4;
                    view.Texture2D = new ResourceTexture2DUav { MipSlice = desc.BaseMip, PlaneSlice = plane };
                    break;
                case TextureType.ThreeD:
                    view.ViewDimension = 8;
                    view.Texture3D = new ResourceTexture3DUav
                    {
                        MipSlice = desc.BaseMip,
                        WSize = Math.Max(1u, textureDesc.Extent.Z >> (int)desc.BaseMip),
                    };
                    break;
                default:
                    view.ViewDimension = 5;
                    view.Texture2DArray = new ResourceArrayUav
                    {
                        MipSlice = desc.BaseMip,
                        FirstArraySlice = desc.BaseLayer,
                        ArraySize = layerCount,
                        PlaneSlice = plane,
                    };
                    break;
            }
            ResourceCreateUnorderedAccessView(Owner.Native, resource.Native, &view, handle);
        }
        ResourceCopyDescriptors(Owner.Native, 1, ShaderCpu + (nuint)(BufferPrefixCount + index) * Owner.ResourceDescriptorSize, handle, 0);
    }

    /// <summary>Copies texture slots with overlap-safe semantics.</summary>
    /// <param name="sourceIndex">The first source slot.</param>
    /// <param name="destination">The destination heap.</param>
    /// <param name="destinationIndex">The first destination slot.</param>
    /// <param name="count">The number of slots to copy.</param>
    public override void CopyTextureDescriptors(uint sourceIndex, GPUTextureDescriptorHeap destination, uint destinationIndex, uint count)
    {
        D3D12TextureDescriptorHeap target = (D3D12TextureDescriptorHeap)destination;
        Debug.Assert(Owner == target.Owner && sourceIndex + count <= Capacity && destinationIndex + count <= target.Capacity);
        if (count == 0 || (this == target && sourceIndex == destinationIndex)) return;
        uint stride = Owner.ResourceDescriptorSize;
        if (this == target && sourceIndex < destinationIndex + count && destinationIndex < sourceIndex + count)
        {
            bool backwards = destinationIndex > sourceIndex;
            for (uint n = 0; n < count; ++n)
            {
                uint i = backwards ? count - 1 - n : n;
                ResourceCopyDescriptors(Owner.Native, 1, target.Cpu + (nuint)(destinationIndex + i) * stride,
                    Cpu + (nuint)(sourceIndex + i) * stride, 0);
            }
        }
        else ResourceCopyDescriptors(Owner.Native, count, target.Cpu + (nuint)destinationIndex * stride, Cpu + (nuint)sourceIndex * stride, 0);
        ResourceCopyDescriptors(Owner.Native, count, target.ShaderCpu + (nuint)(BufferPrefixCount + destinationIndex) * stride,
            target.Cpu + (nuint)destinationIndex * stride, 0);
    }

    /// <summary>Releases the descriptor heap after every recorded and executing use has finished.</summary>
    public override void Destroy()
    {
        lock (Owner.DescriptorLock)
        {
            if (Previous != null) Previous.Next = Next;
            else Owner.ActiveTextureHeaps = Next;
            if (Next != null) Next.Previous = Previous;
        }
        Release(Native);
        Release(Source);
    }
}

internal sealed unsafe partial class D3D12SamplerDescriptorHeap : GPUSamplerDescriptorHeap
{
    internal required D3D12Device Owner;
    internal nint Native;
    internal nint Source;
    internal nuint Cpu;
    internal nuint ShaderCpu;

    /// <summary>Binds a sampler into one heap slot.</summary>
    /// <param name="index">The destination slot.</param>
    /// <param name="desc">The sampler description.</param>
    public override void WriteSamplerDescriptor(uint index, SamplerDesc desc)
    {
        Debug.Assert(index < Capacity);
        ResourceSamplerDesc sampler = new()
        {
            Filter = desc.Anisotropic ? 0x55u : (uint)desc.MinFilter << 4 | (uint)desc.MagFilter << 2 | (uint)desc.MipFilter,
            AddressU = (uint)desc.AddressU + 1,
            AddressV = (uint)desc.AddressV + 1,
            AddressW = (uint)desc.AddressW + 1,
            MaxAnisotropy = desc.Anisotropic ? 4u : 1u,
            ComparisonFunc = desc.CompareEnabled ? (uint)desc.Compare + 1 : 1u,
            MaxLod = float.MaxValue,
        };
        if (desc.CompareEnabled) sampler.Filter |= 0x80;
        nuint handle = Cpu + (nuint)index * Owner.SamplerDescriptorSize;
        ResourceCreateSampler(Owner.Native, &sampler, handle);
        ResourceCopyDescriptors(Owner.Native, 1, ShaderCpu + (nuint)index * Owner.SamplerDescriptorSize, handle, 1);
    }

    /// <summary>Copies sampler slots with overlap-safe semantics.</summary>
    /// <param name="sourceIndex">The first source slot.</param>
    /// <param name="destination">The destination heap.</param>
    /// <param name="destinationIndex">The first destination slot.</param>
    /// <param name="count">The number of slots to copy.</param>
    public override void CopySamplerDescriptors(uint sourceIndex, GPUSamplerDescriptorHeap destination, uint destinationIndex, uint count)
    {
        D3D12SamplerDescriptorHeap target = (D3D12SamplerDescriptorHeap)destination;
        Debug.Assert(Owner == target.Owner && sourceIndex + count <= Capacity && destinationIndex + count <= target.Capacity);
        if (count == 0 || (this == target && sourceIndex == destinationIndex)) return;
        uint stride = Owner.SamplerDescriptorSize;
        if (this == target && sourceIndex < destinationIndex + count && destinationIndex < sourceIndex + count)
        {
            bool backwards = destinationIndex > sourceIndex;
            for (uint n = 0; n < count; ++n)
            {
                uint i = backwards ? count - 1 - n : n;
                ResourceCopyDescriptors(Owner.Native, 1, target.Cpu + (nuint)(destinationIndex + i) * stride,
                    Cpu + (nuint)(sourceIndex + i) * stride, 1);
            }
        }
        else ResourceCopyDescriptors(Owner.Native, count, target.Cpu + (nuint)destinationIndex * stride, Cpu + (nuint)sourceIndex * stride, 1);
        ResourceCopyDescriptors(Owner.Native, count, target.ShaderCpu + (nuint)destinationIndex * stride, target.Cpu + (nuint)destinationIndex * stride, 1);
    }

    /// <summary>Releases the descriptor heap after every recorded and executing use has finished.</summary>
    public override void Destroy()
    {
        Release(Native);
        Release(Source);
    }
}

internal sealed unsafe partial class D3D12Device
{
    internal void CopyBufferDescriptors(uint index)
    {
        nuint srvOffset = (nuint)index * ResourceDescriptorSize;
        nuint uavOffset = (nuint)(65536 + index) * ResourceDescriptorSize;
        ResourceCopyDescriptors(Native, 1, DefaultResourceCpu + srvOffset, BufferDescriptorCpu + srvOffset, 0);
        ResourceCopyDescriptors(Native, 1, DefaultResourceCpu + uavOffset, BufferDescriptorCpu + uavOffset, 0);
        for (D3D12TextureDescriptorHeap? heap = ActiveTextureHeaps; heap != null; heap = heap.Next)
        {
            ResourceCopyDescriptors(Native, 1, heap.ShaderCpu + srvOffset, BufferDescriptorCpu + srvOffset, 0);
            ResourceCopyDescriptors(Native, 1, heap.ShaderCpu + uavOffset, BufferDescriptorCpu + uavOffset, 0);
        }
    }

    /// <summary>Creates a bindless texture descriptor heap.</summary>
    /// <param name="capacity">The requested number of texture slots, clamped to the native heap limit.</param>
    /// <returns>The descriptor heap.</returns>
    public override GPUTextureDescriptorHeap CreateTextureDescriptorHeap(uint capacity)
    {
        Debug.Assert(capacity != 0);
        capacity = Math.Min(capacity, D3D12TextureDescriptorHeap.MaximumCapacity);
        D3D12TextureDescriptorHeap result = new() { Owner = this, Capacity = capacity };
        try
        {
            ResourceDescriptorHeapDesc source = new() { NumDescriptors = capacity };
            D3D12Interop.Check(ResourceCreateDescriptorHeap(Native, &source, out result.Source), "CreateDescriptorHeap (texture source)");
            ResourceDescriptorHeapDesc destination = new()
            {
                NumDescriptors = D3D12TextureDescriptorHeap.BufferPrefixCount + capacity,
                Flags = 1,
            };
            D3D12Interop.Check(ResourceCreateDescriptorHeap(Native, &destination, out result.Native), "CreateDescriptorHeap (texture)");
            result.Cpu = ResourceGetCpuDescriptorHandle(result.Source);
            result.ShaderCpu = ResourceGetCpuDescriptorHandle(result.Native);
            lock (DescriptorLock)
            {
                ResourceCopyDescriptors(Native, D3D12TextureDescriptorHeap.BufferPrefixCount, result.ShaderCpu, BufferDescriptorCpu, 0);
                result.Next = ActiveTextureHeaps;
                if (ActiveTextureHeaps != null) ActiveTextureHeaps.Previous = result;
                ActiveTextureHeaps = result;
            }
            return result;
        }
        catch (GPUException)
        {
            Release(result.Native);
            Release(result.Source);
            throw;
        }
    }

    /// <summary>Creates a sampler descriptor heap.</summary>
    /// <param name="capacity">The number of sampler slots, at most 2048.</param>
    /// <returns>The descriptor heap.</returns>
    public override GPUSamplerDescriptorHeap CreateSamplerDescriptorHeap(uint capacity)
    {
        Debug.Assert(capacity != 0);
        if (capacity > 2048) throw new GPUException(Error.Unsupported, "D3D12 sampler descriptor heaps support at most 2048 slots.");
        D3D12SamplerDescriptorHeap result = new() { Owner = this, Capacity = capacity };
        try
        {
            ResourceDescriptorHeapDesc source = new() { Type = 1, NumDescriptors = capacity };
            D3D12Interop.Check(ResourceCreateDescriptorHeap(Native, &source, out result.Source), "CreateDescriptorHeap (sampler source)");
            ResourceDescriptorHeapDesc destination = new() { Type = 1, NumDescriptors = capacity, Flags = 1 };
            D3D12Interop.Check(ResourceCreateDescriptorHeap(Native, &destination, out result.Native), "CreateDescriptorHeap (sampler)");
            result.Cpu = ResourceGetCpuDescriptorHandle(result.Source);
            result.ShaderCpu = ResourceGetCpuDescriptorHandle(result.Native);
            return result;
        }
        catch (GPUException)
        {
            Release(result.Native);
            Release(result.Source);
            throw;
        }
    }
}
