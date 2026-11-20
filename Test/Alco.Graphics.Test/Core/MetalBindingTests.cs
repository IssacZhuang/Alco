// CPU-only layout, underlying-type, visibility, and handle checks for handwritten Metal bindings.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics.LowLevel;
using Mtl = Metal;

namespace Alco.Graphics.Test;

internal static unsafe class MetalBindingTests
{
    internal static void Run(Action<bool, string> check)
    {
        Console.WriteLine("Metal bindings (CPU layout & visibility):");
        RunVisibilityChecks(check);
        RunValueStructAbiChecks(check);
        RunWrapperHandleChecks(check);
        RunEnumChecks(check);
    }

    private static void RunVisibilityChecks(Action<bool, string> check)
    {
        Assembly gfxAsm = typeof(GPUDevice).Assembly;
        Type[] metalTypes = gfxAsm.GetTypes()
            .Where(static t => t.Namespace == "Metal" && !t.IsNested)
            .ToArray();

        check(metalTypes.Length == 117, $"Metal binding top-level type count == 117 (got {metalTypes.Length})");
        check(metalTypes.All(static t => t.IsNotPublic), "All top-level Metal binding types are internal");

        bool anyInternalMember = false;
        foreach (Type t in metalTypes)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (MethodInfo m in t.GetMethods(Flags))
            {
                if (m.IsAssembly && !m.IsDefined(typeof(CompilerGeneratedAttribute), false)) anyInternalMember = true;
            }
            foreach (ConstructorInfo c in t.GetConstructors(Flags))
            {
                if (c.IsAssembly && !c.IsDefined(typeof(CompilerGeneratedAttribute), false)) anyInternalMember = true;
            }
            foreach (FieldInfo f in t.GetFields(Flags))
            {
                if (f.IsAssembly && !t.IsEnum && !f.IsDefined(typeof(CompilerGeneratedAttribute), false)) anyInternalMember = true;
            }
        }
        check(!anyInternalMember, "Metal binding types declare public/private members (no source internal member modifiers)");
    }

    private static void RunValueStructAbiChecks(Action<bool, string> check)
    {
        check(sizeof(Mtl.NSRange) == 16 &&
              Marshal.OffsetOf<Mtl.NSRange>(nameof(Mtl.NSRange.location)) == 0 &&
              Marshal.OffsetOf<Mtl.NSRange>(nameof(Mtl.NSRange.length)) == 8,
            "NSRange ABI size/offsets match Apple NSRange (16B)");

        check(sizeof(Mtl.MTLOrigin) == 24 &&
              Marshal.OffsetOf<Mtl.MTLOrigin>(nameof(Mtl.MTLOrigin.x)) == 0 &&
              Marshal.OffsetOf<Mtl.MTLOrigin>(nameof(Mtl.MTLOrigin.y)) == 8 &&
              Marshal.OffsetOf<Mtl.MTLOrigin>(nameof(Mtl.MTLOrigin.z)) == 16,
            "MTLOrigin ABI size/offsets match Metal (24B)");

        check(sizeof(Mtl.MTLSize) == 24 &&
              Marshal.OffsetOf<Mtl.MTLSize>(nameof(Mtl.MTLSize.width)) == 0 &&
              Marshal.OffsetOf<Mtl.MTLSize>(nameof(Mtl.MTLSize.height)) == 8 &&
              Marshal.OffsetOf<Mtl.MTLSize>(nameof(Mtl.MTLSize.depth)) == 16,
            "MTLSize ABI size/offsets match Metal (24B)");

        check(sizeof(Mtl.MTLSizeAndAlign) == 16 &&
              Marshal.OffsetOf<Mtl.MTLSizeAndAlign>(nameof(Mtl.MTLSizeAndAlign.size)) == 0 &&
              Marshal.OffsetOf<Mtl.MTLSizeAndAlign>(nameof(Mtl.MTLSizeAndAlign.align)) == 8,
            "MTLSizeAndAlign ABI size/offsets match Metal (16B)");

        check(sizeof(Mtl.MTLResourceID) == 8 &&
              Marshal.OffsetOf<Mtl.MTLResourceID>(nameof(Mtl.MTLResourceID._impl)) == 0,
            "MTLResourceID ABI size/offsets match Metal (8B)");

        check(sizeof(Mtl.MTLViewport) == 48 &&
              Marshal.OffsetOf<Mtl.MTLViewport>(nameof(Mtl.MTLViewport.originX)) == 0 &&
              Marshal.OffsetOf<Mtl.MTLViewport>(nameof(Mtl.MTLViewport.originY)) == 8 &&
              Marshal.OffsetOf<Mtl.MTLViewport>(nameof(Mtl.MTLViewport.width)) == 16 &&
              Marshal.OffsetOf<Mtl.MTLViewport>(nameof(Mtl.MTLViewport.height)) == 24 &&
              Marshal.OffsetOf<Mtl.MTLViewport>(nameof(Mtl.MTLViewport.znear)) == 32 &&
              Marshal.OffsetOf<Mtl.MTLViewport>(nameof(Mtl.MTLViewport.zfar)) == 40,
            "MTLViewport ABI size/offsets match Metal (48B)");

        check(sizeof(Mtl.MTLScissorRect) == 32 &&
              Marshal.OffsetOf<Mtl.MTLScissorRect>(nameof(Mtl.MTLScissorRect.x)) == 0 &&
              Marshal.OffsetOf<Mtl.MTLScissorRect>(nameof(Mtl.MTLScissorRect.y)) == 8 &&
              Marshal.OffsetOf<Mtl.MTLScissorRect>(nameof(Mtl.MTLScissorRect.width)) == 16 &&
              Marshal.OffsetOf<Mtl.MTLScissorRect>(nameof(Mtl.MTLScissorRect.height)) == 24,
            "MTLScissorRect ABI size/offsets match Metal (32B)");

        check(sizeof(Mtl.MTLClearColor) == 32 &&
              Marshal.OffsetOf<Mtl.MTLClearColor>(nameof(Mtl.MTLClearColor.red)) == 0 &&
              Marshal.OffsetOf<Mtl.MTLClearColor>(nameof(Mtl.MTLClearColor.green)) == 8 &&
              Marshal.OffsetOf<Mtl.MTLClearColor>(nameof(Mtl.MTLClearColor.blue)) == 16 &&
              Marshal.OffsetOf<Mtl.MTLClearColor>(nameof(Mtl.MTLClearColor.alpha)) == 24,
            "MTLClearColor ABI size/offsets match Metal (32B)");

        check(sizeof(Mtl.MTLDrawPrimitivesIndirectArguments) == 16 &&
              Marshal.OffsetOf<Mtl.MTLDrawPrimitivesIndirectArguments>(nameof(Mtl.MTLDrawPrimitivesIndirectArguments.vertexCount)) == 0 &&
              Marshal.OffsetOf<Mtl.MTLDrawPrimitivesIndirectArguments>(nameof(Mtl.MTLDrawPrimitivesIndirectArguments.instanceCount)) == 4 &&
              Marshal.OffsetOf<Mtl.MTLDrawPrimitivesIndirectArguments>(nameof(Mtl.MTLDrawPrimitivesIndirectArguments.vertexStart)) == 8 &&
              Marshal.OffsetOf<Mtl.MTLDrawPrimitivesIndirectArguments>(nameof(Mtl.MTLDrawPrimitivesIndirectArguments.baseInstance)) == 12,
            "MTLDrawPrimitivesIndirectArguments ABI size/offsets match Metal (16B)");

        check(sizeof(Mtl.MTLDrawIndexedPrimitivesIndirectArguments) == 20 &&
              Marshal.OffsetOf<Mtl.MTLDrawIndexedPrimitivesIndirectArguments>(nameof(Mtl.MTLDrawIndexedPrimitivesIndirectArguments.indexCount)) == 0 &&
              Marshal.OffsetOf<Mtl.MTLDrawIndexedPrimitivesIndirectArguments>(nameof(Mtl.MTLDrawIndexedPrimitivesIndirectArguments.instanceCount)) == 4 &&
              Marshal.OffsetOf<Mtl.MTLDrawIndexedPrimitivesIndirectArguments>(nameof(Mtl.MTLDrawIndexedPrimitivesIndirectArguments.indexStart)) == 8 &&
              Marshal.OffsetOf<Mtl.MTLDrawIndexedPrimitivesIndirectArguments>(nameof(Mtl.MTLDrawIndexedPrimitivesIndirectArguments.baseVertex)) == 12 &&
              Marshal.OffsetOf<Mtl.MTLDrawIndexedPrimitivesIndirectArguments>(nameof(Mtl.MTLDrawIndexedPrimitivesIndirectArguments.baseInstance)) == 16,
            "MTLDrawIndexedPrimitivesIndirectArguments ABI size/offsets match Metal (20B)");

        check(sizeof(Mtl.MTL4TimestampHeapEntry) == 8 &&
              Marshal.OffsetOf<Mtl.MTL4TimestampHeapEntry>(nameof(Mtl.MTL4TimestampHeapEntry.timestamp)) == 0,
            "MTL4TimestampHeapEntry ABI size/offsets match Metal (8B)");
    }

    private static void RunWrapperHandleChecks(Action<bool, string> check)
    {
        bool allPtrSized =
            Unsafe.SizeOf<Mtl.ObjectiveCClass>() == nint.Size &&
            Unsafe.SizeOf<Mtl.Selector>() == nint.Size &&
            Unsafe.SizeOf<Mtl.NSObject>() == nint.Size &&
            Unsafe.SizeOf<Mtl.NSAutoreleasePool>() == nint.Size &&
            Unsafe.SizeOf<Mtl.NSString>() == nint.Size &&
            Unsafe.SizeOf<Mtl.NSURL>() == nint.Size &&
            Unsafe.SizeOf<Mtl.NSData>() == nint.Size &&
            Unsafe.SizeOf<Mtl.NSArray>() == nint.Size &&
            Unsafe.SizeOf<Mtl.NSError>() == nint.Size &&
            Unsafe.SizeOf<Mtl.CAMetalLayer>() == nint.Size &&
            Unsafe.SizeOf<Mtl.CAMetalDrawable>() == nint.Size &&
            Unsafe.SizeOf<Mtl.MTLDevice>() == nint.Size &&
            Unsafe.SizeOf<Mtl.MTLBuffer>() == nint.Size &&
            Unsafe.SizeOf<Mtl.MTLTexture>() == nint.Size &&
            Unsafe.SizeOf<Mtl.MTLHeap>() == nint.Size &&
            Unsafe.SizeOf<Mtl.MTL4CommandQueue>() == nint.Size &&
            Unsafe.SizeOf<Mtl.MTL4CommandBuffer>() == nint.Size &&
            Unsafe.SizeOf<Mtl.MTL4RenderCommandEncoder>() == nint.Size &&
            Unsafe.SizeOf<Mtl.MTL4ComputeCommandEncoder>() == nint.Size &&
            Unsafe.SizeOf<Mtl.MTL4Compiler>() == nint.Size &&
            Unsafe.SizeOf<Mtl.MTL4Archive>() == nint.Size &&
            Unsafe.SizeOf<Mtl.MTL4RenderPassDescriptor>() == nint.Size;
        check(allPtrSized, "ObjC/Metal wrapper structs are pointer-sized");

        var dev = new Mtl.MTLDevice((nint)0x1234);
        nint devPtr = dev;
        var tex = new Mtl.MTLTexture((nint)0x5678);
        Mtl.MTLResource res = tex;
        Mtl.MTLAllocation alloc = res;
        check(dev.NativePtr == (nint)0x1234 && devPtr == (nint)0x1234 && alloc.NativePtr == (nint)0x5678,
            "ObjC wrapper implicit conversions preserve NativePtr cleanly without native calls");
    }

    private static void RunEnumChecks(Action<bool, string> check)
    {
        check(Enum.GetUnderlyingType(typeof(Mtl.MTLPixelFormat)) == typeof(ulong) &&
              Enum.GetUnderlyingType(typeof(Mtl.MTLGPUFamily)) == typeof(long) &&
              Enum.GetUnderlyingType(typeof(Mtl.MTLHeapType)) == typeof(long) &&
              Enum.GetUnderlyingType(typeof(Mtl.MTL4CounterHeapType)) == typeof(long) &&
              Enum.GetUnderlyingType(typeof(Mtl.MTL4BlendState)) == typeof(long) &&
              Enum.GetUnderlyingType(typeof(Mtl.MTL4TimestampGranularity)) == typeof(long) &&
              Enum.GetUnderlyingType(typeof(Mtl.MTLStages)) == typeof(ulong) &&
              Enum.GetUnderlyingType(typeof(Mtl.MTLRenderStages)) == typeof(ulong) &&
              Enum.GetUnderlyingType(typeof(Mtl.MTLResourceOptions)) == typeof(ulong) &&
              Enum.GetUnderlyingType(typeof(Mtl.MTLColorWriteMask)) == typeof(ulong),
            "Metal enums use expected 64-bit NSInteger/NSUInteger underlying types");

        check((ulong)Mtl.MTLPixelFormat.RGBA8Unorm == 70UL &&
              (ulong)Mtl.MTLPixelFormat.BGRA8Unorm == 80UL &&
              (ulong)Mtl.MTLPixelFormat.Depth32Float == 252UL &&
              (long)Mtl.MTLGPUFamily.Apple7 == 1007L &&
              (long)Mtl.MTLGPUFamily.Apple9 == 1009L &&
              (long)Mtl.MTLGPUFamily.Metal4 == 5002L &&
              (ulong)Mtl.MTLLanguageVersion.Version40 == 262144UL &&
              (ulong)Mtl.MTLStages.StageAll == 9223372036854775807UL &&
              (ulong)Mtl.MTLColorWriteMask.All == 15UL &&
              (ulong)(Mtl.MTLResourceOptions.ResourceStorageModePrivate | Mtl.MTLResourceOptions.ResourceHazardTrackingModeUntracked) == ((1UL << 5) | (1UL << 8)),
            "Metal enum bitmasks and constants match Apple SDK values");
    }
}
