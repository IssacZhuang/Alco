using System.Runtime.CompilerServices;
using WebGPU;

using static WebGPU.WebGPU;

namespace Vocore.Graphics.WebGPU;

internal sealed class WebGPUSampler : GPUSampler
{
    #region Properties

    private readonly WGPUSampler _native;
    private readonly bool _isBuiltIn;

    #endregion

    #region Abstract Implementation

    public override string Name { get; }

    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {
        if (!_isBuiltIn)
        {
            wgpuSamplerRelease(_native);
        }
        else
        {
            throw new InvalidOperationException("Trying to dispose a built-in sampler which is not allowed");
        }
    }

    #endregion

    #region WebGPU Implementation

    public WGPUSampler Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    

    public unsafe WebGPUSampler(WebGPUDevice device, SamplerDescriptor descriptor, bool isBuiltIn)
    {
        _isBuiltIn = isBuiltIn;
        Device = device;
        Name = descriptor.Name;

        WGPUDevice nativeDevice = device.Native;
        fixed (byte* ptrName = descriptor.Name.GetUtf8Span())
        {
            WGPUSamplerDescriptor nativeDescriptor = new WGPUSamplerDescriptor()
            {
                nextInChain = null,
                label = ptrName,
                addressModeU = UtilsWebGPU.AddressModeToWebGPU(descriptor.AddressModeU),
                addressModeV = UtilsWebGPU.AddressModeToWebGPU(descriptor.AddressModeV),
                addressModeW = UtilsWebGPU.AddressModeToWebGPU(descriptor.AddressModeW),
                magFilter = UtilsWebGPU.FilterModeToWebGPU(descriptor.MagFilter),
                minFilter = UtilsWebGPU.FilterModeToWebGPU(descriptor.MinFilter),
                mipmapFilter = UtilsWebGPU.MipmapFilterModeToWebGPU(descriptor.MipFilter),
                lodMinClamp = descriptor.LodMinClamp,
                lodMaxClamp = descriptor.LodMaxClamp,
                compare = UtilsWebGPU.CompareFunctionToWebGPU(descriptor.Compare),
                maxAnisotropy = descriptor.MaxAnisotropy,
            };

            _native = wgpuDeviceCreateSampler(nativeDevice, &nativeDescriptor);
        }
    }

    // only call by GPU device to release default samplers
    internal void InternalDispose()
    {
        wgpuSamplerRelease(_native);
    }


    #endregion

}