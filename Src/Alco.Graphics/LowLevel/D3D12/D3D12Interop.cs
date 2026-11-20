// D3D12 format, resource-description, creation-error, and timeline-wait helpers.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ApiFormat = global::Alco.Graphics.Format;
using D3D12Native = global::D3D12.D3D12Native;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal static unsafe partial class D3D12Interop
{
    internal const uint BufferViewCapacity = 65_536;
    internal const uint BufferUavOffset = BufferViewCapacity;
    internal const uint TextureDescriptorOffset = 2 * BufferViewCapacity;
    internal const uint MaxResourceDescriptors = 1_000_000;
    internal const ulong TextureHeapAlignment = 65_536;
    internal const ulong MsaaTextureHeapAlignment = 4 << 20;
    private static readonly Lock InitializationLock = new();
    private static bool Initialized;
    internal static bool ValidationEnabled;

    internal static void InitializeRuntime()
    {
        lock (InitializationLock)
        {
            if (Initialized) return;
            string? validation = Environment.GetEnvironmentVariable("NOGRAPHICS_VALIDATION");
            ValidationEnabled = int.TryParse(validation, out int enabled) && enabled != 0;
            if (ValidationEnabled)
            {
                Guid iid = D3D12Native.IidDebug1;
                nint debug = 0;
                Check(D3D12Native.GetDebugInterface(&iid, &debug), "D3D12GetDebugInterface (GPU validation)");
                D3D12Native.EnableDebugLayer(debug);
                D3D12Native.SetEnableGPUBasedValidation(debug, 1);
                D3D12Native.Release(debug);
            }
            Initialized = true;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    internal static void ValidationMessage(int category, int severity, int id, byte* description, void* context)
    {
        if (severity > 2) return;
        Console.Error.WriteLine($"NoGraphicsSharp D3D12 validation: [{severity}:{id}] {Marshal.PtrToStringUTF8((nint)description)}");
    }

    internal static GPUException CreationFailure(int result, string operation, Error error = Error.DriverError)
    {
        if (result == unchecked((int)0x8007000e)) error = Error.OutOfMemory;
        else if (result is unchecked((int)0x887a0005) or unchecked((int)0x887a0006) or unchecked((int)0x887a0007)) error = Error.DeviceLost;
        else if (result is unchecked((int)0x887a0004) or unchecked((int)0x80004002)) error = Error.Unsupported;
        string diagnostic = result < 0 ? $" (HRESULT 0x{result:x8}: {Marshal.GetPInvokeErrorMessage(result)})" : "";
        string message = $"D3D12 {operation}{diagnostic}";
        Gpu.ReportError(message, false);
        return new GPUException(error, message);
    }

    internal static void Check(int result, string operation)
    {
        if (result < 0) throw CreationFailure(result, operation);
    }

    internal static void CheckExecution(int result, string operation)
    {
        if (result >= 0) return;
        string message = $"D3D12 {operation} failed (HRESULT 0x{result:x8}).";
        Gpu.ReportError(message, true);
        Debug.Assert(false, message);
        Environment.FailFast(message);
    }

    internal static ulong CompletedValue(D3D12Device owner, nint fence)
    {
        ulong value = D3D12Native.GetCompletedValue(fence);
        if (value == ulong.MaxValue) CheckExecution(D3D12Native.GetDeviceRemovedReason(owner.Native), "device removed while querying a fence");
        return value;
    }

    internal static void Wait(D3D12Device owner, nint fence, ulong value)
    {
        if (CompletedValue(owner, fence) >= value) return;
        CheckExecution(D3D12Native.SetEventOnCompletion(fence, value, 0), "ID3D12Fence.SetEventOnCompletion");
        CompletedValue(owner, fence);
    }

    internal static int Format(ApiFormat format) => format switch
    {
        ApiFormat.Rgba8Srgb => 29,
        ApiFormat.Bgra8Srgb => 91,
        ApiFormat.R5g6b5Unorm => 85,
        ApiFormat.R8Unorm => 61,
        ApiFormat.Rg8Unorm => 49,
        ApiFormat.Rgba8Unorm => 28,
        ApiFormat.Bgra8Unorm => 87,
        ApiFormat.R16Unorm => 56,
        ApiFormat.Rg16Unorm => 35,
        ApiFormat.Rgba16Unorm => 11,
        ApiFormat.R8Uint => 62,
        ApiFormat.Rg8Uint => 50,
        ApiFormat.Rgba8Uint => 30,
        ApiFormat.R16Uint => 57,
        ApiFormat.Rg16Uint => 36,
        ApiFormat.Rgba16Uint => 12,
        ApiFormat.R32Uint => 42,
        ApiFormat.Rg32Uint => 17,
        ApiFormat.Rgba32Uint => 3,
        ApiFormat.R16Float => 54,
        ApiFormat.Rg16Float => 34,
        ApiFormat.Rgba16Float => 10,
        ApiFormat.R32Float => 41,
        ApiFormat.Rg32Float => 16,
        ApiFormat.Rgba32Float => 2,
        ApiFormat.Rgb10a2Unorm => 24,
        ApiFormat.Rg11b10Float => 26,
        ApiFormat.D16Unorm => 55,
        ApiFormat.D24UnormS8Uint or ApiFormat.S8Uint => 45,
        ApiFormat.D32Float => 40,
        ApiFormat.D32FloatS8Uint => 20,
        ApiFormat.Bc1Srgb => 72,
        ApiFormat.Bc1Unorm => 71,
        ApiFormat.Bc2Srgb => 75,
        ApiFormat.Bc2Unorm => 74,
        ApiFormat.Bc3Srgb => 78,
        ApiFormat.Bc3Unorm => 77,
        ApiFormat.Bc4Unorm => 80,
        ApiFormat.Bc4Snorm => 81,
        ApiFormat.Bc5Rg => 83,
        ApiFormat.Bc6HUfloat => 95,
        ApiFormat.Bc6HSfloat => 96,
        ApiFormat.Bc7Srgb => 99,
        ApiFormat.Bc7Unorm => 98,
        _ => 0,
    };

    internal static int CopyFormat(ApiFormat format) => format switch
    {
        ApiFormat.D16Unorm => 53,
        ApiFormat.D32Float => 39,
        ApiFormat.S8Uint => 60,
        _ => Format(format),
    };

    internal static int ShaderResourceFormat(ApiFormat format, TextureAspect aspect) => format switch
    {
        ApiFormat.D16Unorm => 56,
        ApiFormat.D32Float => 41,
        ApiFormat.D24UnormS8Uint => aspect == TextureAspect.Stencil ? 47 : 46,
        ApiFormat.D32FloatS8Uint => aspect == TextureAspect.Stencil ? 22 : 21,
        ApiFormat.S8Uint => 47,
        _ => Format(format),
    };

    internal static uint TexturePlane(ApiFormat format, TextureAspect aspect) =>
        format == ApiFormat.S8Uint || (aspect == TextureAspect.Stencil &&
            (format == ApiFormat.D24UnormS8Uint || format == ApiFormat.D32FloatS8Uint)) ? 1u : 0;

    internal static int ResourceFormat(TextureDesc desc)
    {
        if (desc.Format == ApiFormat.D16Unorm) return 53;
        if (desc.Format == ApiFormat.D32Float) return 39;
        if (desc.Format == ApiFormat.D24UnormS8Uint || desc.Format == ApiFormat.S8Uint) return 44;
        if (desc.Format == ApiFormat.D32FloatS8Uint) return 19;
        if (!desc.MutableFormat) return Format(desc.Format);
        return desc.Format switch
        {
            ApiFormat.R8Unorm or ApiFormat.R8Uint => 60,
            ApiFormat.Rg8Unorm or ApiFormat.Rg8Uint => 48,
            ApiFormat.Rgba8Srgb or ApiFormat.Rgba8Unorm or ApiFormat.Rgba8Uint => 27,
            ApiFormat.Bgra8Srgb or ApiFormat.Bgra8Unorm => 90,
            ApiFormat.R16Unorm or ApiFormat.R16Uint or ApiFormat.R16Float => 53,
            ApiFormat.Rg16Unorm or ApiFormat.Rg16Uint or ApiFormat.Rg16Float => 33,
            ApiFormat.Rgba16Unorm or ApiFormat.Rgba16Uint or ApiFormat.Rgba16Float => 9,
            ApiFormat.R32Uint or ApiFormat.R32Float => 39,
            ApiFormat.Rg32Uint or ApiFormat.Rg32Float => 15,
            ApiFormat.Rgba32Uint or ApiFormat.Rgba32Float => 1,
            ApiFormat.Rgb10a2Unorm => 23,
            ApiFormat.Bc1Srgb or ApiFormat.Bc1Unorm => 70,
            ApiFormat.Bc2Srgb or ApiFormat.Bc2Unorm => 73,
            ApiFormat.Bc3Srgb or ApiFormat.Bc3Unorm => 76,
            ApiFormat.Bc4Unorm or ApiFormat.Bc4Snorm => 79,
            ApiFormat.Bc5Rg => 82,
            ApiFormat.Bc6HUfloat or ApiFormat.Bc6HSfloat => 94,
            ApiFormat.Bc7Srgb or ApiFormat.Bc7Unorm => 97,
            _ => Format(desc.Format),
        };
    }

    internal static D3D12Native.ResourceDesc TextureResourceDesc(TextureDesc desc)
    {
        TextureFormatInfo info = Gpu.GetTextureFormatInfo(desc.Format);
        uint flags = 0;
        if (Backend.HasFlag(desc.Usage, TextureUsage.ColorAttachment)) flags |= D3D12Native.ResourceFlagAllowRenderTarget;
        if (info.Depth || info.Stencil) flags |= D3D12Native.ResourceFlagAllowDepthStencil;
        if (Backend.HasFlag(desc.Usage, TextureUsage.Storage))
            flags |= D3D12Native.ResourceFlagAllowUnorderedAccess | D3D12Native.ResourceFlagAllowSimultaneousAccess;
        if ((info.Depth || info.Stencil) && !Backend.HasFlag(desc.Usage, TextureUsage.Sampled)) flags |= D3D12Native.ResourceFlagDenyShaderResource;
        return new D3D12Native.ResourceDesc
        {
            Dimension = desc.Type == TextureType.OneD ? 2 : desc.Type == TextureType.ThreeD ? 4 : 3,
            Width = desc.Extent.X,
            Height = desc.Type == TextureType.OneD ? 1 : desc.Extent.Y,
            DepthOrArraySize = (ushort)(desc.Type == TextureType.ThreeD ? desc.Extent.Z : desc.LayerCount),
            MipLevels = (ushort)desc.MipLevels,
            Format = ResourceFormat(desc),
            SampleDesc = new D3D12Native.SampleDesc { Count = desc.SampleCount },
            Flags = flags,
        };
    }

    internal static D3D12Native.ResourceDesc1 TextureResourceDesc1(TextureDesc desc)
    {
        D3D12Native.ResourceDesc native = TextureResourceDesc(desc);
        return new D3D12Native.ResourceDesc1
        {
            Dimension = native.Dimension,
            Width = native.Width,
            Height = native.Height,
            DepthOrArraySize = native.DepthOrArraySize,
            MipLevels = native.MipLevels,
            Format = native.Format,
            SampleDesc = native.SampleDesc,
            Flags = native.Flags,
        };
    }
}
