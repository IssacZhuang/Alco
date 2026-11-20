// Native D3D12 command-list, enhanced-barrier, render-pass, query, and indirect-command bindings.

using System.Numerics;
using System.Runtime.InteropServices;

namespace D3D12;

internal static unsafe partial class D3D12Native
{
    internal const uint CommandSyncAll = 1;
    internal const uint CommandSyncIndex = 4;
    internal const uint CommandSyncVertex = 8;
    internal const uint CommandSyncPixel = 16;
    internal const uint CommandSyncDepth = 32;
    internal const uint CommandSyncRender = 64;
    internal const uint CommandSyncCompute = 128;
    internal const uint CommandSyncCopy = 512;
    internal const uint CommandSyncResolve = 1024;
    internal const uint CommandSyncIndirect = 2048;
    internal const uint CommandResolveDestination = 0x1000;
    internal const uint CommandLayoutResolveDestination = 10;
    internal const uint CommandNoAccess = 0x80000000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandGlobalBarrier
    {
        internal uint SyncBefore;
        internal uint SyncAfter;
        internal uint AccessBefore;
        internal uint AccessAfter;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandSubresources
    {
        internal uint FirstMip;
        internal uint MipCount;
        internal uint FirstSlice;
        internal uint SliceCount;
        internal uint FirstPlane;
        internal uint PlaneCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandTextureBarrier
    {
        internal uint SyncBefore;
        internal uint SyncAfter;
        internal uint AccessBefore;
        internal uint AccessAfter;
        internal uint LayoutBefore;
        internal uint LayoutAfter;
        internal nint Resource;
        internal CommandSubresources Subresources;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandBarrierGroup
    {
        internal uint Type;
        internal uint Count;
        internal void* Barriers;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandFootprint
    {
        internal int Format;
        internal uint Width;
        internal uint Height;
        internal uint Depth;
        internal uint RowPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandPlacedFootprint
    {
        internal ulong Offset;
        internal CommandFootprint Footprint;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    internal struct CommandCopyLocation
    {
        [FieldOffset(0)] internal nint Resource;
        [FieldOffset(8)] internal uint Type;
        [FieldOffset(16)] internal CommandPlacedFootprint Placed;
        [FieldOffset(16)] internal uint Subresource;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandBox
    {
        internal uint Left;
        internal uint Top;
        internal uint Front;
        internal uint Right;
        internal uint Bottom;
        internal uint Back;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct CommandBeginningAccess
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(4)] internal int Format;
        [FieldOffset(8)] internal Vector4 Color;
        [FieldOffset(8)] internal float Depth;
        [FieldOffset(12)] internal byte Stencil;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandEndingResolve
    {
        internal nint Source;
        internal nint Destination;
        internal uint SubresourceCount;
        internal CommandResolveSubresource* Subresources;
        internal int Format;
        internal int ResolveMode;
        internal int PreserveSource;
    }

    // D3D12_RENDER_PASS_ENDING_ACCESS_RESOLVE_SUBRESOURCE_PARAMETERS.
    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandResolveSubresource
    {
        internal uint SourceSubresource;
        internal uint DestinationSubresource;
        internal uint X;
        internal uint Y;
        internal CommandRect SourceRect;
    }

    [StructLayout(LayoutKind.Explicit, Size = 56)]
    internal struct CommandEndingAccess
    {
        [FieldOffset(0)] internal uint Type;
        [FieldOffset(8)] internal CommandEndingResolve Resolve;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandRenderTarget
    {
        internal nuint Handle;
        internal CommandBeginningAccess Beginning;
        internal CommandEndingAccess Ending;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandDepthStencil
    {
        internal nuint Handle;
        internal CommandBeginningAccess DepthBeginning;
        internal CommandBeginningAccess StencilBeginning;
        internal CommandEndingAccess DepthEnding;
        internal CommandEndingAccess StencilEnding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandViewport
    {
        internal float X;
        internal float Y;
        internal float Width;
        internal float Height;
        internal float MinDepth;
        internal float MaxDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CommandIndexView
    {
        internal ulong Address;
        internal uint Size;
        internal int Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommandArgumentDesc
    {
        internal uint Type;
        private uint Data0;
        private uint Data1;
        private uint Data2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommandSignatureDesc
    {
        internal uint Stride;
        internal uint Count;
        internal CommandArgumentDesc* Arguments;
        internal uint NodeMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommandQueryDesc
    {
        internal int Type;
        internal uint Count;
        internal uint NodeMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommandReadbackHeap
    {
        internal int Type;
        internal int CpuPage;
        internal int Pool;
        internal uint CreationMask;
        internal uint VisibleMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommandReadbackDesc
    {
        internal int Dimension;
        internal ulong Alignment;
        internal ulong Width;
        internal uint Height;
        internal ushort Depth;
        internal ushort Mips;
        internal int Format;
        internal uint SampleCount;
        internal uint SampleQuality;
        internal int Layout;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommandMapRange
    {
        internal nuint Begin;
        internal nuint End;
    }

    internal static int CommandCreateAllocator(nint device, int type, nint* allocator)
    {
        Guid iid = new(0x6102dee4, 0xaf59, 0x4b09, 0xb9, 0x99, 0xb4, 0x4d, 0x73, 0xf0, 0x9b, 0x24);
        return ((delegate* unmanaged[Stdcall]<nint, int, Guid*, nint*, int>)(*(void***)device)[9])(device, type, &iid, allocator);
    }

    internal static int CommandCreateList(nint device, int type, nint allocator, nint* commands)
    {
        Guid iid = new(0xee936ef9, 0x599d, 0x4d28, 0x93, 0x8e, 0x23, 0xc4, 0xad, 0x05, 0xce, 0x51);
        return ((delegate* unmanaged[Stdcall]<nint, uint, int, nint, nint, Guid*, nint*, int>)(*(void***)device)[12])
            (device, 0, type, allocator, 0, &iid, commands);
    }

    internal static int CommandResetAllocator(nint allocator) =>
        ((delegate* unmanaged[Stdcall]<nint, int>)(*(void***)allocator)[8])(allocator);

    internal static int CommandResetList(nint commands, nint allocator) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)(*(void***)commands)[10])(commands, allocator, 0);

    internal static int CommandClose(nint commands) => ((delegate* unmanaged[Stdcall]<nint, int>)(*(void***)commands)[9])(commands);

    internal static int CommandCreateQueryHeap(nint device, int queueType, uint count, nint* heap)
    {
        Guid iid = new(0x0d9658ae, 0xed45, 0x469e, 0xa6, 0x1d, 0x97, 0x0e, 0xc5, 0x83, 0xca, 0xb4);
        CommandQueryDesc desc = new() { Type = queueType == 3 ? 5 : 1, Count = count };
        return ((delegate* unmanaged[Stdcall]<nint, CommandQueryDesc*, Guid*, nint*, int>)(*(void***)device)[39])(device, &desc, &iid, heap);
    }

    internal static int CommandCreateReadback(nint device, ulong size, nint* resource)
    {
        Guid iid = new(0x696442be, 0xa72e, 0x4059, 0xbc, 0x79, 0x5b, 0x5c, 0x98, 0x04, 0x0f, 0xad);
        CommandReadbackHeap heap = new() { Type = 3, CreationMask = 1, VisibleMask = 1 };
        CommandReadbackDesc desc = new() { Dimension = 1, Width = size, Height = 1, Depth = 1, Mips = 1, SampleCount = 1, Layout = 1 };
        return ((delegate* unmanaged[Stdcall]<nint, CommandReadbackHeap*, uint, CommandReadbackDesc*, uint, void*, Guid*, nint*, int>)
            (*(void***)device)[27])(device, &heap, 0, &desc, 0x400, null, &iid, resource);
    }

    internal static int CommandMapReadback(nint resource, nuint size, void** mapping)
    {
        CommandMapRange range = new() { End = size };
        return ((delegate* unmanaged[Stdcall]<nint, uint, CommandMapRange*, void**, int>)(*(void***)resource)[8])(resource, 0, &range, mapping);
    }

    internal static void CommandUnmapReadback(nint resource)
    {
        CommandMapRange range = new();
        ((delegate* unmanaged[Stdcall]<nint, uint, CommandMapRange*, void>)(*(void***)resource)[9])(resource, 0, &range);
    }

    internal static int CommandCreateSignature(nint device, uint type, uint stride, nint* signature)
    {
        Guid iid = new(0xc36a797c, 0xec80, 0x4f0a, 0x89, 0x85, 0xa7, 0xb2, 0x47, 0x50, 0x82, 0xd1);
        CommandArgumentDesc argument = new() { Type = type };
        CommandSignatureDesc desc = new() { Stride = stride, Count = 1, Arguments = &argument };
        return ((delegate* unmanaged[Stdcall]<nint, CommandSignatureDesc*, nint, Guid*, nint*, int>)(*(void***)device)[41])
            (device, &desc, 0, &iid, signature);
    }

    internal static void CommandExecute(nint queue, uint count, nint* commands) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, nint*, void>)(*(void***)queue)[10])(queue, count, commands);

    internal static int CommandSignal(nint queue, nint fence, ulong value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, ulong, int>)(*(void***)queue)[14])(queue, fence, value);

    internal static int CommandWait(nint queue, nint fence, ulong value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, ulong, int>)(*(void***)queue)[15])(queue, fence, value);

    internal static void CommandRootSignature(nint commands, nint signature, bool graphics) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, void>)(*(void***)commands)[graphics ? 30 : 29])(commands, signature);

    internal static void CommandRootAddress(nint commands, ulong address, bool graphics) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, ulong, void>)(*(void***)commands)[graphics ? 40 : 39])(commands, 0, address);

    internal static void CommandSetHeaps(nint commands, uint count, nint* heaps) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, nint*, void>)(*(void***)commands)[28])(commands, count, heaps);

    internal static void CommandSetPipeline(nint commands, nint pipeline) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, void>)(*(void***)commands)[25])(commands, pipeline);

    internal static void CommandSetTopology(nint commands, uint topology) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, void>)(*(void***)commands)[20])(commands, topology);

    internal static void CommandSetIndices(nint commands, CommandIndexView* indices) =>
        ((delegate* unmanaged[Stdcall]<nint, CommandIndexView*, void>)(*(void***)commands)[43])(commands, indices);

    internal static void CommandSetViewport(nint commands, CommandViewport* viewport) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, CommandViewport*, void>)(*(void***)commands)[21])(commands, 1, viewport);

    internal static void CommandSetScissor(nint commands, CommandRect* scissor) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, CommandRect*, void>)(*(void***)commands)[22])(commands, 1, scissor);

    internal static void CommandSetStencil(nint commands, uint front, uint back) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, void>)(*(void***)commands)[81])(commands, front, back);

    internal static void CommandBarriers(nint commands, CommandBarrierGroup* group) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, CommandBarrierGroup*, void>)(*(void***)commands)[80])(commands, 1, group);

    internal static void CommandDiscard(nint commands, nint resource) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, void*, void>)(*(void***)commands)[51])(commands, resource, null);

    internal static void CommandCopyBuffer(nint commands, nint destination, ulong destinationOffset, nint source, ulong sourceOffset, ulong size) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, ulong, nint, ulong, ulong, void>)(*(void***)commands)[15])
            (commands, destination, destinationOffset, source, sourceOffset, size);

    internal static void CommandCopyTexture(nint commands, CommandCopyLocation* destination, uint x, uint y, uint z,
        CommandCopyLocation* source, CommandBox* box) =>
        ((delegate* unmanaged[Stdcall]<nint, CommandCopyLocation*, uint, uint, uint, CommandCopyLocation*, CommandBox*, void>)(*(void***)commands)[16])
            (commands, destination, x, y, z, source, box);

    internal static void CommandBeginPass(nint commands, uint count, CommandRenderTarget* colors, CommandDepthStencil* depth, uint flags) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, CommandRenderTarget*, CommandDepthStencil*, uint, void>)(*(void***)commands)[68])
            (commands, count, colors, depth, flags);

    internal static void CommandEndPass(nint commands) => ((delegate* unmanaged[Stdcall]<nint, void>)(*(void***)commands)[69])(commands);

    internal static void CommandTimestamp(nint commands, nint heap, uint index) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int, uint, void>)(*(void***)commands)[53])(commands, heap, 2, index);

    internal static void CommandResolveTimestamps(nint commands, nint heap, uint count, nint buffer) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int, uint, uint, nint, ulong, void>)(*(void***)commands)[54])(commands, heap, 2, 0, count, buffer, 0);

    internal static void CommandDraw(nint commands, uint vertices, uint instances, uint firstVertex, uint firstInstance) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, uint, void>)(*(void***)commands)[12])
            (commands, vertices, instances, firstVertex, firstInstance);

    internal static void CommandDrawIndexed(nint commands, uint indices, uint instances, uint firstIndex, int vertexOffset, uint firstInstance) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, int, uint, void>)(*(void***)commands)[13])
            (commands, indices, instances, firstIndex, vertexOffset, firstInstance);

    internal static void CommandDispatch(nint commands, uint x, uint y, uint z) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, void>)(*(void***)commands)[14])(commands, x, y, z);

    internal static void CommandDispatchMesh(nint commands, uint x, uint y, uint z) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, void>)(*(void***)commands)[79])(commands, x, y, z);

    internal static void CommandIndirect(nint commands, nint signature, uint count, nint arguments, ulong offset) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, nint, ulong, nint, ulong, void>)(*(void***)commands)[59])
            (commands, signature, count, arguments, offset, 0, 0);

    // ID3D12GraphicsCommandList::SetMarker; PIX legacy event string payload.
    internal static void CommandSetMarker(nint commands, void* data, uint size) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, void*, uint, void>)(*(void***)commands)[56])(commands, 0, data, size);

    // ID3D12GraphicsCommandList::BeginEvent; PIX legacy event string payload.
    internal static void CommandBeginEvent(nint commands, void* data, uint size) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, void*, uint, void>)(*(void***)commands)[57])(commands, 0, data, size);

    // ID3D12GraphicsCommandList::EndEvent.
    internal static void CommandEndEvent(nint commands) =>
        ((delegate* unmanaged[Stdcall]<nint, void>)(*(void***)commands)[58])(commands);
}
