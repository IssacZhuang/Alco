using System.Runtime.CompilerServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

internal sealed unsafe class AlcoGpuSampler : GPUSampler
{
    #region Properties
    private readonly AlcoHandle _native;

    #endregion

    #region Abstract Implementation
    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        if (!_native.IsNull)
        {
            AlcoGpuNative.SamplerDestroy(((AlcoGpuDevice)Device).Native, _native);
        }
    }

    #endregion

    #region AlcoGpu Implementation
    public AlcoHandle Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    internal AlcoGpuSampler(AlcoGpuDevice device, in SamplerDescriptor descriptor) : base(descriptor)
    {
        Device = device;

        ReadOnlySpan<byte> name = Name.Utf8Z();
        fixed (byte* ptrName = name)
        {
            AlcoSamplerDesc desc = new()
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

    #endregion
}
