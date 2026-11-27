using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private const int TablePageSize = 256;
    private const int TablePageCount = 1024;
    private const int MaxTableSlots = TablePageSize * TablePageCount;

    // Windows in the fixed directory, measured in GPUBufferView entries.
    private const int InstanceWindow = 0;
    private const int MaterialWindow = TablePageCount;
    private const int GeometryWindow = 2 * TablePageCount;
    private const int GeometryPayloadWindow = 3 * TablePageCount;
    private const int DirectoryEntryCount = 4 * TablePageCount;

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private readonly record struct InstanceGpu(
        [field: FieldOffset(0)] World3DInstanceSpatialGpu Spatial,
        [field: FieldOffset(112)] uint GeometrySlot,
        [field: FieldOffset(116)] uint MaterialSlot,
        [field: FieldOffset(120)] uint Flags,
        [field: FieldOffset(124)] float Fade);

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private readonly record struct MaterialGpu(
        [field: FieldOffset(0)] Vector4 BaseColor,
        [field: FieldOffset(16)] Vector4 EmissiveAlphaCutoff,
        [field: FieldOffset(32)] Vector2 MetallicRoughness,
        [field: FieldOffset(40)] uint Flags,
        [field: FieldOffset(44)] GPUSamplerId Sampler,
        [field: FieldOffset(48)] GPUTextureId BaseColorTexture,
        [field: FieldOffset(52)] GPUTextureId NormalTexture,
        [field: FieldOffset(56)] GPUTextureId MetallicRoughnessTexture,
        [field: FieldOffset(60)] GPUTextureId EmissiveTexture);

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private readonly record struct GeometryLodGpu(
        [field: FieldOffset(0)] uint PageId,
        [field: FieldOffset(4)] uint VertexByteOffset,
        [field: FieldOffset(8)] uint IndexByteOffset,
        [field: FieldOffset(12)] uint VertexCount,
        [field: FieldOffset(16)] uint IndexCount,
        [field: FieldOffset(20)] float MinimumDistance,
        [field: FieldOffset(32)] Vector4 LocalBoundsMin,
        [field: FieldOffset(48)] Vector4 LocalBoundsMax)
    {
        [FieldOffset(24)] private readonly uint Reserved0 = 0;
        [FieldOffset(28)] private readonly uint Reserved1 = 0;
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private readonly record struct World3DCullViewGpu(
        [field: FieldOffset(0)] Matrix4x4 ViewProj,
        [field: FieldOffset(64)] World3DCell EyeCell,
        [field: FieldOffset(80)] Vector4 EyeLocalNear,
        [field: FieldOffset(96)] uint TargetViewSlot,
        [field: FieldOffset(100)] uint Flags,
        [field: FieldOffset(104)] float MinObjectRadius,
        [field: FieldOffset(108)] float MinTexelLodBiasMeters,
        [field: FieldOffset(112)] float MaxDistance,
        [field: FieldOffset(116)] uint Reserved0 = 0,
        [field: FieldOffset(120)] uint Reserved1 = 0,
        [field: FieldOffset(124)] uint Reserved2 = 0);

    [StructLayout(LayoutKind.Sequential, Size = 8)]
    private readonly record struct World3DOccludedCandidate(uint InstanceSlot, uint Lod);

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private readonly record struct PointLightGpu(
        [field: FieldOffset(0)] World3DCell Cell,
        [field: FieldOffset(16)] Vector4 LocalPositionAndRadius,
        [field: FieldOffset(32)] Vector4 ColorAndIntensity);

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private readonly record struct ScatterRoot(
        [field: FieldOffset(0)] GPUBufferView Patches,
        [field: FieldOffset(8)] GPUBufferView PatchSlots,
        [field: FieldOffset(16)] GPUBufferView Pool,
        [field: FieldOffset(24)] uint PatchCount,
        [field: FieldOffset(28)] uint WordsPerInstance);

    private static string ReadWorldShader(string moduleName)
    {
        string resourceName = moduleName + ".slang";
        using Stream stream = typeof(World3DScene).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded shader module {resourceName}.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static void ValidateRoot<T>(ShaderRootLayout root) where T : unmanaged =>
        GpuStore.ValidateLayout<T>(root.TypeName, root.SizeBytes, root.Fields);

    private readonly record struct CopyJob(BufferSlice Source, BufferSlice Destination);

    private readonly record struct ScatterJob(GpuBuffer Destination, BufferSlice Patches, BufferSlice Slots, ScatterRoot Root);

    private readonly struct GeometryAllocation(GpuBuffer buffer, uint pageId, ulong offset, uint token)
    {
        /// <summary>The scene-owned payload page backing this allocation.</summary>
        public GpuBuffer Buffer { get; } = buffer;
        /// <summary>The index of the page in the payload directory window.</summary>
        public uint PageId { get; } = pageId;
        /// <summary>The byte offset relative to the start of <see cref="Buffer"/>.</summary>
        public ulong Offset { get; } = offset;
        /// <summary>The heap allocator's release token.</summary>
        public uint Token { get; } = token;
    }

    private sealed class GpuStore : IDisposable
    {
        private const string SceneDataModule = "World3DSceneData";
        private const ulong GeometryPageBytes = 4 * 1024 * 1024;
        private const ulong GeometryPageLimit = uint.MaxValue & ~(HeapAllocator.Alignment - 1);
        private const uint GeometryAllocationsPerPage = 4096;
        private static readonly ulong DirectoryEntryBytes = (ulong)Unsafe.SizeOf<GPUBufferView>();

        private readonly RenderingSystem _system;
        private readonly Table<InstanceGpu> _instances = new(InstanceWindow);
        private readonly Table<MaterialGpu> _materials = new(MaterialWindow);
        private readonly Table<GeometryLodGpu> _geometry = new(GeometryWindow, MaxGeometryLods);
        private readonly Dictionary<int, GPUBufferView> _directoryPatches = [];
        private readonly List<int> _directoryOrder = [];
        private readonly GeometryPage?[] _geometryPages = new GeometryPage?[TablePageCount];
        private readonly Stack<uint> _freeGeometryPages = [];
        private uint _nextGeometryPage;
        private bool _directoryInitialized;

        /// <summary>Creates a scene store with no allocated pages and validates its shader ABI.</summary>
        public GpuStore(RenderingSystem system)
        {
            _system = system;
            ShaderSystem shaders = system.Shaders;
            AlcoSprite2DShaders.EnsureBuiltInModulesLoaded(shaders);
            ScatterShader = shaders.GetShader(AlcoSprite2DShaders.GpuInstanceScatterModule, ShaderKind.Compute);
            ShaderRootLayout scatterRoot = ScatterShader.Reflection.Root
                ?? throw new InvalidOperationException("GpuInstanceScatter did not reflect a root layout.");
            ValidateShaderLayouts(shaders.Compiler, scatterRoot);
        }

        /// <summary>The pending transfer jobs for the recorded frame.</summary>
        public List<CopyJob> Copies { get; } = [];
        /// <summary>The pending table scatter jobs for the recorded frame.</summary>
        public List<ScatterJob> Scatters { get; } = [];
        /// <summary>The rendering system's shared table scatter shader.</summary>
        public Shader ScatterShader { get; }
        /// <summary>The stable GPU directory, or null until there are pages to publish.</summary>
        public GpuBuffer? Directory { get; private set; }
        /// <summary>The number of distinct dirty instance entries staged this frame.</summary>
        public int InstancePatches { get; private set; }
        /// <summary>The number of distinct dirty material entries staged this frame.</summary>
        public int MaterialPatches { get; private set; }
        /// <summary>The number of distinct dirty geometry LOD entries staged this frame.</summary>
        public int GeometryPatches { get; private set; }
        /// <summary>The number of table pages requiring a full initial copy this frame.</summary>
        public int InitializedTablePages { get; private set; }

        /// <summary>Declares reads of the directory and the allocated pages it addresses.</summary>
        public void ReadTables(RenderGraphBuilder builder, RenderGraphPassBuilder pass, RGUsage usage, bool materials = false)
        {
            pass.Read(builder.ImportBuffer(Directory!, "World3D.Directory"), usage);
            _instances.ReadPages(builder, pass, usage, "World3D.Instances");
            _geometry.ReadPages(builder, pass, usage, "World3D.GeometryLods");
            if (materials)
                _materials.ReadPages(builder, pass, usage, "World3D.Materials");
        }

        /// <summary>Queues an instance table write.</summary>
        public void SetInstance(int slot, in InstanceGpu data) => _instances.Set(slot, 0, in data);

        /// <summary>Queues a material table write.</summary>
        public void SetMaterial(int slot, in MaterialGpu data) => _materials.Set(slot, 0, in data);

        /// <summary>Queues a geometry LOD write; default clears the entry.</summary>
        public void SetGeometryLod(int slot, int lod, in GeometryLodGpu data) => _geometry.Set(slot, lod, in data);

        /// <summary>Clears all LODs in a geometry slot without creating a new page.</summary>
        public void ClearGeometry(int slot) => _geometry.Clear(slot);

        /// <summary>Gets the GPU slice for the instance, or empty before page allocation.</summary>
        public BufferSlice GetInstanceSlice(int slot) => _instances.GetSlice(slot, 0);

        /// <summary>Gets the GPU slice for a geometry LOD, or empty before page allocation.</summary>
        public BufferSlice GetGeometrySlice(int slot, int lod) => _geometry.GetSlice(slot, lod);

        /// <summary>Reserves a nonmoving, 16-byte-aligned range within one GPU payload page.</summary>
        public bool TryAllocateGeometry(ulong bytes, out GeometryAllocation allocation)
        {
            allocation = default;
            if (bytes == 0 || bytes > GeometryPageLimit)
                return false;

            bool dedicated = bytes > GeometryPageBytes;
            if (!dedicated)
            {
                for (uint pageId = 0; pageId < _nextGeometryPage; ++pageId)
                {
                    GeometryPage? page = _geometryPages[pageId];
                    if (page?.Buffer.SizeBytes == GeometryPageBytes && page.TryAllocate(pageId, bytes, out allocation))
                        return true;
                }
            }

            bool reuseId = _freeGeometryPages.TryPeek(out uint newPageId);
            if (!reuseId)
            {
                if (_nextGeometryPage == TablePageCount)
                    return false;
                newPageId = _nextGeometryPage;
            }

            ulong pageBytes = dedicated ? (bytes + HeapAllocator.Alignment - 1) & ~(HeapAllocator.Alignment - 1) : GeometryPageBytes;
            GpuBuffer buffer;
            try
            {
                buffer = _system.CreateBuffer(pageBytes, MemoryType.GpuOnly);
            }
            catch (InvalidOperationException)
            {
                return false; // RenderingSystem could not fit another page in its buffer budget.
            }
            catch (GPUException ex) when (ex.Error == Error.OutOfMemory)
            {
                return false;
            }

            try
            {
                GPURange range = buffer.GetRange();
                // Both the underlying view offset and any byte offset added in Slang must fit in uint.
                if (range.Offset > (ulong)uint.MaxValue + 1 - pageBytes)
                {
                    buffer.Dispose();
                    return false;
                }

                GeometryPage page = new(buffer, in range, dedicated ? 1u : GeometryAllocationsPerPage);
                if (!page.TryAllocate(newPageId, bytes, out allocation))
                    throw new InvalidOperationException("An empty geometry page could not fit its initial allocation.");

                _directoryPatches[GeometryPayloadWindow + (int)newPageId] = buffer.View;
                _geometryPages[newPageId] = page;
                if (reuseId)
                    _freeGeometryPages.Pop();
                else
                    ++_nextGeometryPage;
                return true;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }

        /// <summary>Returns a range after the caller's GPU retirement fence and removes the page if empty.</summary>
        public void ReleaseGeometry(in GeometryAllocation allocation)
        {
            uint pageId = allocation.PageId;
            GeometryPage page = _geometryPages[pageId]!;
            Debug.Assert(ReferenceEquals(page.Buffer, allocation.Buffer));
            if (!page.ReleaseAndIsEmpty(allocation.Token))
                return;

            _directoryPatches[GeometryPayloadWindow + (int)pageId] = default;
            _geometryPages[pageId] = null;
            _freeGeometryPages.Push(pageId);
            page.Buffer.Dispose();
        }

        /// <summary>Appends dirty table and directory writes using the current frame's transient storage.</summary>
        public void PrepareTables(RenderContext context)
        {
            InstancePatches = _instances.Prepare(this, context);
            MaterialPatches = _materials.Prepare(this, context);
            GeometryPatches = _geometry.Prepare(this, context);
            PrepareDirectory(context);
        }

        /// <summary>Commits staged data only after submission; scene edits are excluded until commit or discard.</summary>
        public void CommitTables()
        {
            _instances.Commit();
            _materials.Commit();
            _geometry.Commit();
            _directoryInitialized |= Directory != null;
            _directoryPatches.Clear();
        }

        /// <summary>Discards only frame-local jobs and counters, retaining any uncommitted table data.</summary>
        public void ResetJobs()
        {
            Copies.Clear();
            Scatters.Clear();
            InstancePatches = MaterialPatches = GeometryPatches = InitializedTablePages = 0;
        }

        /// <summary>Appends a transfer between two existing buffer slices.</summary>
        public void AddCopy(BufferSlice source, BufferSlice destination) => Copies.Add(new CopyJob(source, destination));

        /// <summary>Retires all owned GPU pages and the directory.</summary>
        public void Dispose()
        {
            ResetJobs();
            _instances.Dispose();
            _materials.Dispose();
            _geometry.Dispose();
            for (int i = 0; i < _nextGeometryPage; ++i)
            {
                _geometryPages[i]?.Buffer.Dispose();
                _geometryPages[i] = null;
            }
            Directory?.Dispose();
            Directory = null;
            _directoryPatches.Clear();
        }

        private void PrepareDirectory(RenderContext context)
        {
            if (_directoryPatches.Count == 0)
                return;

            GpuBuffer directory = Directory ??= _system.CreateBuffer(DirectoryEntryCount * DirectoryEntryBytes, MemoryType.GpuOnly);
            if (!_directoryInitialized)
            {
                Span<GPUBufferView> entries = context.AllocateTransient<GPUBufferView>(DirectoryEntryCount, out BufferSlice source);
                entries.Clear();
                foreach ((int entry, GPUBufferView view) in _directoryPatches)
                    entries[entry] = view;
                AddCopy(source, directory);
                return;
            }

            _directoryOrder.Clear();
            _directoryOrder.AddRange(_directoryPatches.Keys);
            _directoryOrder.Sort();
            Span<GPUBufferView> patches = context.AllocateTransient<GPUBufferView>(_directoryOrder.Count, out BufferSlice staging);
            for (int start = 0; start < _directoryOrder.Count;)
            {
                int entry = _directoryOrder[start];
                patches[start] = _directoryPatches[entry];
                int end = start + 1;
                for (; end < _directoryOrder.Count && _directoryOrder[end] == entry + end - start; ++end)
                    patches[end] = _directoryPatches[_directoryOrder[end]];
                ulong bytes = (ulong)(end - start) * DirectoryEntryBytes;
                AddCopy(staging.Slice((ulong)start * DirectoryEntryBytes, bytes), directory.Slice((ulong)entry * DirectoryEntryBytes, bytes));
                start = end;
            }
        }

        private sealed class GeometryPage(GpuBuffer buffer, in GPURange range, uint capacity)
        {
            private readonly HeapAllocator _heap = new(new GPUAllocation<byte>
            {
                Buffer = range.Buffer,
                Offset = range.Offset,
                SizeBytes = range.Size,
            }, capacity);
            private readonly ulong _startOffset = range.Offset;
            private uint _allocationCount;

            /// <summary>The owned payload storage.</summary>
            public GpuBuffer Buffer { get; } = buffer;

            /// <summary>Tries to allocate a page-relative range.</summary>
            public bool TryAllocate(uint pageId, ulong bytes, out GeometryAllocation allocation)
            {
                HeapAllocation<byte> result = _heap.Allocate(bytes);
                if (result.Range.SizeBytes == 0)
                {
                    allocation = default;
                    return false;
                }
                ++_allocationCount;
                allocation = new GeometryAllocation(Buffer, pageId, result.Range.Offset - _startOffset, result.Token);
                return true;
            }

            /// <summary>Releases a range and reports whether any allocations remain.</summary>
            public bool ReleaseAndIsEmpty(uint token)
            {
                _heap.Free(new HeapAllocation<byte> { Token = token });
                return --_allocationCount == 0;
            }
        }

        private sealed class Table<T>(int window, int entriesPerSlot = 1) : IDisposable where T : unmanaged
        {
            private static readonly ulong EntryBytes = (ulong)Unsafe.SizeOf<T>();
            private readonly Page?[] _pages = new Page?[TablePageCount];
            private readonly List<int> _dirtyPages = [];
            private readonly List<GpuBuffer> _buffers = [];

            /// <summary>Declares reads of the backing pages, including pages first allocated this frame.</summary>
            public void ReadPages(RenderGraphBuilder builder, RenderGraphPassBuilder pass, RGUsage usage, string name)
            {
                foreach (GpuBuffer buffer in _buffers)
                    pass.Read(builder.ImportBuffer(buffer, name), usage);
            }

            /// <summary>Coalesces writes to the same page-local entry until submission.</summary>
            public void Set(int slot, int entry, in T data)
            {
                int pageId = slot / TablePageSize;
                Page page = _pages[pageId] ??= new Page();
                MarkDirty(pageId, page);
                page.Patches[GetEntryIndex(slot, entry)] = data;
            }

            /// <summary>Queues clear writes without creating a previously untouched page.</summary>
            public void Clear(int slot)
            {
                int pageId = slot / TablePageSize;
                Page? page = _pages[pageId];
                if (page == null)
                    return;
                MarkDirty(pageId, page);
                int first = GetEntryIndex(slot, 0);
                for (int i = 0; i < entriesPerSlot; ++i)
                    page.Patches[first + i] = default;
            }

            /// <summary>Gets the stable GPU slice corresponding to a table entry.</summary>
            public BufferSlice GetSlice(int slot, int entry)
            {
                GpuBuffer? buffer = _pages[slot / TablePageSize]?.Buffer;
                return buffer == null ? default : buffer.Slice((ulong)GetEntryIndex(slot, entry) * EntryBytes, EntryBytes);
            }

            /// <summary>Stages only dirty pages and returns the number of entries written.</summary>
            public int Prepare(GpuStore store, RenderContext context)
            {
                int patchCount = 0;
                for (int i = 0; i < _dirtyPages.Count; ++i)
                {
                    int pageId = _dirtyPages[i];
                    Page page = _pages[pageId]!;
                    patchCount += page.Patches.Count;
                    if (page.Initialized)
                        StageScatter(store, context, page);
                    else
                        StageInitialization(store, context, pageId, page);
                }
                return patchCount;
            }

            /// <summary>Commits initialized pages and clears their dirty entries.</summary>
            public void Commit()
            {
                foreach (int pageId in _dirtyPages)
                {
                    Page page = _pages[pageId]!;
                    page.Initialized = true;
                    page.Patches.Clear();
                }
                _dirtyPages.Clear();
            }

            /// <summary>Retires the GPU storage for every allocated page.</summary>
            public void Dispose()
            {
                foreach (Page? page in _pages)
                    page?.Buffer?.Dispose();
                Array.Clear(_pages);
                _buffers.Clear();
                _dirtyPages.Clear();
            }

            private int GetEntryIndex(int slot, int entry) => (slot % TablePageSize) * entriesPerSlot + entry;

            private void MarkDirty(int pageId, Page page)
            {
                if (page.Patches.Count == 0)
                    _dirtyPages.Add(pageId);
            }

            private void StageInitialization(GpuStore store, RenderContext context, int pageId, Page page)
            {
                int count = TablePageSize * entriesPerSlot;
                if (page.Buffer == null)
                {
                    page.Buffer = store._system.CreateBuffer((ulong)count * EntryBytes, MemoryType.GpuOnly);
                    _buffers.Add(page.Buffer);
                }
                GpuBuffer buffer = page.Buffer;
                Span<T> rows = context.AllocateTransient<T>(count, out BufferSlice source);
                rows.Clear();
                foreach ((int slot, T value) in page.Patches)
                    rows[slot] = value;
                store.AddCopy(source, buffer);
                store._directoryPatches[window + pageId] = buffer.View;
                ++store.InitializedTablePages;
            }

            private static void StageScatter(GpuStore store, RenderContext context, Page page)
            {
                int count = page.Patches.Count;
                Span<T> patches = context.AllocateTransient<T>(count, out BufferSlice patchSlice);
                Span<uint> slots = context.AllocateTransient<uint>(count, out BufferSlice slotSlice);
                int index = 0;
                foreach ((int slot, T value) in page.Patches)
                {
                    patches[index] = value;
                    slots[index++] = (uint)slot;
                }
                GpuBuffer destination = page.Buffer!;
                ScatterRoot root = new(patchSlice.View, slotSlice.View, destination.View, (uint)count, (uint)(EntryBytes / sizeof(uint)));
                store.Scatters.Add(new ScatterJob(destination, patchSlice, slotSlice, root));
            }

            private sealed class Page
            {
                public readonly Dictionary<int, T> Patches = [];
                public GpuBuffer? Buffer;
                public bool Initialized;
            }
        }

        private static void ValidateShaderLayouts(ShaderCompiler compiler, ShaderRootLayout root)
        {
            IReadOnlyList<ShaderTypeLayout> types = compiler.LoadModuleFromSource(
                SceneDataModule, SceneDataModule + ".slang", ReadWorldShader(SceneDataModule)).GetTypes();
            ShaderTypeLayout Find(string name) => types.FirstOrDefault(type => type.TypeName == name)
                ?? throw new InvalidOperationException($"Shader ABI type '{name}' was not reflected in {SceneDataModule}.");

            ValidateLayout<World3DInstanceSpatialGpu>(Find(nameof(World3DInstanceSpatialGpu)));
            ValidateLayout<World3DView>(Find(nameof(World3DView)));
            ValidateLayout<World3DCullViewGpu>(Find(nameof(World3DCullViewGpu)));
            ValidateLayout<World3DOccludedCandidate>(Find(nameof(World3DOccludedCandidate)));
            ValidateLayout<PointLightGpu>(Find("World3DPointLightGpu"));
            ValidateLayout<InstanceGpu>(Find("World3DInstanceGpu"));
            ValidateLayout<MaterialGpu>(Find("World3DMaterialGpu"));
            ValidateLayout<GeometryLodGpu>(Find("World3DGeometryLodGpu"));
            ValidateLayout<VertexPBR>(Find("World3DVertexGpu"));
            ValidateLayout<ScatterRoot>(root.TypeName, root.SizeBytes, root.Fields);

            uint viewBytes = (uint)DirectoryEntryBytes;
            ShaderTypeLayout directory = Find("World3DPageDirectory");
            if (directory.SizeBytes != DirectoryEntryCount * viewBytes)
                throw new InvalidOperationException("World3D page directory size does not match the managed ABI.");
            ValidateLayout<GPUBufferView>(ValidateArray(directory, "Instances", InstanceWindow * viewBytes, TablePageCount, viewBytes));
            ValidateArray(directory, "Materials", MaterialWindow * viewBytes, TablePageCount, viewBytes);
            ValidateArray(directory, "Geometry", GeometryWindow * viewBytes, TablePageCount, viewBytes);
            ValidateArray(directory, "GeometryPayload", GeometryPayloadWindow * viewBytes, TablePageCount, viewBytes);

            ValidateArray(Find("World3DInstancePage"), "Rows", 0, TablePageSize, (uint)Unsafe.SizeOf<InstanceGpu>());
            ValidateArray(Find("World3DMaterialPage"), "Rows", 0, TablePageSize, (uint)Unsafe.SizeOf<MaterialGpu>());
            uint lodBytes = (uint)Unsafe.SizeOf<GeometryLodGpu>();
            ValidateArray(Find("World3DGeometryRow"), "Lods", 0, MaxGeometryLods, lodBytes);
            ValidateArray(Find("World3DGeometryPage"), "Rows", 0, TablePageSize, MaxGeometryLods * lodBytes);
        }

        /// <summary>Checks a reflected payload type against its managed layout.</summary>
        public static void ValidateLayout<T>(ShaderTypeLayout layout) where T : unmanaged =>
            ValidateLayout<T>(layout.TypeName!, layout.SizeBytes, layout.Fields);

        /// <summary>Verifies the exact reflected size, field offsets, and scalar types of a managed shader layout.</summary>
        public static void ValidateLayout<T>(string shaderType, uint size, IReadOnlyList<ShaderFieldLayout> fields) where T : unmanaged
        {
            FieldInfo[] managedFields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (size != Unsafe.SizeOf<T>() || fields.Count != managedFields.Length)
                throw new InvalidOperationException($"Shader ABI '{shaderType}' does not match the managed size or field count.");

            foreach (FieldInfo managed in managedFields)
            {
                string name = managed.Name;
                // Positional record properties carry their layout on the generated backing field.
                if (name[0] == '<')
                    name = name[1..name.IndexOf('>')];
                ShaderFieldLayout? field = fields.FirstOrDefault(candidate => candidate.Name == name);
                if (field == null || field.OffsetBytes != (uint)Marshal.OffsetOf<T>(managed.Name) || field.SizeBytes != Marshal.SizeOf(managed.FieldType))
                    throw new InvalidOperationException($"Shader ABI layout differs at '{shaderType}.{name}'.");
                if (managed.FieldType == typeof(Matrix4x4) && field.Type is not
                    { Kind: ShaderTypeKind.Matrix, ScalarType: ShaderScalarType.Float32, MatrixLayout: ShaderMatrixLayout.RowMajor, MatrixRows: 4, MatrixColumns: 4 })
                    throw new InvalidOperationException($"Shader ABI matrix '{shaderType}.{name}' must be a row-major float4x4.");
                if (managed.FieldType == typeof(World3DCell) && field.Type is not
                    { Kind: ShaderTypeKind.Vector, ScalarType: ShaderScalarType.Int32, VectorLength: 4 })
                    throw new InvalidOperationException($"Shader ABI cell '{shaderType}.{name}' must be an int4.");
            }
        }

        private static ShaderTypeLayout ValidateArray(ShaderTypeLayout layout, string name, uint offset, uint count, uint stride)
        {
            ShaderFieldLayout? field = layout.FindField(name);
            if (field == null || field.OffsetBytes != offset || field.Type.Kind != ShaderTypeKind.Array ||
                field.Type.ElementCount != count || field.Type.StrideBytes != stride || field.Type.ElementType?.SizeBytes != stride)
                throw new InvalidOperationException($"Shader ABI array '{layout.TypeName}.{name}' has an unexpected offset, count or stride.");
            return field.Type.ElementType;
        }
    }
}
