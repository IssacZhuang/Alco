using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private const uint IndexedArgsStride = 20;
    private const uint DrawRefStride = 8;
    private readonly GBufferPrograms _gbufferPrograms;
    private BufferSlice _frameViews;
    private GpuBuffer? _selection;
    private GpuBuffer? _drawArgs;
    private GpuBuffer? _drawRefs;
    private GBufferTargets _gbufferTargets;
    private int _frameDrawSlots;
    private bool _recordedWorld;
    private bool _submittedWorld;

    /// <summary>Synchronously counts nonempty GBuffer commands from the latest submitted world frame.</summary>
    /// <param name="count">Receives the number of commands that passed GPU culling.</param>
    /// <returns>Whether a submitted GPU world frame was available to read.</returns>
    public bool TryReadVisibleDrawCount(out int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        count = 0;
        if (_context == null || _context.IsDisposed || _context.CurrentFrameId != 0 || _system.ActiveBackend == GraphicsBackend.NoGPU)
            return false;
        ReconcileFrame();
        if (!_submittedWorld)
            return false;
        if (LastRecordedDraws.IndirectSlots == 0)
            return true;

        byte[] bytes = GC.AllocateUninitializedArray<byte>(checked(_frameDrawSlots * (int)IndexedArgsStride));
        _system.ReadBuffer(_drawArgs!, bytes);
        foreach (ref readonly IndexedDrawArgs command in MemoryMarshal.Cast<byte, IndexedDrawArgs>(bytes))
        {
            if (command.InstanceCount != 0)
                ++count;
        }
        return true;
    }

    internal int LastCandidateWrites { get; private set; }

    internal BufferSlice GetDrawArgsGpuSlice() => _frameDrawSlots == 0
        ? default : _drawArgs!.Slice(0, (ulong)_frameDrawSlots * IndexedArgsStride);

    private void PrepareGeometryPasses(RenderContext context)
    {
        LastCandidateWrites = 0;
        _frameViews = default;
        _frameDrawSlots = 0;
        if (!_recordedWorld)
            return;

        RefreshDrawLayout();
        LastCandidateWrites = _drawLayout.Stage(context, _system, _gpu);
        _frameViews = context.UploadTransient(in _recordedView);
        _frameDrawSlots = _drawLayout.SlotCount;
        if (_frameDrawSlots == 0)
            return;

        uint views = FrameViewCount;
        EnsureDrawBuffer(ref _selection, (ulong)_instances.End * sizeof(uint) * views);
        EnsureDrawBuffer(ref _drawArgs, (ulong)_frameDrawSlots * IndexedArgsStride * views);
        EnsureDrawBuffer(ref _drawRefs, (ulong)_frameDrawSlots * DrawRefStride * views);
    }

    private uint FrameViewCount => (uint)(_frameViews.SizeBytes / (ulong)Unsafe.SizeOf<World3DView>());

    private void EnsureDrawBuffer(ref GpuBuffer? current, ulong bytes)
    {
        if (current != null && current.SizeBytes >= bytes)
            return;
        GpuBuffer next = _system.CreateBuffer(Math.Max(bytes, (current?.SizeBytes ?? 0) * 2), MemoryType.GpuOnly);
        current?.Dispose();
        current = next;
    }

    private readonly record struct GBufferTargets(RGTexture Albedo, RGTexture Normal, RGTexture Material, RGTexture Emissive, RGTexture Depth);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CullSelectRoot(GPUBufferView Directory, GPUBufferView Views, GPUBufferView Selection,
        uint InstanceCount, uint GroupsX, float MaximumDistance);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct BuildArgsRoot(GPUBufferView Directory, GPUBufferView Candidates, GPUBufferView Selection,
        GPUBufferView Args, GPUBufferView DrawRefs, uint InstanceCount, uint CandidateCount, uint GroupsX);

    [StructLayout(LayoutKind.Sequential, Size = (int)IndexedArgsStride)]
    private readonly record struct IndexedDrawArgs(uint IndexCount, uint InstanceCount, uint FirstIndex, int VertexOffset, uint FirstInstance);

    [StructLayout(LayoutKind.Sequential, Size = (int)DrawRefStride)]
    private readonly record struct DrawRef(uint InstanceSlot, uint Lod);

    private sealed class GeometryPasses(World3DScene scene) : IRenderGraphNode
    {
        private const int CullSelect = 0;
        private const int BuildArgs = 1;
        private const int GBuffer = 2;
        private readonly World3DScene _scene = scene;
        private Shader? _cullSelect;
        private Shader? _buildArgs;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            _scene._gbufferTargets = default;
            if (!_scene._recordedWorld)
                return;

            bool draws = _scene._frameDrawSlots != 0;
            RGBuffer args = default;
            RGBuffer drawRefs = default;
            RGBuffer views = builder.ImportBuffer(_scene._frameViews.Buffer!, "World3D.Views");
            if (draws)
            {
                RGBuffer selection = builder.ImportBuffer(_scene._selection!, "World3D.LodSelection");
                args = builder.ImportBuffer(_scene._drawArgs!, "World3D.IndexedArgs");
                drawRefs = builder.ImportBuffer(_scene._drawRefs!, "World3D.DrawRefs");

                RenderGraphPassBuilder cull = builder.AddPass("World3D.CullSelect", CullSelect);
                cull.Read(views, RGUsage.ComputeRead);
                _scene._gpu.ReadTables(builder, cull, RGUsage.ComputeRead);
                cull.Write(selection, RGUsage.ComputeWrite);

                RenderGraphPassBuilder emit = builder.AddPass("World3D.BuildArgs", BuildArgs);
                _scene._gpu.ReadTables(builder, emit, RGUsage.ComputeRead);
                emit.Read(builder.ImportBuffer(_scene._drawLayout.Buffer, "World3D.Candidates"), RGUsage.ComputeRead);
                emit.Read(selection, RGUsage.ComputeRead);
                emit.Write(args, RGUsage.ComputeWrite);
                emit.Write(drawRefs, RGUsage.ComputeWrite);
            }

            GBufferTargets targets = new(
                builder.CreateTexture(new RGTextureDesc { Name = "World3D.Albedo", Format = Format.Rgba8Unorm }),
                builder.CreateTexture(new RGTextureDesc { Name = "World3D.Normal", Format = Format.Rgba16Float }),
                builder.CreateTexture(new RGTextureDesc { Name = "World3D.Material", Format = Format.Rgba8Unorm }),
                builder.CreateTexture(new RGTextureDesc { Name = "World3D.Emissive", Format = Format.Rgba16Float }),
                builder.CreateTexture(new RGTextureDesc { Name = "World3D.Depth", Format = Format.D32Float }));
            _scene._gbufferTargets = targets;
            RenderGraphPassBuilder gbuffer = builder.AddPass("World3D.GBuffer", GBuffer);
            gbuffer.Write(targets.Albedo, RGUsage.ColorAttachmentWrite);
            gbuffer.Write(targets.Normal, RGUsage.ColorAttachmentWrite);
            gbuffer.Write(targets.Material, RGUsage.ColorAttachmentWrite);
            gbuffer.Write(targets.Emissive, RGUsage.ColorAttachmentWrite);
            gbuffer.Write(targets.Depth, RGUsage.DepthStencil);
            if (!draws)
                return;
            gbuffer.Read(views, RGUsage.VertexRead);
            gbuffer.Read(args, RGUsage.IndirectRead);
            gbuffer.Read(drawRefs, RGUsage.VertexRead);
            _scene._gpu.ReadTables(builder, gbuffer, RGUsage.VertexRead | RGUsage.PixelRead, materials: true);
            foreach (DrawBucket bucket in _scene._drawLayout.Buckets)
                gbuffer.Read(builder.ImportBuffer(bucket.Key.Page, "World3D.GeometryPayload"), RGUsage.IndexRead | RGUsage.VertexRead);
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            if (passId == CullSelect)
            {
                DispatchCull(context.RenderContext);
                return;
            }
            if (passId == BuildArgs)
            {
                DispatchBuildArgs(context.RenderContext);
                return;
            }
            RenderGBuffer(in context);
        }

        private void DispatchCull(RenderContext context)
        {
            _cullSelect ??= LoadCompute<CullSelectRoot>("World3DCullSelect");
            uint count = (uint)_scene._instances.End;
            Extent3D groups = Groups(count);
            CullSelectRoot root = new(_scene._gpu.Directory!.View, _scene._frameViews.View, _scene._selection!.View,
                count, groups.X, _scene.MaximumDrawDistance);
            BufferSlice data = context.UploadTransient(in root);
            using var compute = context.BeginCompute();
            compute.Dispatch(_cullSelect, data, groups);
        }

        private void DispatchBuildArgs(RenderContext context)
        {
            _buildArgs ??= LoadCompute<BuildArgsRoot>("World3DBuildArgs");
            uint count = (uint)_scene._frameDrawSlots;
            Extent3D groups = Groups(count);
            BuildArgsRoot root = new(_scene._gpu.Directory!.View, _scene._drawLayout.Buffer.View, _scene._selection!.View,
                _scene._drawArgs!.View, _scene._drawRefs!.View, (uint)_scene._instances.End, count, groups.X);
            BufferSlice data = context.UploadTransient(in root);
            using var compute = context.BeginCompute();
            compute.Dispatch(_buildArgs, data, groups);
        }

        private Shader LoadCompute<T>(string name) where T : unmanaged
        {
            ShaderSystem shaders = _scene._system.Shaders;
            IReadOnlyList<ShaderTypeLayout> types = shaders.Compiler.LoadModuleFromSource(name, name + ".slang", ReadWorldShader(name)).GetTypes();
            if (typeof(T) == typeof(BuildArgsRoot))
            {
                GpuStore.ValidateLayout<DrawCandidate>(RequireType(types, nameof(DrawCandidate)));
                GpuStore.ValidateLayout<IndexedDrawArgs>(RequireType(types, nameof(IndexedDrawArgs)));
                GpuStore.ValidateLayout<DrawRef>(RequireType(types, nameof(DrawRef)));
            }
            Shader shader = shaders.GetShader(name, ShaderKind.Compute);
            ValidateRoot<T>(shader.Reflection.Root ?? throw new InvalidOperationException($"{name} has no GPU root."));
            return shader;
        }

        private static ShaderTypeLayout RequireType(IReadOnlyList<ShaderTypeLayout> types, string name) =>
            types.FirstOrDefault(t => t.TypeName == name) ?? throw new InvalidOperationException($"Missing shader ABI type {name}.");

        private Extent3D Groups(uint count)
        {
            uint groups = (count + 63) / 64;
            uint x = Math.Min(65535u, groups);
            return new Extent3D(x, (groups + x - 1) / x, _scene.FrameViewCount);
        }

        private void RenderGBuffer(in RenderGraphContext context)
        {
            GBufferTargets targets = _scene._gbufferTargets;
            RenderingDesc attachments = new()
            {
                Colors = [ClearColor(context.GetRenderTarget(targets.Albedo)), ClearColor(context.GetRenderTarget(targets.Normal)),
                    ClearColor(context.GetRenderTarget(targets.Material)), ClearColor(context.GetRenderTarget(targets.Emissive))],
                Depth = new DepthAttachment { Target = context.GetRenderTarget(targets.Depth), Load = LoadOp.Clear, Store = StoreOp.Store, Clear = 0 },
            };
            using var pass = context.RenderContext.BeginRenderPass(attachments);
            if (_scene._frameDrawSlots == 0)
                return;

            int packetLimit = _scene._system.ActiveBackend is GraphicsBackend.Vulkan or GraphicsBackend.VulkanCompact
                ? (int)Math.Clamp(_scene._system.Caps.MaxIndirectDrawCount, 1u, (uint)int.MaxValue) : 1;
            int calls = 0;
            int slots = 0;
            foreach (DrawBucket bucket in _scene._drawLayout.Buckets)
            {
                DrawRoute route = bucket.Key.Route;
                foreach (DrawChunk chunk in bucket.Chunks)
                {
                    slots += chunk.Length;
                    for (int first = 0; first < chunk.Length;)
                    {
                        int count = Math.Min(packetLimit, chunk.Length - first);
                        uint start = (uint)(chunk.First + first);
                        BufferSlice args = _scene._drawArgs!.Slice((ulong)start * IndexedArgsStride, (ulong)count * IndexedArgsStride);
                        _scene._gbufferPrograms.Draw(pass, route.SurfaceModule, route.AlphaMode == World3DAlphaMode.Mask, route.Cull,
                            _scene._gpu.Directory!.View, _scene._frameViews.View, _scene._drawRefs!.View, bucket.Key.Page, start, args, (uint)count);
                        ++calls;
                        first += count;
                    }
                }
            }
            _scene.LastRecordedDraws = new World3DDrawStats(calls, slots);
        }

        private static ColorAttachment ClearColor(RenderTarget target) =>
            new() { Target = target, Load = LoadOp.Clear, Store = StoreOp.Store };
    }
}
