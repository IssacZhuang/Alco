using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

/// <summary>Describes AlcoGpuSampler.</summary>
internal sealed unsafe class AlcoGpuSampler : GPUSampler
{
    #region Properties
    private AlcoGpuAbi.SamplerHandle _native;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        try
        {
            AlcoGpuAbi.SamplerHandle handle = _native;
            _native = AlcoGpuAbi.SamplerHandle.Null;
            if (!handle.IsNull)
            {
                try
                {
                    AlcoGpuNative.SamplerDestroy(handle);
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
    /// <summary>Gets or stores the native ABI value.</summary>
    public AlcoGpuAbi.SamplerHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    internal AlcoGpuSampler(AlcoGpuDevice device, in SamplerDescriptor descriptor) : base(descriptor)
    {
        try
        {
            Device = device;

            ReadOnlySpan<byte> name = Name.Utf8Z();
            fixed (byte* ptrName = name)
            {
                AlcoGpuAbi.SamplerDesc desc = new()
                {
                    AddressU = (uint)descriptor.AddressModeU,
                    AddressV = (uint)descriptor.AddressModeV,
                    AddressW = (uint)descriptor.AddressModeW,
                    MagFilter = (uint)descriptor.MagFilter,
                    MinFilter = (uint)descriptor.MinFilter,
                    MipmapFilter = (uint)descriptor.MipFilter,
                    LodMinClamp = descriptor.LodMinClamp,
                    LodMaxClamp = descriptor.LodMaxClamp,
                    Compare = (uint)descriptor.Compare,
                    MaxAnisotropy = descriptor.MaxAnisotropy,
                    Name = ptrName,
                };

                AlcoGpuNative.SamplerCreate(device.Native, in desc, out _native);
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
        }
    }

    #endregion
}
