namespace Vocore.Rendering;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

/// <summary>
/// The facility to manage rendering resource and perform rendering.
/// <br/>It is a high-level encapsulation of <see cref="GPUDevice"/>.
/// </summary>
public partial class RenderingSystem
{

    private readonly GPUDevice _device;

    //preffered
    private readonly GPURenderPass _prefferedSDRPass;
    private readonly GPURenderPass _prefferedHDRPass;
    private readonly PixelFormat _prefferedSDRFormat;
    private readonly PixelFormat _prefferedHDRFormat;
    

    public GPUDevice GraphicsDevice
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _device;
    }


    public GPURenderPass PrefferedSDRPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _prefferedSDRPass;
        }
    }

    public GPURenderPass PrefferedHDRPass
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _prefferedHDRPass;
        }
    }

    public RenderingSystem(GPUDevice device)
    {
        _device = device;

        _prefferedSDRFormat = device.PrefferedSDRFormat;
        _prefferedHDRFormat = device.PrefferedHDRFormat;

        _prefferedSDRPass = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(_prefferedSDRFormat)],
            new(PixelFormat.Depth24PlusStencil8),
            "sdr_pass"
        ));

        _prefferedHDRPass = device.CreateRenderPass(new RenderPassDescriptor
        (
            [new(_prefferedHDRFormat)],
            new(PixelFormat.Depth24PlusStencil8),
            "hdr_pass"
        ));
    }
}