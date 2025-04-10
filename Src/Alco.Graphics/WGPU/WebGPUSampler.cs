using System.Runtime.CompilerServices;
using WebGPU;

using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal sealed class WebGPUSampler : GPUSampler
{
    #region Properties

    private readonly WGPUSampler _native;

    #endregion

    #region Abstract Implementation

    protected override GPUDevice Device { get; }

    protected override void Dispose(bool disposing)
    {

            wgpuSamplerRelease(_native);
    }

    #endregion

    #region WebGPU Implementation

    public WGPUSampler Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _native;
    }

    

    public unsafe WebGPUSampler(WebGPUDevice device, in SamplerDescriptor descriptor): base(descriptor)
    {
        Device = device;

        WGPUDevice nativeDevice = device.Native;
        ReadOnlySpan<byte> name = descriptor.Name.GetUtf8Span();
        fixed (byte* ptrName = name)
        {
            WGPUSamplerDescriptor nativeDescriptor = new WGPUSamplerDescriptor()
            {
                nextInChain = null,
                label = new WGPUStringView(ptrName, name.Length),
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