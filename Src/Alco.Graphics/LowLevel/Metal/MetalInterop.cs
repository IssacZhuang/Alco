// Metal backend interop: Objective-C, Libdispatch, and mach P/Invokes plus enum conversions from the Metal .mm helpers.


using System.Runtime.InteropServices;
using Mtl = Metal;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

[StructLayout(LayoutKind.Sequential)]
internal struct MtlCGSize
{
    public double Width;
    public double Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MachTimebaseInfo
{
    public uint Numer;
    public uint Denom;
}

internal static unsafe partial class MetalInterop
{
    internal const ulong ArgRootIndex = 0;
    internal const ulong ArgTextureHeapIndex = 1;
    internal const ulong ArgSamplerHeapIndex = 2;
    internal const ulong ArgBufferViewTableIndex = 3;

    internal const Mtl.MTLRenderStages RenderStages = Mtl.MTLRenderStages.RenderStageVertex | Mtl.MTLRenderStages.RenderStageFragment |
                                                       Mtl.MTLRenderStages.RenderStageObject | Mtl.MTLRenderStages.RenderStageMesh;

    private static readonly Mtl.Selector selRetain = "retain";
    private static readonly Mtl.Selector selBytes = "bytes";
    private static readonly Mtl.Selector sel_drawableSize = "drawableSize";
    private static readonly Mtl.Selector sel_setDrawableSize = "setDrawableSize:";
    private static readonly Mtl.Selector sel_setResidencySet = "setResidencySet:";

    private const string Libobjc = "/usr/lib/libobjc.A.dylib";
    private const string Libdispatch = "/usr/lib/system/libdispatch.dylib";
    private const string LibsystemKernel = "/usr/lib/system/libsystem_kernel.dylib";

    [LibraryImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_retain(nint receiver, nint selector);

    [LibraryImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_object(nint receiver, nint selector, nint argument);

    [LibraryImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend_bytes(nint receiver, nint selector);

    [LibraryImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static partial MtlCGSize objc_msgSendCGSize(nint receiver, nint selector);

    [LibraryImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend_drawable_size(nint receiver, nint selector, MtlCGSize size);

    [LibraryImport(Libdispatch, EntryPoint = "dispatch_queue_create")]
    internal static partial nint DispatchQueueCreate(byte* label, nint attributes);

    [LibraryImport(Libdispatch, EntryPoint = "dispatch_queue_attr_make_with_autorelease_frequency")]
    internal static partial nint DispatchQueueAttrMakeWithAutoreleaseFrequency(nint attributes, int autoreleaseFrequency);

    [LibraryImport(Libdispatch, EntryPoint = "dispatch_release")]
    internal static partial void DispatchRelease(nint queue);

    [LibraryImport(Libdispatch, EntryPoint = "dispatch_data_create")]
    internal static partial nint DispatchDataCreate(void* buffer, nuint size, nint queue, nint destructor);

    [LibraryImport(LibsystemKernel, EntryPoint = "mach_timebase_info")]
    private static partial int MachTimebaseInfo(ref MachTimebaseInfo info);

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "sysctlbyname")]
    private static partial int SysctlByName(byte* name, byte* value, ref nuint length, void* newValue, nuint newLength);

    internal static nint retain(nint objectPointer) => objc_msgSend_retain(objectPointer, selRetain.SelPtr);

    internal static void* NsdataBytes(nint data) => (void*)objc_msgSend_bytes(data, selBytes.SelPtr);

    internal static MtlCGSize LayerDrawableSize(nint layer) => objc_msgSendCGSize(layer, sel_drawableSize.SelPtr);

    internal static void SetLayerDrawableSize(nint layer, MtlCGSize size) => objc_msgSend_drawable_size(layer, sel_setDrawableSize.SelPtr, size);

    internal static void SetLayerResidencySet(nint layer, nint residencySet) => objc_msgSend_object(layer, sel_setResidencySet.SelPtr, residencySet);

    internal static string? SystemBuild()
    {
        byte* version = stackalloc byte[128];
        nuint length = 128;
        fixed (byte* name = "kern.osversion"u8)
            if (SysctlByName(name, version, ref length, null, 0) != 0) return null;
        return System.Text.Encoding.UTF8.GetString(new ReadOnlySpan<byte>(version, (int)length - 1));
    }

    // DISPATCH_QUEUE_SERIAL with DISPATCH_AUTORELEASE_FREQUENCY_WORK_ITEM: every feedback callback runs inside its own autorelease pool.
    internal static nint CreateFeedbackQueue(byte* label)
    {
        nint attributes = DispatchQueueAttrMakeWithAutoreleaseFrequency(0, 2);
        return DispatchQueueCreate(label, attributes);
    }

    internal static float MachTimestampPeriodNs()
    {
        MachTimebaseInfo timebase = new MachTimebaseInfo();
        MachTimebaseInfo(ref timebase);
        return timebase.Numer / (float)timebase.Denom;
    }

    internal static string ReportError(Mtl.NSError error, string operation)
    {
        string description = error.NativePtr != 0 ? $"{error.LocalizedDescription} (native error {error.Code})" : "creation failed";
        string message = $"Metal {operation}: {description}";
        Console.Error.WriteLine($"Alco.Graphics Metal: {operation}: {description}");
        Gpu.ReportError(message, false);
        return message;
    }

    internal static Mtl.MTLStages BarrierStages(Stage stages, bool source)
    {
        ulong mask = (ulong)stages;
        if ((mask & (ulong)Stage.AllCommands) != 0) return Mtl.MTLStages.StageAll;
        const Mtl.MTLStages raster = Mtl.MTLStages.StageFragment | Mtl.MTLStages.StageTile;
        const Mtl.MTLStages geometry = Mtl.MTLStages.StageVertex | Mtl.MTLStages.StageObject | Mtl.MTLStages.StageMesh;
        Mtl.MTLStages result = 0;
        // Preserve the API's logical execution scopes without joining its alternative graphics/compute branches.
        if ((mask & (ulong)Stage.Indirect) != 0) result |= geometry | Mtl.MTLStages.StageDispatch | (source ? 0 : raster);
        if ((mask & (ulong)(Stage.IndexInput | Stage.Vertex)) != 0) result |= Mtl.MTLStages.StageVertex | (source ? 0 : raster);
        if ((mask & (ulong)Stage.Task) != 0) result |= Mtl.MTLStages.StageObject | (source ? 0 : Mtl.MTLStages.StageMesh | raster);
        if ((mask & (ulong)Stage.Mesh) != 0) result |= Mtl.MTLStages.StageMesh | (source ? Mtl.MTLStages.StageObject : raster);
        if ((mask & (ulong)(Stage.DepthStencilTests | Stage.Pixel | Stage.ColorOutput)) != 0) result |= raster | (source ? geometry : 0);
        if ((mask & (ulong)Stage.Compute) != 0) result |= Mtl.MTLStages.StageDispatch;
        if ((mask & (ulong)Stage.Transfer) != 0) result |= Mtl.MTLStages.StageBlit;
        return result;
    }

    internal static Mtl.MTLPixelFormat PixelFormat(Format format) => format switch
    {
        Format.R8Srgb => Mtl.MTLPixelFormat.R8UnormsRGB,
        Format.Rg8Srgb => Mtl.MTLPixelFormat.RG8UnormsRGB,
        Format.Rgba8Srgb => Mtl.MTLPixelFormat.RGBA8UnormsRGB,
        Format.Bgra8Srgb => Mtl.MTLPixelFormat.BGRA8UnormsRGB,
        Format.R8Unorm => Mtl.MTLPixelFormat.R8Unorm,
        Format.Rg8Unorm => Mtl.MTLPixelFormat.RG8Unorm,
        Format.Rgba8Unorm => Mtl.MTLPixelFormat.RGBA8Unorm,
        Format.Bgra8Unorm => Mtl.MTLPixelFormat.BGRA8Unorm,
        Format.R16Unorm => Mtl.MTLPixelFormat.R16Unorm,
        Format.Rg16Unorm => Mtl.MTLPixelFormat.RG16Unorm,
        Format.Rgba16Unorm => Mtl.MTLPixelFormat.RGBA16Unorm,
        Format.R8Uint => Mtl.MTLPixelFormat.R8Uint,
        Format.Rg8Uint => Mtl.MTLPixelFormat.RG8Uint,
        Format.Rgba8Uint => Mtl.MTLPixelFormat.RGBA8Uint,
        Format.R16Uint => Mtl.MTLPixelFormat.R16Uint,
        Format.Rg16Uint => Mtl.MTLPixelFormat.RG16Uint,
        Format.Rgba16Uint => Mtl.MTLPixelFormat.RGBA16Uint,
        Format.R32Uint => Mtl.MTLPixelFormat.R32Uint,
        Format.Rg32Uint => Mtl.MTLPixelFormat.RG32Uint,
        Format.Rgba32Uint => Mtl.MTLPixelFormat.RGBA32Uint,
        Format.R16Float => Mtl.MTLPixelFormat.R16Float,
        Format.Rg16Float => Mtl.MTLPixelFormat.RG16Float,
        Format.Rgba16Float => Mtl.MTLPixelFormat.RGBA16Float,
        Format.R32Float => Mtl.MTLPixelFormat.R32Float,
        Format.Rg32Float => Mtl.MTLPixelFormat.RG32Float,
        Format.Rgba32Float => Mtl.MTLPixelFormat.RGBA32Float,
        Format.Rgb10a2Unorm => Mtl.MTLPixelFormat.RGB10A2Unorm,
        Format.Rg11b10Float => Mtl.MTLPixelFormat.RG11B10Float,
        Format.D16Unorm => Mtl.MTLPixelFormat.Depth16Unorm,
        Format.D32Float => Mtl.MTLPixelFormat.Depth32Float,
        Format.S8Uint => Mtl.MTLPixelFormat.Stencil8,
        Format.D32FloatS8Uint => Mtl.MTLPixelFormat.Depth32FloatStencil8,
        Format.EacRg => Mtl.MTLPixelFormat.EACRG11Unorm,
        Format.Astc4x4Srgb => Mtl.MTLPixelFormat.ASTC4x4sRGB,
        Format.Astc4x4Unorm => Mtl.MTLPixelFormat.ASTC4x4LDR,
        Format.Bc3Srgb => Mtl.MTLPixelFormat.BC3RGBAsRGB,
        Format.Bc3Unorm => Mtl.MTLPixelFormat.BC3RGBA,
        Format.Bc5Rg => Mtl.MTLPixelFormat.BC5RGUnorm,
        Format.Bc6HUfloat => Mtl.MTLPixelFormat.BC6HRGBUfloat,
        Format.Bc6HSfloat => Mtl.MTLPixelFormat.BC6HRGBFloat,
        Format.Bc7Srgb => Mtl.MTLPixelFormat.BC7RGBAUnormsRGB,
        Format.Bc7Unorm => Mtl.MTLPixelFormat.BC7RGBAUnorm,
        Format.Bc1Srgb => Mtl.MTLPixelFormat.BC1RGBAsRGB,
        Format.Bc1Unorm => Mtl.MTLPixelFormat.BC1RGBA,
        Format.Bc2Srgb => Mtl.MTLPixelFormat.BC2RGBAsRGB,
        Format.Bc2Unorm => Mtl.MTLPixelFormat.BC2RGBA,
        Format.Bc4Unorm => Mtl.MTLPixelFormat.BC4RUnorm,
        Format.Bc4Snorm => Mtl.MTLPixelFormat.BC4RSnorm,
        Format.Rgba4Unorm => Mtl.MTLPixelFormat.ABGR4Unorm,
        Format.R5g5b5a1Unorm => Mtl.MTLPixelFormat.A1BGR5Unorm,
        Format.R5g6b5Unorm => Mtl.MTLPixelFormat.B5G6R5Unorm,
        _ => Mtl.MTLPixelFormat.Invalid,
    };

    internal static Mtl.MTLTextureType MtlTextureType(TextureType type) => type switch
    {
        TextureType.OneD => Mtl.MTLTextureType.Type1D,
        TextureType.ThreeD => Mtl.MTLTextureType.Type3D,
        TextureType.Cube => Mtl.MTLTextureType.Cube,
        TextureType.TwoDArray => Mtl.MTLTextureType.Type2DArray,
        TextureType.CubeArray => Mtl.MTLTextureType.CubeArray,
        _ => Mtl.MTLTextureType.Type2D,
    };

    internal static Mtl.MTLPrimitiveType PrimitiveType(PrimitiveTopology topology) => topology switch
    {
        PrimitiveTopology.TriangleList => Mtl.MTLPrimitiveType.Triangle,
        PrimitiveTopology.PointList => Mtl.MTLPrimitiveType.Point,
        PrimitiveTopology.LineList => Mtl.MTLPrimitiveType.Line,
        PrimitiveTopology.LineStrip => Mtl.MTLPrimitiveType.LineStrip,
        PrimitiveTopology.TriangleStrip => Mtl.MTLPrimitiveType.TriangleStrip,
        _ => (Mtl.MTLPrimitiveType)ulong.MaxValue,
    };

    internal static Mtl.MTLPrimitiveTopologyClass PrimitiveTopologyClass(PrimitiveTopology topology) => topology switch
    {
        PrimitiveTopology.PointList => Mtl.MTLPrimitiveTopologyClass.Point,
        PrimitiveTopology.LineList or PrimitiveTopology.LineStrip => Mtl.MTLPrimitiveTopologyClass.Line,
        PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip => Mtl.MTLPrimitiveTopologyClass.Triangle,
        _ => (Mtl.MTLPrimitiveTopologyClass)ulong.MaxValue,
    };

    private static readonly Mtl.MTLBlendFactor[] blendFactors =
    [
        Mtl.MTLBlendFactor.Zero,
        Mtl.MTLBlendFactor.One,
        Mtl.MTLBlendFactor.SourceColor,
        Mtl.MTLBlendFactor.OneMinusSourceColor,
        Mtl.MTLBlendFactor.DestinationColor,
        Mtl.MTLBlendFactor.OneMinusDestinationColor,
        Mtl.MTLBlendFactor.SourceAlpha,
        Mtl.MTLBlendFactor.OneMinusSourceAlpha,
        Mtl.MTLBlendFactor.DestinationAlpha,
        Mtl.MTLBlendFactor.OneMinusDestinationAlpha,
        Mtl.MTLBlendFactor.SourceAlphaSaturated,
    ];

    internal static Mtl.MTLBlendFactor BlendFactor(BlendFactor factor) => blendFactors[(int)factor];

    private static readonly Mtl.MTLLoadAction[] loadActions = [Mtl.MTLLoadAction.Load, Mtl.MTLLoadAction.Clear, Mtl.MTLLoadAction.DontCare];

    internal static Mtl.MTLLoadAction LoadAction(LoadOp load) => loadActions[(int)load];

    private static readonly Mtl.MTLSamplerAddressMode[] addressModes =
    [
        Mtl.MTLSamplerAddressMode.Repeat,
        Mtl.MTLSamplerAddressMode.MirrorRepeat,
        Mtl.MTLSamplerAddressMode.ClampToEdge,
    ];

    internal static Mtl.MTLSamplerAddressMode AddressMode(AddressMode mode) => addressModes[(int)mode];

    internal static Mtl.MTLSize MetalSize(Extent3D size) => new() { width = size.X, height = size.Y, depth = size.Z };
}
