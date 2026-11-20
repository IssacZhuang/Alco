// CPU-only regressions for buffer-identity ranges, allocation projections, and the pointerless public command API.

using System.Reflection;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;

internal static unsafe class GPURangeTests
{
    internal static void Run(Action<bool, string> check)
    {
        Console.WriteLine("Buffer identity ranges:");
        RunPublicApi(check);
        RunRanges(check);
    }

    private static void RunPublicApi(Action<bool, string> check)
    {
        ReadOnlySpan<Type> types = [typeof(GPURange), typeof(GPUAllocation<byte>), typeof(GPUAllocation<ulong>), typeof(GPUHeapOwner)];
        foreach (Type type in types)
        {
            bool pointerless = true;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
                pointerless &= IsMetadataMember(field.Name, field.FieldType);
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
                pointerless &= IsMetadataMember(property.Name, property.PropertyType);
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                pointerless &= !method.ReturnType.IsPointer && !method.ReturnType.IsFunctionPointer &&
                    method.ReturnType != typeof(ulong) && method.ReturnType != typeof(nuint) && method.ReturnType != typeof(nint);
                foreach (ParameterInfo parameter in method.GetParameters())
                    pointerless &= !parameter.ParameterType.IsPointer && !parameter.ParameterType.IsFunctionPointer && !parameter.ParameterType.IsByRef;
            }
            check(pointerless && type.GetMember("Gpu").Length == 0 && type.GetMember("Address").Length == 0,
                $"{type.Name} exposes buffer identity and byte metadata, not a public GPU pointer or integer address");
        }
        check(typeof(GPURange).GetField(nameof(GPURange.Buffer))?.FieldType == typeof(GPUHeapOwner) &&
            typeof(GPUAllocation<byte>).GetField(nameof(GPUAllocation<byte>.Buffer))?.FieldType == typeof(GPUHeapOwner) &&
            RuntimeHelpers.IsReferenceOrContainsReferences<GPURange>() && RuntimeHelpers.IsReferenceOrContainsReferences<GPUAllocation<byte>>(),
            "ranges and allocations carry a backing-buffer reference rather than an unmanaged GPU address");
        check(typeof(GPUAllocation<byte>).GetField("View") == null && typeof(GPUAllocation<byte>).GetProperty("View") is { CanRead: true, CanWrite: false },
            "allocation shader views are computed read-only properties, not independently writable metadata");
        int commandCount = 0;
        foreach (MethodInfo method in typeof(GPUCommandBuffer).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            if (!method.Name.StartsWith("Draw", StringComparison.Ordinal) && !method.Name.StartsWith("Dispatch", StringComparison.Ordinal)) continue;
            ++commandCount;
            ParameterInfo[] parameters = method.GetParameters();
            bool pointerless = method.ReturnType == typeof(void) && parameters.Length != 0 && parameters[0].Name == "root" &&
                parameters[0].ParameterType == typeof(GPURange);
            foreach (ParameterInfo parameter in parameters)
                pointerless &= !parameter.ParameterType.IsPointer && !parameter.ParameterType.IsFunctionPointer && !parameter.ParameterType.IsByRef &&
                    parameter.ParameterType != typeof(ulong) && parameter.ParameterType != typeof(nuint) && parameter.ParameterType != typeof(nint);
            check(pointerless, $"{method.Name} accepts its root by-value as GPURange with no public GPU pointer or UInt64 address parameter");
        }
        check(commandCount == 8, "all direct and indirect draw, indexed, mesh, and dispatch entry points retain pointerless root coverage");
        check(typeof(GPURange).Assembly.GetType("Alco.Graphics.LowLevel.BufferAddressMap") == null &&
            typeof(GPUDevice).GetField("BufferMap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null &&
            typeof(GPUHeapOwner).GetField("RecordSlot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null,
            "the obsolete address-map type, device map, and owner map-slot bookkeeping are removed");
    }

    private static bool IsMetadataMember(string name, Type type)
    {
        if (name == "Cpu") return type.IsPointer && type.GetElementType() != typeof(void);
        if (type.IsPointer || type.IsFunctionPointer || type == typeof(nuint) || type == typeof(nint)) return false;
        return type != typeof(ulong) || name is "Offset" or "Size" or "SizeBytes";
    }

    private static void RunRanges(Action<bool, string> check)
    {
        // These owners carry CPU-side identity metadata only; no native resource is created or destroyed.
        Type ownerType = typeof(GPUHeapOwner).Assembly.GetType("Alco.Graphics.LowLevel.VulkanGpuHeapOwner", throwOnError: true)!;
        GPUHeapOwner first = (GPUHeapOwner)Activator.CreateInstance(ownerType, nonPublic: true)!;
        GPUHeapOwner second = (GPUHeapOwner)Activator.CreateInstance(ownerType, nonPublic: true)!;
        FieldInfo viewIndex = typeof(GPUHeapOwner).GetField("ViewIndex", BindingFlags.Instance | BindingFlags.NonPublic)!;
        viewIndex.SetValue(first, 37u);
        viewIndex.SetValue(second, 91u);
        GPURange range = new() { Buffer = first, Offset = 512, Size = 1024 };
        GPURange tail = range.Slice(128);
        GPURange slice = range.Slice(64, 256).Slice(32, 48);
        GPURange end = range.Slice(range.Size);
        check(ReferenceEquals(tail.Buffer, first) && tail.Offset == 640 && tail.Size == 896 && ReferenceEquals(slice.Buffer, first) &&
            slice.Offset == 608 && slice.Size == 48 && ReferenceEquals(end.Buffer, first) && end.Offset == 1536 && end.Size == 0,
            "both Slice overloads preserve buffer identity, accumulate byte offsets, and retain exact subrange sizes including an empty tail");
        check(ReferenceEquals(range.Buffer, first) && range.Offset == 512 && range.Size == 1024 &&
            default(GPURange).Slice(0).Equals(default(GPURange)) && default(GPURange).Slice(0, 0).Equals(default(GPURange)),
            "slicing leaves its source unchanged and preserves a default empty range");
        GPUAllocation<ulong> allocation = new() { Buffer = first, Offset = 608, SizeBytes = 48 };
        GPURange projected = allocation.GPURange;
        check(allocation.Cpu == null && allocation.CpuSpan.IsEmpty && ReferenceEquals(projected.Buffer, first) && projected.Offset == 608 &&
            projected.Size == 48 && allocation.View.Index == 37 && allocation.View.Offset == 608,
            "typed GPU-only allocation projections preserve byte metadata and empty CPU mappings without element-size scaling");
        allocation.Buffer = second;
        allocation.Offset = 704;
        check(allocation.View.Index == 91 && allocation.View.Offset == 704 && ReferenceEquals(allocation.GPURange.Buffer, second) &&
            allocation.GPURange.Offset == 704 && allocation.GPURange.Size == 48,
            "range and shader-view projections follow the current owner identity and offset rather than stale writable view state");
        viewIndex.SetValue(second, 0u);
        check(allocation.View.Equals(default(GPUBufferView)) && ReferenceEquals(allocation.GPURange.Buffer, second) && allocation.GPURange.Offset == 704,
            "an owner without a shader-view index yields a fully empty view while retaining its native command range");
        ulong* cached = stackalloc ulong[4];
        GPUAllocation<ulong> cpuOnly = new() { Cpu = cached, Offset = 96, SizeBytes = 4 * sizeof(ulong) };
        check(cpuOnly.CpuSpan.Length == 4 && cpuOnly.Offset == 96 && cpuOnly.SizeBytes == 32 && cpuOnly.View.Equals(default(GPUBufferView)) &&
            cpuOnly.GPURange.Equals(default(GPURange)),
            "CPU-only allocations retain mappings and nonzero byte metadata without exposing GPU ranges or shader views");
        allocation.Buffer = first;
        bool valid = true;
        for (int iteration = 0; iteration < 128; ++iteration)
        {
            GPURange warmup = allocation.GPURange.Slice(8, 16);
            valid &= ReferenceEquals(warmup.Buffer, first) && warmup.Offset == 712;
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 100000; ++iteration)
        {
            GPURange current = allocation.GPURange.Slice(8, 16);
            GPUBufferView view = allocation.View;
            valid &= ReferenceEquals(current.Buffer, first) && current.Offset == 712 && current.Size == 16 && view.Index == 37 && view.Offset == 704;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        check(valid && allocated == 0, $"range slicing and computed allocation projections allocate no managed memory ({allocated} bytes)");
    }
}
