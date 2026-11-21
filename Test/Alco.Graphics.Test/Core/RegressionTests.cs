// Focused regressions for resource dispatch, command recording, shader math/ABI, API defaults, GPU rendering/copies, and runtime Slang compilation.

using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Alco.Graphics;
using Alco.Graphics.LowLevel;

internal static unsafe class RegressionTests
{
    private const string ComputeSource = "[shader(\"compute\")] [numthreads(1,1,1)] void computeMain() {}";
    private const string RasterSource = """
        [shader("vertex")]
        float4 vertexMain(uint vertexId : SV_VertexID) : SV_Position
        {
            uint vertex = vertexId % 3;
            if (vertexId >= 3) vertex = 2 - vertex;
            return float4(float2((vertex << 1) & 2, vertex & 2) * 2.0 - 1.0, 0.5, 1.0);
        }
        [shader("pixel")]
        float4 pixelMain(float4 position : SV_Position) : SV_Target0 { return float4(position.zzz, 1.0); }
        """;
    private const string FacingSource = """
        struct FacingVertex
        {
            nointerpolation float4 Colors[2] : COLOR0;
            nointerpolation float4 Tint : COLOR2;
            float4 Position : SV_Position;
        };
        FacingVertex makeFacingVertex(uint vertex)
        {
            FacingVertex output;
            output.Colors[0] = float4(0.25, 0.125, 0.5, 0.375);
            output.Colors[1] = float4(0.625, 0.625, 0.375, 0.375);
            output.Tint = float4(0.125, 0.25, 0.125, 0.25);
            output.Position = float4(float2((vertex << 1) & 2, vertex & 2) * 2.0 - 1.0, 0.5, 1.0);
            return output;
        }
        [shader("vertex")]
        FacingVertex vertexMain(uint vertexId : SV_VertexID) { return makeFacingVertex(vertexId); }
        [shader("vertex")]
        FacingVertex reverseVertexMain(uint vertexId : SV_VertexID) { return makeFacingVertex(2 - vertexId); }
        [shader("pixel")]
        float4 facingMain(FacingVertex input, bool front : SV_IsFrontFace) : SV_Target0
        {
            float4 color = input.Colors[0] + input.Colors[1] + input.Tint;
            if (!front) color.r = 0.0;
            return color;
        }
        [shader("pixel")]
        float4 scalarFacingMain(nointerpolation float4 color0 : COLOR0, nointerpolation float4 color1 : COLOR1,
            nointerpolation float4 tint : COLOR2, bool front : SV_IsFrontFace) : SV_Target0
        {
            float4 color = color0 + color1 + tint;
            if (!front) color.r = 0.0;
            return color;
        }
        """;
    private const string CoordinateSource = """
        import Core;
        [GPURoot]
        struct CoordinateData
        {
            float Depth;
            uint TextureIndex;
            uint SamplerIndex;
            uint Padding;
        };
        struct CoordinateVertex
        {
            float4 Position : SV_Position;
            float2 UV : TEXCOORD0;
            float NdcY : TEXCOORD1;
        };
        CoordinateVertex makeCoordinateVertex(uint vertex)
        {
            CoordinateData root = gpu_load_root<CoordinateData>();
            float2 position = vertex == 0 ? float2(-0.75, 0.875) : vertex == 1 ? float2(0.75, 0.875) : float2(-0.75, 0.125);
            CoordinateVertex output;
            output.Position = float4(position, root.Depth, 1.0);
            output.UV = position * float2(0.5, -0.5) + 0.5;
            output.NdcY = position.y;
            return output;
        }
        [shader("vertex")]
        CoordinateVertex vertexMain(uint vertex : SV_VertexID) { return makeCoordinateVertex(vertex); }
        [shader("mesh")]
        [outputtopology("triangle")]
        [numthreads(32,1,1)]
        void meshMain(uint lane : SV_GroupIndex, out vertices CoordinateVertex vertices[3], out indices uint3 triangles[1])
        {
            SetMeshOutputCounts(3, 1);
            if (lane < 3) vertices[lane] = makeCoordinateVertex(lane);
            if (lane == 0) triangles[0] = uint3(0, 1, 2);
        }
        [shader("pixel")]
        float4 pixelMain(CoordinateVertex input) : SV_Target0
        {
            float v = ddy(input.NdcY) < 0.0 && ddy(input.UV.y) > 0.0 ? input.UV.y : -1.0;
            return float4(input.Position.xyz, v);
        }
        [shader("vertex")]
        CoordinateVertex sampleVertexMain(uint vertex : SV_VertexID)
        {
            float2 position = float2((vertex << 1) & 2, vertex & 2) * 2.0 - 1.0;
            CoordinateVertex output;
            output.Position = float4(position, 0.5, 1.0);
            output.UV = position * float2(0.5, -0.5) + 0.5;
            output.NdcY = position.y;
            return output;
        }
        [shader("pixel")]
        float4 sampleMain(CoordinateVertex input) : SV_Target0
        {
            CoordinateData root = gpu_load_root<CoordinateData>();
            Texture2D<float4> texture = gpu_texture<Texture2D<float4>>(root.TextureIndex);
            SamplerState sampler = gpu_sampler(root.SamplerIndex);
            return texture.SampleLevel(sampler, input.UV, 0);
        }
        """;
    private const string TopologySource = """
        import Core;
        [GPURoot]
        struct TopologyData
        {
            GPUBufferView Vertices;
            uint VertexCount;
            uint Padding;
        };
        struct VertexOutput
        {
            float4 Position : SV_Position;
            float PointSize : SV_PointSize;
        };
        [shader("vertex")]
        VertexOutput vertexMain(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID
        #if ALCO_GRAPHICS_METAL
            , uint baseVertex : SV_StartVertexLocation, uint baseInstance : SV_StartInstanceLocation
        #endif
        )
        {
            TopologyData root = gpu_load_root<TopologyData>();
            VertexOutput output;
            float2 position = float2(3.0, 3.0);
        #if ALCO_GRAPHICS_METAL
            if (baseVertex != 3 || baseInstance != 5) vertexId = root.VertexCount;
        #endif
            if (vertexId < root.VertexCount && instanceId == 0) position = gpu_load<float2>(root.Vertices, vertexId);
            output.Position = float4(position, 0.5, 1.0);
            output.PointSize = 1.0;
            return output;
        }
        [shader("vertex")]
        float4 plainVertexMain(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID
        #if ALCO_GRAPHICS_METAL
            , uint baseVertex : SV_StartVertexLocation, uint baseInstance : SV_StartInstanceLocation
        #endif
        ) : SV_Position
        {
            TopologyData root = gpu_load_root<TopologyData>();
            float2 position = float2(3.0, 3.0);
        #if ALCO_GRAPHICS_METAL
            if (baseVertex != 3 || baseInstance != 5) vertexId = root.VertexCount;
        #endif
            if (vertexId < root.VertexCount && instanceId == 0) position = gpu_load<float2>(root.Vertices, vertexId);
            return float4(position, 0.5, 1.0);
        }
        [shader("pixel")]
        float4 pixelMain() : SV_Target0 { return float4(1.0, 1.0, 1.0, 1.0); }
        """;
    private const string StencilSource = """
        import Core;
        [GPURoot]
        struct StencilData
        {
            GPUBufferView Output;
            uint TextureIndex;
            uint Padding;
        };
        [shader("compute")]
        [numthreads(4,4,1)]
        void computeMain(uint3 thread : SV_DispatchThreadID)
        {
            StencilData root = gpu_load_root<StencilData>();
            Texture2D<uint> stencil = gpu_texture<Texture2D<uint>>(root.TextureIndex);
            gpu_store<uint>(root.Output, 1 + thread.y * 4 + thread.x, stencil.Load(int3(thread.xy, 0)));
        }
        """;
    private const string MeshSource = """
        import Core;
        [GPURoot]
        struct MeshData
        {
            uint Seed;
            uint Padding0;
            uint Padding1;
            uint Padding2;
        };
        struct MeshVertex
        {
            float4 Position : SV_Position;
            float4 Color : COLOR0;
        };
        struct MeshPayload { uint Value; };
        MeshVertex makeVertex(uint vertex, uint value)
        {
            MeshVertex output;
            output.Position = float4(float2((vertex << 1) & 2, vertex & 2) * 2.0 - 1.0, 0.5, 1.0);
            output.Color = float4(float(value), 7.0, 13.0, 17.0) / 255.0;
            return output;
        }
        [shader("amplification")]
        [numthreads(1,1,1)]
        void taskMain()
        {
            MeshData root = gpu_load_root<MeshData>();
            MeshPayload payload;
            payload.Value = root.Seed + 17;
            DispatchMesh(1, 1, 1, payload);
        }
        [shader("mesh")]
        [outputtopology("triangle")]
        [numthreads(32,1,1)]
        void meshMain(uint lane : SV_GroupIndex, out vertices MeshVertex vertices[3], out indices uint3 triangles[1])
        {
            MeshData root = gpu_load_root<MeshData>();
            SetMeshOutputCounts(3, 1);
            if (lane < 3) vertices[lane] = makeVertex(lane, root.Seed);
            if (lane == 0) triangles[0] = uint3(0, 1, 2);
        }
        [shader("mesh")]
        [outputtopology("triangle")]
        [numthreads(32,1,1)]
        void taskMeshMain(uint lane : SV_GroupIndex, in payload MeshPayload payload,
            out vertices MeshVertex vertices[3], out indices uint3 triangles[1])
        {
            SetMeshOutputCounts(3, 1);
            if (lane < 3) vertices[lane] = makeVertex(lane, payload.Value);
            if (lane == 0) triangles[0] = uint3(0, 1, 2);
        }
        [shader("pixel")]
        float4 pixelMain(MeshVertex input) : SV_Target0 { return input.Color; }
        """;
    private const string BcSource = """
        import Core;
        [GPURoot]
        struct BcData
        {
            GPUBufferView Output;
            uint Mip;
            uint Slice;
        };
        [shader("compute")]
        [numthreads(1,1,1)]
        void computeMain()
        {
            BcData root = gpu_load_root<BcData>();
            for (uint view = 0; view < 2; ++view)
            {
                Texture2DArray<float4> texture = gpu_texture<Texture2DArray<float4>>(view);
                gpu_store<float4>(root.Output, view * 2, texture.Load(int4(0, 0, root.Slice, root.Mip)));
                gpu_store<float4>(root.Output, view * 2 + 1, texture.Load(int4(1, 0, root.Slice, root.Mip)));
            }
        }
        """;
    private const string RootDeclarations = """
        import Core;
        struct RootData
        {
            GPUBufferView Output;
            uint OutputIndex;
            uint Seed;
            uint Values[60];
        };
        """;
    private const string ConsumeSource = """
        import Core;
        [GPURoot]
        struct ConsumeData
        {
            GPUBufferView Output;
            uint OutputIndex;
            uint Seed;
            uint Values[60];
        };
        [shader("compute")]
        [numthreads(64,1,1)]
        void computeMain(uint lane : SV_GroupIndex)
        {
            ConsumeData root = gpu_load_root<ConsumeData>();
            uint value = root.Seed + lane;
            for (uint index = 0; index < 60; ++index) value = (value * 33) ^ root.Values[index];
            gpu_store<uint>(root.Output, root.OutputIndex + lane, value);
        }
        """;
    private const string GenerateSource = RootDeclarations + """

        [GPURoot]
        struct GenerateData
        {
            GPUBufferView Destination;
            GPUBufferView Output;
            GPUBufferView Arguments;
            uint FirstIndex;
            uint Seed;
        };
        [shader("compute")]
        [numthreads(1,1,1)]
        void computeMain(uint3 group : SV_GroupID)
        {
            GenerateData root = gpu_load_root<GenerateData>();
            uint seed = root.Seed + group.x;
            RootData generated;
            generated.Output = root.Output;
            generated.OutputIndex = (root.FirstIndex + group.x) * 64;
            generated.Seed = seed;
            for (uint index = 0; index < 60; ++index) generated.Values[index] = seed * 7 + index;
            gpu_store<RootData>(root.Destination, group.x, generated);
            if (group.x == 0)
            {
                gpu_store<uint>(root.Arguments, 0, 1);
                gpu_store<uint>(root.Arguments, 1, 1);
                gpu_store<uint>(root.Arguments, 2, 1);
            }
        }
        """;

    private const string BufferViewSource = """
        import Core;
        struct BufferViewEntry
        {
            GPUBufferView Source;
            GPUBufferView Destination;
        };
        [GPURoot]
        struct BufferViewData
        {
            GPUBufferView Entries;
            uint EntryCount;
            uint Seed;
            uint TextureIndex;
            uint SamplerIndex;
            uint Padding0;
            uint Padding1;
        };
        [shader("compute")]
        [numthreads(64,1,1)]
        void computeMain(uint lane : SV_GroupIndex)
        {
            BufferViewData root = gpu_load_root<BufferViewData>();
            uint entryIndex = (lane * 3 + 1) % root.EntryCount;
            BufferViewEntry entry = gpu_load<BufferViewEntry>(root.Entries, entryIndex);
            uint value = gpu_load<uint>(gpu_offset_bytes(entry.Source, 4), lane / root.EntryCount);
            Texture2D<float4> texture = gpu_texture<Texture2D<float4>>(root.TextureIndex);
            SamplerState sampler = gpu_sampler(root.SamplerIndex);
            uint sampled = uint(round(texture.Load(int3(0, 0, 0)).r * 255.0)) +
                uint(round(texture.SampleLevel(sampler, float2(0.5, 0.5), 0).g * 255.0));
            gpu_store<uint>(gpu_offset_bytes(entry.Destination, 8), lane / root.EntryCount, (value ^ (root.Seed + lane)) + sampled);
            uint valuesPerEntry = 64 / root.EntryCount;
            uint previous = gpu_atomic_add_u32(gpu_offset_bytes(entry.Destination, (valuesPerEntry + 3) * 4), 3);
            gpu_store<uint>(gpu_offset_bytes(entry.Destination, (valuesPerEntry + 5) * 4), lane / root.EntryCount, previous);
        }
        """;

    internal static void RunCpu(Action<bool, string> check)
    {
        DeviceDesc device = new();
        check(device.Backend == GraphicsBackend.Auto && default(DeviceDesc).Backend == GraphicsBackend.Auto,
            "both new and zero-initialized device descriptions select the backend automatically");
        check(device.Headless && device.DesiredQueueCount == 1 && device.DesiredComputeQueueCount == 0 &&
            device.DesiredCopyQueueCount == 0 && device.TimestampQueryCount == 256 &&
            device.PipelineCacheDirectory == null && default(DeviceDesc).PipelineCacheDirectory == null,
            "new device descriptions default to headless without changing queue, timestamp, or pipeline cache defaults");
        RunInvalidBackendSelections(check);
        SwapchainDesc swapchain = new();
        check(swapchain.Surface.Equals(default(SurfaceSource)) && swapchain.Extent.X == 0 && swapchain.Extent.Y == 0 &&
            swapchain.Format == Format.Undefined && swapchain.ColorSpace == ColorSpace.Srgb && swapchain.DesiredImageCount == 2 &&
            swapchain.VSync && !swapchain.SteamOverlayCompatibility,
            "new swapchain descriptions default to an empty surface/extent, automatic sRGB format, two images and VSync");
        ReadOnlySpan<SurfaceSource> surfaces =
        [
            SurfaceSource.FromWin32(1, 2), SurfaceSource.FromMetalLayer(1), SurfaceSource.FromXlib(1, 2),
            SurfaceSource.FromXcb(1, 2), SurfaceSource.FromWayland(1, 2),
        ];
        bool sourcesDistinct = true;
        for (int index = 0; index < surfaces.Length; ++index)
        {
            sourcesDistinct &= !surfaces[index].Equals(default(SurfaceSource));
            for (int previous = 0; previous < index; ++previous)
                sourcesDistinct &= !surfaces[index].Equals(surfaces[previous]);
        }
        check(sourcesDistinct && SurfaceSource.FromWin32(1, 2).Equals(SurfaceSource.FromWin32(1, 2)),
            "native surface factories preserve platform identity and stable handle values without touching native windows");
        DepthStencilState depthStencil = new() { StencilTest = true };
        check(depthStencil.Front.Compare == CompareOp.Always && depthStencil.Back.Compare == CompareOp.Always,
            "new depth/stencil state preserves both nested stencil-face defaults");
        ColorTargetDesc target = new();
        target.Blend.Enabled = true;
        check(target.Blend.Color.Source == BlendFactor.One && target.Blend.Alpha.Source == BlendFactor.One &&
            target.Blend.Color.Destination == BlendFactor.Zero && target.Blend.Alpha.Destination == BlendFactor.Zero,
            "new color target preserves nested source-one/destination-zero blend defaults");
        RenderingDesc rendering = new();
        rendering.Depth.Load = LoadOp.Clear;
        check(rendering.Depth.Clear == 1.0f, "new rendering description preserves its nested depth-clear default");
        GraphicsPipelineDesc graphics = new();
        check(default(GraphicsPipelineDesc).Topology == PrimitiveTopology.TriangleList && graphics.Topology == PrimitiveTopology.TriangleList &&
            (byte)PrimitiveTopology.TriangleList == 0, "default and new graphics descriptions preserve zero-valued triangle-list topology");
        MeshPipelineDesc mesh = new();
        check(graphics.Vertex.EntryPoint == "main" && graphics.Pixel.EntryPoint == "main" &&
            mesh.Task.EntryPoint == "main" && mesh.Mesh.ThreadgroupSize.X == 1 && mesh.Pixel.EntryPoint == "main",
            "new graphics/mesh descriptions preserve nested shader-stage defaults");
        check(sizeof(GPUBufferView) == 8 && (int)Marshal.OffsetOf<GPUBufferView>(nameof(GPUBufferView.Index)) == 0 &&
            (int)Marshal.OffsetOf<GPUBufferView>(nameof(GPUBufferView.Offset)) == 4,
            "GPUBufferView retains an 8-byte ABI with a 32-bit index followed by a 32-bit byte offset");
        GPUBufferView view = new() { Index = 0x13579bdf, Offset = 0x2468ac00 };
        GPUBufferView shiftedView = view.OffsetBy(28).OffsetBy(36);
        ReadOnlySpan<uint> viewWords = MemoryMarshal.Cast<GPUBufferView, uint>(new ReadOnlySpan<GPUBufferView>(in view));
        check(viewWords[0] == 0x13579bdf && viewWords[1] == 0x2468ac00 && shiftedView.Index == view.Index &&
            shiftedView.Offset == view.Offset + 64 && view.Offset == 0x2468ac00,
            "buffer-view serialization and chained OffsetBy preserve the original index and source value");
        check(default(GPUBufferView).Index == 0 && default(GPUBufferView).Offset == 0,
            "the default GPUBufferView is an empty index/offset pair");
        uint* cached = stackalloc uint[4];
        GPUAllocation<uint> cpuAllocation = new() { Cpu = cached, SizeBytes = 4 * sizeof(uint) };
        check(cpuAllocation.CpuSpan.Length == 4, "CPU allocation spans retain their element count");
        GPUHeap emptyGpuHeap = default;
        emptyGpuHeap.Destroy();
        check(emptyGpuHeap.Owner == null && emptyGpuHeap.Range.Cpu == null && emptyGpuHeap.Range.Buffer == null && emptyGpuHeap.Range.SizeBytes == 0,
            "default GPU heap destruction is a no-op");
        GPUTextureHeap emptyTextureHeap = default;
        emptyTextureHeap.Destroy();
        check(emptyTextureHeap.Owner == null && emptyTextureHeap.Size == 0, "default texture heap destruction is a no-op");
        TimelinePoint emptyPoint = default;
        emptyPoint.Wait();
        emptyPoint.Value = 3;
        emptyPoint.Wait();
        check(emptyPoint.Semaphore == null && emptyPoint.Value == 3, "null timeline point waits are no-ops at zero and nonzero values");
        GPUException failure = new(Error.DriverError, "vkCreateImage: deterministic failure 鈥?娴嬭瘯");
        check(typeof(GPUException).IsPublic && typeof(GPUException).IsSealed && failure is Exception,
            "GPUException is a public sealed exception");
        check(failure.Error == Error.DriverError && failure.Message == "vkCreateImage: deterministic failure 鈥?娴嬭瘯",
            "GPUException preserves its error code and diagnostic message verbatim");
        GPUException unsupported = new(Error.Unsupported, string.Empty);
        check(unsupported.Error == Error.Unsupported && unsupported.Message == string.Empty,
            "GPUException preserves a different error code and an empty message");
        RunResourceShapes(check);
        RunMathLayouts(check);
        RunTextureUploadSizes(check);
    }

    private static void RunTextureUploadSizes(Action<bool, string> check)
    {
        Console.WriteLine("Texture upload sizes:");
        TextureDesc desc = new();
        check(TextureUpload.GetSizeBytes(desc) == 4, "default texture upload size is one tightly packed RGBA texel");
        desc = new TextureDesc { Type = TextureType.TwoDArray, Extent = new Extent3D(64, 32, 1), MipLevels = 2, LayerCount = 4 };
        TextureCopyDesc copy = new()
        {
            MipLevel = 1, BaseSlice = 1, Offset = new Extent3D(4, 3, 0), RowPitchBytes = 128, SlicePitchBytes = 1792,
        };
        check(TextureUpload.GetSizeBytes(desc, copy) == 4368,
            "array upload sizing uses the remaining mip extent and layers without source padding");
        check(copy.Extent.X == 0 && copy.SliceCount == 0 && copy.RowPitchBytes == 128 && copy.SlicePitchBytes == 1792,
            "upload sizing leaves the caller's region and pitches unchanged");
        copy.Extent = new Extent3D(17, 5, 1);
        copy.SliceCount = 2;
        check(TextureUpload.GetSizeBytes(desc, copy) == 680, "array upload sizing respects explicit extent and slice count");
        desc = new TextureDesc { Type = TextureType.ThreeD, Extent = new Extent3D(12, 10, 8), MipLevels = 4 };
        copy = new TextureCopyDesc { MipLevel = 1, Offset = new Extent3D(1, 2, 1), RowPitchBytes = 32, SlicePitchBytes = 128 };
        check(TextureUpload.GetSizeBytes(desc, copy) == 180, "3D upload sizing counts remaining depth planes rather than array layers");
        copy.Extent = new Extent3D(3, 2, 2);
        check(TextureUpload.GetSizeBytes(desc, copy) == 48, "3D upload sizing respects an explicit partial mip extent");
        check(TextureUpload.GetSizeBytes(desc, new TextureCopyDesc { MipLevel = 3 }) == 4,
            "upload sizing clamps small mip dimensions to one texel");
        desc = new TextureDesc
        {
            Type = TextureType.TwoDArray, Extent = new Extent3D(60, 52, 1), MipLevels = 2, LayerCount = 4, Format = Format.Bc1Unorm,
        };
        copy = new TextureCopyDesc { MipLevel = 1, BaseSlice = 1, Offset = new Extent3D(4, 4, 0), RowPitchBytes = 64, SlicePitchBytes = 512 };
        check(TextureUpload.GetSizeBytes(desc, copy) == 1008, "compressed upload sizing rounds partial mip-edge blocks up in each plane");
        desc = new TextureDesc
        {
            Type = TextureType.TwoDArray, Extent = new Extent3D(8, 8, 1), MipLevels = 2, LayerCount = 3, Format = Format.D32Float,
        };
        check(TextureUpload.GetSizeBytes(desc, new TextureCopyDesc { MipLevel = 1, BaseSlice = 1 }) == 128,
            "depth upload sizing counts whole selected mip subresources");
        desc = new TextureDesc { Extent = new Extent3D(4, 3, 1) };
        Span<byte> cached = stackalloc byte[64];
        cached.Fill(0xa5);
        bool rejected = false;
        fixed (byte* output = cached)
        {
            GPUAllocation<byte> staging = new() { Cpu = output, SizeBytes = 47 };
            try
            {
                TextureUpload.UploadTexture(null!, staging, null!, desc, new byte[48]);
            }
            catch (InvalidOperationException exception)
            {
                rejected = exception.Message.Contains("48", StringComparison.Ordinal) && exception.Message.Contains("47", StringComparison.Ordinal);
            }
        }
        bool unchanged = true;
        foreach (byte value in cached) unchanged &= value == 0xa5;
        check(rejected && unchanged, "insufficient direct-upload staging throws before CPU copying or accessing command resources");
    }

    private static void RunResourceShapes(Action<bool, string> check)
    {
        ReadOnlySpan<Type> resources =
        [
            typeof(GPUDevice), typeof(GPUSwapchain), typeof(GPUCommandPool), typeof(GPUCommandBuffer), typeof(GPUTexture), typeof(GPURenderView),
            typeof(GPUPipeline), typeof(GPUTimelineSemaphore), typeof(GPUHeapOwner), typeof(GPUTextureHeapOwner), typeof(GPUTextureDescriptorHeap),
            typeof(GPUSamplerDescriptorHeap),
        ];
        foreach (Type resource in resources)
        {
            Type[] parameters = resource == typeof(GPUDevice) ? [typeof(GraphicsBackend)] : Type.EmptyTypes;
            ConstructorInfo? constructor = resource.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, parameters, null);
            check(resource.IsPublic && resource.IsAbstract && !resource.IsSealed && resource.BaseType == typeof(object) &&
                constructor?.IsFamilyAndAssembly == true, $"{resource.Name} is a public abstract resource with a private-protected constructor");
            Type? vulkan = null;
            Type? metal = null;
            Type? d3d12 = null;
            foreach (Type candidate in resource.Assembly.GetTypes())
            {
                if (!candidate.IsSubclassOf(resource) || !candidate.IsNotPublic || !candidate.IsSealed) continue;
                if (candidate.Name.StartsWith("Vulkan", StringComparison.Ordinal)) vulkan = candidate;
                else if (candidate.Name.StartsWith("Metal", StringComparison.Ordinal)) metal = candidate;
                else if (candidate.BaseType == resource && candidate.Name.StartsWith("D3D12", StringComparison.Ordinal)) d3d12 = candidate;
            }
            check(vulkan is not null && metal is not null && d3d12 is not null,
                $"{resource.Name} has sealed nonpublic Vulkan, Metal and direct D3D12 implementations");
        }
    }

    private static void RunMathLayouts(Action<bool, string> check)
    {
        Console.WriteLine("Numerics shader ABI:");
        check(sizeof(Vector2) == 8 && (int)Marshal.OffsetOf<Vector2>(nameof(Vector2.X)) == 0 &&
            (int)Marshal.OffsetOf<Vector2>(nameof(Vector2.Y)) == 4, "Vector2 retains the 8-byte float2 layout");
        check(sizeof(Vector3) == 12 && (int)Marshal.OffsetOf<Vector3>(nameof(Vector3.X)) == 0 &&
            (int)Marshal.OffsetOf<Vector3>(nameof(Vector3.Y)) == 4 && (int)Marshal.OffsetOf<Vector3>(nameof(Vector3.Z)) == 8,
            "Vector3 retains the unpadded 12-byte float3 layout");
        check(sizeof(Vector4) == 16 && (int)Marshal.OffsetOf<Vector4>(nameof(Vector4.X)) == 0 &&
            (int)Marshal.OffsetOf<Vector4>(nameof(Vector4.Y)) == 4 && (int)Marshal.OffsetOf<Vector4>(nameof(Vector4.Z)) == 8 &&
            (int)Marshal.OffsetOf<Vector4>(nameof(Vector4.W)) == 12, "Vector4 retains the 16-byte float4 layout");
        check(sizeof(Matrix4x4) == 64 &&
            (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M11)) == 0 && (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M12)) == 4 &&
            (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M13)) == 8 && (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M14)) == 12 &&
            (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M21)) == 16 && (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M22)) == 20 &&
            (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M23)) == 24 && (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M24)) == 28 &&
            (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M31)) == 32 && (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M32)) == 36 &&
            (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M33)) == 40 && (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M34)) == 44 &&
            (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M41)) == 48 && (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M42)) == 52 &&
            (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M43)) == 56 && (int)Marshal.OffsetOf<Matrix4x4>(nameof(Matrix4x4.M44)) == 60,
            "Matrix4x4 stores all 16 floats in 64-byte row-major order");
    }

    // Must run before the process's first graphics device is created.
    internal static GPUDevice? CreateDevice(string? requestedBackend, Action<bool, string> check)
    {
        const int count = 8;
        using Barrier gate = new(count);
        GPUDevice?[] results = new GPUDevice?[count];
        GPUException?[] failures = new GPUException?[count];
        Thread[] threads = new Thread[count];
        for (int index = 0; index < count; ++index)
        {
            int worker = index;
            threads[index] = new Thread(() =>
            {
                gate.SignalAndWait();
                try
                {
                    results[worker] = Gpu.CreateDevice(new DeviceDesc());
                }
                catch (GPUException exception)
                {
                    failures[worker] = exception;
                }
            });
            threads[index].Start();
        }
        for (int index = 0; index < count; ++index) threads[index].Join();
        int retained = -1;
        bool allUnsupported = true;
        for (int index = 0; index < count; ++index)
        {
            if (failures[index]?.Error != Error.Unsupported) allUnsupported = false;
            if (retained < 0 && results[index] != null) retained = index;
        }
        if (retained >= 0 || !allUnsupported || requestedBackend is "dx12" or "vulkan-compact")
        {
            for (int index = 0; index < count; ++index)
                check(results[index] != null && failures[index] == null,
                    $"cold concurrent CreateDevice worker {index} returns a nonnull device ({failures[index]?.Message ?? "success"})");
        }
        else Console.WriteLine("  [SKIP] all eight cold device creations report an unsupported environment");
        for (int index = 0; index < count; ++index)
        {
            GPUDevice? device = results[index];
            if (index == retained || device == null) continue;
            device.WaitIdle();
            device.Destroy();
        }
        return retained >= 0 ? results[retained] : null;
    }

    private static void RunInvalidBackendSelections(Action<bool, string> check)
    {
        check((byte)GraphicsBackend.Metal == 4 && (byte)GraphicsBackend.VulkanCompact == 5 && !Enum.IsDefined((GraphicsBackend)3),
            "backend value 3 is undefined while Metal and VulkanCompact retain values 4 and 5");
        check(RejectsBackend((GraphicsBackend)3, "Invalid graphics backend"), "CreateDevice rejects backend value 3 without selecting a backend");

        const string variable = "ALCO_GRAPHICS_BACKEND";
        string? setting = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "dx12-compat");
            check(RejectsBackend(GraphicsBackend.Auto, $"Invalid {variable}"), "Auto rejects the former dx12-compat override");
        }
        finally { Environment.SetEnvironmentVariable(variable, setting); }
    }

    private static bool RejectsBackend(GraphicsBackend backend, string diagnostic)
    {
        try
        {
            GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = backend });
            device.WaitIdle();
            device.Destroy();
            return false;
        }
        catch (GPUException exception)
        {
            return exception.Error == Error.Unsupported && exception.Message.StartsWith(diagnostic, StringComparison.Ordinal);
        }
    }

    internal static void RunBackendSelection(GPUDevice automaticDevice, string? requestedBackend, Action<bool, string> check)
    {
        GraphicsBackend backend = automaticDevice.ActiveBackend;
        bool expectedBackend = requestedBackend switch
        {
            null when OperatingSystem.IsWindows() => backend == GraphicsBackend.Dx12,
            null when OperatingSystem.IsMacOS() => backend == GraphicsBackend.Metal,
            null or "vulkan" => backend == GraphicsBackend.Vulkan,
            "vulkan-compact" => backend == GraphicsBackend.VulkanCompact,
            "dx12" => backend == GraphicsBackend.Dx12,
            "metal" => backend == GraphicsBackend.Metal,
            _ => false,
        };
        check(expectedBackend, $"requested {requestedBackend ?? nameof(GraphicsBackend.Auto)} creates {backend}");
        const string variable = "ALCO_GRAPHICS_BACKEND";
        string? setting = Environment.GetEnvironmentVariable(variable);
        GPUDevice? explicitDevice = null;
        try
        {
            Environment.SetEnvironmentVariable(variable, "invalid-test-backend");
            explicitDevice = Gpu.CreateDevice(new DeviceDesc { Backend = backend });
            check(explicitDevice.ActiveBackend == backend, $"explicit {backend} selects the same backend profile without reading {variable}");
        }
        catch (GPUException exception)
        {
            check(false, $"explicit {backend} creation succeeds ({exception.Error}: {exception.Message})");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, setting);
            explicitDevice?.WaitIdle();
            explicitDevice?.Destroy();
        }
    }

    internal static ShaderTarget ShaderTarget(GPUDevice device) => ShaderCompiler.TargetForBackend(device.ActiveBackend);

    internal static void RunGpu(GPUDevice device, Action<bool, string> check)
    {
        ShaderTarget target = ShaderTarget(device);
        bool vulkan = device.GetType().FullName == "Alco.Graphics.LowLevel.VulkanDevice";
        Console.WriteLine($"Portable GPU regressions ({device.GetType().Name}, {target}):");
        RunRequiredFactories(device, check);
        RunGpuOnlyAllocators(device, check);
        if (OperatingSystem.IsMacOS() && device.GetType().FullName == "Alco.Graphics.LowLevel.MetalDevice") MetalPipelineCacheTests.Run(check);
        if (vulkan)
        {
            RunFactoryFailures(device, check);
            RunCommandBeginFailures(device, check);
            RunIndexBindings(device, check);
            RunUploadQueueFailure(device, check);
        }
        else
            Console.WriteLine("  [SKIP] Vulkan-native factory/recording/upload fault injection and index-binding interception require a VulkanDevice");
        RunTextureRegionCopies(device, check);
        RunPrimitiveTopologies(device, check);
        RunBufferViews(device, check);
        RunTimestamps(device, check);
        DeviceCaps caps = device.Caps;
        using ShaderCompiler compiler = new(device);
        CompiledShader raster = compiler.CompileSource("regression_raster", "regression_raster.slang", RasterSource,
            ShaderPipelineKind.Graphics);
        RunRasterDefaults(device, raster, check);
        RunRasterFacing(device, compiler, check);
        RunCoordinates(device, compiler, check);
        RunStencilSampling(device, compiler, check);
        if (device.ActiveBackend == GraphicsBackend.VulkanCompact)
            RunVulkanCompactRejections(device, check);
        else RunMeshDraws(device, compiler, check);
        CompiledShader consume = compiler.CompileSource("regression_consume", "regression_consume.slang", ConsumeSource,
            ShaderPipelineKind.Compute);
        CompiledShader generate = compiler.CompileSource("regression_generate", "regression_generate.slang", GenerateSource,
            ShaderPipelineKind.Compute);
        Console.WriteLine($"Root and texture-copy execution ({device.GetType().Name}):");
        RunRoots(device, consume, generate, check);
        check(ReferenceEquals(caps, device.Caps), "Caps retains its object identity across resource creation and GPU submissions");
        RunTextureCopies(device, check);
        if (vulkan)
        {
            FieldInfo addressCommands = device.GetType().GetField("AddressCommands", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bool originalAddressCommands = (bool)addressCommands.GetValue(device)!;
            try
            {
                addressCommands.SetValue(device, false);
                Console.WriteLine("Root and texture-copy execution (Vulkan forced ordinary buffer-plus-offset commands):");
                RunRoots(device, consume, generate, check);
                RunTextureCopies(device, check);
            }
            finally
            {
                addressCommands.SetValue(device, originalAddressCommands);
            }
        }
        CompiledShader bc = compiler.CompileSource("regression_bc", "regression_bc.slang", BcSource, ShaderPipelineKind.Compute);
        RunBcTextures(device, bc.GetStage(ShaderStageKind.Compute).Code.ToArray(), check);
        RunTextureRequirements(device, check, "baseline");
        RunTextureUploadTiles(device, check);
        RunTextureUploadCommands(device, check);
        GPUDevice? multipleQueues = null;
        try
        {
            multipleQueues = Gpu.CreateDevice(new DeviceDesc { Backend = device.ActiveBackend, DesiredComputeQueueCount = 1, DesiredCopyQueueCount = 1 });
            RunTextureRequirements(multipleQueues, check, "multiple queues");
            RunUploadQueueBatches(multipleQueues, check);
        }
        catch (GPUException exception) when (multipleQueues == null && exception.Error == Error.Unsupported)
        {
            Console.WriteLine("  [SKIP] dedicated compute/copy queues are unavailable");
        }
        finally
        {
            multipleQueues?.WaitIdle();
            multipleQueues?.Destroy();
        }
        if (vulkan)
        {
            CompiledShader empty = compiler.CompileSource("regression_long_entry", "regression_long_entry.slang", ComputeSource,
                ShaderPipelineKind.Compute);
            RunLongEntryPoint(device, empty.GetStage(ShaderStageKind.Compute).Code.ToArray(), check);
            RunQueueRecordings(device, check);
        }
        else
            Console.WriteLine("  [SKIP] Vulkan-native long SPIR-V entry-point editing and queue dispatch interception require a VulkanDevice");
    }

    // Cross-queue uploads through UploadQueue on the dedicated copy queue: initializations and copies batch per flush,
    // frame submissions on queue zero wait on the returned completion, and staging recycles across batches.
    private static void RunUploadQueueBatches(GPUDevice device, Action<bool, string> check)
    {
        Console.WriteLine("Cross-queue upload batches:");
        DeviceCaps caps = device.Caps;
        if (caps.CopyQueueCount == 0)
        {
            Console.WriteLine("  [SKIP] dedicated copy queue is unavailable");
            return;
        }
        uint copyQueueIndex = caps.GeneralQueueCount + caps.ComputeQueueCount;
        const uint extent = 64;
        const ulong byteCount = extent * extent * 4;
        TextureDesc desc = new()
        {
            Extent = new Extent3D(extent, extent, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled | TextureUsage.TransferDestination | TextureUsage.TransferSource,
        };
        UploadQueue uploads = new(device, byteCount, copyQueueIndex, 2);
        GPUTextureHeap textureHeap = default;
        GPUTexture? texture = null;
        GPUHeap readback = default;
        GPUHeap storage = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        byte[] expected = new byte[byteCount];
        try
        {
            textureHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(desc).Size);
            readback = device.CreateGPUHeap(byteCount, MemoryType.Readback);
            storage = device.CreateGPUHeap(byteCount, MemoryType.GpuOnly);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            texture = device.CreateTexture(desc, textureHeap, 0);
            uploads.InitializeTexture(texture);

            TimelinePoint completion = new() { Semaphore = timeline };
            for (int round = 0; round < 3; ++round)
            {
                bool uploadTexture = round != 1;
                for (int index = 0; index < expected.Length; ++index) expected[index] = (byte)(round * 37 + index);
                if (uploadTexture)
                    uploads.UploadTexture(texture, expected);
                else
                    uploads.UploadBuffer(storage.Range.GPURange, expected);
                TimelinePoint uploadPoint = uploads.Flush();
                check(uploadPoint.Value == (ulong)(round + 1), $"upload batch {round} signals a distinct completion value");

                GPUCommandBuffer commands = pool.BeginCommands();
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
                if (uploadTexture)
                    commands.CopyTextureToMemory(texture, readback.Range.GPURange);
                else
                    commands.CopyMemory(storage.Range.GPURange, readback.Range.GPURange);
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                commands.EndCommands();
                ++completion.Value;
                device.Submit(new SubmitDesc { Commands = [commands], Waits = [uploadPoint], Completion = completion });
                completion.Wait();
                check(readback.Range.CpuSpan.SequenceEqual(expected),
                    $"upload batch {round} ({(uploadTexture ? "texture" : "buffer")}) completes before the waiting queue-zero frame");
                pool.Reset();
            }
            uploads.Reclaim();
            UploadQueueStats stats = uploads.Stats;
            check(stats.PendingBatches == 0 && stats.Submissions == 3, "flushed upload batches submit and retire on the copy queue");
            check(uploads.Flush().Value == 3, "flushing without pending work returns the latest completion");
        }
        finally
        {
            uploads.Wait();
            uploads.Destroy();
            device.WaitIdle();
            texture?.Destroy();
            timeline?.Destroy();
            pool?.Destroy();
            storage.Destroy();
            readback.Destroy();
            textureHeap.Destroy();
        }
    }

    private static void RunTextureUploadCommands(GPUDevice device, Action<bool, string> check)
    {
        Console.WriteLine("Caller-recorded texture uploads:");
        const TextureUsage usage = TextureUsage.TransferSource | TextureUsage.TransferDestination;
        TextureDesc desc = new() { Extent = new Extent3D(7, 3, 1), Usage = usage };
        RunTextureUploadCommandCase(device, desc, default, check, "tightly packed direct upload with oversized staging");
        desc = new TextureDesc { Type = TextureType.ThreeD, Extent = new Extent3D(40, 16, 8), MipLevels = 2, Usage = usage };
        RunTextureUploadCommandCase(device, desc, new TextureCopyDesc
        {
            MipLevel = 1, Offset = new Extent3D(3, 1, 1), Extent = new Extent3D(17, 5, 2), RowPitchBytes = 80, SlicePitchBytes = 640,
        }, check, "pitched direct 3D partial mip without trailing padding", exactStaging: true);
        desc = new TextureDesc
        {
            Type = TextureType.TwoDArray, Extent = new Extent3D(64, 32, 1), MipLevels = 2, LayerCount = 4, Usage = usage,
        };
        RunTextureUploadCommandCase(device, desc, new TextureCopyDesc
        {
            MipLevel = 1, BaseSlice = 1, Offset = new Extent3D(4, 3, 0), RowPitchBytes = 128, SlicePitchBytes = 1792,
        }, check, "pitched direct array with default remaining extent and layers");
    }

    private static void RunTextureUploadCommandCase(GPUDevice device, in TextureDesc desc, TextureCopyDesc copy,
        Action<bool, string> check, string label, bool exactStaging = false)
    {
        uint width = copy.Extent.X != 0 ? copy.Extent.X : Math.Max(1u, desc.Extent.X >> (int)copy.MipLevel) - copy.Offset.X;
        uint height = copy.Extent.Y != 0 ? copy.Extent.Y : Math.Max(1u, desc.Extent.Y >> (int)copy.MipLevel) - copy.Offset.Y;
        uint depth = copy.Extent.Z != 0 ? copy.Extent.Z : Math.Max(1u, desc.Extent.Z >> (int)copy.MipLevel) - copy.Offset.Z;
        int planes = (int)(desc.Type == TextureType.ThreeD ? depth : copy.SliceCount != 0 ? copy.SliceCount : desc.LayerCount - copy.BaseSlice);
        int rowBytes = (int)width * 4;
        int rows = (int)height;
        int rowPitch = copy.RowPitchBytes != 0 ? (int)copy.RowPitchBytes : rowBytes;
        int slicePitch = copy.SlicePitchBytes != 0 ? (int)copy.SlicePitchBytes : rowPitch * rows;
        int payloadBytes = planes * rows * rowBytes;
        int guardedBytes = payloadBytes + 48;
        byte[] source = new byte[(planes - 1) * slicePitch + (rows - 1) * rowPitch + rowBytes];
        source.AsSpan().Fill(0xe7);
        byte[] expected = new byte[guardedBytes * 2];
        expected.AsSpan().Fill(0xa5);
        for (int plane = 0; plane < planes; ++plane)
            for (int row = 0; row < rows; ++row)
            {
                Span<byte> data = source.AsSpan(plane * slicePitch + row * rowPitch, rowBytes);
                for (int index = 0; index < data.Length; ++index) data[index] = unchecked((byte)(plane * 67 + row * 13 + index * 7));
                int offset = 16 + (plane * rows + row) * rowBytes;
                data.CopyTo(expected.AsSpan(offset, rowBytes));
                data.CopyTo(expected.AsSpan(guardedBytes + offset, rowBytes));
            }
        check(TextureUpload.GetSizeBytes(desc, copy) == (ulong)payloadBytes, $"{label}: size matches the tightly packed CPU payload");
        GPUTextureHeap heap = default;
        GPUTexture? texture = null;
        GPUHeap staging = default;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            heap = device.CreateTextureHeap(device.GetTextureSizeAlign(desc).Size);
            texture = device.CreateTexture(desc, heap, 0);
            staging = device.CreateGPUHeap((ulong)guardedBytes, MemoryType.CpuVisible);
            readback = device.CreateGPUHeap((ulong)expected.Length, MemoryType.Readback);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            NativeMemory.Fill(staging.Range.Cpu, (nuint)guardedBytes, 0xa5);
            NativeMemory.Fill(readback.Range.Cpu, (nuint)expected.Length, 0xa5);
            GPUAllocation<byte> allocation = new()
            {
                Cpu = staging.Range.Cpu + 16,
                Buffer = staging.Range.Buffer,
                Offset = staging.Range.Offset + 16,
                SizeBytes = (ulong)payloadBytes + (exactStaging ? 0ul : 16ul),
            };
            GPUCommandBuffer uploaded = pool.BeginCommands();
            uploaded.InitializeTexture(texture);
            TextureUpload.UploadTexture(uploaded, allocation, texture, desc, source, copy);
            source.AsSpan().Clear();
            uploaded.EndCommands();
            copy.RowPitchBytes = 0;
            copy.SlicePitchBytes = 0;
            // Address-based texture upload/readback must use separate command buffers on affected NVIDIA drivers.
            GPUCommandBuffer commands = pool.BeginCommands();
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            commands.CopyTextureToMemory(texture, readback.Range.GPURange.Slice(16, (ulong)payloadBytes), copy);
            commands.CopyMemory(staging.Range.GPURange, readback.Range.GPURange.Slice((ulong)guardedBytes, (ulong)guardedBytes));
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            commands.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [uploaded, commands], Completion = completion });
            completion.Wait();
            check(readback.Range.CpuSpan.SequenceEqual(expected), $"{label}: GPU texture and staging readback match immediately captured data and guards");
        }
        finally
        {
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            staging.Destroy();
            texture?.Destroy();
            heap.Destroy();
        }
    }

    private static void RunTextureUploadTiles(GPUDevice device, Action<bool, string> check)
    {
        Console.WriteLine("Fixed-capacity texture upload tiles:");
        const TextureUsage usage = TextureUsage.TransferSource | TextureUsage.TransferDestination;
        TextureDesc desc = new() { Extent = new Extent3D(73, 7, 1), Usage = usage };
        RunTextureUploadTileCase(device, desc, default, 64, check, "tightly packed rows wider than staging");
        desc = new TextureDesc { Type = TextureType.ThreeD, Extent = new Extent3D(5, 3, 7), Usage = usage };
        RunTextureUploadTileCase(device, desc, default, 128, check, "tightly packed 3D slices");
        desc = new TextureDesc { Type = TextureType.ThreeD, Extent = new Extent3D(40, 16, 8), MipLevels = 2, Usage = usage };
        RunTextureUploadTileCase(device, desc, new TextureCopyDesc
        {
            MipLevel = 1,
            Offset = new Extent3D(3, 1, 1),
            Extent = new Extent3D(17, 5, 2),
            RowPitchBytes = 80,
            SlicePitchBytes = 640,
        }, 64, check, "pitched 3D partial mip without trailing padding");
        desc = new TextureDesc
        {
            Type = TextureType.TwoDArray, Extent = new Extent3D(64, 32, 1), MipLevels = 2, LayerCount = 4, Usage = usage,
        };
        RunTextureUploadTileCase(device, desc, new TextureCopyDesc
        {
            MipLevel = 1, BaseSlice = 1, Offset = new Extent3D(4, 3, 0), SlicePitchBytes = 1792,
        }, 256, check, "slice-padded array with default remaining extent and layers");
        if (device.Caps.TextureCompressionBc)
        {
            desc = new TextureDesc
            {
                Type = TextureType.TwoDArray, Extent = new Extent3D(60, 52, 1), MipLevels = 2,
                LayerCount = 4, Format = Format.Bc1Unorm, Usage = usage,
            };
            RunTextureUploadTileCase(device, desc, new TextureCopyDesc
            {
                MipLevel = 1, BaseSlice = 1, Offset = new Extent3D(4, 4, 0), RowPitchBytes = 64, SlicePitchBytes = 512,
            }, 32, check, "pitched compressed array with partial mip-edge blocks");
        }
        else
            Console.WriteLine("  [SKIP] compressed upload tiles require BC support");
        desc = new TextureDesc
        {
            Type = TextureType.TwoDArray, Extent = new Extent3D(8, 8, 1), LayerCount = 3, Format = Format.D32Float, Usage = usage,
        };
        RunTextureUploadTileCase(device, desc, new TextureCopyDesc { RowPitchBytes = 40, SlicePitchBytes = 400 },
            256, check, "pitched depth array split only between whole subresources");
        RunTextureUploadCapacityFailure(device, check);
    }

    private static void RunTextureUploadTileCase(GPUDevice device, in TextureDesc desc, TextureCopyDesc copy, ulong capacity,
        Action<bool, string> check, string label)
    {
        TextureFormatInfo format = Gpu.GetTextureFormatInfo(desc.Format);
        uint width = copy.Extent.X != 0 ? copy.Extent.X : Math.Max(1u, desc.Extent.X >> (int)copy.MipLevel) - copy.Offset.X;
        uint height = copy.Extent.Y != 0 ? copy.Extent.Y : Math.Max(1u, desc.Extent.Y >> (int)copy.MipLevel) - copy.Offset.Y;
        uint depth = copy.Extent.Z != 0 ? copy.Extent.Z : Math.Max(1u, desc.Extent.Z >> (int)copy.MipLevel) - copy.Offset.Z;
        int planes = (int)(desc.Type == TextureType.ThreeD ? depth : copy.SliceCount != 0 ? copy.SliceCount : desc.LayerCount - copy.BaseSlice);
        int rowBytes = (int)((width + format.BlockExtent.X - 1) / format.BlockExtent.X * format.BytesPerBlock);
        int rows = (int)((height + format.BlockExtent.Y - 1) / format.BlockExtent.Y);
        int rowPitch = copy.RowPitchBytes != 0 ? (int)copy.RowPitchBytes : rowBytes;
        int slicePitch = copy.SlicePitchBytes != 0 ? (int)copy.SlicePitchBytes : rowPitch * rows;
        byte[] source = new byte[(planes - 1) * slicePitch + (rows - 1) * rowPitch + rowBytes];
        source.AsSpan().Fill(0xe7);
        byte[] expected = new byte[planes * rows * rowBytes + 32];
        expected.AsSpan().Fill(0xa5);
        for (int plane = 0; plane < planes; ++plane)
            for (int row = 0; row < rows; ++row)
            {
                Span<byte> data = source.AsSpan(plane * slicePitch + row * rowPitch, rowBytes);
                for (int index = 0; index < data.Length; ++index) data[index] = unchecked((byte)(plane * 67 + row * 13 + index * 7));
                if (desc.Format == Format.D32Float)
                {
                    Span<float> depths = MemoryMarshal.Cast<byte, float>(data);
                    for (int x = 0; x < depths.Length; ++x) depths[x] = ((plane * 31 + row * 7 + x) & 255) / 256.0f;
                }
                data.CopyTo(expected.AsSpan(16 + (plane * rows + row) * rowBytes, rowBytes));
            }
        check((ulong)source.Length > capacity && (ulong)(expected.Length - 32) > capacity, $"{label}: source and texel payload exceed the small queue");
        UploadQueue uploads = new(device, capacity);
        GPUTextureHeap heap = default;
        GPUTexture? texture = null;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            heap = device.CreateTextureHeap(device.GetTextureSizeAlign(desc).Size);
            texture = device.CreateTexture(desc, heap, 0);
            readback = device.CreateGPUHeap((ulong)expected.Length, MemoryType.Readback);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            NativeMemory.Fill(readback.Range.Cpu, (nuint)expected.Length, 0xa5);
            uploads.InitializeTexture(texture);
            TextureUpload.UploadTexture(uploads, texture, desc, source, copy);
            source.AsSpan().Clear();
            TimelinePoint uploaded = uploads.Flush();
            copy.RowPitchBytes = 0;
            copy.SlicePitchBytes = 0;
            GPUCommandBuffer commands = pool.BeginCommands();
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            commands.CopyTextureToMemory(texture, readback.Range.GPURange.Slice(16, (ulong)expected.Length - 32), copy);
            commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            commands.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [commands], Waits = [uploaded], Completion = completion });
            completion.Wait();
            check(readback.Range.CpuSpan.SequenceEqual(expected), $"{label}: GPU readback matches CPU data and preserves guards");
            uploads.Reclaim();
            UploadQueueStats stats = uploads.Stats;
            check(stats.Submissions > 1 && stats.PeakBytes <= capacity && stats.BytesInUse == 0 && stats.PendingBatches == 0,
                $"{label}: tiles submit and recycle within fixed staging capacity");
        }
        finally
        {
            uploads.Destroy();
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            texture?.Destroy();
            heap.Destroy();
        }
    }

    private static void RunTextureUploadCapacityFailure(GPUDevice device, Action<bool, string> check)
    {
        TextureDesc desc = new()
        {
            Type = TextureType.TwoDArray, Extent = new Extent3D(8, 8, 1), MipLevels = 2, LayerCount = 3,
            Format = Format.D32Float, Usage = TextureUsage.TransferDestination,
        };
        UploadQueue uploads = new(device, 128);
        GPUTextureHeap heap = default;
        GPUTexture? texture = null;
        try
        {
            heap = device.CreateTextureHeap(device.GetTextureSizeAlign(desc).Size);
            texture = device.CreateTexture(desc, heap, 0);
            uploads.InitializeTexture(texture);
            UploadQueueStats before = uploads.Stats;
            bool rejected = false;
            try
            {
                TextureUpload.UploadTexture(uploads, texture, desc, new byte[256], new TextureCopyDesc { BaseSlice = 1, SliceCount = 1 });
            }
            catch (InvalidOperationException exception)
            {
                rejected = exception.Message.Contains("capacity", StringComparison.OrdinalIgnoreCase) &&
                    exception.Message.Contains("256", StringComparison.Ordinal) && exception.Message.Contains("128", StringComparison.Ordinal);
            }
            UploadQueueStats after = uploads.Stats;
            check(rejected, "whole depth mip larger than staging throws a clear capacity exception");
            check(after.BytesInUse == before.BytesInUse && after.PeakBytes == before.PeakBytes && after.PendingOperations == before.PendingOperations &&
                after.Submissions == before.Submissions && after.PendingBatches == before.PendingBatches,
                "depth capacity rejection does not reserve staging, record commands, or flush existing initialization");
            TextureUpload.UploadTexture(uploads, texture, desc, new byte[64], new TextureCopyDesc { MipLevel = 1, BaseSlice = 1, SliceCount = 1 });
            uploads.Wait();
            check(uploads.Stats.BytesInUse == 0, "a smaller whole depth mip remains uploadable after capacity rejection");
        }
        finally
        {
            uploads.Destroy();
            device.WaitIdle();
            texture?.Destroy();
            heap.Destroy();
        }
    }

    private static void RunTimestamps(GPUDevice device, Action<bool, string> check)
    {
        if (device.Caps.TimestampPeriodNs == 0)
        {
            Console.WriteLine("  [SKIP] GPU timestamp profiling is unavailable");
            return;
        }
        check(float.IsFinite(device.Caps.TimestampPeriodNs) && device.Caps.TimestampPeriodNs > 0,
            "GPU timestamp ticks have a finite positive nanosecond period");
        GPUHeap upload = default;
        GPUHeap storage = default;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        Span<ulong> timestamps = stackalloc ulong[6];
        Span<byte> expected = stackalloc byte[64];
        try
        {
            upload = device.CreateGPUHeap(64);
            storage = device.CreateGPUHeap(64, MemoryType.GpuOnly);
            readback = device.CreateGPUHeap(64, MemoryType.Readback);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            GPUCommandBuffer? previous = null;
            for (int round = 0; round < 2; ++round)
            {
                timestamps.Fill(ulong.MaxValue);
                for (int index = 0; index < expected.Length; ++index) expected[index] = (byte)(round * 71 + index);
                expected.CopyTo(upload.Range.CpuSpan);
                GPUCommandBuffer first = pool.BeginCommands();
                if (round != 0) check(ReferenceEquals(previous, first), "timestamp command buffers are recycled after completed readback and pool reset");
                first.WriteTimestamp(timestamps.Slice(1, 1));
                first.CopyMemory(upload.Range.GPURange, storage.Range.GPURange);
                first.WriteTimestamp(timestamps.Slice(2, 1), Stage.Transfer);
                first.EndCommands();
                GPUCommandBuffer second = pool.BeginCommands();
                second.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
                second.WriteTimestamp(timestamps.Slice(3, 1), Stage.Transfer);
                second.CopyMemory(storage.Range.GPURange, readback.Range.GPURange);
                second.WriteTimestamp(timestamps.Slice(4, 1));
                second.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                second.EndCommands();
                TimelinePoint completion = new() { Semaphore = timeline, Value = (ulong)round + 1 };
                device.Submit(new SubmitDesc { Commands = [first, second], Completion = completion });
                completion.Wait();
                pool.ReadTimestamps();
                check(timestamps[0] == ulong.MaxValue && timestamps[5] == ulong.MaxValue && timestamps[1] != ulong.MaxValue &&
                    timestamps[2] != ulong.MaxValue && timestamps[3] != ulong.MaxValue && timestamps[4] != ulong.MaxValue &&
                    timestamps[1] <= timestamps[2] && timestamps[3] <= timestamps[4] && readback.Range.CpuSpan.SequenceEqual(expected),
                    $"direct timestamps from two completed command buffers preserve ordering, stationary destination guards and copy results in round {round}");
                previous = first;
                pool.Reset();
            }
        }
        finally
        {
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            storage.Destroy();
            upload.Destroy();
        }
    }

    private static void RunRequiredFactories(GPUDevice device, Action<bool, string> check)
    {
        Console.WriteLine("Required resource factories:");
        GPUHeap gpuHeap = default;
        GPUTextureHeap textureHeap = default;
        GPUTexture? texture = null;
        GPURenderView? view = null;
        GPUTextureDescriptorHeap? textures = null;
        GPUSamplerDescriptorHeap? samplers = null;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            TextureDesc desc = new() { Extent = new Extent3D(4, 4, 1), Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled };
            gpuHeap = device.CreateGPUHeap(256);
            textureHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(desc).Size);
            texture = device.CreateTexture(desc, textureHeap, 0);
            view = texture.CreateRenderView(new RenderViewDesc());
            textures = device.CreateTextureDescriptorHeap(1);
            samplers = device.CreateSamplerDescriptorHeap(1);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore(7);
            GPUCommandBuffer commands = pool.BeginCommands();
            commands.EndCommands();
            check(gpuHeap.Owner != null && gpuHeap.Range.Cpu != null && ReferenceEquals(gpuHeap.Range.Buffer, gpuHeap.Owner) && textureHeap.Owner != null &&
                texture != null && view != null && textures != null && samplers != null && pool != null && timeline != null && commands != null,
                "successful heap, texture, view, descriptor, pool, semaphore and recording factories return live nonnull resources");
        }
        finally
        {
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            samplers?.Destroy();
            textures?.Destroy();
            view?.Destroy();
            texture?.Destroy();
            textureHeap.Destroy();
            gpuHeap.Destroy();
        }
    }

    private static void RunGpuOnlyAllocators(GPUDevice device, Action<bool, string> check)
    {
        GPUHeap heap = device.CreateGPUHeap(4096, MemoryType.GpuOnly);
        try
        {
            GPUAllocation<byte> storage = new() { Buffer = heap.Range.Buffer, Offset = 128, SizeBytes = 512 };
            BumpAllocator bump = new(storage);
            GPUAllocation<byte> prefix = bump.Allocate(1);
            GPUAllocation<uint> ordinary = bump.Allocate<uint>(3);
            GPUAllocation<ulong> atomic = bump.AllocateAtomic<ulong>(3);
            check(heap.Range.Cpu == null && ReferenceEquals(heap.Owner, heap.Range.Buffer) && prefix.Cpu == null && ordinary.Cpu == null &&
                atomic.Cpu == null && ordinary.CpuSpan.IsEmpty && atomic.CpuSpan.IsEmpty && ReferenceEquals(ordinary.Buffer, heap.Owner) &&
                ReferenceEquals(atomic.Buffer, heap.Owner) && ordinary.Offset == 144 && ordinary.SizeBytes == 12 && atomic.Offset == 160 &&
                atomic.SizeBytes == 24 && ordinary.View.Index == heap.Range.View.Index && ordinary.View.Index != 0 && ordinary.View.Offset == 144 &&
                atomic.View.Index == heap.Range.View.Index && atomic.View.Offset == 160 && ordinary.GPURange.Offset == 144 &&
                ordinary.GPURange.Size == 12 && ReferenceEquals(ordinary.GPURange.Buffer, heap.Owner),
                "ordinary and atomic GPU-only bump suballocations preserve buffer identity and byte ranges while every CPU pointer stays null");
            bump.Reset();
            GPUAllocation<uint> reset = bump.AllocateAtomic<uint>();
            check(reset.Cpu == null && ReferenceEquals(reset.Buffer, heap.Owner) && reset.Offset == 128 && reset.SizeBytes == sizeof(uint),
                "GPU-only bump reset restores the storage offset without inventing a CPU mapping");
            HeapAllocator allocator = new(storage, 8);
            HeapAllocation<byte> first = allocator.Allocate(1);
            HeapAllocation<uint> second = allocator.Allocate<uint>(3);
            check(first.Range.Cpu == null && second.Range.Cpu == null && second.Range.CpuSpan.IsEmpty &&
                ReferenceEquals(first.Range.Buffer, heap.Owner) && ReferenceEquals(second.Range.Buffer, heap.Owner) && first.Range.Offset == 128 &&
                second.Range.Offset == 144 && second.Range.SizeBytes == 12 && second.Range.View.Index == heap.Range.View.Index &&
                second.Range.View.Offset == 144 && second.Range.GPURange.Offset == 144 && second.Range.GPURange.Size == 12,
                "typed GPU-only heap suballocations preserve owner and byte metadata at nonzero offsets without CPU pointer arithmetic");
            allocator.Free(first);
            HeapAllocation<byte> reused = allocator.Allocate(1);
            check(reused.Range.Cpu == null && ReferenceEquals(reused.Range.Buffer, first.Range.Buffer) && reused.Range.Offset == first.Range.Offset &&
                reused.Range.SizeBytes == first.Range.SizeBytes, "GPU-only heap reuse preserves the null CPU pointer and exact resource range");
            allocator.Reset();
            HeapAllocation<ulong> resetHeap = allocator.Allocate<ulong>(2);
            check(resetHeap.Range.Cpu == null && ReferenceEquals(resetHeap.Range.Buffer, heap.Owner) && resetHeap.Range.Offset == 128 &&
                resetHeap.Range.SizeBytes == 16, "GPU-only heap reset restores the backing range without inventing a CPU mapping");
        }
        finally
        {
            heap.Destroy();
        }
    }

    private static Vulkan.VkImage FailedImage;
    private static int BindFailureCount;
    private static int FailedImageDestroyCount;
    private static int FactoryDiagnosticCount;
    private static string FactoryDiagnostic = string.Empty;
    private static bool FactoryDiagnosticFatal;
    private static delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkImage, Vulkan.VkAllocationCallbacks*, void> OriginalDestroyImage;

    private static void RunFactoryFailures(GPUDevice device, Action<bool, string> check)
    {
        Vulkan.VkDeviceApi api = (Vulkan.VkDeviceApi)device.GetType().GetField("Api", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device)!;
        FieldInfo bind = typeof(Vulkan.VkDeviceApi).GetField(nameof(Vulkan.VkDeviceApi.vkBindImageMemory_ptr))!;
        FieldInfo destroy = typeof(Vulkan.VkDeviceApi).GetField(nameof(Vulkan.VkDeviceApi.vkDestroyImage_ptr))!;
        FieldInfo allocate = typeof(Vulkan.VkDeviceApi).GetField(nameof(Vulkan.VkDeviceApi.vkAllocateMemory_ptr))!;
        FieldInfo callback = typeof(Alco.Graphics.LowLevel.Gpu).GetField("ErrorCallback", BindingFlags.Static | BindingFlags.NonPublic)!;
        ErrorCallback? previousCallback = (ErrorCallback?)callback.GetValue(null);
        Vulkan.PFN_vkVoidFunction originalBind = api.vkBindImageMemory_ptr;
        Vulkan.PFN_vkVoidFunction originalDestroy = api.vkDestroyImage_ptr;
        Vulkan.PFN_vkVoidFunction originalAllocate = api.vkAllocateMemory_ptr;
        GPUTextureHeap heap = default;
        TextureAllocator? allocator = null;
        PlacedTexture placed = default;
        try
        {
            TextureDesc desc = new() { Extent = new Extent3D(4, 4, 1), Usage = TextureUsage.Sampled };
            heap = device.CreateTextureHeap(Math.Max(device.GetTextureSizeAlign(desc).Size, device.Caps.TextureHeapAlignment));
            allocator = new TextureAllocator(device, heap, 1);
            Gpu.SetErrorCallback(OnFactoryDiagnostic);
            OriginalDestroyImage = (delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkImage, Vulkan.VkAllocationCallbacks*, void>)originalDestroy.Value;
            FailedImage = default;
            BindFailureCount = 0;
            FailedImageDestroyCount = 0;
            FactoryDiagnosticCount = 0;
            FactoryDiagnostic = string.Empty;
            FactoryDiagnosticFatal = false;
            try
            {
                // This device is idle and used only by this thread while its dispatch entries are replaced.
                bind.SetValue(api, new Vulkan.PFN_vkVoidFunction((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkDevice,
                    Vulkan.VkImage, Vulkan.VkDeviceMemory, ulong, Vulkan.VkResult>)&FailBindImageMemory));
                destroy.SetValue(api, new Vulkan.PFN_vkVoidFunction((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkDevice,
                    Vulkan.VkImage, Vulkan.VkAllocationCallbacks*, void>)&CountDestroyImage));
                try
                {
                    placed = allocator.Allocate(desc);
                    check(false, "injected image binding failure throws GPUException instead of returning an empty placement");
                }
                catch (GPUException exception)
                {
                    check(exception.Error == Error.DriverError && exception.Message.Contains("vkBindImageMemory", StringComparison.Ordinal) &&
                        exception.Message.Contains("VK_ERROR_UNKNOWN", StringComparison.Ordinal),
                        "texture factory failure preserves its Vulkan operation, result and DriverError classification");
                    check(FactoryDiagnosticCount == 1 && !FactoryDiagnosticFatal && FactoryDiagnostic == exception.Message,
                        "a factory failure reports its diagnostic exactly once without becoming fatal");
                }
            }
            finally
            {
                bind.SetValue(api, originalBind);
                destroy.SetValue(api, originalDestroy);
            }
            check(BindFailureCount == 1 && !FailedImage.IsNull && FailedImageDestroyCount == 1,
                "failed texture binding destroys its real partially initialized native image exactly once");
            allocator.Free(ref placed);
            for (int iteration = 0; iteration < 3; ++iteration)
            {
                placed = allocator.Allocate(desc);
                check(placed.Texture != null, $"single-token texture allocator recovers its reservation after factory failure (iteration {iteration})");
                PlacedTexture exhausted = allocator.Allocate(desc);
                check(exhausted.Texture == null, "texture allocator capacity exhaustion still returns an empty placement without throwing");
                allocator.Free(ref exhausted);
                allocator.Free(ref placed);
            }
            try
            {
                allocate.SetValue(api, new Vulkan.PFN_vkVoidFunction((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkDevice,
                    Vulkan.VkMemoryAllocateInfo*, Vulkan.VkAllocationCallbacks*, Vulkan.VkDeviceMemory*, Vulkan.VkResult>)&FailAllocateMemory));
                for (int kind = 0; kind < 2; ++kind)
                {
                    FactoryDiagnosticCount = 0;
                    try
                    {
                        if (kind == 0)
                        {
                            GPUHeap failed = device.CreateGPUHeap(256);
                            failed.Destroy();
                        }
                        else
                        {
                            GPUTextureHeap failed = device.CreateTextureHeap(heap.Size);
                            failed.Destroy();
                        }
                        check(false, $"injected {(kind == 0 ? "GPU" : "texture")} heap allocation failure throws rather than returning empty storage");
                    }
                    catch (GPUException exception)
                    {
                        check(exception.Error == Error.DriverError && exception.Message.Contains("vkAllocateMemory", StringComparison.Ordinal) &&
                            FactoryDiagnosticCount == 1 && FactoryDiagnostic == exception.Message && !FactoryDiagnosticFatal,
                            $"injected {(kind == 0 ? "GPU" : "texture")} heap allocation failure throws a single diagnosed GPUException");
                    }
                }
            }
            finally
            {
                allocate.SetValue(api, originalAllocate);
            }
        }
        finally
        {
            bind.SetValue(api, originalBind);
            destroy.SetValue(api, originalDestroy);
            allocate.SetValue(api, originalAllocate);
            Gpu.SetErrorCallback(previousCallback);
            OriginalDestroyImage = null;
            device.WaitIdle();
            allocator?.Free(ref placed);
            heap.Destroy();
        }
    }

    private static void OnFactoryDiagnostic(string message, bool fatal)
    {
        ++FactoryDiagnosticCount;
        FactoryDiagnostic = message;
        FactoryDiagnosticFatal = fatal;
    }

    [UnmanagedCallersOnly]
    private static Vulkan.VkResult FailBindImageMemory(Vulkan.VkDevice device, Vulkan.VkImage image, Vulkan.VkDeviceMemory memory, ulong offset)
    {
        FailedImage = image;
        ++BindFailureCount;
        return Vulkan.VkResult.ErrorUnknown;
    }

    [UnmanagedCallersOnly]
    private static void CountDestroyImage(Vulkan.VkDevice device, Vulkan.VkImage image, Vulkan.VkAllocationCallbacks* allocator)
    {
        if (image == FailedImage) ++FailedImageDestroyCount;
        OriginalDestroyImage(device, image, allocator);
    }

    [UnmanagedCallersOnly]
    private static Vulkan.VkResult FailAllocateMemory(Vulkan.VkDevice device, Vulkan.VkMemoryAllocateInfo* info,
        Vulkan.VkAllocationCallbacks* allocator, Vulkan.VkDeviceMemory* memory)
    {
        *memory = default;
        return Vulkan.VkResult.ErrorUnknown;
    }

    private static Vulkan.VkCommandBuffer FailedCommands;
    private static int FailedCommandsFreeCount;
    private static delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkCommandPool, uint, Vulkan.VkCommandBuffer*, void> OriginalFreeCommands;

    private static void RunCommandBeginFailures(GPUDevice device, Action<bool, string> check)
    {
        Vulkan.VkDeviceApi api = (Vulkan.VkDeviceApi)device.GetType().GetField("Api", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device)!;
        FieldInfo begin = typeof(Vulkan.VkDeviceApi).GetField(nameof(Vulkan.VkDeviceApi.vkBeginCommandBuffer_ptr))!;
        FieldInfo free = typeof(Vulkan.VkDeviceApi).GetField(nameof(Vulkan.VkDeviceApi.vkFreeCommandBuffers_ptr))!;
        Vulkan.PFN_vkVoidFunction originalBegin = api.vkBeginCommandBuffer_ptr;
        Vulkan.PFN_vkVoidFunction originalFree = api.vkFreeCommandBuffers_ptr;
        FieldInfo callback = typeof(Alco.Graphics.LowLevel.Gpu).GetField("ErrorCallback", BindingFlags.Static | BindingFlags.NonPublic)!;
        ErrorCallback? previousCallback = (ErrorCallback?)callback.GetValue(null);
        GPUCommandPool? pool = null;
        TimelinePoint completion = default;
        try
        {
            pool = device.CreateCommandPool(0);
            completion.Semaphore = device.CreateTimelineSemaphore();
            OriginalFreeCommands = (delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkCommandPool, uint, Vulkan.VkCommandBuffer*, void>)originalFree.Value;
            Gpu.SetErrorCallback(OnFactoryDiagnostic);
            GPUCommandBuffer? previous = null;
            for (int reused = 0; reused < 2; ++reused)
            {
                FailedCommands = default;
                FailedCommandsFreeCount = 0;
                FactoryDiagnosticCount = 0;
                try
                {
                    begin.SetValue(api, new Vulkan.PFN_vkVoidFunction((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkCommandBuffer,
                        Vulkan.VkCommandBufferBeginInfo*, Vulkan.VkResult>)&FailBeginCommands));
                    free.SetValue(api, new Vulkan.PFN_vkVoidFunction((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkDevice,
                        Vulkan.VkCommandPool, uint, Vulkan.VkCommandBuffer*, void>)&CountFreeCommands));
                    try
                    {
                        GPUCommandBuffer unexpected = pool.BeginCommands();
                        unexpected.EndCommands();
                        check(false, $"{(reused == 0 ? "fresh" : "reused")} BeginCommands failure throws GPUException");
                    }
                    catch (GPUException exception)
                    {
                        check(exception.Error == Error.DriverError && exception.Message.Contains("vkBeginCommandBuffer", StringComparison.Ordinal) &&
                            FactoryDiagnosticCount == 1 && FactoryDiagnostic == exception.Message && !FactoryDiagnosticFatal,
                            $"{(reused == 0 ? "fresh" : "reused")} command-begin failure throws one diagnosed GPUException");
                    }
                }
                finally
                {
                    begin.SetValue(api, originalBegin);
                    free.SetValue(api, originalFree);
                }
                check(!FailedCommands.IsNull && FailedCommandsFreeCount == 1,
                    $"{(reused == 0 ? "fresh" : "reused")} command-begin failure frees the allocated native command buffer exactly once");
                GPUCommandBuffer commands = pool.BeginCommands();
                if (reused != 0)
                    check(ReferenceEquals(previous, commands), "retry after failed reuse retains the command pool's current recording node");
                commands.EndCommands();
                ++completion.Value;
                device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                completion.Wait();
                check(completion.Semaphore?.CompletedValue >= completion.Value,
                    $"{(reused == 0 ? "fresh" : "reused")} command-begin failure permits a successful retry and GPU submission");
                previous = commands;
                pool.Reset();
            }
        }
        finally
        {
            begin.SetValue(api, originalBegin);
            free.SetValue(api, originalFree);
            Gpu.SetErrorCallback(previousCallback);
            OriginalFreeCommands = null;
            device.WaitIdle();
            pool?.Destroy();
            completion.Semaphore?.Destroy();
        }
    }

    [UnmanagedCallersOnly]
    private static Vulkan.VkResult FailBeginCommands(Vulkan.VkCommandBuffer commands, Vulkan.VkCommandBufferBeginInfo* info)
    {
        FailedCommands = commands;
        return Vulkan.VkResult.ErrorUnknown;
    }

    [UnmanagedCallersOnly]
    private static void CountFreeCommands(Vulkan.VkDevice device, Vulkan.VkCommandPool pool, uint count, Vulkan.VkCommandBuffer* commands)
    {
        for (uint index = 0; index < count; ++index)
            if (commands[index] == FailedCommands) ++FailedCommandsFreeCount;
        OriginalFreeCommands(device, pool, count, commands);
    }

    private struct IndexRecordingCapture
    {
        internal int BufferBinds;
        internal int AddressBinds;
        internal int Draws;
        internal int BufferIndirectDraws;
        internal int AddressIndirectDraws;
        internal int Roots;
        internal int BufferTables;
        internal ulong BufferTableAddress;
        internal int Barriers;
        internal Vulkan.VkBuffer IndexBuffer;
        internal ulong IndexOffset;
        internal ulong IndexSize;
        internal Vulkan.VkIndexType IndexType;
        internal Vulkan.VkBindIndexBuffer3InfoKHR AddressBinding;
        internal Vulkan.VkDrawIndexedIndirectCommand Draw;
        internal Vulkan.VkBuffer ArgumentBuffer;
        internal ulong ArgumentOffset;
        internal Vulkan.VkDrawIndirect2InfoKHR Indirect;
        internal ulong Root;
        internal bool InvalidPush;
        internal Vulkan.VkMemoryBarrier2 Barrier;
        internal bool InvalidBarrier;
    }

    private static IndexRecordingCapture IndexCapture;

    private static void RunIndexBindings(GPUDevice device, Action<bool, string> check)
    {
        Console.WriteLine("Vulkan index binding cache:");
        Vulkan.VkDeviceApi api = (Vulkan.VkDeviceApi)device.GetType().GetField("Api", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device)!;
        FieldInfo addressCommands = device.GetType().GetField("AddressCommands", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool originalAddressCommands = (bool)addressCommands.GetValue(device)!;
        GPUHeap firstHeap = default;
        GPUHeap secondHeap = default;
        GPUHeap rootHeap = default;
        string[] names =
        [
            nameof(Vulkan.VkDeviceApi.vkCmdBindIndexBuffer2_ptr), nameof(Vulkan.VkDeviceApi.vkCmdBindIndexBuffer3KHR_ptr),
            nameof(Vulkan.VkDeviceApi.vkCmdDrawIndexed_ptr), nameof(Vulkan.VkDeviceApi.vkCmdDrawIndexedIndirect_ptr),
            nameof(Vulkan.VkDeviceApi.vkCmdDrawIndexedIndirect2KHR_ptr), nameof(Vulkan.VkDeviceApi.vkCmdPushDataEXT_ptr),
        ];
        FieldInfo[] fields = new FieldInfo[names.Length];
        Vulkan.PFN_vkVoidFunction[] originals = new Vulkan.PFN_vkVoidFunction[names.Length];
        for (int index = 0; index < names.Length; ++index)
        {
            fields[index] = typeof(Vulkan.VkDeviceApi).GetField(names[index])!;
            originals[index] = (Vulkan.PFN_vkVoidFunction)fields[index].GetValue(api)!;
        }
        Vulkan.PFN_vkVoidFunction[] replacements =
        [
            new((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkCommandBuffer, Vulkan.VkBuffer, ulong, ulong,
                Vulkan.VkIndexType, void>)&CaptureIndexBufferBinding),
            new((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkCommandBuffer, Vulkan.VkBindIndexBuffer3InfoKHR*,
                void>)&CaptureIndexAddressBinding),
            new((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkCommandBuffer, uint, uint, uint, int, uint, void>)&CaptureIndexedDraw),
            new((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkCommandBuffer, Vulkan.VkBuffer, ulong, uint, uint,
                void>)&CaptureIndexedBufferIndirectDraw),
            new((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkCommandBuffer, Vulkan.VkDrawIndirect2InfoKHR*,
                void>)&CaptureIndexedAddressIndirectDraw),
            new((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkCommandBuffer, Vulkan.VkPushDataInfoEXT*, void>)&CaptureIndexRoot),
        ];
        const BindingFlags memberFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo dispatch = device.GetType().GetField("Dispatch", memberFlags)!;
        object originalDispatch = dispatch.GetValue(device)!;
        object instrumentedDispatch = dispatch.GetValue(device)!;
        FieldInfo barrier = instrumentedDispatch.GetType().GetField("CmdPipelineBarrier2Pointer", memberFlags)!;
        barrier.SetValue(instrumentedDispatch,
            new Vulkan.PFN_vkVoidFunction((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkCommandBuffer, Vulkan.VkDependencyInfo*, void>)
                &CaptureIndexBarrier));
        GPUCommandPool? pool = null;
        try
        {
            device.WaitIdle();
            firstHeap = device.CreateGPUHeap(1024);
            secondHeap = device.CreateGPUHeap(1024);
            rootHeap = device.CreateGPUHeap(256);
            FieldInfo backingField = firstHeap.Owner!.GetType().GetField("Backing", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object cachedBacking = backingField.GetValue(firstHeap.Owner)!;
            object secondBacking = backingField.GetValue(secondHeap.Owner)!;
            object rootBacking = backingField.GetValue(rootHeap.Owner)!;
            FieldInfo nativeAddress = cachedBacking.GetType().GetField("Address")!;
            FieldInfo nativeBuffer = cachedBacking.GetType().GetField("Buffer")!;
            ulong firstAddress = (ulong)nativeAddress.GetValue(cachedBacking)!;
            ulong secondAddress = (ulong)nativeAddress.GetValue(secondBacking)!;
            ulong rootAddress = (ulong)nativeAddress.GetValue(rootBacking)!;
            Vulkan.VkBuffer firstBuffer = (Vulkan.VkBuffer)nativeBuffer.GetValue(cachedBacking)!;
            Vulkan.VkBuffer secondBuffer = (Vulkan.VkBuffer)nativeBuffer.GetValue(secondBacking)!;
            GPURange firstRoot = rootHeap.Range.GPURange.Slice(0, 16);
            GPURange secondRoot = rootHeap.Range.GPURange.Slice(16, 16);
            GPURange thirdRoot = rootHeap.Range.GPURange.Slice(32, 16);
            pool = device.CreateCommandPool(0);
            // This idle device is thread-owned; stubs capture both native command paths without submitting work.
            for (int index = 0; index < fields.Length; ++index) fields[index].SetValue(api, replacements[index]);
            dispatch.SetValue(device, instrumentedDispatch);
            for (int path = 0; path < 2; ++path)
            {
                bool fallback = path != 0;
                string label = fallback ? "buffer-plus-offset" : "direct-address";
                addressCommands.SetValue(device, !fallback);
                IndexCapture = default;
                GPURange indices = firstHeap.Range.GPURange.Slice(32, 64);
                GPURange arguments = secondHeap.Range.GPURange.Slice(64, 64);
                GPUCommandBuffer commands = pool.BeginCommands();
                check(IndexCapture.BufferTables == 1 && IndexCapture.BufferTableAddress != 0 && IndexCapture.Roots == 0 && !IndexCapture.InvalidPush,
                    $"{label}: command-buffer begin initializes the private buffer table at push offset 8 without changing the command root");
                commands.DrawIndexed(firstRoot, indices, IndexType.UInt16, 6);
                commands.DrawIndexed(secondRoot, indices, IndexType.UInt16, 3, instanceCount: 2, firstIndex: 1, vertexOffset: -2, firstInstance: 7);
                check(IndexCapture.BufferBinds == (fallback ? 1 : 0) && IndexCapture.AddressBinds == (fallback ? 0 : 1) &&
                    IndexCapture.Draws == 2 && IndexCapture.Roots == 2 && IndexCapture.BufferTables == 1 &&
                    IndexCapture.Root == rootAddress + secondRoot.Offset && !IndexCapture.InvalidPush &&
                    IndexCapture.Draw.indexCount == 3 && IndexCapture.Draw.instanceCount == 2 && IndexCapture.Draw.firstIndex == 1 &&
                    IndexCapture.Draw.vertexOffset == -2 && IndexCapture.Draw.firstInstance == 7,
                    $"{label}: identical bindings preserve every root and indexed draw argument while binding once");
                check(IndexBindingMatches(commands, indices, IndexType.UInt16),
                    $"{label}: cached indices retain their backing-buffer identity, byte offset, size and element type");
                check(fallback
                    ? IndexCapture.IndexBuffer == firstBuffer && IndexCapture.IndexOffset == 32 && IndexCapture.IndexSize == 64 &&
                        IndexCapture.IndexType == Vulkan.VkIndexType.Uint16
                    : IndexCapture.AddressBinding.addressRange.address == firstAddress + indices.Offset &&
                        IndexCapture.AddressBinding.addressRange.size == 64 &&
                        IndexCapture.AddressBinding.indexType == Vulkan.VkIndexType.Uint16 &&
                        IndexCapture.AddressBinding.addressFlags ==
                            (Vulkan.VkAddressCommandFlagsKHR.FullyBound | Vulkan.VkAddressCommandFlagsKHR.StorageBufferUsage),
                    $"{label}: the first bind preserves its native buffer/address, range and type");

                commands.DrawIndexedIndirect(thirdRoot, indices, IndexType.UInt16, arguments, drawCount: 2);
                check(IndexCapture.Indirect.drawCount == 2 && IndexCapture.Indirect.addressRange.stride == 20 &&
                    (fallback ? IndexCapture.ArgumentBuffer == secondBuffer && IndexCapture.ArgumentOffset == 64 :
                        IndexCapture.Indirect.addressRange.address == secondAddress + arguments.Offset &&
                        IndexCapture.Indirect.addressRange.size == arguments.Size &&
                        IndexCapture.Indirect.addressFlags ==
                            (Vulkan.VkAddressCommandFlagsKHR.FullyBound | Vulkan.VkAddressCommandFlagsKHR.StorageBufferUsage)),
                    $"{label}: cached indexed-indirect draws retain their argument range and default stride");
                commands.DrawIndexed(firstRoot, indices, IndexType.UInt16, 6);
                GPURange changedArguments = arguments.Slice(8);
                commands.DrawIndexedIndirect(secondRoot, indices, IndexType.UInt16, changedArguments, drawCount: 2, stride: 24);
                check(IndexCapture.BufferBinds + IndexCapture.AddressBinds == 1 && IndexCapture.Draws == 3 &&
                    IndexCapture.Roots == 5 && IndexCapture.BufferTables == 1 && !IndexCapture.InvalidPush &&
                    IndexCapture.BufferIndirectDraws == (fallback ? 2 : 0) && IndexCapture.AddressIndirectDraws == (fallback ? 0 : 2) &&
                    IndexCapture.Indirect.drawCount == 2 && IndexCapture.Indirect.addressRange.stride == 24 &&
                    (fallback ? IndexCapture.ArgumentOffset == 72 : IndexCapture.Indirect.addressRange.address == secondAddress + changedArguments.Offset &&
                        IndexCapture.Indirect.addressRange.size == changedArguments.Size),
                    $"{label}: ordinary and indirect draws share one binding despite different roots and indirect arguments");
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.IndexInput, Access.IndexRead);
                commands.DrawIndexed(default, indices, IndexType.UInt16, 6);
                check(IndexCapture.BufferBinds + IndexCapture.AddressBinds == 1 && IndexCapture.Draws == 4 &&
                    IndexCapture.Roots == 5 && IndexCapture.Root == rootAddress + secondRoot.Offset &&
                    IndexCapture.BufferTables == 1 && !IndexCapture.InvalidPush &&
                    IndexCapture.Barriers == 1 && !IndexCapture.InvalidBarrier &&
                    IndexCapture.Barrier.srcStageMask == Vulkan.VkPipelineStageFlags2.Copy &&
                    IndexCapture.Barrier.srcAccessMask == Vulkan.VkAccessFlags2.TransferWrite &&
                    IndexCapture.Barrier.dstStageMask == Vulkan.VkPipelineStageFlags2.IndexInput &&
                    IndexCapture.Barrier.dstAccessMask == Vulkan.VkAccessFlags2.IndexRead,
                    $"{label}: barriers remain intact without invalidating indices, and default roots preserve the previous binding without pushing data");

                GPURange shifted = firstHeap.Range.GPURange.Slice(indices.Offset + 4, indices.Size);
                commands.DrawIndexed(default, shifted, IndexType.UInt16, 3);
                check(IndexCapture.BufferBinds + IndexCapture.AddressBinds == 2 && IndexBindingMatches(commands, shifted, IndexType.UInt16) &&
                    (fallback ? IndexCapture.IndexOffset == 36 : IndexCapture.AddressBinding.addressRange.address == firstAddress + shifted.Offset),
                    $"{label}: changing only the byte offset rebinds the same buffer identity");
                commands.DrawIndexed(default, indices, IndexType.UInt16, 3);
                check(IndexCapture.BufferBinds + IndexCapture.AddressBinds == 3,
                    $"{label}: A-to-B-to-A bindings rebind A rather than reusing overwritten state");
                GPURange enlarged = firstHeap.Range.GPURange.Slice(indices.Offset, 96);
                commands.DrawIndexedIndirect(default, enlarged, IndexType.UInt16, arguments);
                check(IndexCapture.BufferBinds + IndexCapture.AddressBinds == 4 && IndexBindingMatches(commands, enlarged, IndexType.UInt16) &&
                    (fallback ? IndexCapture.IndexSize == 96 : IndexCapture.AddressBinding.addressRange.size == 96),
                    $"{label}: changing only the index range size rebinds through indirect draws");
                commands.DrawIndexed(default, enlarged, IndexType.UInt32, 3);
                check(IndexCapture.BufferBinds + IndexCapture.AddressBinds == 5 && IndexBindingMatches(commands, enlarged, IndexType.UInt32) &&
                    (fallback ? IndexCapture.IndexType == Vulkan.VkIndexType.Uint32 : IndexCapture.AddressBinding.indexType == Vulkan.VkIndexType.Uint32),
                    $"{label}: changing only the index type rebinds");
                GPURange otherBuffer = secondHeap.Range.GPURange.Slice(32, enlarged.Size);
                commands.DrawIndexedIndirect(default, otherBuffer, IndexType.UInt32, arguments);
                check(IndexCapture.BufferBinds + IndexCapture.AddressBinds == 6 && IndexBindingMatches(commands, otherBuffer, IndexType.UInt32) &&
                    (fallback ? IndexCapture.IndexBuffer == secondBuffer && IndexCapture.IndexOffset == 32 :
                        IndexCapture.AddressBinding.addressRange.address == secondAddress + otherBuffer.Offset),
                    $"{label}: equal offsets in different backing buffers do not share a binding");
                commands.DrawIndexed(default, indices, IndexType.UInt16, 3);
                commands.EndCommands();
                GPUCommandBuffer separate = pool.BeginCommands();
                separate.DrawIndexed(default, indices, IndexType.UInt16, 3);
                separate.EndCommands();
                check(!ReferenceEquals(commands, separate) && IndexCapture.BufferBinds + IndexCapture.AddressBinds == 8 &&
                    IndexCapture.BufferTables == 2 && !IndexCapture.InvalidPush,
                    $"{label}: separate command buffers establish their own first index binding");
                pool.Reset();
                GPUCommandBuffer recycled = pool.BeginCommands();
                recycled.DrawIndexed(default, indices, IndexType.UInt16, 3);
                recycled.EndCommands();
                check(ReferenceEquals(commands, recycled) && IndexCapture.BufferBinds + IndexCapture.AddressBinds == 9 &&
                    IndexCapture.BufferTables == 3 && !IndexCapture.InvalidPush,
                    $"{label}: a recycled command buffer rebinds identical indices in its new recording");
                GPUCommandBuffer recycledSeparate = pool.BeginCommands();
                recycledSeparate.DrawIndexedIndirect(default, indices, IndexType.UInt16, arguments);
                recycledSeparate.EndCommands();
                check(ReferenceEquals(separate, recycledSeparate) && IndexCapture.BufferBinds + IndexCapture.AddressBinds == 10 &&
                    IndexCapture.BufferTables == 4 && !IndexCapture.InvalidPush,
                    $"{label}: indirect draws also rebind identical indices after command-pool reset");
                pool.Reset();

                commands = pool.BeginCommands();
                for (int iteration = 0; iteration < 1000; ++iteration)
                {
                    commands.DrawIndexed(firstRoot, indices, IndexType.UInt16, 3);
                    commands.DrawIndexedIndirect(secondRoot, indices, IndexType.UInt16, arguments);
                    commands.DrawIndexed(thirdRoot, shifted, IndexType.UInt16, 3);
                }
                IndexCapture = default;
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < 10000; ++iteration)
                {
                    commands.DrawIndexed(firstRoot, indices, IndexType.UInt16, 3);
                    commands.DrawIndexedIndirect(secondRoot, indices, IndexType.UInt16, arguments);
                    commands.DrawIndexed(thirdRoot, shifted, IndexType.UInt16, 3);
                }
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                check(allocated == 0 && IndexCapture.BufferBinds + IndexCapture.AddressBinds == 20000 && IndexCapture.Draws == 20000 &&
                    IndexCapture.BufferIndirectDraws + IndexCapture.AddressIndirectDraws == 10000 &&
                    IndexCapture.Roots == 30000 && IndexCapture.BufferTables == 0 && !IndexCapture.InvalidPush,
                    $"{label}: warmed binding hits and misses preserve every draw/root without managed allocations");
                commands.EndCommands();
                pool.Reset();
            }
        }
        finally
        {
            for (int index = 0; index < fields.Length; ++index) fields[index].SetValue(api, originals[index]);
            dispatch.SetValue(device, originalDispatch);
            addressCommands.SetValue(device, originalAddressCommands);
            pool?.Destroy();
            rootHeap.Destroy();
            secondHeap.Destroy();
            firstHeap.Destroy();
            IndexCapture = default;
        }
    }

    private static void RunQueueRecordings(GPUDevice device, Action<bool, string> check)
    {
        Console.WriteLine("Vulkan queue command recording:");
        RunQueueRecording(device, 0, 1, "general", check);
        for (int kind = 0; kind < 2; ++kind)
        {
            bool copyOnly = kind != 0;
            string label = copyOnly ? "dedicated copy" : "dedicated compute";
            GPUDevice? dedicated = null;
            try
            {
                dedicated = Gpu.CreateDevice(copyOnly ? new DeviceDesc { Backend = device.ActiveBackend, DesiredCopyQueueCount = 1 }
                    : new DeviceDesc { Backend = device.ActiveBackend, DesiredComputeQueueCount = 1 });
                uint queueIndex = copyOnly ? dedicated.Caps.GeneralQueueCount + dedicated.Caps.ComputeQueueCount : dedicated.Caps.GeneralQueueCount;
                RunQueueRecording(dedicated, queueIndex, copyOnly ? 0 : 1, label, check);
            }
            catch (GPUException exception) when (dedicated == null && exception.Error == Error.Unsupported)
            {
                Console.WriteLine($"  [SKIP] {label} queue family is unavailable");
            }
            finally
            {
                dedicated?.WaitIdle();
                dedicated?.Destroy();
            }
        }
    }

    private static void RunQueueRecording(GPUDevice device, uint queueIndex, int expectedTables, string label, Action<bool, string> check)
    {
        Vulkan.VkDeviceApi api =
            (Vulkan.VkDeviceApi)device.GetType().GetField("Api", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device)!;
        FieldInfo push = typeof(Vulkan.VkDeviceApi).GetField(nameof(Vulkan.VkDeviceApi.vkCmdPushDataEXT_ptr))!;
        Vulkan.PFN_vkVoidFunction originalPush = api.vkCmdPushDataEXT_ptr;
        GPUHeap upload = default;
        GPUHeap storage = default;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        TimelinePoint completion = default;
        try
        {
            device.WaitIdle();
            upload = device.CreateGPUHeap(64);
            storage = device.CreateGPUHeap(64, MemoryType.GpuOnly);
            readback = device.CreateGPUHeap(64, MemoryType.Readback);
            pool = device.CreateCommandPool(queueIndex);
            completion.Semaphore = device.CreateTimelineSemaphore();
            Span<byte> expected = stackalloc byte[64];
            GPUCommandBuffer? previous = null;
            IndexCapture = default;
            // Suppress shader pushes so a copy-queue regression fails its assertion without recording invalid native commands.
            push.SetValue(api, new Vulkan.PFN_vkVoidFunction((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkCommandBuffer,
                Vulkan.VkPushDataInfoEXT*, void>)&CaptureIndexRoot));
            for (int round = 0; round < 2; ++round)
            {
                for (int index = 0; index < expected.Length; ++index) expected[index] = (byte)(round * 71 + index);
                expected.CopyTo(upload.Range.CpuSpan);
                string phase = round == 0 ? "fresh" : "recycled";
                GPUCommandBuffer commands = pool.BeginCommands();
                if (round != 0) check(ReferenceEquals(previous, commands), $"{label}: reset recycles the original command buffer");
                commands.CopyMemory(upload.Range.GPURange, storage.Range.GPURange);
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
                commands.CopyMemory(storage.Range.GPURange, readback.Range.GPURange);
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                commands.EndCommands();
                check(IndexCapture.BufferTables == expectedTables * (round + 1) && IndexCapture.Roots == 0 && !IndexCapture.InvalidPush &&
                    (expectedTables == 0 ? IndexCapture.BufferTableAddress == 0 : IndexCapture.BufferTableAddress != 0),
                    $"{label}: {phase} recording pushes {expectedTables} private buffer tables at offset 8 and no other shader data");
                ++completion.Value;
                device.Submit(new SubmitDesc { Commands = [commands], Completion = completion }, queueIndex);
                completion.Wait();
                check(readback.Range.CpuSpan.SequenceEqual(expected), $"{label}: {phase} recording completes upload/GPU-only/readback copies");
                previous = commands;
                pool.Reset();
            }
        }
        finally
        {
            push.SetValue(api, originalPush);
            device.WaitIdle();
            pool?.Destroy();
            completion.Semaphore?.Destroy();
            readback.Destroy();
            storage.Destroy();
            upload.Destroy();
            IndexCapture = default;
        }
    }

    private static bool IndexBindingMatches(GPUCommandBuffer commands, GPURange indices, IndexType type)
    {
        Type implementation = commands.GetType();
        GPURange bound = (GPURange)implementation.GetField("BoundIndices", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(commands)!;
        IndexType boundType = (IndexType)implementation.GetField("BoundIndexType", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(commands)!;
        return ReferenceEquals(bound.Buffer, indices.Buffer) && bound.Offset == indices.Offset && bound.Size == indices.Size && boundType == type;
    }

    [UnmanagedCallersOnly]
    private static void CaptureIndexBufferBinding(Vulkan.VkCommandBuffer commands, Vulkan.VkBuffer buffer, ulong offset, ulong size, Vulkan.VkIndexType type)
    {
        ++IndexCapture.BufferBinds;
        IndexCapture.IndexBuffer = buffer;
        IndexCapture.IndexOffset = offset;
        IndexCapture.IndexSize = size;
        IndexCapture.IndexType = type;
    }

    [UnmanagedCallersOnly]
    private static void CaptureIndexAddressBinding(Vulkan.VkCommandBuffer commands, Vulkan.VkBindIndexBuffer3InfoKHR* info)
    {
        ++IndexCapture.AddressBinds;
        IndexCapture.AddressBinding = *info;
    }

    [UnmanagedCallersOnly]
    private static void CaptureIndexedDraw(Vulkan.VkCommandBuffer commands, uint indexCount, uint instanceCount, uint firstIndex,
        int vertexOffset, uint firstInstance)
    {
        ++IndexCapture.Draws;
        IndexCapture.Draw = new Vulkan.VkDrawIndexedIndirectCommand
        {
            indexCount = indexCount,
            instanceCount = instanceCount,
            firstIndex = firstIndex,
            vertexOffset = vertexOffset,
            firstInstance = firstInstance,
        };
    }

    [UnmanagedCallersOnly]
    private static void CaptureIndexedBufferIndirectDraw(Vulkan.VkCommandBuffer commands, Vulkan.VkBuffer buffer, ulong offset, uint drawCount, uint stride)
    {
        ++IndexCapture.BufferIndirectDraws;
        IndexCapture.ArgumentBuffer = buffer;
        IndexCapture.ArgumentOffset = offset;
        IndexCapture.Indirect = new Vulkan.VkDrawIndirect2InfoKHR { drawCount = drawCount, addressRange = new() { stride = stride } };
    }

    [UnmanagedCallersOnly]
    private static void CaptureIndexedAddressIndirectDraw(Vulkan.VkCommandBuffer commands, Vulkan.VkDrawIndirect2InfoKHR* info)
    {
        ++IndexCapture.AddressIndirectDraws;
        IndexCapture.Indirect = *info;
    }

    [UnmanagedCallersOnly]
    private static void CaptureIndexRoot(Vulkan.VkCommandBuffer commands, Vulkan.VkPushDataInfoEXT* info)
    {
        if (info->data.size != (nuint)sizeof(void*) || info->data.address == null)
        {
            IndexCapture.InvalidPush = true;
            return;
        }
        if (info->offset == 0)
        {
            ++IndexCapture.Roots;
            IndexCapture.Root = *(ulong*)info->data.address;
        }
        else if (info->offset == 8)
        {
            ++IndexCapture.BufferTables;
            ulong address = *(ulong*)info->data.address;
            if (address == 0 || IndexCapture.BufferTableAddress != 0 && IndexCapture.BufferTableAddress != address)
                IndexCapture.InvalidPush = true;
            IndexCapture.BufferTableAddress = address;
        }
        else IndexCapture.InvalidPush = true;
    }

    [UnmanagedCallersOnly]
    private static void CaptureIndexBarrier(Vulkan.VkCommandBuffer commands, Vulkan.VkDependencyInfo* info)
    {
        ++IndexCapture.Barriers;
        if (info->memoryBarrierCount != 1 || info->pMemoryBarriers == null) IndexCapture.InvalidBarrier = true;
        else IndexCapture.Barrier = *info->pMemoryBarriers;
    }

    private static int UploadPoolCreateCount;
    private static int UploadPoolDestroyCount;
    private static int UploadBufferDestroyCount;
    private static int UploadMemoryFreeCount;
    private static int UploadSemaphoreDestroyCount;
    private static Vulkan.VkCommandPool FirstUploadPool;
    private static delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkCommandPoolCreateInfo*, Vulkan.VkAllocationCallbacks*, Vulkan.VkCommandPool*,
        Vulkan.VkResult> OriginalCreateUploadPool;
    private static delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkCommandPool, Vulkan.VkAllocationCallbacks*, void> OriginalDestroyUploadPool;
    private static delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkBuffer, Vulkan.VkAllocationCallbacks*, void> OriginalDestroyUploadBuffer;
    private static delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkDeviceMemory, Vulkan.VkAllocationCallbacks*, void> OriginalFreeUploadMemory;
    private static delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkSemaphore, Vulkan.VkAllocationCallbacks*, void> OriginalDestroyUploadSemaphore;

    private static void RunUploadQueueFailure(GPUDevice device, Action<bool, string> check)
    {
        Vulkan.VkDeviceApi api = (Vulkan.VkDeviceApi)device.GetType().GetField("Api", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device)!;
        string[] names =
        [
            nameof(Vulkan.VkDeviceApi.vkCreateCommandPool_ptr), nameof(Vulkan.VkDeviceApi.vkDestroyCommandPool_ptr),
            nameof(Vulkan.VkDeviceApi.vkDestroyBuffer_ptr), nameof(Vulkan.VkDeviceApi.vkFreeMemory_ptr), nameof(Vulkan.VkDeviceApi.vkDestroySemaphore_ptr),
        ];
        FieldInfo[] fields = new FieldInfo[names.Length];
        Vulkan.PFN_vkVoidFunction[] originals = new Vulkan.PFN_vkVoidFunction[names.Length];
        for (int index = 0; index < names.Length; ++index)
        {
            fields[index] = typeof(Vulkan.VkDeviceApi).GetField(names[index])!;
            originals[index] = (Vulkan.PFN_vkVoidFunction)fields[index].GetValue(api)!;
        }
        OriginalCreateUploadPool = (delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkCommandPoolCreateInfo*, Vulkan.VkAllocationCallbacks*,
            Vulkan.VkCommandPool*, Vulkan.VkResult>)originals[0].Value;
        OriginalDestroyUploadPool = (delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkCommandPool, Vulkan.VkAllocationCallbacks*, void>)originals[1].Value;
        OriginalDestroyUploadBuffer = (delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkBuffer, Vulkan.VkAllocationCallbacks*, void>)originals[2].Value;
        OriginalFreeUploadMemory = (delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkDeviceMemory, Vulkan.VkAllocationCallbacks*, void>)originals[3].Value;
        OriginalDestroyUploadSemaphore = (delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkSemaphore, Vulkan.VkAllocationCallbacks*, void>)originals[4].Value;
        Vulkan.PFN_vkVoidFunction[] replacements =
        [
            new((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkCommandPoolCreateInfo*, Vulkan.VkAllocationCallbacks*,
                Vulkan.VkCommandPool*, Vulkan.VkResult>)&FailSecondUploadPool),
            new((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkCommandPool, Vulkan.VkAllocationCallbacks*,
                void>)&CountDestroyUploadPool),
            new((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkBuffer, Vulkan.VkAllocationCallbacks*,
                void>)&CountDestroyUploadBuffer),
            new((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkDeviceMemory, Vulkan.VkAllocationCallbacks*,
                void>)&CountFreeUploadMemory),
            new((delegate* unmanaged<void>)(delegate* unmanaged<Vulkan.VkDevice, Vulkan.VkSemaphore, Vulkan.VkAllocationCallbacks*,
                void>)&CountDestroyUploadSemaphore),
        ];
        FieldInfo callback = typeof(Alco.Graphics.LowLevel.Gpu).GetField("ErrorCallback", BindingFlags.Static | BindingFlags.NonPublic)!;
        ErrorCallback? previousCallback = (ErrorCallback?)callback.GetValue(null);
        UploadQueue? uploads = null;
        UploadPoolCreateCount = 0;
        UploadPoolDestroyCount = 0;
        UploadBufferDestroyCount = 0;
        UploadMemoryFreeCount = 0;
        UploadSemaphoreDestroyCount = 0;
        FirstUploadPool = default;
        FactoryDiagnosticCount = 0;
        try
        {
            Gpu.SetErrorCallback(OnFactoryDiagnostic);
            for (int index = 0; index < fields.Length; ++index) fields[index].SetValue(api, replacements[index]);
            try
            {
                uploads = new UploadQueue(device, 256, maxPendingBatches: 2);
                check(false, "UploadQueue propagates failure from its second command-pool factory");
            }
            catch (GPUException exception)
            {
                check(exception.Error == Error.DriverError && exception.Message.Contains("vkCreateCommandPool", StringComparison.Ordinal) &&
                    FactoryDiagnosticCount == 1 && FactoryDiagnostic == exception.Message && !FactoryDiagnosticFatal,
                    "UploadQueue propagates the late pool factory's single diagnosed GPUException");
                check(UploadPoolCreateCount == 2 && !FirstUploadPool.IsNull && UploadPoolDestroyCount == 1 && UploadBufferDestroyCount == 1 &&
                    UploadMemoryFreeCount == 1 && UploadSemaphoreDestroyCount == 1,
                    "failed UploadQueue construction immediately releases its first pool, staging buffer/memory and semaphore without finalization");
            }
        }
        finally
        {
            for (int index = 0; index < fields.Length; ++index) fields[index].SetValue(api, originals[index]);
            Gpu.SetErrorCallback(previousCallback);
            device.WaitIdle();
            uploads?.Destroy();
            OriginalCreateUploadPool = null;
            OriginalDestroyUploadPool = null;
            OriginalDestroyUploadBuffer = null;
            OriginalFreeUploadMemory = null;
            OriginalDestroyUploadSemaphore = null;
        }
        uploads = new UploadQueue(device, 256, maxPendingBatches: 2);
        try
        {
            check(uploads.Valid, "UploadQueue construction succeeds after restoring the failed native factory");
        }
        finally
        {
            device.WaitIdle();
            uploads.Destroy();
        }
        check(!uploads.Valid, "successfully reconstructed UploadQueue supports explicit destruction");
    }

    [UnmanagedCallersOnly]
    private static Vulkan.VkResult FailSecondUploadPool(Vulkan.VkDevice device, Vulkan.VkCommandPoolCreateInfo* info,
        Vulkan.VkAllocationCallbacks* allocator, Vulkan.VkCommandPool* pool)
    {
        if (++UploadPoolCreateCount == 2)
        {
            *pool = default;
            return Vulkan.VkResult.ErrorUnknown;
        }
        Vulkan.VkResult result = OriginalCreateUploadPool(device, info, allocator, pool);
        if (result == Vulkan.VkResult.Success) FirstUploadPool = *pool;
        return result;
    }

    [UnmanagedCallersOnly]
    private static void CountDestroyUploadPool(Vulkan.VkDevice device, Vulkan.VkCommandPool pool, Vulkan.VkAllocationCallbacks* allocator)
    {
        if (pool == FirstUploadPool) ++UploadPoolDestroyCount;
        OriginalDestroyUploadPool(device, pool, allocator);
    }

    [UnmanagedCallersOnly]
    private static void CountDestroyUploadBuffer(Vulkan.VkDevice device, Vulkan.VkBuffer buffer, Vulkan.VkAllocationCallbacks* allocator)
    {
        ++UploadBufferDestroyCount;
        OriginalDestroyUploadBuffer(device, buffer, allocator);
    }

    [UnmanagedCallersOnly]
    private static void CountFreeUploadMemory(Vulkan.VkDevice device, Vulkan.VkDeviceMemory memory, Vulkan.VkAllocationCallbacks* allocator)
    {
        ++UploadMemoryFreeCount;
        OriginalFreeUploadMemory(device, memory, allocator);
    }

    [UnmanagedCallersOnly]
    private static void CountDestroyUploadSemaphore(Vulkan.VkDevice device, Vulkan.VkSemaphore semaphore, Vulkan.VkAllocationCallbacks* allocator)
    {
        ++UploadSemaphoreDestroyCount;
        OriginalDestroyUploadSemaphore(device, semaphore, allocator);
    }

    private static void RunPrimitiveTopologies(GPUDevice device, Action<bool, string> check)
    {
        const int tileSize = 64;
        const int width = tileSize * 6;
        const int paddedIndirectTile = 5;
        const uint indirectStride = 32;
        using ShaderCompiler compiler = new(device);
        CompiledShader compiled = compiler.CompileSource("regression_topology", "regression_topology.slang", TopologySource,
        [
            new ShaderEntryRequest("vertexMain", ShaderStageKind.Vertex),
            new ShaderEntryRequest("plainVertexMain", ShaderStageKind.Vertex),
            new ShaderEntryRequest("pixelMain", ShaderStageKind.Pixel),
        ]);
        ReadOnlySpan<Vector2> points =
        [
            new(8.5f, 8.5f), new(24.5f, 8.5f), new(8.5f, 24.5f), new(24.5f, 24.5f),
            new(40.5f, 40.5f), new(56.5f, 40.5f), new(40.5f, 56.5f), new(56.5f, 56.5f),
        ];
        (Vector2[] Filled, Vector2[] Empty)[] probes =
        [
            ([new(12, 12), new(40, 35)], [new(20, 20), new(44, 44), new(52, 52)]),
            (points.ToArray(), [new(16, 8), new(16, 24), new(48, 40), new(48, 56), new(12, 12)]),
            ([new(16, 8), new(16, 24), new(48, 40), new(48, 56)], [new(16, 16), new(48, 48), new(32, 32), new(12, 12)]),
            ([new(16, 8), new(16, 16), new(16, 24), new(48, 40), new(48, 48), new(48, 56)], [new(12, 12), new(44, 44), new(52, 52)]),
            ([new(12, 12), new(20, 20), new(44, 44), new(52, 52)], [new(32, 12), new(12, 48)]),
        ];
        ReadOnlySpan<string> modes = ["Draw", "DrawIndirect", "DrawIndexed UInt16", "DrawIndexedIndirect UInt16",
            "DrawIndexed UInt32", "DrawIndexedIndirect UInt32"];
        GPUPipeline[] pipelines = new GPUPipeline[6];
        GPUTextureHeap colorHeap = default;
        GPUTexture? color = null;
        GPURenderView? view = null;
        GPUHeap upload = default;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        TimelinePoint pending = default;
        try
        {
            ShaderStage pixel = compiled.GetGpuStage("pixelMain");
            ShaderStage pointVertex = compiled.GetGpuStage("vertexMain");
            ShaderStage plainVertex = compiled.GetGpuStage("plainVertexMain");
            for (int index = 0; index < pipelines.Length; ++index)
            {
                // Metal permits point-size outputs only with point-class pipelines, matching the PointList contract.
                bool pointTopology = (index == 0 ? PrimitiveTopology.TriangleList : (PrimitiveTopology)(index % probes.Length)) == PrimitiveTopology.PointList;
                GraphicsPipelineDesc desc = new()
                {
                    Vertex = pointTopology ? pointVertex : plainVertex,
                    Pixel = pixel,
                    ColorTargets = [new ColorTargetDesc { Format = Format.Rgba8Unorm }],
                };
                if (index != 0) desc.Topology = (PrimitiveTopology)(index % probes.Length);
                pipelines[index] = device.CreateGraphicsPipeline(desc);
            }
            TextureDesc colorDesc = new()
            {
                Extent = new Extent3D(width, tileSize, 1), Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            };
            colorHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(colorDesc).Size);
            color = device.CreateTexture(colorDesc, colorHeap, 0);
            view = color.CreateRenderView(new RenderViewDesc());
            upload = device.CreateGPUHeap(1024);
            readback = device.CreateGPUHeap(width * tileSize * 4, MemoryType.Readback);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            check(sizeof(TopologyData) == 16, "topology root layout retains its 16-byte C ABI");
            BumpAllocator arena = new(upload.Range);
            Span<Vector2> cachedVertices = stackalloc Vector2[points.Length];
            for (int index = 0; index < points.Length; ++index)
                cachedVertices[index] = new Vector2(points[index].X / (tileSize / 2) - 1, 1 - points[index].Y / (tileSize / 2));
            GPUAllocation<Vector2> vertices = arena.Allocate<Vector2>((ulong)cachedVertices.Length);
            cachedVertices.CopyTo(vertices.CpuSpan);
            TopologyData cachedRoot = new() { Vertices = vertices.View, VertexCount = (uint)cachedVertices.Length };
            GPUAllocation<TopologyData> root = arena.Allocate<TopologyData>();
            *root.Cpu = cachedRoot;
            // The second index range contains leading, consecutive and trailing fixed restart markers, never used by lists.
            ReadOnlySpan<uint> cachedIndices =
            [
                0, 0, 0, 1, 2, 3, 4, 5, 6, 7,
                0, 0, uint.MaxValue, 0, 1, 2, 3, uint.MaxValue, uint.MaxValue, 4, 5, 6, 7, uint.MaxValue,
            ];
            Span<ushort> cachedIndices16 = stackalloc ushort[cachedIndices.Length];
            for (int index = 0; index < cachedIndices.Length; ++index)
                cachedIndices16[index] = cachedIndices[index] == uint.MaxValue ? ushort.MaxValue : (ushort)cachedIndices[index];
            GPUAllocation<uint> indices32 = arena.Allocate<uint>((ulong)cachedIndices.Length);
            GPUAllocation<ushort> indices16 = arena.Allocate<ushort>((ulong)cachedIndices16.Length);
            cachedIndices.CopyTo(indices32.CpuSpan);
            cachedIndices16.CopyTo(indices16.CpuSpan);
            ReadOnlySpan<uint> cachedArguments =
            [
                8, 1, 3, 5, 8, 1, 2, 3, 5, 12, 1, 2, 3, 5,
                // An empty first draw and zero padding make the second draw necessary.
                0, 1, 3, 5, 0, 0, 0, 0, 8, 1, 3, 5,
            ];
            GPUAllocation<uint> arguments = arena.Allocate<uint>((ulong)cachedArguments.Length);
            cachedArguments.CopyTo(arguments.CpuSpan);
            GPURange paddedArguments = arguments.GPURange.Slice(56, indirectStride + 16);
            for (int mode = 0; mode < modes.Length; ++mode)
            {
                GPUCommandBuffer commands = pool.BeginCommands();
                if (mode == 0) commands.InitializeTexture(color);
                else commands.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorWrite);
                commands.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new ColorAttachment { RenderView = view, Load = LoadOp.Clear, Clear = Vector4.Zero }],
                });
                for (int index = 0; index < pipelines.Length; ++index)
                {
                    PrimitiveTopology topology = (PrimitiveTopology)(index % probes.Length);
                    bool strip = topology is PrimitiveTopology.LineStrip or PrimitiveTopology.TriangleStrip;
                    IndexType indexType = mode < 4 ? IndexType.UInt16 : IndexType.UInt32;
                    int indexSize = indexType == IndexType.UInt16 ? sizeof(ushort) : sizeof(uint);
                    GPURange indices = (indexType == IndexType.UInt16 ? indices16.GPURange : indices32.GPURange).Slice(
                        (ulong)((strip ? 10 : 0) * indexSize), (ulong)((strip ? 14 : 10) * indexSize));
                    GPURange indirect = arguments.GPURange.Slice((ulong)(mode < 2 ? 0 : strip ? 36 : 16), mode < 2 ? 16u : 20u);
                    commands.SetViewport(new Viewport { X = index * tileSize, Width = tileSize, Height = tileSize });
                    commands.SetScissor(new Scissor { X = index * tileSize, Width = tileSize, Height = tileSize });
                    commands.BindPipeline(pipelines[index]);
                    if (mode == 0) commands.Draw(root.GPURange, 8, firstVertex: 3, firstInstance: 5);
                    else if (mode == 1 && index == paddedIndirectTile)
                        commands.DrawIndirect(root.GPURange, paddedArguments, drawCount: 2, stride: indirectStride);
                    else if (mode == 1) commands.DrawIndirect(root.GPURange, indirect);
                    else if ((mode & 1) == 0)
                        commands.DrawIndexed(root.GPURange, indices, indexType, strip ? 12u : 8u, firstIndex: 2, vertexOffset: 3, firstInstance: 5);
                    else commands.DrawIndexedIndirect(root.GPURange, indices, indexType, indirect);
                }
                commands.EndRenderPass();
                commands.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                commands.CopyTextureToMemory(color, readback.Range.GPURange);
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                commands.EndCommands();
                pending = new TimelinePoint { Semaphore = timeline, Value = (ulong)mode + 1 };
                device.Submit(new SubmitDesc { Commands = [commands], Completion = pending });
                pending.Wait();
                pending = default;
                for (int index = 0; index < pipelines.Length; ++index)
                {
                    int probeIndex = index % probes.Length;
                    PrimitiveTopology topology = (PrimitiveTopology)probeIndex;
                    bool matches = TopologyRegionMatches(readback.Range.Cpu, width, index * tileSize, new Vector2(2, 2), false) &&
                        TopologyRegionMatches(readback.Range.Cpu, width, index * tileSize, new Vector2(61, 61), false);
                    foreach (Vector2 point in probes[probeIndex].Filled)
                        matches &= TopologyRegionMatches(readback.Range.Cpu, width, index * tileSize, point, true);
                    foreach (Vector2 point in probes[probeIndex].Empty)
                        matches &= TopologyRegionMatches(readback.Range.Cpu, width, index * tileSize, point, false);
                    if (topology == PrimitiveTopology.PointList)
                    {
                        int pointPixels = 0;
                        uint* pixels = (uint*)readback.Range.Cpu;
                        for (int y = 0; y < tileSize; ++y)
                            for (int x = 0; x < tileSize; ++x)
                                if (pixels[y * width + index * tileSize + x] != 0) ++pointPixels;
                        matches &= pointPixels == points.Length;
                    }
                    if (topology is PrimitiveTopology.LineStrip or PrimitiveTopology.TriangleStrip)
                    {
                        Vector2 bridge = topology == PrimitiveTopology.LineStrip ? new Vector2(32, 32) : new Vector2(28, 33);
                        matches &= TopologyRegionMatches(readback.Range.Cpu, width, index * tileSize, bridge, mode < 2);
                        if (topology == PrimitiveTopology.TriangleStrip)
                            matches &= TopologyRegionMatches(readback.Range.Cpu, width, index * tileSize, new Vector2(40, 35), mode < 2);
                    }
                    string draw = mode == 1 && index == paddedIndirectTile ? "DrawIndirect (two records, padded stride)" : modes[mode];
                    check(matches, $"{draw} {(index == 0 ? "default TriangleList" : topology)} draws expected primitives and preserves clear gaps");
                }
                pool.Reset();
            }
        }
        finally
        {
            pending.Wait();
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            upload.Destroy();
            view?.Destroy();
            color?.Destroy();
            colorHeap.Destroy();
            foreach (GPUPipeline? pipeline in pipelines) pipeline?.Destroy();
        }
    }

    private static bool TopologyRegionMatches(byte* pixels, int width, int originX, Vector2 point, bool filled)
    {
        bool found = false;
        for (int y = (int)point.Y - 1; y <= (int)point.Y + 1; ++y)
            for (int x = (int)point.X - 1; x <= (int)point.X + 1; ++x)
            {
                uint pixel = ((uint*)pixels)[y * width + originX + x];
                if (!filled && pixel != 0) return false;
                found |= pixel == uint.MaxValue;
            }
        return !filled || found;
    }

    private static void RunCoordinates(GPUDevice device, ShaderCompiler compiler, Action<bool, string> check)
    {
        const int width = 48;
        const int height = 48;
        const int colorBytes = width * height * 16;
        const int depthBytes = width * height * sizeof(float);
        bool mesh = device.ActiveBackend != GraphicsBackend.VulkanCompact;
        ShaderEntryRequest[] entries =
        [
            new("vertexMain", ShaderStageKind.Vertex),
            new("pixelMain", ShaderStageKind.Pixel),
            new("sampleVertexMain", ShaderStageKind.Vertex),
            new("sampleMain", ShaderStageKind.Pixel),
            new("meshMain", ShaderStageKind.Mesh),
        ];
        CompiledShader compiled = compiler.CompileSource("regression_coordinates", "regression_coordinates.slang", CoordinateSource,
            mesh ? entries : entries[..^1]);
        ReadOnlySpan<(float Depth, float MinDepth, float MaxDepth)> probes =
        [
            (-0.25f, 0, 1), (0, 0, 1), (1, 0, 1), (1.25f, 0, 1), (0.25f, 0.125f, 0.875f), (0.75f, 0.125f, 0.875f),
        ];
        GPUPipeline[] pipelines = new GPUPipeline[mesh ? 2 : 1];
        GPUPipeline? sampling = null;
        GPUTextureHeap colorHeap = default;
        GPUTextureHeap sampledHeap = default;
        GPUTextureHeap depthHeap = default;
        GPUTexture? color = null;
        GPUTexture? sampled = null;
        GPUTexture? depthTexture = null;
        GPURenderView? colorView = null;
        GPURenderView? depthView = null;
        GPUTextureDescriptorHeap? textures = null;
        GPUSamplerDescriptorHeap? samplers = null;
        GPUHeap upload = default;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        TimelinePoint pending = default;
        try
        {
            ReadOnlySpan<ColorTargetDesc> targets = [new ColorTargetDesc { Format = Format.Rgba32Float }];
            ShaderStage pixel = compiled.GetGpuStage("pixelMain");
            GraphicsPipelineDesc graphics = new()
            {
                Vertex = compiled.GetGpuStage("vertexMain"),
                Pixel = pixel, ColorTargets = targets, DepthFormat = Format.D32Float,
            };
            pipelines[0] = device.CreateGraphicsPipeline(graphics);
            if (device.ActiveBackend == GraphicsBackend.Dx12)
            {
                graphics.Rasterization.DepthBiasConstant = 0.5f;
                device.CreateGraphicsPipeline(graphics).Destroy();
                check(true, "Dx12 creates a graphics pipeline with a fractional depth-bias constant");
            }
            if (mesh)
                pipelines[1] = device.CreateMeshPipeline(new MeshPipelineDesc
                {
                    Mesh = compiled.GetGpuStage("meshMain"),
                    Pixel = pixel, ColorTargets = targets, DepthFormat = Format.D32Float,
                });
            sampling = device.CreateGraphicsPipeline(new GraphicsPipelineDesc
            {
                Vertex = compiled.GetGpuStage("sampleVertexMain"),
                Pixel = compiled.GetGpuStage("sampleMain"),
                ColorTargets = targets,
            });
            TextureDesc colorDesc = new()
            {
                Extent = new Extent3D(width, height, 1), Format = Format.Rgba32Float,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            };
            TextureDesc sampledDesc = colorDesc;
            sampledDesc.Usage = TextureUsage.Sampled | TextureUsage.TransferDestination;
            TextureDesc depthDesc = new()
            {
                Extent = new Extent3D(width, height, 1), Format = Format.D32Float,
                Usage = TextureUsage.DepthStencilAttachment | TextureUsage.TransferSource,
            };
            colorHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(colorDesc).Size);
            sampledHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(sampledDesc).Size);
            depthHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(depthDesc).Size);
            color = device.CreateTexture(colorDesc, colorHeap, 0);
            sampled = device.CreateTexture(sampledDesc, sampledHeap, 0);
            depthTexture = device.CreateTexture(depthDesc, depthHeap, 0);
            colorView = color.CreateRenderView(new RenderViewDesc());
            depthView = depthTexture.CreateRenderView(new RenderViewDesc());
            textures = device.CreateTextureDescriptorHeap(1);
            samplers = device.CreateSamplerDescriptorHeap(1);
            textures.WriteTextureDescriptor(0, sampled, TextureDescriptorType.Sampled, new TextureDescriptorDesc());
            samplers.WriteSamplerDescriptor(0, new SamplerDesc
            {
                MinFilter = Filter.Nearest, MagFilter = Filter.Nearest, MipFilter = Filter.Nearest,
                AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge,
            });
            upload = device.CreateGPUHeap(256);
            readback = device.CreateGPUHeap(colorBytes * 2 + depthBytes, MemoryType.Readback);
            BumpAllocator arena = new(upload.Range);
            GPUAllocation<CoordinateData> root = arena.Allocate<CoordinateData>();
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            ulong value = 0;
            for (int path = 0; path < pipelines.Length; ++path)
                foreach ((float depth, float minDepth, float maxDepth) in probes)
                {
                    CoordinateData cachedRoot = new() { Depth = depth };
                    *root.Cpu = cachedRoot;
                    GPUCommandBuffer commands = pool.BeginCommands();
                    if (value == 0)
                    {
                        commands.InitializeTexture(color);
                        commands.InitializeTexture(sampled);
                        commands.InitializeTexture(depthTexture);
                    }
                    else
                    {
                        commands.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput | Stage.DepthStencilTests,
                            Access.ColorWrite | Access.DepthStencilRead | Access.DepthStencilWrite);
                        commands.Barrier(Stage.Pixel, Access.ShaderRead, Stage.Transfer, Access.TransferWrite);
                    }
                    commands.BeginRenderPass(new RenderingDesc
                    {
                        Colors = [new ColorAttachment { RenderView = colorView, Load = LoadOp.Clear, Clear = Vector4.Zero }],
                        Depth = new DepthAttachment { RenderView = depthView, Load = LoadOp.Clear },
                    });
                    commands.SetViewport(new Viewport { X = 5, Y = 7, Width = 32, Height = 32, MinDepth = minDepth, MaxDepth = maxDepth });
                    commands.SetScissor(new Scissor { X = 12, Y = 11, Width = 11, Height = 6 });
                    commands.SetDepthStencil(new DepthStencilState { DepthTest = true, DepthWrite = true, DepthCompare = CompareOp.Always });
                    commands.BindPipeline(pipelines[path]);
                    if (path == 0) commands.Draw(root.GPURange, 3);
                    else commands.DrawMeshlets(root.GPURange, new Extent3D(1, 1, 1));
                    commands.EndRenderPass();
                    commands.Barrier(Stage.ColorOutput | Stage.DepthStencilTests, Access.ColorWrite | Access.DepthStencilWrite,
                        Stage.Transfer, Access.TransferRead);
                    commands.CopyTextureToMemory(color, readback.Range.GPURange.Slice(0, colorBytes));
                    commands.CopyTextureToMemory(depthTexture, readback.Range.GPURange.Slice(colorBytes * 2, depthBytes));
                    commands.CopyTexture(color, sampled);
                    commands.Barrier(Stage.Transfer, Access.TransferRead | Access.TransferWrite,
                        Stage.Pixel | Stage.ColorOutput, Access.ShaderRead | Access.ColorWrite);
                    commands.BeginRenderPass(new RenderingDesc
                    {
                        Colors = [new ColorAttachment { RenderView = colorView, Load = LoadOp.Clear, Clear = Vector4.Zero }],
                    });
                    commands.SetViewport(new Viewport { Width = width, Height = height });
                    commands.SetScissor(new Scissor { Width = width, Height = height });
                    commands.SetDepthStencil(new DepthStencilState());
                    commands.SetTextureDescriptorHeap(textures);
                    commands.SetSamplerDescriptorHeap(samplers);
                    commands.BindPipeline(sampling);
                    commands.Draw(root.GPURange, 3);
                    commands.EndRenderPass();
                    commands.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                    commands.CopyTextureToMemory(color, readback.Range.GPURange.Slice(colorBytes, colorBytes));
                    commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                    commands.EndCommands();
                    pending = new TimelinePoint { Semaphore = timeline, Value = ++value };
                    device.Submit(new SubmitDesc { Commands = [commands], Completion = pending });
                    pending.Wait();
                    pending = default;
                    Vector4* pixels = (Vector4*)readback.Range.Cpu;
                    Vector4* sampledPixels = (Vector4*)(readback.Range.Cpu + colorBytes);
                    float* depths = (float*)(readback.Range.Cpu + colorBytes * 2);
                    bool clipped = depth < 0 || depth > 1;
                    bool coverageMatches = true;
                    bool positionsMatch = true;
                    bool uvMatches = true;
                    bool depthsMatch = true;
                    bool samplingMatches = true;
                    for (int y = 0; y < height; ++y)
                        for (int x = 0; x < width; ++x)
                        {
                            int index = y * width + x;
                            // The projected triangle is (9,9), (33,9), (9,21); no pixel center lies on its diagonal.
                            bool covered = !clipped && x >= 12 && x < 23 && y >= 11 && y < 17 && x + 2 * y < 50;
                            Vector4 actual = pixels[index];
                            coverageMatches &= covered == (actual != Vector4.Zero);
                            float expectedDepth = covered ? minDepth + depth * (maxDepth - minDepth) : 1;
                            depthsMatch &= MathF.Abs(depths[index] - expectedDepth) < 0.00001f;
                            if (covered)
                            {
                                positionsMatch &= MathF.Abs(actual.X - (x + 0.5f)) < 0.00001f && MathF.Abs(actual.Y - (y + 0.5f)) < 0.00001f;
                                uvMatches &= MathF.Abs(actual.W - (y + 0.5f - 7) / 32) < 0.00001f;
                                depthsMatch &= MathF.Abs(actual.Z - expectedDepth) < 0.00001f;
                            }
                            samplingMatches &= sampledPixels[index] == actual;
                        }
                    string name = $"{(path == 0 ? "vertex" : "mesh")} coordinates z={depth}, depth=[{minDepth},{maxDepth}]";
                    check(coverageMatches, $"{name}: Y-positive upper triangle, viewport/scissor origins and zero-to-one clipping");
                    if (!clipped)
                    {
                        check(positionsMatch, $"{name}: SV_Position.xy contains absolute top-left framebuffer pixel centers");
                        check(uvMatches, $"{name}: interpolated V increases downward, ddy(V)>0 and ddy(NDC Y)<0");
                    }
                    check(depthsMatch, $"{name}: SV_Position.z and stored D32 depth use the explicit viewport depth range");
                    check(samplingMatches, $"{name}: texture copy and top-left UV nearest sampling preserve every rendered pixel");
                    pool.Reset();
                }
        }
        finally
        {
            pending.Wait();
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            sampling?.Destroy();
            foreach (GPUPipeline? pipeline in pipelines) pipeline?.Destroy();
            samplers?.Destroy();
            textures?.Destroy();
            colorView?.Destroy();
            depthView?.Destroy();
            color?.Destroy();
            sampled?.Destroy();
            depthTexture?.Destroy();
            colorHeap.Destroy();
            sampledHeap.Destroy();
            depthHeap.Destroy();
            readback.Destroy();
            upload.Destroy();
        }
    }

    private static void RunRasterFacing(GPUDevice device, ShaderCompiler compiler, Action<bool, string> check)
    {
        CompiledShader compiled = compiler.CompileSource("regression_facing", "regression_facing.slang", FacingSource,
        [
            new ShaderEntryRequest("vertexMain", ShaderStageKind.Vertex),
            new ShaderEntryRequest("reverseVertexMain", ShaderStageKind.Vertex),
            new ShaderEntryRequest("facingMain", ShaderStageKind.Pixel),
            new ShaderEntryRequest("scalarFacingMain", ShaderStageKind.Pixel),
        ]);
        ReadOnlySpan<CullMode> culls = [CullMode.None, CullMode.Clockwise, CullMode.CounterClockwise];
        GPUPipeline[] pipelines = new GPUPipeline[6];
        GPUTextureHeap colorHeap = default;
        GPUTextureHeap stencilHeap = default;
        GPUTexture? color = null;
        GPUTexture? stencil = null;
        GPURenderView? colorView = null;
        GPURenderView? stencilView = null;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            for (int cull = 0; cull < culls.Length; ++cull)
                for (int winding = 0; winding < 2; ++winding)
                    pipelines[cull * 2 + winding] = device.CreateGraphicsPipeline(new GraphicsPipelineDesc
                    {
                        Vertex = compiled.GetGpuStage(winding == 0 ? "vertexMain" : "reverseVertexMain"),
                        Pixel = compiled.GetGpuStage(winding == 0 ? "facingMain" : "scalarFacingMain"),
                        ColorTargets = [new ColorTargetDesc { Format = Format.Rgba8Unorm }],
                        StencilFormat = Format.S8Uint,
                        Rasterization = new RasterizationState { Cull = culls[cull] },
                    });
            TextureDesc colorDesc = new() { Extent = new Extent3D(8, 8, 1), Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource };
            TextureDesc stencilDesc = new() { Extent = new Extent3D(8, 8, 1), Format = Format.S8Uint, Usage = TextureUsage.DepthStencilAttachment };
            colorHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(colorDesc).Size);
            stencilHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(stencilDesc).Size);
            color = device.CreateTexture(colorDesc, colorHeap, 0);
            stencil = device.CreateTexture(stencilDesc, stencilHeap, 0);
            colorView = color.CreateRenderView(new RenderViewDesc());
            stencilView = stencil.CreateRenderView(new RenderViewDesc());
            readback = device.CreateGPUHeap(8 * 8 * 4, MemoryType.Readback);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            StencilFaceState pass = new() { Compare = CompareOp.Equal, Reference = 37 };
            StencilFaceState fail = new() { Compare = CompareOp.Equal, Reference = 91 };
            ulong value = 0;
            for (int cull = 0; cull < culls.Length; ++cull)
                for (int winding = 0; winding < 2; ++winding)
                    for (int mode = 0; mode < 3; ++mode)
                    {
                        bool front = winding == 0;
                        bool rejected = culls[cull] == (front ? CullMode.CounterClockwise : CullMode.Clockwise);
                        bool visible = !rejected && (mode == 0 || (mode == 1) == front);
                        GPUCommandBuffer commands = pool.BeginCommands();
                        if (value == 0)
                        {
                            commands.InitializeTexture(color);
                            commands.InitializeTexture(stencil);
                        }
                        else
                        {
                            commands.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorWrite);
                            commands.Barrier(Stage.DepthStencilTests, Access.DepthStencilRead | Access.DepthStencilWrite,
                                Stage.DepthStencilTests, Access.DepthStencilRead | Access.DepthStencilWrite);
                        }
                        commands.BeginRenderPass(new RenderingDesc
                        {
                            Colors = [new ColorAttachment { RenderView = colorView, Load = LoadOp.Clear, Clear = Vector4.Zero }],
                            Stencil = new StencilAttachment { RenderView = stencilView, Load = LoadOp.Clear, Clear = 37 },
                        });
                        commands.BindPipeline(pipelines[cull * 2 + winding]);
                        DepthStencilState stencilState = new()
                        {
                            StencilTest = mode != 0,
                            Front = mode == 2 ? fail : pass,
                            Back = mode == 2 ? pass : fail,
                        };
                        commands.SetDepthStencil(stencilState);
                        commands.Draw(default, 3);
                        commands.EndRenderPass();
                        commands.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                        commands.CopyTextureToMemory(color, readback.Range.GPURange);
                        commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                        commands.EndCommands();
                        TimelinePoint completion = new() { Semaphore = timeline, Value = ++value };
                        device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                        completion.Wait();
                        bool matches = true;
                        byte* pixels = readback.Range.Cpu;
                        for (int pixel = 0; pixel < 64; ++pixel)
                            matches &= pixels[pixel * 4] == (visible && front ? 255 : 0) && pixels[pixel * 4 + 1] == (visible ? 255 : 0) &&
                                pixels[pixel * 4 + 2] == (visible ? 255 : 0) && pixels[pixel * 4 + 3] == (visible ? 255 : 0);
                        string reference = mode == 0 ? "disabled stencil" : $"Front={stencilState.Front.Reference}/Back={stencilState.Back.Reference}";
                        check(matches, $"{culls[cull]} culling preserves {(front ? "CCW front" : "CW back")} SV_IsFrontFace and {reference} pixel rejection");
                        pool.Reset();
                    }
        }
        finally
        {
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            colorView?.Destroy();
            stencilView?.Destroy();
            color?.Destroy();
            stencil?.Destroy();
            colorHeap.Destroy();
            stencilHeap.Destroy();
            foreach (GPUPipeline? pipeline in pipelines) pipeline?.Destroy();
        }
    }

    private static void RunStencilSampling(GPUDevice device, ShaderCompiler compiler, Action<bool, string> check)
    {
        CompiledShader compiled = compiler.CompileSource("regression_stencil", "regression_stencil.slang", StencilSource,
            ShaderPipelineKind.Compute);
        ShaderStage compute = compiled.GetGpuStage(ShaderStageKind.Compute);
        ReadOnlySpan<Format> formats = [Format.S8Uint, Format.D24UnormS8Uint, Format.D32FloatS8Uint];
        bool copyAfterRecording = device.ActiveBackend == GraphicsBackend.VulkanCompact;
        Span<uint> cachedOutput = stackalloc uint[18];
        cachedOutput.Fill(0xa5b6c7d8);
        foreach (Format format in formats)
        {
            const TextureUsage usage = TextureUsage.Sampled | TextureUsage.DepthStencilAttachment;
            if (!device.SupportsTextureFormat(format, usage))
            {
                Console.WriteLine($"  [SKIP] sampled stencil attachment {format} is unavailable");
                continue;
            }
            GPUTextureHeap textureHeap = default;
            GPUTexture? texture = null;
            GPURenderView? view = null;
            GPUTextureDescriptorHeap? descriptors = null;
            GPUPipeline? pipeline = null;
            GPUHeap upload = default;
            GPUHeap output = default;
            GPUHeap readback = default;
            GPUCommandPool? pool = null;
            GPUTimelineSemaphore? timeline = null;
            try
            {
                TextureDesc desc = new() { Extent = new Extent3D(4, 4, 1), Format = format, Usage = usage };
                textureHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(desc).Size);
                texture = device.CreateTexture(desc, textureHeap, 0);
                view = texture.CreateRenderView(new RenderViewDesc());
                descriptors = device.CreateTextureDescriptorHeap(4);
                TextureAspect aspect = format == Format.S8Uint ? TextureAspect.Automatic : TextureAspect.Stencil;
                TextureDescriptorDesc descriptor = new() { Aspect = aspect };
                if (!copyAfterRecording) descriptors.WriteTextureDescriptor(2, texture, TextureDescriptorType.Sampled, descriptor);
                pipeline = device.CreateComputePipeline(compute);
                upload = device.CreateGPUHeap(256);
                output = device.CreateGPUHeap(18 * sizeof(uint), MemoryType.GpuOnly);
                readback = device.CreateGPUHeap(18 * sizeof(uint), MemoryType.Readback);
                BumpAllocator arena = new(upload.Range);
                arena.Allocate(16);
                GPUAllocation<StencilData> root = arena.Allocate<StencilData>();
                GPUAllocation<uint> initializedOutput = arena.Allocate<uint>(18);
                StencilData cachedRoot = new() { Output = output.Range.View, TextureIndex = 2 };
                *root.Cpu = cachedRoot;
                cachedOutput.CopyTo(initializedOutput.CpuSpan);
                pool = device.CreateCommandPool(0);
                timeline = device.CreateTimelineSemaphore();
                GPUCommandBuffer commands = pool.BeginCommands();
                commands.InitializeTexture(texture);
                commands.CopyMemory(initializedOutput.GPURange, output.Range.GPURange);
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Compute, Access.ShaderWrite);
                commands.BeginRenderPass(new RenderingDesc
                {
                    Stencil = new StencilAttachment { RenderView = view, Load = LoadOp.Clear, Clear = 37 },
                });
                commands.EndRenderPass();
                commands.Barrier(Stage.DepthStencilTests, Access.DepthStencilWrite, Stage.Compute, Access.ShaderRead);
                commands.SetTextureDescriptorHeap(descriptors);
                commands.BindPipeline(pipeline);
                commands.Dispatch(root.GPURange, new Extent3D(1, 1, 1));
                commands.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Transfer, Access.TransferRead);
                commands.CopyMemory(output.Range.GPURange, readback.Range.GPURange);
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                commands.EndCommands();
                if (copyAfterRecording)
                {
                    GPUTextureDescriptorHeap source = device.CreateTextureDescriptorHeap(3);
                    try
                    {
                        source.WriteTextureDescriptor(1, texture, TextureDescriptorType.Sampled, descriptor);
                        source.CopyTextureDescriptors(0, source, 1, 2);
                        source.CopyTextureDescriptors(2, descriptors, 2, 1);
                    }
                    finally { source.Destroy(); }
                }
                TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
                device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                completion.Wait();
                uint* actual = (uint*)readback.Range.Cpu;
                bool matches = actual[0] == cachedOutput[0] && actual[17] == cachedOutput[17];
                for (int index = 1; index <= 16; ++index) matches &= actual[index] == 37;
                check(sizeof(StencilData) == 16 && matches,
                    $"{format} {aspect} stencil descriptor Texture2D<uint>.Load returns clear 37 in every texel and preserves output guards");
            }
            finally
            {
                device.WaitIdle();
                pool?.Destroy();
                timeline?.Destroy();
                pipeline?.Destroy();
                descriptors?.Destroy();
                view?.Destroy();
                texture?.Destroy();
                textureHeap.Destroy();
                readback.Destroy();
                output.Destroy();
                upload.Destroy();
            }
        }
    }

    private static void RunVulkanCompactRejections(GPUDevice device, Action<bool, string> check)
    {
        bool rejectedPipeline = false;
        try { device.CreateMeshPipeline(new MeshPipelineDesc()).Destroy(); }
        catch (GPUException exception) when (exception.Error == Error.Unsupported) { rejectedPipeline = true; }
        check(rejectedPipeline, "vulkan-compact rejects mesh pipelines without consuming shader code");

        GPUCommandPool pool = device.CreateCommandPool(0);
        try
        {
            GPUCommandBuffer commands = pool.BeginCommands();
            bool rejectedDirect = false;
            bool rejectedIndirect = false;
            try { commands.DrawMeshlets(default, new Extent3D(1, 1, 1)); }
            catch (GPUException exception) when (exception.Error == Error.Unsupported) { rejectedDirect = true; }
            try { commands.DrawMeshletsIndirect(default, default); }
            catch (GPUException exception) when (exception.Error == Error.Unsupported) { rejectedIndirect = true; }
            commands.EndCommands();
            check(rejectedDirect && rejectedIndirect, "vulkan-compact rejects direct and indirect mesh draws");
        }
        finally { pool.Destroy(); }
    }

    private static void RunMeshDraws(GPUDevice device, ShaderCompiler compiler, Action<bool, string> check)
    {
        CompiledShader compiled = compiler.CompileSource("regression_mesh", "regression_mesh.slang", MeshSource,
        [
            new ShaderEntryRequest("meshMain", ShaderStageKind.Mesh),
            new ShaderEntryRequest("pixelMain", ShaderStageKind.Pixel),
            new ShaderEntryRequest("taskMain", ShaderStageKind.Task),
            new ShaderEntryRequest("taskMeshMain", ShaderStageKind.Mesh),
        ]);
        ColorTargetDesc colorTarget = new() { Format = Format.Rgba8Unorm };
        colorTarget.Blend.Enabled = true;
        colorTarget.Blend.Color.Destination = BlendFactor.One;
        colorTarget.Blend.Alpha.Destination = BlendFactor.One;
        GPUPipeline[] pipelines = new GPUPipeline[2];
        GPUTextureHeap colorHeap = default;
        GPUTexture? color = null;
        GPURenderView? view = null;
        GPUHeap upload = default;
        GPUHeap argumentsHeap = default;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            ShaderStage task = compiled.GetGpuStage("taskMain");
            ShaderStage meshMain = compiled.GetGpuStage("meshMain");
            ShaderStage taskMeshMain = compiled.GetGpuStage("taskMeshMain");
            ShaderStage pixelStage = compiled.GetGpuStage("pixelMain");
            for (int index = 0; index < pipelines.Length; ++index)
                pipelines[index] = device.CreateMeshPipeline(new MeshPipelineDesc
                {
                    Task = index == 0 ? default : task,
                    Mesh = index == 0 ? meshMain : taskMeshMain,
                    Pixel = pixelStage,
                    ColorTargets = [colorTarget],
                });
            TextureDesc desc = new() { Extent = new Extent3D(8, 8, 1), Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource };
            colorHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(desc).Size);
            color = device.CreateTexture(desc, colorHeap, 0);
            view = color.CreateRenderView(new RenderViewDesc());
            upload = device.CreateGPUHeap(256);
            pool = device.CreateCommandPool(0);
            BumpAllocator arena = new(upload.Range);
            arena.Allocate(16);
            GPUAllocation<MeshData> root = arena.Allocate<MeshData>();
            ReadOnlySpan<uint> cachedArguments = [0, 0, 0, 0, 2, 1, 1, 0, 1, 1, 1, 0, 2, 1, 1];
            GPUAllocation<uint> uploadedArguments = arena.Allocate<uint>((ulong)cachedArguments.Length);
            cachedArguments.CopyTo(uploadedArguments.CpuSpan);
            check(sizeof(MeshData) == 16, "mesh/task root retains its 16-byte C layout");
            argumentsHeap = device.CreateGPUHeap(256, MemoryType.GpuOnly);
            readback = device.CreateGPUHeap(8 * 8 * 4, MemoryType.Readback);
            timeline = device.CreateTimelineSemaphore();
            GPURange arguments = argumentsHeap.Range.GPURange.Slice(32, (ulong)cachedArguments.Length * sizeof(uint));
            if (!device.Caps.IndirectMeshDraw)
                Console.WriteLine("  [SKIP] indirect mesh/task draws are unavailable; direct mesh/task execution remains covered");
            ulong value = 0;
            for (int pipelineIndex = 0; pipelineIndex < pipelines.Length; ++pipelineIndex)
                for (int mode = 0; mode < (device.Caps.IndirectMeshDraw ? 4 : 1); ++mode)
                {
                    uint seed = (uint)(11 + pipelineIndex * 3 + mode);
                    *root.Cpu = new MeshData { Seed = seed };
                    GPUCommandBuffer commands = pool.BeginCommands();
                    if (value == 0)
                    {
                        commands.InitializeTexture(color);
                        commands.CopyMemory(uploadedArguments.GPURange, arguments);
                        commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Indirect, Access.IndirectRead);
                    }
                    else commands.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorRead | Access.ColorWrite);
                    commands.BeginRenderPass(new RenderingDesc
                    {
                        Colors = [new ColorAttachment { RenderView = view, Load = LoadOp.Clear, Clear = Vector4.Zero }],
                    });
                    commands.BindPipeline(pipelines[pipelineIndex]);
                    if (mode == 0) commands.DrawMeshlets(root.GPURange, new Extent3D(2, 1, 1));
                    else if (mode == 1) commands.DrawMeshletsIndirect(root.GPURange, arguments.Slice(16, 12));
                    else if (mode == 2) commands.DrawMeshletsIndirect(root.GPURange, arguments.Slice(32, 28), drawCount: 2, stride: 16);
                    else
                    {
                        commands.DrawMeshlets(root.GPURange, new Extent3D(1, 1, 1));
                        commands.DrawMeshletsIndirect(default, arguments.Slice(32, 28), drawCount: 2, stride: 16);
                    }
                    commands.EndRenderPass();
                    commands.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                    commands.CopyTextureToMemory(color, readback.Range.GPURange);
                    commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                    commands.EndCommands();
                    TimelinePoint completion = new() { Semaphore = timeline, Value = ++value };
                    device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                    completion.Wait();
                    uint primitiveCount = mode == 3 ? 4u : mode == 2 ? 3u : 2u;
                    uint red = (seed + (pipelineIndex == 0 ? 0u : 17u)) * primitiveCount;
                    bool matches = true;
                    byte* pixels = readback.Range.Cpu;
                    for (int pixel = 0; pixel < 64; ++pixel)
                        matches &= Math.Abs(pixels[pixel * 4] - (int)red) <= 1 &&
                            Math.Abs(pixels[pixel * 4 + 1] - (int)(7 * primitiveCount)) <= 1 &&
                            Math.Abs(pixels[pixel * 4 + 2] - (int)(13 * primitiveCount)) <= 1 &&
                            Math.Abs(pixels[pixel * 4 + 3] - (int)(17 * primitiveCount)) <= 1;
                    string path = mode switch
                    {
                        0 => "direct",
                        1 => "indirect default stride",
                        2 => "indirect two draws with explicit stride",
                        _ => "indirect two draws preserving the previous root",
                    };
                    check(matches, $"{(pipelineIndex == 0 ? "mesh-only" : "task payload + mesh")} {path} preserves roots, group counts and additive pixels");
                    pool.Reset();
                }
        }
        finally
        {
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            argumentsHeap.Destroy();
            upload.Destroy();
            view?.Destroy();
            color?.Destroy();
            colorHeap.Destroy();
            foreach (GPUPipeline? pipeline in pipelines) pipeline?.Destroy();
        }
    }

    private static void RunRasterDefaults(GPUDevice device, CompiledShader shader, Action<bool, string> check)
    {
        ColorTargetDesc blendTarget = new() { Format = Format.Rgba8Unorm };
        blendTarget.Blend.Enabled = true;
        GPUPipeline? plain = null;
        GPUPipeline? blend = null;
        GPUPipeline? stencil = null;
        GPUTextureHeap colorHeap = default;
        GPUTextureHeap stencilHeap = default;
        GPUTexture? color = null;
        GPUTexture? stencilTexture = null;
        GPURenderView? colorView = null;
        GPURenderView? stencilView = null;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            ShaderStage vertex = shader.GetGpuStage("vertexMain");
            ShaderStage pixelStage = shader.GetGpuStage("pixelMain");
            plain = device.CreateGraphicsPipeline(new GraphicsPipelineDesc
            {
                Vertex = vertex,
                Pixel = pixelStage,
                ColorTargets = [new ColorTargetDesc { Format = Format.Rgba8Unorm }],
            });
            blend = device.CreateGraphicsPipeline(new GraphicsPipelineDesc
            {
                Vertex = vertex,
                Pixel = pixelStage,
                ColorTargets = [blendTarget],
            });
            stencil = device.CreateGraphicsPipeline(new GraphicsPipelineDesc
            {
                Vertex = vertex,
                Pixel = pixelStage,
                ColorTargets = [new ColorTargetDesc { Format = Format.Rgba8Unorm }],
                StencilFormat = Format.S8Uint,
            });
            TextureDesc colorDesc = new() { Extent = new Extent3D(8, 8, 1), Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource };
            TextureDesc stencilDesc = new() { Extent = new Extent3D(8, 8, 1), Format = Format.S8Uint, Usage = TextureUsage.DepthStencilAttachment };
            colorHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(colorDesc).Size);
            stencilHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(stencilDesc).Size);
            color = device.CreateTexture(colorDesc, colorHeap, 0);
            stencilTexture = device.CreateTexture(stencilDesc, stencilHeap, 0);
            colorView = color.CreateRenderView(new RenderViewDesc());
            stencilView = stencilTexture.CreateRenderView(new RenderViewDesc());
            readback = device.CreateGPUHeap(8 * 8 * 4, MemoryType.Readback);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            int defaultDepth = -1;
            for (uint mode = 0; mode < 5; ++mode)
            {
                GPUCommandBuffer commands = pool.BeginCommands();
                if (mode == 0)
                {
                    commands.InitializeTexture(color);
                    commands.InitializeTexture(stencilTexture);
                }
                else commands.Barrier(Stage.Transfer, Access.TransferRead, Stage.ColorOutput, Access.ColorRead | Access.ColorWrite);
                if (mode == 4)
                    commands.Barrier(Stage.DepthStencilTests, Access.DepthStencilWrite,
                        Stage.DepthStencilTests, Access.DepthStencilRead | Access.DepthStencilWrite);
                commands.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new ColorAttachment { RenderView = colorView, Load = LoadOp.Clear, Clear = Vector4.Zero }],
                    Stencil = mode >= 3 ? new StencilAttachment { RenderView = stencilView, Load = LoadOp.Clear, Clear = 77 } : default,
                });
                if (mode == 1) commands.SetViewport(new Viewport { Width = 8, Height = 8 });
                if (mode >= 3) commands.SetDepthStencil(new DepthStencilState { StencilTest = true });
                commands.BindPipeline(mode >= 3 ? stencil : mode == 2 ? blend : plain);
                commands.Draw(default, 3, firstVertex: mode == 4 ? 3u : 0u);
                commands.EndRenderPass();
                commands.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
                commands.CopyTextureToMemory(color, readback.Range.GPURange);
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                commands.EndCommands();
                TimelinePoint completion = new() { Semaphore = timeline, Value = mode + 1 };
                device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                completion.Wait();
                bool pixelsMatch = true;
                byte* pixels = readback.Range.Cpu;
                for (int pixel = 0; pixel < 64; ++pixel)
                {
                    for (int channel = 0; channel < 3; ++channel)
                        pixelsMatch &= pixels[pixel * 4 + channel] >= 127 && pixels[pixel * 4 + channel] <= 128;
                    pixelsMatch &= pixels[pixel * 4 + 3] == 255;
                }
                if (mode == 0) defaultDepth = pixels[0];
                string name = mode switch
                {
                    0 => "default viewport maps clip z=0.5 to 127/128 in every output pixel",
                    1 => "explicit and default viewports have the same depth mapping",
                    2 => "enabled default blending preserves source color and alpha",
                    3 => "default stencil comparison permits the fullscreen draw",
                    _ => "default stencil comparison also permits the opposite fullscreen winding",
                };
                check(pixelsMatch && (mode != 1 || pixels[0] == defaultDepth), name);
                pool.Reset();
            }
        }
        finally
        {
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            colorView?.Destroy();
            stencilView?.Destroy();
            color?.Destroy();
            stencilTexture?.Destroy();
            colorHeap.Destroy();
            stencilHeap.Destroy();
            plain?.Destroy();
            blend?.Destroy();
            stencil?.Destroy();
        }
    }

    private static bool HasSamplerHeapRecordingDriverIssue(GPUDevice device)
    {
        if (!OperatingSystem.IsWindows() || device.GetType().FullName != "Alco.Graphics.LowLevel.VulkanDevice" ||
            device.Caps.DeviceName != "NVIDIA GeForce RTX 4070 Ti")
            return false;
        Vulkan.VkPhysicalDeviceProperties properties = (Vulkan.VkPhysicalDeviceProperties)device.GetType()
            .GetField("PhysicalProperties", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(device)!;
        return properties.vendorID == 0x10de && (properties.driverVersion >> 22) == 617 && ((properties.driverVersion >> 14) & 0xff) == 42;
    }

    private static void RunBufferViews(GPUDevice device, Action<bool, string> check)
    {
        const int entryCount = 4;
        const int valuesPerEntry = 16;
        const int counterIndex = valuesPerEntry + 3;
        const int previousIndex = valuesPerEntry + 5;
        const int outputWords = valuesPerEntry * 2 + 6;
        const int outputBytes = outputWords * sizeof(uint);
        const uint guard = 0xa5b6c7d8;
        using ShaderCompiler compiler = new(device);
        CompiledShader compiled = compiler.CompileSource("regression_buffer_views", "regression_buffer_views.slang", BufferViewSource,
            ShaderPipelineKind.Compute);
        GPUHeap[] sourceHeaps = new GPUHeap[entryCount];
        GPUHeap[] destinationHeaps = new GPUHeap[entryCount];
        GPUAllocation<uint>[] sources = new GPUAllocation<uint>[entryCount];
        GPUAllocation<uint>[] destinations = new GPUAllocation<uint>[entryCount];
        BufferViewEntry[] cachedEntries = new BufferViewEntry[entryCount];
        GPUHeap upload = default;
        GPUHeap readback = default;
        GPUTextureHeap textureHeap = default;
        GPUTexture? texture = null;
        GPUTextureDescriptorHeap? textures = null;
        GPUSamplerDescriptorHeap? samplers = null;
        GPUPipeline? pipeline = null;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            pipeline = device.CreateComputePipeline(compiled.GetGpuStage(ShaderStageKind.Compute));
            upload = device.CreateGPUHeap(4096);
            readback = device.CreateGPUHeap(entryCount * outputBytes, MemoryType.Readback);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            BumpAllocator arena = new(upload.Range);
            arena.Allocate(16);
            GPUAllocation<BufferViewEntry> entries = arena.Allocate<BufferViewEntry>(entryCount);
            GPUAllocation<BufferViewData> root = arena.Allocate<BufferViewData>();
            GPUAllocation<uint> guards = arena.Allocate<uint>(entryCount * outputWords);
            Span<uint> cachedGuards = stackalloc uint[entryCount * outputWords];
            cachedGuards.Fill(guard);
            GPUAllocation<byte> texel = arena.Allocate(4);
            ReadOnlySpan<byte> cachedTexel = [17, 43, 91, 255];
            cachedTexel.CopyTo(texel.CpuSpan);
            TextureDesc textureDesc = new() { Extent = new Extent3D(1, 1, 1), Usage = TextureUsage.Sampled | TextureUsage.TransferDestination };
            textureHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(textureDesc).Size);
            texture = device.CreateTexture(textureDesc, textureHeap, 0);
            textures = device.CreateTextureDescriptorHeap(4);
            samplers = device.CreateSamplerDescriptorHeap(4);
            textures.WriteTextureDescriptor(2, texture, TextureDescriptorType.Sampled, new TextureDescriptorDesc());
            samplers.WriteSamplerDescriptor(3, new SamplerDesc());
            GPUCommandBuffer initialize = pool.BeginCommands();
            initialize.InitializeTexture(texture);
            initialize.CopyMemoryToTexture(texel.GPURange, texture);
            initialize.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Compute, Access.ShaderRead);
            initialize.EndCommands();
            TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [initialize], Completion = completion });
            completion.Wait();
            pool.Reset();
            for (int index = 0; index < entryCount; ++index)
            {
                sourceHeaps[index] = device.CreateGPUHeap(4096, index == 2 ? MemoryType.Readback : MemoryType.CpuVisible);
                destinationHeaps[index] = device.CreateGPUHeap(4096, index == 2 ? MemoryType.Readback : MemoryType.GpuOnly);
                BumpAllocator sourceArena = new(sourceHeaps[index].Range);
                BumpAllocator destinationArena = new(destinationHeaps[index].Range);
                sourceArena.Allocate((ulong)(32 + index * 16));
                destinationArena.Allocate((ulong)(48 + index * 16));
                sources[index] = sourceArena.Allocate<uint>(valuesPerEntry + 1);
                destinations[index] = destinationArena.Allocate<uint>(outputWords);
            }
            bool indicesDistinct = true;
            for (int index = 0; index < entryCount; ++index)
            {
                indicesDistinct &= sources[index].View.Index != 0 && destinations[index].View.Index != 0 &&
                    sources[index].View.Offset == 32 + index * 16 && destinations[index].View.Offset == 48 + index * 16 &&
                    ReferenceEquals(sources[index].Buffer, sourceHeaps[index].Owner) &&
                    ReferenceEquals(destinations[index].Buffer, destinationHeaps[index].Owner) &&
                    sources[index].Offset == (ulong)(32 + index * 16) && destinations[index].Offset == (ulong)(48 + index * 16) &&
                    sources[index].Cpu != null && sources[index].CpuSpan.Length == valuesPerEntry + 1 &&
                    (index == 2 ? destinations[index].Cpu != null && destinations[index].CpuSpan.Length == outputWords :
                        destinations[index].Cpu == null && destinations[index].CpuSpan.IsEmpty);
                for (int previous = 0; previous < index; ++previous)
                    indicesDistinct &= sources[index].View.Index != sources[previous].View.Index &&
                        destinations[index].View.Index != destinations[previous].View.Index;
                for (int destination = 0; destination < entryCount; ++destination)
                    indicesDistinct &= sources[index].View.Index != destinations[destination].View.Index;
            }
            check(indicesDistinct, "independent upload/readback/GPU-only heaps receive distinct nonzero buffer indices, mappings and suballocation offsets");
            Span<uint> cachedSource = stackalloc uint[valuesPerEntry + 1];
            for (uint round = 0; round < 2; ++round)
            {
                for (int index = 0; index < entryCount; ++index)
                {
                    for (int element = 0; element < cachedSource.Length; ++element)
                        cachedSource[element] = 0x10203040u + round * 10000 + (uint)(index * 1000 + element * 17);
                    cachedSource.CopyTo(sources[index].CpuSpan);
                    cachedGuards[index * outputWords + counterIndex] = round * 10000 + (uint)index * 1000 + 17;
                    cachedEntries[index] = new BufferViewEntry
                    {
                        Source = sources[index].View,
                        Destination = destinations[(index + 1) % entryCount].View,
                    };
                }
                cachedEntries.AsSpan().CopyTo(entries.CpuSpan);
                cachedGuards.CopyTo(guards.CpuSpan);
                *root.Cpu = new BufferViewData { Entries = entries.View, EntryCount = entryCount, Seed = round * 123 + 7, TextureIndex = 2, SamplerIndex = 3 };
                GPUCommandBuffer commands = pool.BeginCommands();
                for (int index = 0; index < entryCount; ++index)
                    commands.CopyMemory(guards.GPURange.Slice((ulong)(index * outputBytes), outputBytes), destinations[index].GPURange);
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Compute, Access.ShaderRead | Access.ShaderWrite);
                commands.SetTextureDescriptorHeap(textures);
                commands.SetSamplerDescriptorHeap(samplers);
                commands.BindPipeline(pipeline);
                commands.Dispatch(root.GPURange, new Extent3D(1, 1, 1));
                commands.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Transfer, Access.TransferRead);
                for (int index = 0; index < entryCount; ++index)
                    commands.CopyMemory(destinations[index].GPURange,
                        readback.Range.GPURange.Slice((ulong)(index * outputBytes), outputBytes));
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                commands.EndCommands();
                ++completion.Value;
                device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                completion.Wait();
                bool matches = true;
                uint* actual = (uint*)readback.Range.Cpu;
                for (int index = 0; index < entryCount; ++index)
                    matches &= actual[index * outputWords] == guard && actual[index * outputWords + 1] == guard;
                for (uint lane = 0; lane < 64; ++lane)
                {
                    uint entry = (lane * 3 + 1) % entryCount;
                    uint destination = (entry + 1) % entryCount;
                    uint element = lane / entryCount;
                    uint value = 0x10203040u + round * 10000 + entry * 1000 + (element + 1) * 17;
                    uint expected = (value ^ (round * 123 + 7 + lane)) + 60;
                    matches &= actual[destination * outputWords + element + 2] == expected;
                }
                check(matches, $"nonuniform upload/readback/GPU-only views preserve byte offsets, guards and texture/sampler heaps in round {round}");
                bool atomicsMatch = true;
                for (int index = 0; index < entryCount; ++index)
                {
                    uint seed = round * 10000 + (uint)index * 1000 + 17;
                    uint* result = actual + index * outputWords;
                    atomicsMatch &= result[counterIndex] == seed + valuesPerEntry * 3 && result[counterIndex - 1] == guard &&
                        result[counterIndex + 1] == guard && result[outputWords - 1] == guard;
                    uint seen = 0;
                    for (int element = 0; element < valuesPerEntry; ++element)
                    {
                        uint previous = result[previousIndex + element];
                        if (previous < seed || (previous - seed) % 3 != 0 || (previous - seed) / 3 >= valuesPerEntry)
                        {
                            atomicsMatch = false;
                            continue;
                        }
                        uint bit = 1u << (int)((previous - seed) / 3);
                        atomicsMatch &= (seen & bit) == 0;
                        seen |= bit;
                    }
                    atomicsMatch &= seen == (1u << valuesPerEntry) - 1;
                }
                check(atomicsMatch, $"nonuniform atomic adds preserve distinct seeds, add-three old values, offsets and guards in round {round}");
                pool.Reset();
                if (round == 0)
                {
                    uint releasedIndex = sourceHeaps[1].Range.View.Index;
                    sourceHeaps[1].Destroy();
                    sourceHeaps[1] = default;
                    sourceHeaps[1] = device.CreateGPUHeap(4096);
                    BumpAllocator replacement = new(sourceHeaps[1].Range);
                    replacement.Allocate(48);
                    sources[1] = replacement.Allocate<uint>(valuesPerEntry + 1);
                    check(sourceHeaps[1].Range.View.Index == releasedIndex && sources[1].View.Offset == 48,
                        "destroying a heap after GPU completion reuses its private buffer-table slot for new backing storage");
                }
            }
            // NVIDIA 617.42 (RTX 4070 Ti) driver issue: repeated unsubmitted sampler-heap bindings can cause DeviceLost after heap memory is freed.
            // The Copy Engine can write to the freed reserved-range page even after command-buffer reset, pool destruction, and WaitIdle.
            // This also reproduces without shaders or application submissions; validation can mask it.
            if (Environment.GetEnvironmentVariable("ALCO_GRAPHICS_TEST_SAMPLER_HEAP_STRESS") != "1" && HasSamplerHeapRecordingDriverIssue(device))
                Console.WriteLine("  [SKIP] sampler-heap recording allocation stress: NVIDIA 617.42 on RTX 4070 Ti; " +
                    "set ALCO_GRAPHICS_TEST_SAMPLER_HEAP_STRESS=1 to run");
            else
            {
                long allocatedBefore = 0;
                for (int iteration = -128; iteration < 1024; ++iteration)
                {
                    if (iteration == 0) allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    GPUCommandBuffer commands = pool.BeginCommands();
                    commands.SetTextureDescriptorHeap(textures);
                    commands.SetSamplerDescriptorHeap(samplers);
                    commands.BindPipeline(pipeline);
                    commands.Dispatch(root.GPURange, new Extent3D(1, 1, 1));
                    commands.EndCommands();
                    pool.Reset();
                }
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                check(allocatedBytes == 0,
                    $"warmed buffer-view root and texture/sampler heap recording allocates no managed memory ({allocatedBytes} bytes)");
            }
        }
        finally
        {
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            pipeline?.Destroy();
            samplers?.Destroy();
            textures?.Destroy();
            texture?.Destroy();
            textureHeap.Destroy();
            for (int index = 0; index < entryCount; ++index)
            {
                destinationHeaps[index].Destroy();
                sourceHeaps[index].Destroy();
            }
            readback.Destroy();
            upload.Destroy();
        }
    }

    private static void RunRoots(GPUDevice device, CompiledShader consumeShader, CompiledShader generateShader, Action<bool, string> check)
    {
        check(sizeof(RootData) == 256 && sizeof(GenerateData) == 32 &&
            (int)Marshal.OffsetOf<RootData>(nameof(RootData.OutputIndex)) == 8 && (int)Marshal.OffsetOf<RootData>(nameof(RootData.Values)) == 16 &&
            (int)Marshal.OffsetOf<GenerateData>(nameof(GenerateData.Output)) == 8 &&
            (int)Marshal.OffsetOf<GenerateData>(nameof(GenerateData.Arguments)) == 16 &&
            (int)Marshal.OffsetOf<GenerateData>(nameof(GenerateData.FirstIndex)) == 24,
            "buffer-view GPU roots retain their 256/32-byte C layouts and all view/scalar/array offsets");
        GPUPipeline? consume = null;
        GPUPipeline? generate = null;
        GPUHeap cpuRoots = default;
        GPUHeap gpuRoots = default;
        GPUHeap output = default;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUCommandPool? externalPool = null;
        GPUTimelineSemaphore? timeline = null;
        try
        {
            consume = device.CreateComputePipeline(consumeShader.GetGpuStage(ShaderStageKind.Compute));
            generate = device.CreateComputePipeline(generateShader.GetGpuStage(ShaderStageKind.Compute));
            cpuRoots = device.CreateGPUHeap(3 * 256 + 2 * 32);
            gpuRoots = device.CreateGPUHeap(2 * 256 + 16, MemoryType.GpuOnly);
            output = device.CreateGPUHeap(6 * 64 * sizeof(uint), MemoryType.GpuOnly);
            readback = device.AllocateGPUHeap(size: output.Range.SizeBytes, memory: MemoryType.Readback);
            pool = device.CreateCommandPool(0);
            externalPool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            bool readbackMatches = readback.Range.Cpu != null && readback.Range.Buffer != null && readback.Range.SizeBytes == output.Range.SizeBytes;
            check(readbackMatches, "named AllocateGPUHeap(size:, memory:) preserves the readback allocation contract");
            if (!readbackMatches) return;
            BumpAllocator arena = new(cpuRoots.Range);
            for (uint round = 0; round < 4; ++round)
            {
                GPUCommandBuffer first = pool.BeginCommands();
                GPUCommandBuffer second = pool.BeginCommands();
                first.BindPipeline(consume);
                second.BindPipeline(consume);
                RootData root = new() { Output = output.Range.View };
                for (uint index = 0; index < 3; ++index)
                {
                    root.OutputIndex = index * 64;
                    root.Seed = round * 100 + index;
                    for (uint value = 0; value < 60; ++value) root.Values[value] = root.Seed * 7 + value;
                    GPUAllocation<RootData> copied = arena.Allocate<RootData>();
                    *copied.Cpu = root;
                    (index == 1 ? second : first).Dispatch(copied.GPURange, new Extent3D(1, 1, 1));
                }
                GenerateData generator = new()
                {
                    Destination = gpuRoots.Range.View,
                    Output = output.Range.View,
                    Arguments = gpuRoots.Range.View.OffsetBy(512),
                    FirstIndex = 3,
                    Seed = round * 100 + 3,
                };
                GPUAllocation<GenerateData> generated = arena.AllocateAtomic<GenerateData>();
                *generated.Cpu = generator;
                second.BindPipeline(generate);
                second.Dispatch(generated.GPURange, new Extent3D(2, 1, 1));
                first.EndCommands();
                second.EndCommands();
                GPUCommandBuffer third = externalPool.BeginCommands();
                third.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Compute | Stage.Indirect, Access.ShaderRead | Access.IndirectRead);
                third.BindPipeline(consume);
                third.Dispatch(gpuRoots.Range.GPURange.Slice(0, 256), new Extent3D(1, 1, 1));
                third.DispatchIndirect(gpuRoots.Range.GPURange.Slice(256, 256), gpuRoots.Range.GPURange.Slice(512, 12));
                third.Barrier(Stage.Compute | Stage.Indirect, Access.ShaderRead | Access.IndirectRead, Stage.Compute, Access.ShaderWrite);
                generator.FirstIndex = 5;
                generator.Seed = round * 100 + 5;
                GPUAllocation<GenerateData> rewritten = arena.Allocate<GenerateData>();
                *rewritten.Cpu = generator;
                third.BindPipeline(generate);
                third.Dispatch(rewritten.GPURange, new Extent3D(1, 1, 1));
                third.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Compute, Access.ShaderRead);
                third.BindPipeline(consume);
                third.Dispatch(gpuRoots.Range.GPURange.Slice(0, 256), new Extent3D(1, 1, 1));
                third.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Transfer, Access.TransferRead);
                third.CopyMemory(output.Range.GPURange, readback.Range.GPURange);
                third.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                third.EndCommands();
                TimelinePoint completion = new() { Semaphore = timeline, Value = round + 1 };
                device.Submit(new SubmitDesc { Commands = [first, second, third], Completion = completion });
                completion.Wait();
                bool matches = true;
                uint* actual = (uint*)readback.Range.Cpu;
                for (uint index = 0; index < 6; ++index)
                    for (uint lane = 0; lane < 64; ++lane)
                    {
                        uint seed = round * 100 + index;
                        uint expected = seed + lane;
                        for (uint value = 0; value < 60; ++value) expected = unchecked((expected * 33) ^ (seed * 7 + value));
                        matches &= actual[index * 64 + lane] == expected;
                    }
                check(matches, $"256-byte CPU/GPU roots, indirect dispatch and interleaved command buffers match in round {round}");
                pool.Reset();
                externalPool.Reset();
                arena.Reset();
            }
            UploadQueue uploads = new(device, 1024);
            try
            {
                GPUAllocation<byte> stagingStorage = uploads._state.Heap.Range;
                ReadOnlySpan<ulong> sizes = [256, 384, 256, 384, 256];
                ReadOnlySpan<uint> offsets = [0, 256, 640, 0, 384];
                uint seed = 0;
                GPUBufferView stagedView = default;
                GPURange stagedRange = default;
                Action<GPUCommandBuffer, GPUAllocation<byte>> record = (GPUCommandBuffer commands, GPUAllocation<byte> staging) =>
                {
                    RootData root = new() { Output = output.Range.View, Seed = seed };
                    for (uint index = 0; index < 60; ++index) root.Values[index] = seed * 7 + index;
                    *(RootData*)staging.Cpu = root;
                    stagedView = staging.View;
                    stagedRange = staging.GPURange;
                    commands.BindPipeline(consume);
                    commands.Dispatch(staging.GPURange, new Extent3D(1, 1, 1));
                };
                bool viewsMatch = stagingStorage.View.Index != 0;
                bool rangesMatch = stagingStorage.Buffer != null;
                bool valuesMatch = true;
                for (int iteration = 0; iteration < sizes.Length; ++iteration)
                {
                    seed = (uint)iteration + 700;
                    uploads.UploadWithCompute(sizes[iteration], record);
                    uploads.Wait();
                    viewsMatch &= stagedView.Index == stagingStorage.View.Index && stagedView.Offset == stagingStorage.View.Offset + offsets[iteration];
                    rangesMatch &= ReferenceEquals(stagedRange.Buffer, stagingStorage.Buffer) &&
                        stagedRange.Offset == stagingStorage.Offset + offsets[iteration] && stagedRange.Size == sizes[iteration];
                    GPUCommandBuffer copied = pool.BeginCommands();
                    copied.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Transfer, Access.TransferRead);
                    copied.CopyMemory(output.Range.GPURange.Slice(0, 64 * sizeof(uint)), readback.Range.GPURange);
                    copied.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                    copied.EndCommands();
                    TimelinePoint completion = new() { Semaphore = timeline, Value = (ulong)iteration + 5 };
                    device.Submit(new SubmitDesc { Commands = [copied], Completion = completion });
                    completion.Wait();
                    uint* actual = (uint*)readback.Range.Cpu;
                    for (uint lane = 0; lane < 64; ++lane)
                    {
                        uint expected = seed + lane;
                        for (uint index = 0; index < 60; ++index) expected = unchecked((expected * 33) ^ (seed * 7 + index));
                        valuesMatch &= actual[lane] == expected;
                    }
                    pool.Reset();
                }
                check(viewsMatch && rangesMatch,
                    "upload-ring compute reservations preserve owner identity, shader-view index, exact sizes and aligned byte offsets through wraparound");
                check(valuesMatch, "256-byte buffer-view roots staged through a wrapping upload ring execute before their storage is reclaimed");
            }
            finally
            {
                uploads.Wait();
                uploads.Destroy();
            }
        }
        finally
        {
            device.WaitIdle();
            pool?.Destroy();
            externalPool?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            output.Destroy();
            gpuRoots.Destroy();
            cpuRoots.Destroy();
            generate?.Destroy();
            consume?.Destroy();
        }
    }

    private static void RunTextureRegionCopies(GPUDevice device, Action<bool, string> check)
    {
        const TextureUsage usage = TextureUsage.TransferSource | TextureUsage.TransferDestination;
        TextureDesc source = new() { Extent = new Extent3D(13, 9, 1), Usage = usage | TextureUsage.ColorAttachment };
        TextureDesc destination = source;
        destination.Usage = usage;
        RunTextureRegionCopy(device, source, destination, [default], check, "default RGBA offscreen clear/screenshot copy", clearSource: true);

        source = new TextureDesc { Extent = new Extent3D(32, 24, 1), MipLevels = 3, Usage = usage };
        destination = new TextureDesc { Extent = new Extent3D(16, 12, 1), MipLevels = 3, Usage = usage };
        RunTextureRegionCopy(device, source, destination,
        [
            new TextureRegionCopyDesc
            {
                SourceMipLevel = 2, DestinationMipLevel = 1,
                SourceOffset = new Extent3D(2, 1, 0), DestinationOffset = new Extent3D(1, 2, 0), Extent = new Extent3D(3, 3, 1),
            },
            new TextureRegionCopyDesc
            {
                SourceMipLevel = 1,
                SourceOffset = new Extent3D(3, 2, 0), DestinationOffset = new Extent3D(1, 1, 0), Extent = new Extent3D(0, 3, 0),
            },
        ], check, "independent mip indices/offsets and per-component source-default extents");

        ReadOnlySpan<TextureType> types = [TextureType.TwoDArray, TextureType.Cube, TextureType.CubeArray];
        foreach (TextureType type in types)
        {
            source = new TextureDesc
            {
                Type = type, Extent = new Extent3D(8, 8, 1), MipLevels = 2,
                LayerCount = type == TextureType.TwoDArray ? 4u : type == TextureType.Cube ? 6u : 12u,
                Format = Format.R8Unorm, Usage = usage,
            };
            destination = source;
            destination.LayerCount = type == TextureType.TwoDArray ? 6u : type == TextureType.Cube ? 6u : 18u;
            RunTextureRegionCopy(device, source, destination,
            [
                new TextureRegionCopyDesc
                {
                    SourceBaseSlice = type == TextureType.TwoDArray ? 1u : type == TextureType.Cube ? 3u : 4u,
                    DestinationBaseSlice = type == TextureType.TwoDArray ? 2u : type == TextureType.Cube ? 1u : 8u,
                    SliceCount = type == TextureType.TwoDArray ? 2u : 0u,
                    SourceOffset = new Extent3D(2, 1, 0), DestinationOffset = new Extent3D(1, 2, 0), Extent = new Extent3D(3, 4, 1),
                },
            ], check, $"{type} physical slices and source-default face counts");
        }

        source = new TextureDesc
        {
            Type = TextureType.ThreeD, Extent = new Extent3D(16, 12, 8), MipLevels = 3, Format = Format.R8Unorm, Usage = usage,
        };
        destination = source;
        destination.Extent = new Extent3D(8, 6, 4);
        destination.MipLevels = 2;
        RunTextureRegionCopy(device, source, destination,
        [
            new TextureRegionCopyDesc
            {
                SourceMipLevel = 1,
                SourceOffset = new Extent3D(1, 1, 1), DestinationOffset = new Extent3D(3, 2, 2), Extent = new Extent3D(3, 2, 2),
            },
            new TextureRegionCopyDesc
            {
                SourceMipLevel = 2, DestinationMipLevel = 1,
                SourceOffset = new Extent3D(1, 1, 1), DestinationOffset = new Extent3D(0, 0, 1),
            },
        ], check, "3D subvolumes with nonzero Z and source-default depth");

        source = new TextureDesc { Extent = new Extent3D(8, 8, 1), MipLevels = 2, Usage = usage };
        RunTextureRegionCopy(device, source, source,
        [
            new TextureRegionCopyDesc
            {
                SourceOffset = new Extent3D(0, 1, 0), DestinationOffset = new Extent3D(4, 2, 0), Extent = new Extent3D(3, 3, 1),
            },
        ], check, "same-texture disjoint regions", sameTexture: true);

        ReadOnlySpan<Format> aspectFormats = [Format.D16Unorm, Format.D32Float, Format.S8Uint];
        foreach (Format format in aspectFormats)
        {
            if (!device.SupportsTextureFormat(format, usage))
            {
                Console.WriteLine($"  [SKIP] {format} texture transfers are unsupported");
                continue;
            }
            source = new TextureDesc { Extent = new Extent3D(8, 6, 1), MipLevels = 2, Format = format, Usage = usage };
            RunTextureRegionCopy(device, source, source,
            [
                new TextureRegionCopyDesc(),
                new TextureRegionCopyDesc { SourceMipLevel = 1, DestinationMipLevel = 1, Extent = new Extent3D(4, 3, 1) },
            ], check, $"{format} complete mip subresources with default and explicit extents");
            ReadOnlySpan<TextureUsage> depthUsages = [usage, usage | TextureUsage.DepthStencilAttachment];
            foreach (TextureUsage depthUsage in depthUsages)
            {
                if (!device.SupportsTextureFormat(format, depthUsage)) continue;
                source = new TextureDesc
                {
                    Type = TextureType.TwoDArray, Extent = new Extent3D(16, 12, 1), MipLevels = 3, LayerCount = 4,
                    Format = format, Usage = depthUsage,
                };
                destination = source;
                destination.Extent = new Extent3D(8, 6, 1);
                destination.MipLevels = 2;
                destination.LayerCount = 5;
                RunTextureRegionCopy(device, source, destination,
                [
                    new TextureRegionCopyDesc
                    {
                        SourceMipLevel = 1, SourceBaseSlice = 1, DestinationBaseSlice = 2, SliceCount = 2,
                    },
                    new TextureRegionCopyDesc
                    {
                        SourceMipLevel = 2, DestinationMipLevel = 1, DestinationBaseSlice = 3, SliceCount = 1, Extent = new Extent3D(4, 3, 1),
                    },
                ], check, $"{format} whole array slices with independent matching mips ({depthUsage})");
            }
        }

        ReadOnlySpan<Format> compressedFormats =
        [
            Format.Bc1Unorm, Format.Bc2Unorm, Format.Bc4Unorm, Format.Bc3Unorm, Format.Bc5Rg,
            Format.Bc6HUfloat, Format.Bc7Unorm, Format.Astc4x4Unorm, Format.EacRg,
        ];
        foreach (Format format in compressedFormats)
        {
            if (!device.SupportsTextureFormat(format, usage))
            {
                Console.WriteLine($"  [SKIP] {format} texture transfers are unsupported");
                continue;
            }
            source = new TextureDesc
            {
                Type = TextureType.TwoDArray, Extent = new Extent3D(24, 24, 1), MipLevels = 5, LayerCount = 3, Format = format, Usage = usage,
            };
            destination = source;
            destination.Extent = new Extent3D(12, 12, 1);
            destination.MipLevels = 4;
            destination.LayerCount = 4;
            RunTextureRegionCopy(device, source, destination,
            [
                new TextureRegionCopyDesc
                {
                    SourceMipLevel = 1, SourceBaseSlice = 1, DestinationBaseSlice = 1, SliceCount = 2,
                    SourceOffset = new Extent3D(4, 0, 0), DestinationOffset = new Extent3D(0, 4, 0), Extent = new Extent3D(4, 4, 1),
                },
                new TextureRegionCopyDesc
                {
                    SourceMipLevel = 2, DestinationMipLevel = 1, SourceBaseSlice = 1, DestinationBaseSlice = 1,
                    SourceOffset = new Extent3D(4, 0, 0), DestinationOffset = new Extent3D(4, 0, 0),
                },
                new TextureRegionCopyDesc { SourceMipLevel = 3, DestinationMipLevel = 2, SourceBaseSlice = 1, DestinationBaseSlice = 1 },
                new TextureRegionCopyDesc { SourceMipLevel = 4, DestinationMipLevel = 3, SourceBaseSlice = 1, DestinationBaseSlice = 1 },
            ], check, $"{format} aligned blocks, 6x6 mip edges, and 3x3/1x1 mips");
        }
    }

    private static void RunTextureRegionCopy(GPUDevice device, in TextureDesc sourceDesc, in TextureDesc destinationDesc,
        ReadOnlySpan<TextureRegionCopyDesc> copies, Action<bool, string> check, string label, bool sameTexture = false, bool clearSource = false)
    {
        byte[] sourceData = CreateTextureRegionData(sourceDesc, 37, out TextureCopyDesc[] sourceLayouts, out int[] sourceOffsets);
        TextureCopyDesc[] destinationLayouts = sourceLayouts;
        int[] destinationOffsets = sourceOffsets;
        byte[] destinationData = sourceData;
        if (!sameTexture) destinationData = CreateTextureRegionData(destinationDesc, 149, out destinationLayouts, out destinationOffsets);
        if (clearSource)
        {
            for (int y = 0; y < sourceDesc.Extent.Y; ++y)
                for (int x = 0; x < sourceDesc.Extent.X; ++x)
                {
                    int pixel = sourceOffsets[0] + y * (int)sourceLayouts[0].RowPitchBytes + x * 4;
                    sourceData[pixel] = 255;
                    sourceData[pixel + 1] = 0;
                    sourceData[pixel + 2] = 255;
                    sourceData[pixel + 3] = 255;
                }
        }
        byte[] expected = (byte[])destinationData.Clone();
        TextureFormatInfo format = Gpu.GetTextureFormatInfo(sourceDesc.Format);
        foreach (TextureRegionCopyDesc copy in copies)
        {
            TextureCopyDesc sourceLayout = sourceLayouts[copy.SourceMipLevel];
            TextureCopyDesc destinationLayout = destinationLayouts[copy.DestinationMipLevel];
            uint width = copy.Extent.X != 0 ? copy.Extent.X : Math.Max(1u, sourceDesc.Extent.X >> (int)copy.SourceMipLevel) - copy.SourceOffset.X;
            uint height = copy.Extent.Y != 0 ? copy.Extent.Y : Math.Max(1u, sourceDesc.Extent.Y >> (int)copy.SourceMipLevel) - copy.SourceOffset.Y;
            uint depth = copy.Extent.Z != 0 ? copy.Extent.Z : Math.Max(1u, sourceDesc.Extent.Z >> (int)copy.SourceMipLevel) - copy.SourceOffset.Z;
            uint planes = sourceDesc.Type == TextureType.ThreeD ? depth :
                copy.SliceCount != 0 ? copy.SliceCount : sourceDesc.LayerCount - copy.SourceBaseSlice;
            int sourcePlane = (int)(sourceDesc.Type == TextureType.ThreeD ? copy.SourceOffset.Z : copy.SourceBaseSlice);
            int destinationPlane = (int)(destinationDesc.Type == TextureType.ThreeD ? copy.DestinationOffset.Z : copy.DestinationBaseSlice);
            int sourceStart = sourceOffsets[copy.SourceMipLevel] + sourcePlane * (int)sourceLayout.SlicePitchBytes +
                (int)(copy.SourceOffset.Y / format.BlockExtent.Y * sourceLayout.RowPitchBytes +
                    copy.SourceOffset.X / format.BlockExtent.X * format.BytesPerBlock);
            int destinationStart = destinationOffsets[copy.DestinationMipLevel] + destinationPlane * (int)destinationLayout.SlicePitchBytes +
                (int)(copy.DestinationOffset.Y / format.BlockExtent.Y * destinationLayout.RowPitchBytes +
                    copy.DestinationOffset.X / format.BlockExtent.X * format.BytesPerBlock);
            int rowBytes = (int)((width + format.BlockExtent.X - 1) / format.BlockExtent.X * format.BytesPerBlock);
            int rows = (int)((height + format.BlockExtent.Y - 1) / format.BlockExtent.Y);
            for (int plane = 0; plane < planes; ++plane)
                for (int row = 0; row < rows; ++row)
                    sourceData.AsSpan(sourceStart + plane * (int)sourceLayout.SlicePitchBytes + row * (int)sourceLayout.RowPitchBytes, rowBytes)
                        .CopyTo(expected.AsSpan(destinationStart + plane * (int)destinationLayout.SlicePitchBytes +
                            row * (int)destinationLayout.RowPitchBytes));
        }

        GPUTextureHeap sourceHeap = default;
        GPUTextureHeap destinationHeap = default;
        GPUTexture? source = null;
        GPUTexture? destination = null;
        GPURenderView? colorView = null;
        GPUHeap upload = default;
        GPUHeap readback = default;
        GPUCommandPool? pool = null;
        GPUTimelineSemaphore? timeline = null;
        TimelinePoint pending = default;
        try
        {
            sourceHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(sourceDesc).Size);
            if (!sameTexture) destinationHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(destinationDesc).Size);
            source = device.CreateTexture(sourceDesc, sourceHeap, 0);
            destination = sameTexture ? source : device.CreateTexture(destinationDesc, destinationHeap, 0);
            if (clearSource) colorView = source.CreateRenderView(new RenderViewDesc());
            int destinationUploadOffset = (sourceData.Length + 255) & ~255;
            upload = device.CreateGPUHeap((ulong)(destinationUploadOffset + (sameTexture ? 0 : destinationData.Length)));
            readback = device.CreateGPUHeap((ulong)expected.Length, MemoryType.Readback);
            pool = device.CreateCommandPool(0);
            timeline = device.CreateTimelineSemaphore();
            GPUCommandBuffer uploaded = pool.BeginCommands();
            GPUCommandBuffer copied = pool.BeginCommands();
            GPUCommandBuffer downloaded = pool.BeginCommands();
            sourceData.AsSpan().CopyTo(upload.Range.CpuSpan);
            if (!sameTexture) destinationData.AsSpan().CopyTo(upload.Range.CpuSpan.Slice(destinationUploadOffset));
            NativeMemory.Fill(readback.Range.Cpu, (nuint)expected.Length, 0xa5);
            uploaded.InitializeTexture(source);
            if (!sameTexture) uploaded.InitializeTexture(destination);
            if (clearSource)
            {
                uploaded.BeginRenderPass(new RenderingDesc
                {
                    Colors = [new ColorAttachment { RenderView = colorView, Load = LoadOp.Clear, Clear = new Vector4(1, 0, 1, 1) }],
                });
                uploaded.EndRenderPass();
                uploaded.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            }
            else
                for (int mip = 0; mip < sourceLayouts.Length; ++mip)
                    uploaded.CopyMemoryToTexture(upload.Range.GPURange.Slice((ulong)sourceOffsets[mip], (ulong)(sourceData.Length - sourceOffsets[mip])),
                        source, sourceLayouts[mip]);
            if (!sameTexture)
                for (int mip = 0; mip < destinationLayouts.Length; ++mip)
                    uploaded.CopyMemoryToTexture(upload.Range.GPURange.Slice((ulong)(destinationUploadOffset + destinationOffsets[mip]),
                        (ulong)(destinationData.Length - destinationOffsets[mip])), destination, destinationLayouts[mip]);
            uploaded.EndCommands();
            copied.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead | Access.TransferWrite);
            if (clearSource) copied.CopyTexture(source, destination);
            else
                foreach (TextureRegionCopyDesc copy in copies) copied.CopyTexture(source, destination, copy);
            copied.EndCommands();
            // Address-based texture uploads/readbacks must remain in separate command buffers on affected NVIDIA drivers.
            downloaded.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            for (int mip = 0; mip < destinationLayouts.Length; ++mip)
                downloaded.CopyTextureToMemory(destination,
                    readback.Range.GPURange.Slice((ulong)destinationOffsets[mip], (ulong)(expected.Length - destinationOffsets[mip])), destinationLayouts[mip]);
            downloaded.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            downloaded.EndCommands();
            pending = new TimelinePoint { Semaphore = timeline, Value = 1 };
            device.Submit(new SubmitDesc { Commands = [uploaded, copied, downloaded], Completion = pending });
            pending.Wait();
            pending = default;
            ReadOnlySpan<byte> actual = readback.Range.CpuSpan.Slice(0, expected.Length);
            bool matches = actual.SequenceEqual(expected);
            if (!matches)
                for (int index = 0; index < expected.Length; ++index)
                {
                    if (actual[index] == expected[index]) continue;
                    Console.WriteLine($"  {label} first mismatch: byte={index}, actual=0x{actual[index]:x2}, expected=0x{expected[index]:x2}");
                    break;
                }
            check(matches, $"{label} preserves copied data, untouched texels/blocks/mips/slices, readback gaps and guards");
        }
        finally
        {
            pending.Wait();
            device.WaitIdle();
            pool?.Destroy();
            timeline?.Destroy();
            readback.Destroy();
            upload.Destroy();
            colorView?.Destroy();
            if (!sameTexture) destination?.Destroy();
            source?.Destroy();
            destinationHeap.Destroy();
            sourceHeap.Destroy();
        }
    }

    private static byte[] CreateTextureRegionData(in TextureDesc desc, int seed, out TextureCopyDesc[] layouts, out int[] offsets)
    {
        TextureFormatInfo format = Gpu.GetTextureFormatInfo(desc.Format);
        layouts = new TextureCopyDesc[desc.MipLevels];
        offsets = new int[desc.MipLevels];
        int size = 256;
        for (int mip = 0; mip < layouts.Length; ++mip)
        {
            uint width = Math.Max(1u, desc.Extent.X >> mip);
            uint height = Math.Max(1u, desc.Extent.Y >> mip);
            uint planes = desc.Type == TextureType.ThreeD ? Math.Max(1u, desc.Extent.Z >> mip) : desc.LayerCount;
            int rowBytes = (int)((width + format.BlockExtent.X - 1) / format.BlockExtent.X * format.BytesPerBlock);
            int rows = (int)((height + format.BlockExtent.Y - 1) / format.BlockExtent.Y);
            ulong rowPitch = (ulong)(rowBytes / 64 + 1) * 64;
            layouts[mip] = new TextureCopyDesc { MipLevel = (uint)mip, RowPitchBytes = rowPitch, SlicePitchBytes = rowPitch * (ulong)(rows + 1) };
            offsets[mip] = size;
            size += (int)(layouts[mip].SlicePitchBytes * planes) + 256;
        }
        byte[] data = new byte[size];
        data.AsSpan().Fill(0xa5);
        for (int mip = 0; mip < layouts.Length; ++mip)
        {
            uint width = Math.Max(1u, desc.Extent.X >> mip);
            uint height = Math.Max(1u, desc.Extent.Y >> mip);
            uint planes = desc.Type == TextureType.ThreeD ? Math.Max(1u, desc.Extent.Z >> mip) : desc.LayerCount;
            int rowBytes = (int)((width + format.BlockExtent.X - 1) / format.BlockExtent.X * format.BytesPerBlock);
            int rows = (int)((height + format.BlockExtent.Y - 1) / format.BlockExtent.Y);
            for (int plane = 0; plane < planes; ++plane)
                for (int row = 0; row < rows; ++row)
                {
                    Span<byte> bytes = data.AsSpan(offsets[mip] + plane * (int)layouts[mip].SlicePitchBytes +
                        row * (int)layouts[mip].RowPitchBytes, rowBytes);
                    for (int index = 0; index < bytes.Length; ++index) bytes[index] = unchecked((byte)(seed + mip * 29 + plane * 47 + row * 11 + index * 3));
                    if (desc.Format != Format.D32Float) continue;
                    Span<float> depths = MemoryMarshal.Cast<byte, float>(bytes);
                    for (int x = 0; x < depths.Length; ++x) depths[x] = ((seed + mip * 29 + plane * 47 + row * 11 + x * 3) & 255) / 256.0f;
                }
        }
        return data;
    }

    private static void RunTextureCopies(GPUDevice device, Action<bool, string> check)
    {
        ReadOnlySpan<TextureType> types = [TextureType.ThreeD, TextureType.TwoDArray, TextureType.Cube];
        foreach (TextureType type in types)
        {
            TextureDesc desc = new()
            {
                Type = type,
                Extent = new Extent3D(8, 8, type == TextureType.ThreeD ? 4u : 1u),
                LayerCount = type == TextureType.ThreeD ? 1u : type == TextureType.Cube ? 6u : 3u,
                Format = Format.R8Unorm,
                Usage = TextureUsage.TransferSource | TextureUsage.TransferDestination,
            };
            GPUTextureHeap heap = default;
            GPUTexture? texture = null;
            GPUHeap upload = default;
            GPUHeap readback = default;
            GPUCommandPool? pool = null;
            GPUTimelineSemaphore? timeline = null;
            try
            {
                heap = device.CreateTextureHeap(device.GetTextureSizeAlign(desc).Size);
                texture = device.CreateTexture(desc, heap, 0);
                upload = device.CreateGPUHeap(4096);
                readback = device.CreateGPUHeap(4096, MemoryType.Readback);
                pool = device.CreateCommandPool(0);
                timeline = device.CreateTimelineSemaphore();
                byte[] source = new byte[4096];
                for (int index = 0; index < source.Length; ++index) source[index] = (byte)(index * 17 + index / 16);
                source.AsSpan().CopyTo(upload.Range.CpuSpan);
                NativeMemory.Fill(readback.Range.Cpu, 4096, 0xa5);
                TextureCopyDesc copy = new()
                {
                    BaseSlice = type == TextureType.ThreeD ? 0u : 1u,
                    SliceCount = type == TextureType.ThreeD ? 1u : 2u,
                    Offset = new Extent3D(1, 1, type == TextureType.ThreeD ? 1u : 0u),
                    Extent = new Extent3D(3, 2, type == TextureType.ThreeD ? 2u : 1u),
                    RowPitchBytes = 16,
                    SlicePitchBytes = 64,
                };
                GPUCommandBuffer commands = pool.BeginCommands();
                commands.InitializeTexture(texture);
                commands.CopyMemoryToTexture(upload.Range.GPURange.Slice((ulong)(32), 4096 - 32), texture, copy);
                // Address-based texture upload/readback must use separate command buffers on affected NVIDIA drivers.
                commands.EndCommands();
                GPUCommandBuffer uploaded = commands;
                commands = pool.BeginCommands();
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
                commands.CopyTextureToMemory(texture, readback.Range.GPURange.Slice((ulong)(32), 4096 - 32), copy);
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                commands.EndCommands();
                TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
                device.Submit(new SubmitDesc { Commands = [uploaded, commands], Completion = completion });
                completion.Wait();
                int mismatches = 0;
                for (int index = 0; index < source.Length; ++index)
                {
                    int relative = index - 32;
                    bool copied = relative >= 0 && relative / 64 < 2 && relative % 64 / 16 < 2 && relative % 16 < 3;
                    byte expected = copied ? source[index] : (byte)0xa5;
                    byte actual = readback.Range.Cpu[index];
                    if (actual == expected) continue;
                    if (mismatches == 0)
                        Console.WriteLine($"  pitched {type} first mismatch: byte={index}, relative={relative}, copied={copied}, " +
                            $"actual=0x{actual:x2}, expected=0x{expected:x2}");
                    ++mismatches;
                }
                if (mismatches != 0) Console.WriteLine($"  pitched {type} mismatches: {mismatches}/{source.Length}");
                check(mismatches == 0, $"pitched {type} subregion round-trip preserves every row/slice gap and guard byte");
            }
            finally
            {
                device.WaitIdle();
                pool?.Destroy();
                timeline?.Destroy();
                upload.Destroy();
                readback.Destroy();
                texture?.Destroy();
                heap.Destroy();
            }
        }
    }

    private static void RunBcTextures(GPUDevice device, byte[] code, Action<bool, string> check)
    {
        ShaderTarget target = ShaderTarget(device);
        ReadOnlySpan<Format> formats = [Format.Bc1Srgb, Format.Bc1Unorm, Format.Bc2Srgb, Format.Bc2Unorm, Format.Bc4Unorm, Format.Bc4Snorm];
        foreach (Format format in formats)
        {
            const TextureUsage usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination;
            bool supported = device.SupportsTextureFormat(format, usage);
            check(supported == device.Caps.TextureCompressionBc && !device.SupportsTextureFormat(format, TextureUsage.Storage) &&
                !device.SupportsTextureFormat(format, TextureUsage.ColorAttachment), $"{format} follows BC capability and compressed-usage restrictions");
            if (!supported) continue;
            TextureDesc desc = new()
            {
                Type = TextureType.TwoDArray,
                Extent = new Extent3D(12, 12, 1),
                MipLevels = 4,
                LayerCount = 3,
                Format = format,
                MutableFormat = true,
                Usage = usage,
            };
            GPUTextureHeap heap = default;
            GPUTexture? texture = null;
            GPUTextureDescriptorHeap? descriptors = null;
            GPUPipeline? pipeline = null;
            GPUHeap upload = default;
            GPUHeap output = default;
            GPUHeap readback = default;
            GPUCommandPool? pool = null;
            GPUTimelineSemaphore? timeline = null;
            UploadQueue? uploads = null;
            try
            {
                heap = device.CreateTextureHeap(device.GetTextureSizeAlign(desc).Size);
                texture = device.CreateTexture(desc, heap, 0);
                descriptors = device.CreateTextureDescriptorHeap(2);
                pipeline = device.CreateComputePipeline(new ShaderStage
                {
                    Code = code,
                    EntryPoint = ShaderCompiler.GetCompiledEntryPointName(target, "computeMain"),
                });
                upload = device.CreateGPUHeap(4096);
                output = device.CreateGPUHeap(4 * (ulong)sizeof(Vector4), MemoryType.GpuOnly);
                readback = device.CreateGPUHeap(4096, MemoryType.Readback);
                pool = device.CreateCommandPool(0);
                timeline = device.CreateTimelineSemaphore();
                uploads = new UploadQueue(device, 16);
                Format viewFormat = format switch
                {
                    Format.Bc1Srgb => Format.Bc1Unorm,
                    Format.Bc1Unorm => Format.Bc1Srgb,
                    Format.Bc2Srgb => Format.Bc2Unorm,
                    Format.Bc2Unorm => Format.Bc2Srgb,
                    Format.Bc4Unorm => Format.Bc4Snorm,
                    _ => Format.Bc4Unorm,
                };
                descriptors.WriteTextureDescriptor(0, texture, TextureDescriptorType.Sampled, new TextureDescriptorDesc());
                descriptors.WriteTextureDescriptor(1, texture, TextureDescriptorType.Sampled, new TextureDescriptorDesc { Format = viewFormat });
                byte[] source = new byte[4096];
                for (int index = 0; index < source.Length; ++index) source[index] = (byte)(index * 17 + index / 16);
                source.AsSpan().CopyTo(upload.Range.CpuSpan);
                NativeMemory.Fill(readback.Range.Cpu, 4096, 0xa5);
                TextureCopyDesc copy = new()
                {
                    MipLevel = 1,
                    BaseSlice = 1,
                    SliceCount = 2,
                    Offset = new Extent3D(4, 0, 0),
                    Extent = new Extent3D(2, 6, 1),
                    RowPitchBytes = 32,
                    SlicePitchBytes = 128,
                };
                GPUCommandBuffer commands = pool.BeginCommands();
                commands.InitializeTexture(texture);
                commands.CopyMemoryToTexture(upload.Range.GPURange.Slice((ulong)(32), 4096 - 32), texture, copy);
                commands.EndCommands();
                GPUCommandBuffer uploaded = commands;
                commands = pool.BeginCommands();
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
                commands.CopyTextureToMemory(texture, readback.Range.GPURange.Slice((ulong)(32), 4096 - 32), copy);
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                commands.EndCommands();
                TimelinePoint completion = new() { Semaphore = timeline, Value = 1 };
                device.Submit(new SubmitDesc { Commands = [uploaded, commands], Completion = completion });
                completion.Wait();
                int blockBytes = (int)Gpu.GetTextureFormatInfo(format).BytesPerBlock;
                bool copiesMatch = true;
                for (int index = 0; index < source.Length; ++index)
                {
                    int relative = index - 32;
                    bool copied = relative >= 0 && relative / 128 < 2 && relative % 128 / 32 < 2 && relative % 32 < blockBytes;
                    copiesMatch &= readback.Range.Cpu[index] == (copied ? source[index] : (byte)0xa5);
                }
                check(copiesMatch, $"{format} pitched mip-edge array copy preserves compressed blocks, row/slice gaps and guards");
                pool.Reset();
                byte[] block = format switch
                {
                    Format.Bc1Srgb or Format.Bc1Unorm => [0, 0, 0x10, 0x84, 0x5d, 0x55, 0x55, 0x55],
                    Format.Bc2Srgb or Format.Bc2Unorm => [0x08, 0x88, 0x88, 0x88, 0x88, 0x88, 0x88, 0x88, 0, 0, 0x10, 0x84, 0x55, 0x55, 0x55, 0x55],
                    Format.Bc4Unorm => [128, 64, 8, 0, 0, 0, 0, 0],
                    _ => [0xc0, 64, 8, 0, 0, 0, 0, 0],
                };
                uploads.UploadTexture(texture, block, new TextureCopyDesc { MipLevel = 2, BaseSlice = 2, SliceCount = 1 });
                uploads.UploadTexture(texture, block, new TextureCopyDesc { MipLevel = 3, BaseSlice = 2, SliceCount = 1 });
                uploads.Wait();
                BumpAllocator arena = new(upload.Range);
                GPUAllocation<BcData> root = arena.Allocate<BcData>();
                *root.Cpu = new BcData { Output = output.Range.View, Mip = 2, Slice = 2 };
                commands = pool.BeginCommands();
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Compute | Stage.Transfer, Access.ShaderRead | Access.TransferRead);
                commands.SetTextureDescriptorHeap(descriptors);
                commands.BindPipeline(pipeline);
                commands.Dispatch(root.GPURange, new Extent3D(1, 1, 1));
                commands.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Transfer, Access.TransferRead);
                commands.CopyMemory(output.Range.GPURange, readback.Range.GPURange);
                commands.CopyTextureToMemory(texture, readback.Range.GPURange.Slice((ulong)(128), (ulong)blockBytes),
                    new TextureCopyDesc { MipLevel = 2, BaseSlice = 2, SliceCount = 1 });
                commands.CopyTextureToMemory(texture, readback.Range.GPURange.Slice((ulong)(256), (ulong)blockBytes),
                    new TextureCopyDesc { MipLevel = 3, BaseSlice = 2, SliceCount = 1 });
                commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                commands.EndCommands();
                completion.Value++;
                device.Submit(new SubmitDesc { Commands = [commands], Completion = completion });
                completion.Wait();
                check(readback.Range.CpuSpan.Slice(128, blockBytes).SequenceEqual(block) &&
                    readback.Range.CpuSpan.Slice(256, blockBytes).SequenceEqual(block), $"{format} uploads complete blocks for 3x3 and 1x1 mips");
                Vector4* texels = (Vector4*)readback.Range.Cpu;
                bool samplesMatch = true;
                for (int view = 0; view < 2; ++view)
                    for (int pixel = 0; pixel < 2; ++pixel)
                        samplesMatch &= Vector4.DistanceSquared(texels[view * 2 + pixel], BcTexel(view == 0 ? format : viewFormat, block, pixel)) < 0.0001f;
                check(samplesMatch, $"{format} and its {viewFormat} view decode color, alpha and signedness on the GPU");
            }
            finally
            {
                uploads?.Wait();
                device.WaitIdle();
                uploads?.Destroy();
                pool?.Destroy();
                timeline?.Destroy();
                readback.Destroy();
                output.Destroy();
                upload.Destroy();
                pipeline?.Destroy();
                descriptors?.Destroy();
                texture?.Destroy();
                heap.Destroy();
            }
        }
    }

    private static Vector4 BcTexel(Format format, ReadOnlySpan<byte> block, int pixel)
    {
        if (format == Format.Bc4Unorm) return new Vector4(block[pixel] / 255.0f, 0, 0, 1);
        if (format == Format.Bc4Snorm) return new Vector4(MathF.Max(unchecked((sbyte)block[pixel]) / 127.0f, -1), 0, 0, 1);
        if (pixel == 1 && (format == Format.Bc1Srgb || format == Format.Bc1Unorm)) return Vector4.Zero;
        Vector4 color = new(132 / 255.0f, 130 / 255.0f, 132 / 255.0f,
            format is Format.Bc2Srgb or Format.Bc2Unorm ? (pixel == 0 ? 8 / 15.0f : 0) : 1);
        if (format == Format.Bc1Srgb || format == Format.Bc2Srgb)
        {
            color.X = MathF.Pow((color.X + 0.055f) / 1.055f, 2.4f);
            color.Y = MathF.Pow((color.Y + 0.055f) / 1.055f, 2.4f);
            color.Z = MathF.Pow((color.Z + 0.055f) / 1.055f, 2.4f);
        }
        return color;
    }

    private static void RunTextureRequirements(GPUDevice device, Action<bool, string> check, string label)
    {
        for (int mode = 0; mode < 2; ++mode)
        {
            TextureDesc desc = new()
            {
                Extent = new Extent3D(16, 16, 1),
                MutableFormat = mode != 0,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            };
            SizeAlign requirements = device.GetTextureSizeAlign(desc);
            check(requirements.Size != 0 && requirements.Align != 0, $"{label} texture requirements are valid (mutable={desc.MutableFormat})");
            if (requirements.Size == 0 || requirements.Align == 0) continue;
            GPUTextureHeap heap = default;
            GPUTexture? texture = null;
            GPUTextureDescriptorHeap? descriptors = null;
            try
            {
                heap = device.CreateTextureHeap(requirements.Size);
                texture = device.CreateTexture(desc, heap, 0);
                if (desc.MutableFormat)
                {
                    descriptors = device.CreateTextureDescriptorHeap(1);
                    descriptors.WriteTextureDescriptor(0, texture, TextureDescriptorType.Sampled, new TextureDescriptorDesc { Format = Format.Rgba8Srgb });
                }
                check(heap.Size == requirements.Size, $"{label} texture creation matches queried requirements (mutable={desc.MutableFormat})");
            }
            finally
            {
                device.WaitIdle();
                descriptors?.Destroy();
                texture?.Destroy();
                heap.Destroy();
            }
        }
    }

    private static void RunLongEntryPoint(GPUDevice device, byte[] original, Action<bool, string> check)
    {
        ReadOnlySpan<uint> words = MemoryMarshal.Cast<byte, uint>(original.AsSpan());
        int instruction = 5;
        while (instruction < words.Length && (words[instruction] & 0xffff) != 15)
        {
            int count = (int)(words[instruction] >> 16);
            if (count == 0) break;
            instruction += count;
        }
        bool found = instruction < words.Length && (words[instruction] & 0xffff) == 15;
        check(found, "compiled SPIR-V exposes an OpEntryPoint for the long-name regression");
        if (!found) return;
        int oldCount = (int)(words[instruction] >> 16);
        int nameOffset = (instruction + 3) * sizeof(uint);
        int oldBytes = Array.IndexOf(original, (byte)0, nameOffset) - nameOffset + 1;
        int oldNameWords = (oldBytes + 3) / 4;
        const int newNameWords = 65;
        byte[] code = new byte[original.Length + (newNameWords - oldNameWords) * sizeof(uint)];
        original.AsSpan(0, nameOffset).CopyTo(code);
        code.AsSpan(nameOffset, 256).Fill((byte)'a');
        original.AsSpan(nameOffset + oldNameWords * sizeof(uint)).CopyTo(code.AsSpan(nameOffset + newNameWords * sizeof(uint)));
        MemoryMarshal.Cast<byte, uint>(code.AsSpan())[instruction] = (uint)((oldCount + newNameWords - oldNameWords) << 16) | 15u;
        GPUPipeline pipeline = device.CreateComputePipeline(new ShaderStage { Code = code, EntryPoint = new string('a', 256) });
        pipeline.Destroy();
    }

    internal static void RunSlang(Action<bool, string> check)
    {
        check(ShaderCompiler.GetCompiledEntryPointName(Alco.Graphics.ShaderTarget.Spirv, "computeMain") == "main" &&
            ShaderCompiler.GetCompiledEntryPointName(Alco.Graphics.ShaderTarget.Dxil, "computeMain") == "computeMain" &&
            ShaderCompiler.GetCompiledEntryPointName(Alco.Graphics.ShaderTarget.MetalSource, "vertexMain") == "vertexMain" &&
            ShaderCompiler.GetCompiledEntryPointName(Alco.Graphics.ShaderTarget.MetalLibrary, "taskMain") == "taskMain",
            "SPIR-V uses main while DXIL and Metal preserve source stage entry-point names");
        if (OperatingSystem.IsWindows())
        {
            using ShaderCompiler dxilCompiler = new(Alco.Graphics.ShaderTarget.Dxil);
            CompiledShader dxil = dxilCompiler.CompileSource("regression_dxil_profile", "regression_dxil_profile.slang",
                ComputeSource, ShaderPipelineKind.Compute);
            check(IsDxil(dxil.GetStage(ShaderStageKind.Compute).Code.Span), "DXIL defaults to shader model 6.6");
        }
        using (ShaderCompiler compiler = new(Alco.Graphics.ShaderTarget.Spirv))
        {
            try
            {
                CompiledShader compiled = compiler.CompileSource("regression_spirv", "regression_spirv.slang",
                    ComputeSource, ShaderPipelineKind.Compute);
                ReadOnlySpan<byte> code = compiled.GetStage(ShaderStageKind.Compute).Code.Span;
                check(IsSpirv15(code), "default shader compilation emits SPIR-V 1.5 or newer");
                check(!HasSpirvUnitPointSize(code), "PointSize oracle rejects shaders without a PointSize output");
            }
            catch (ShaderCompilationException exception)
            {
                check(false, $"default Slang SPIR-V regression: {exception.Message}");
            }
            try
            {
                CompiledShader compiled = compiler.CompileSource("regression_task", "regression_task.slang", """
                    struct Payload { uint Value; };
                    [shader("amplification")]
                    [numthreads(1,1,1)]
                    void taskMain() { Payload payload; payload.Value = 7; DispatchMesh(1,1,1,payload); }
                    """, [new ShaderEntryRequest("taskMain", ShaderStageKind.Task)]);
                CompiledShaderStage stage = compiled.GetStage("taskMain");
                check((int)ShaderStageKind.Task == 1 << 7 && stage.Stage == ShaderStageKind.Task &&
                    IsSpirv15(stage.Code.Span), "Slang task/amplification stage 14 compiles and maps to ShaderStageKind.Task");
            }
            catch (ShaderCompilationException exception)
            {
                check(false, $"Slang task-stage regression: {exception.Message}");
            }
        }
        const string pointSizeSource = """
            struct VertexOutput
            {
                float4 Position : SV_Position;
                float Unrelated : TEXCOORD0;
                float PointSize : SV_PointSize;
            };
            [shader("vertex")]
            VertexOutput vertexMain(uint vertexId : SV_VertexID)
            {
                VertexOutput output;
                output.Position = float4(0.0, 0.0, 0.5, 1.0);
                output.Unrelated = 1.0;
                output.PointSize = POINT_SIZE;
                return output;
            }
            """;
        ReadOnlySpan<(string Name, string Value, bool Unit)> pointSizes =
        [
            ("unit", "1.0", true), ("nonunit", "2.0", false), ("unknown", "float(vertexId)", false),
        ];
        ReadOnlySpan<ShaderOptimization> pointSizeOptimizations = [ShaderOptimization.None, ShaderOptimization.Maximal];
        foreach (ShaderOptimization optimization in pointSizeOptimizations)
        {
            try
            {
                using ShaderCompiler optCompiler = new(Alco.Graphics.ShaderTarget.Spirv,
                    new ShaderCompilerOptions { Optimization = optimization });
                foreach ((string name, string value, bool unit) in pointSizes)
                {
                    string moduleName = "regression_point_size_" + name;
                    CompiledShader compiled = optCompiler.CompileSource(moduleName, moduleName + ".slang",
                        pointSizeSource.Replace("POINT_SIZE", value, StringComparison.Ordinal),
                        ShaderPipelineKind.Graphics);
                    check(HasSpirvUnitPointSize(compiled.GetStage(ShaderStageKind.Vertex).Code.Span) == unit,
                        $"PointSize oracle recognizes {name} output independently of an unrelated unit store (optimization {optimization})");
                }
            }
            catch (ShaderCompilationException exception)
            {
                check(false, $"PointSize oracle compilation regression (optimization {optimization}): {exception.Message}");
            }
        }
        const string vertexIdSource = """
            struct VertexInput { uint Id : VERTEX_ID; };
            struct VertexOutput
            {
                float4 Position : SV_Position;
            #if ARRAY_OUTPUT
                float4 Colors[2] : COLOR0;
            #else
                float4 Color : COLOR0;
            #endif
            };
            [shader("vertex")]
            VertexOutput vertexMain(
            #if STRUCT_INPUT
                VertexInput input
            #else
                uint vertexId : VERTEX_ID
            #endif
            )
            {
            #if STRUCT_INPUT
                uint vertexId = input.Id;
            #endif
                VertexOutput output;
                output.Position = float4(float(vertexId), 0.0, 0.5, 1.0);
            #if ARRAY_OUTPUT
                output.Colors[0] = 0.25;
                output.Colors[1] = 0.75;
            #else
                output.Color = 1.0;
            #endif
                return output;
            }
            """;
        using (ShaderCompiler metalCompiler = new(Alco.Graphics.ShaderTarget.MetalSource))
        {
            for (int variant = 0; variant < 8; ++variant)
            {
                bool absolute = (variant & 1) != 0;
                bool structured = (variant & 2) != 0;
                bool array = (variant & 4) != 0;
                try
                {
                    CompiledShader compiled = metalCompiler.CompileSource("regression_vertex_id", "regression_vertex_id.slang",
                        vertexIdSource, ShaderPipelineKind.Graphics, new ShaderCompileOptions
                        {
                            Defines =
                            [
                                ("VERTEX_ID", absolute ? "SV_VulkanVertexID" : "SV_VertexID"),
                                ("STRUCT_INPUT", structured ? "1" : "0"),
                                ("ARRAY_OUTPUT", array ? "1" : "0"),
                            ],
                        });
                    string msl = System.Text.Encoding.UTF8.GetString(compiled.GetStage(ShaderStageKind.Vertex).Code.Span);
                    check(msl.Contains("[[base_vertex]]", StringComparison.Ordinal) != absolute,
                        $"Metal preserves {(absolute ? "absolute" : "relative")} vertex IDs (struct input={structured}, array output={array})");
                }
                catch (ShaderCompilationException exception)
                {
                    check(false, $"Metal vertex-ID regression (variant {variant}): {exception.Message}");
                }
            }
        }
        ReadOnlySpan<ShaderTarget> topologyTargets = OperatingSystem.IsWindows()
            ? [Alco.Graphics.ShaderTarget.Spirv, Alco.Graphics.ShaderTarget.MetalSource, Alco.Graphics.ShaderTarget.Dxil]
            : [Alco.Graphics.ShaderTarget.Spirv, Alco.Graphics.ShaderTarget.MetalSource];
        foreach (ShaderTarget target in topologyTargets)
        {
            try
            {
                using ShaderCompiler targetCompiler = new(target);
                CompiledShader compiled = targetCompiler.CompileSource("regression_topology", "regression_topology.slang", TopologySource,
                [
                    new ShaderEntryRequest("vertexMain", ShaderStageKind.Vertex),
                    new ShaderEntryRequest("pixelMain", ShaderStageKind.Pixel),
                ]);
                ReadOnlySpan<byte> vtxCode = compiled.Stages[0].Code.Span;
                ReadOnlySpan<byte> pxlCode = compiled.Stages[1].Code.Span;
                bool valid = target == Alco.Graphics.ShaderTarget.Spirv
                    ? HasSpirvUnitPointSize(vtxCode) && IsSpirv15(pxlCode)
                    : target == Alco.Graphics.ShaderTarget.Dxil ? IsDxil(vtxCode) && IsDxil(pxlCode)
                    : System.Text.Encoding.UTF8.GetString(vtxCode).Contains("[[point_size]]", StringComparison.Ordinal) &&
                        System.Text.Encoding.UTF8.GetString(pxlCode).Contains("pixelMain(", StringComparison.Ordinal);
                check(valid, $"topology vertex/pixel shaders compile to {target} with explicit unit SV_PointSize output");

                CompiledShader facing = targetCompiler.CompileSource("regression_facing", "regression_facing.slang", FacingSource,
                [
                    new ShaderEntryRequest("vertexMain", ShaderStageKind.Vertex),
                    new ShaderEntryRequest("reverseVertexMain", ShaderStageKind.Vertex),
                    new ShaderEntryRequest("facingMain", ShaderStageKind.Pixel),
                    new ShaderEntryRequest("scalarFacingMain", ShaderStageKind.Pixel),
                ]);
                ReadOnlySpan<string> facingEntries = ["vertexMain", "reverseVertexMain", "facingMain", "scalarFacingMain"];
                for (int entry = 0; entry < facingEntries.Length; ++entry)
                {
                    ReadOnlySpan<byte> code = facing.Stages[entry].Code.Span;
                    bool facingValid = target == Alco.Graphics.ShaderTarget.Spirv ? IsSpirv15(code)
                        : target == Alco.Graphics.ShaderTarget.Dxil && IsDxil(code);
                    if (target == Alco.Graphics.ShaderTarget.MetalSource)
                    {
                        string msl = System.Text.Encoding.UTF8.GetString(code);
                        facingValid = msl.Contains(facingEntries[entry] + "(", StringComparison.Ordinal) &&
                            msl.Contains("[[user(COLOR)]]", StringComparison.Ordinal) && msl.Contains("[[user(COLOR_1)]]", StringComparison.Ordinal) &&
                            msl.Contains(entry >= 2 ? "[[front_facing]]" : "[[position]]", StringComparison.Ordinal);
                    }
                    check(facingValid, $"facing {facingEntries[entry]} compiles array varyings and front-face semantics to {target}");
                }

                CompiledShader coordinates = targetCompiler.CompileSource("regression_coordinates", "regression_coordinates.slang",
                    CoordinateSource,
                    [
                        new ShaderEntryRequest("vertexMain", ShaderStageKind.Vertex),
                        new ShaderEntryRequest("meshMain", ShaderStageKind.Mesh),
                        new ShaderEntryRequest("pixelMain", ShaderStageKind.Pixel),
                        new ShaderEntryRequest("sampleVertexMain", ShaderStageKind.Vertex),
                        new ShaderEntryRequest("sampleMain", ShaderStageKind.Pixel),
                    ]);
                ReadOnlySpan<string> coordinateEntries = ["vertexMain", "meshMain", "pixelMain", "sampleVertexMain", "sampleMain"];
                bool coordinatesValid = true;
                for (int entry = 0; entry < coordinateEntries.Length; ++entry)
                {
                    ReadOnlySpan<byte> code = coordinates.Stages[entry].Code.Span;
                    coordinatesValid &= target == Alco.Graphics.ShaderTarget.Spirv ? IsSpirv15(code)
                        : target == Alco.Graphics.ShaderTarget.Dxil ? IsDxil(code) :
                        System.Text.Encoding.UTF8.GetString(code).Contains(coordinateEntries[entry] + "(", StringComparison.Ordinal);
                }
                check(coordinatesValid, $"shared coordinate vertex/mesh, pixel-center/derivative and top-left UV sampling shaders compile to {target}");
            }
            catch (ShaderCompilationException exception)
            {
                check(false, $"raster {target} compilation regression: {exception.Message}");
            }
        }
        RunBufferViewSlang(check);
        RunConcurrentSlang(check);
        RunPinnedSearchPaths(check);
        ReadOnlySpan<string> includeContents = ["\n", ""];
        foreach (string contents in includeContents)
        {
            try
            {
                using ShaderCompiler includeCompiler = new(Alco.Graphics.ShaderTarget.Spirv, new ShaderCompilerOptions
                {
                    FileResolver = (string path) => path.Replace('\\', '/').EndsWith("regression_empty.h", StringComparison.Ordinal) ? contents : null,
                });
                CompiledShader compiled = includeCompiler.CompileSource("regression_include", "regression_include.slang",
                    "#include \"regression_empty.h\"\n" + ComputeSource, ShaderPipelineKind.Compute);
                check(IsSpirv15(compiled.GetStage(ShaderStageKind.Compute).Code.Span),
                    contents.Length == 0 ? "empty virtual include compiles" : "newline virtual include control compiles");
            }
            catch (ShaderCompilationException exception)
            {
                check(false, $"{(contents.Length == 0 ? "empty" : "newline")} virtual include regression: {exception.Message}");
            }
        }
    }

    private static void RunBufferViewSlang(Action<bool, string> check)
    {
        ReadOnlySpan<ShaderTarget> targets = OperatingSystem.IsWindows()
            ? [Alco.Graphics.ShaderTarget.Spirv, Alco.Graphics.ShaderTarget.MetalSource, Alco.Graphics.ShaderTarget.Dxil]
            : [Alco.Graphics.ShaderTarget.Spirv, Alco.Graphics.ShaderTarget.MetalSource];
        (string Name, string Source, uint SizeBytes)[] sources =
        [
            ("consume", ConsumeSource, 256), ("generate", GenerateSource, 32), ("bc", BcSource, 16), ("views", BufferViewSource, 32),
        ];
        foreach (ShaderTarget target in targets)
        {
            foreach ((string name, string source, uint sizeBytes) in sources)
            {
                try
                {
                    CompiledShader compiled;
                    ShaderTypeLayout? root;
                    using (ShaderCompiler compiler = new(target))
                    {
                        compiled = compiler.CompileSource("regression_view_" + name, "regression_view_" + name + ".slang",
                            source, ShaderPipelineKind.Compute);
                        ReadOnlySpan<byte> code = compiled.GetStage(ShaderStageKind.Compute).Code.Span;
                        bool valid = target == Alco.Graphics.ShaderTarget.Spirv ? IsSpirv15(code) :
                            target == Alco.Graphics.ShaderTarget.Dxil ? IsDxil(code) :
                            System.Text.Encoding.UTF8.GetString(code).Contains("computeMain(", StringComparison.Ordinal);
                        check(valid, $"raw-root {name} shader compiles buffer-view loads/stores to {target}");
                        check(compiled.Reflection.FindType("NoSuchRegressionType") == null,
                            $"{target} {name} named layout lookup reports an absent type without substituting a global parameter");
                    }
                    // CompiledShader and ShaderReflection remain alive after ShaderCompiler.Dispose().
                    root = compiled.Reflection.FindType("GPURootArguments");
                    bool layoutMatches = compiled.Reflection.Root != null &&
                        root is { Kind: ShaderTypeKind.Struct } && root.SizeBytes == sizeBytes && root.Fields.Count != 0;
                    if (root != null && layoutMatches)
                    {
                        ShaderFieldLayout field = root.Fields[0];
                        ShaderTypeLayout view = field.Type;
                        layoutMatches = field.OffsetBytes == 0 && view.Kind == ShaderTypeKind.Struct && view.TypeName == "GPUBufferView" &&
                            view.SizeBytes == 8 && view.Fields.Count == 2 && view.Fields[0].Name == "Index" && view.Fields[0].OffsetBytes == 0 &&
                            view.Fields[0].Type is { Kind: ShaderTypeKind.Scalar, SizeBytes: 4 } && view.Fields[1].Name == "Offset" &&
                            view.Fields[1].OffsetBytes == 4 && view.Fields[1].Type is { Kind: ShaderTypeKind.Scalar, SizeBytes: 4 };
                        if (name == "consume")
                            layoutMatches &= root.Fields.Count == 4 && root.Fields[1].OffsetBytes == 8 && root.Fields[2].OffsetBytes == 12 &&
                                root.Fields[3].OffsetBytes == 16 && root.Fields[3].Type is
                                { Kind: ShaderTypeKind.Array, ElementCount: 60, SizeBytes: 240, ElementType: { Kind: ShaderTypeKind.Scalar, SizeBytes: 4 } };
                    }
                    check(layoutMatches,
                        $"{target} {name} GPURootArguments reflects the {sizeBytes}-byte C root and 8-byte GPUBufferView after compiler disposal");
                }
                catch (ShaderCompilationException exception)
                {
                    check(false, $"buffer-view {name} {target} compilation/reflection regression: {exception.Message}");
                }
            }
        }
    }

    private static void RunConcurrentSlang(Action<bool, string> check)
    {
        const int count = 4;
        using ShaderCompiler compiler = new(Alco.Graphics.ShaderTarget.Spirv);
        using Barrier gate = new(count);
        Thread[] threads = new Thread[count];
        bool[] passed = new bool[count];
        string?[] errors = new string?[count];
        for (int index = 0; index < count; ++index)
        {
            int worker = index;
            threads[index] = new Thread(() =>
            {
                gate.SignalAndWait();
                try
                {
                    bool valid = true;
                    for (int iteration = 0; iteration < 3; ++iteration)
                    {
                        string name = $"regression_worker_{worker}_{iteration}";
                        ShaderModule module = compiler.LoadModuleFromSource(name, name + ".slang", ConsumeSource);
                        IReadOnlyList<ShaderModuleEntryPoint> entries = module.EntryPoints;
                        IReadOnlyList<string> dependencies = module.DependencyPaths;
                        bool anyGeneric = entries.Count > 0 && entries[0].HasGenericParameters;
                        valid &= module.Name.Length != 0 && module.FilePath != null && entries.Count == 1 && !anyGeneric;
                        foreach (string dependency in dependencies) valid &= dependency.Length != 0;
                        CompiledShader compiled = module.Compile(ShaderPipelineKind.Compute);
                        ShaderTypeLayout? root = compiled.Reflection.FindType("GPURootArguments");
                        valid &= IsSpirv15(compiled.GetStage("computeMain").Code.Span) && module.EntryPoints.Count == 1 &&
                            root is { Kind: ShaderTypeKind.Struct, SizeBytes: 256, Fields.Count: 4 } &&
                            root.Fields[0].Type is { Kind: ShaderTypeKind.Struct, TypeName: "GPUBufferView", SizeBytes: 8 } &&
                            root.Fields[3].Type is { Kind: ShaderTypeKind.Array, ElementCount: 60, SizeBytes: 240 } &&
                            compiled.Reflection.FindType("MissingConcurrentRoot") == null;
                    }
                    passed[worker] = valid;
                }
                catch (ShaderCompilationException exception)
                {
                    errors[worker] = exception.Message;
                }
            });
            threads[index].Start();
        }
        for (int index = 0; index < count; ++index) threads[index].Join();
        for (int index = 0; index < count; ++index)
            check(passed[index],
                $"concurrent shader compile/named-layout/metadata/disposal worker {index}{(errors[index] == null ? "" : ": " + errors[index])}");
    }

    private static void RunPinnedSearchPaths(Action<bool, string> check)
    {
        ShaderCompilerOptions options = new() { SearchPaths = [AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "examples")] };
        for (int iteration = 0; iteration < 2; ++iteration)
        {
            using ShaderCompiler warmup = new(Alco.Graphics.ShaderTarget.Spirv, options);
            warmup.LoadModuleFromSource("pin_search_warmup", "pin_search_warmup.slang", ComputeSource);
        }
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        long before = GC.GetGCMemoryInfo().PinnedObjectsCount;
        for (int iteration = 0; iteration < 16; ++iteration)
        {
            using ShaderCompiler compiler = new(Alco.Graphics.ShaderTarget.Spirv, options);
            compiler.LoadModuleFromSource("pin_search", "pin_search.slang", ComputeSource);
        }
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        long after = GC.GetGCMemoryInfo().PinnedObjectsCount;
        check(after <= before + 2, $"disposed search-path compilers do not retain UTF-8 pins (before={before}, after={after})");
    }

    private static bool HasSpirvUnitPointSize(ReadOnlySpan<byte> code)
    {
        if (!IsSpirv15(code)) return false;
        ReadOnlySpan<uint> words = MemoryMarshal.Cast<byte, uint>(code);
        uint pointSize = 0;
        for (int index = 5; index < words.Length;)
        {
            int count = (int)(words[index] >> 16);
            if (count == 0 || count > words.Length - index) return false;
            if ((words[index] & 0xffff) == 71 && count == 4 && words[index + 2] == 11 && words[index + 3] == 1)
            {
                if (pointSize != 0 && pointSize != words[index + 1]) return false;
                pointSize = words[index + 1];
            }
            index += count;
        }
        int output = FindDefinition(words, pointSize, words.Length);
        if (output < 0 || (words[output] & 0xffff) != 59 || words[output] >> 16 < 4 || words[output + 3] != 3) return false;
        bool stored = false;
        for (int index = 5; index < words.Length; index += (int)(words[index] >> 16))
        {
            if ((words[index] & 0xffff) != 62 || words[index] >> 16 < 3 || words[index + 1] != pointSize) continue;
            if (!IsUnitValue(words, words[index + 2], index, -1, 0)) return false;
            stored = true;
        }
        return stored;

        static int FindDefinition(ReadOnlySpan<uint> words, uint id, int before)
        {
            if (id == 0) return -1;
            for (int index = 5; index < before; index += (int)(words[index] >> 16))
            {
                uint opcode = words[index] & 0xffff;
                int count = (int)(words[index] >> 16);
                if (opcode is 21 or 22 && count >= 3 && words[index + 1] == id) return index;
                if (opcode is 43 or 59 or 61 or 65 or 81 or 83 && count >= 3 && words[index + 2] == id) return index;
            }
            return -1;
        }

        static bool IsUnitValue(ReadOnlySpan<uint> words, uint id, int before, int member, int depth)
        {
            if (depth > 8) return false;
            int definition = FindDefinition(words, id, before);
            if (definition < 0) return false;
            uint opcode = words[definition] & 0xffff;
            int count = (int)(words[definition] >> 16);
            if (opcode == 43 && count == 4 && member < 0 && words[definition + 3] == 0x3f800000)
            {
                int type = FindDefinition(words, words[definition + 1], definition);
                return type >= 0 && (words[type] & 0xffff) == 22 && words[type] >> 16 == 3 && words[type + 2] == 32;
            }
            if (opcode == 83 && count == 4) return IsUnitValue(words, words[definition + 3], definition, member, depth + 1);
            if (opcode == 81 && count == 5 && member < 0 && words[definition + 4] <= int.MaxValue)
                return IsUnitValue(words, words[definition + 3], definition, (int)words[definition + 4], depth + 1);
            if (opcode != 61 || count < 4) return false;
            uint pointer = words[definition + 3];
            int location = FindDefinition(words, pointer, definition);
            if (location < 0) return false;
            if ((words[location] & 0xffff) == 65 && words[location] >> 16 == 5 && member < 0)
            {
                member = GetMember(words, words[location + 4], location);
                if (member < 0) return false;
                pointer = words[location + 3];
                location = FindDefinition(words, pointer, location);
            }
            if (location < 0 || (words[location] & 0xffff) != 59 || words[location] >> 16 < 4 || words[location + 3] != 7) return false;
            // Follow local member stores only within the current basic block.
            int store = -1;
            for (int index = location; index < definition; index += (int)(words[index] >> 16))
            {
                uint operation = words[index] & 0xffff;
                if (operation is 57 or 63 or 64 or 248) store = -1;
                if (operation != 62 || words[index] >> 16 < 3) continue;
                if (words[index + 1] == pointer)
                {
                    store = member < 0 ? index : -1;
                    continue;
                }
                int target = FindDefinition(words, words[index + 1], index);
                if (target < 0 || ((words[target] & 0xffff) != 59 && (words[target] & 0xffff) != 65))
                {
                    store = -1;
                    continue;
                }
                if ((words[target] & 0xffff) != 65) continue;
                int root = words[target] >> 16 >= 4 ? FindDefinition(words, words[target + 3], target) : -1;
                if (words[target] >> 16 != 5 || root < 0 || (words[root] & 0xffff) != 59)
                {
                    store = -1;
                    continue;
                }
                if (words[target + 3] != pointer) continue;
                int targetMember = GetMember(words, words[target + 4], target);
                if (targetMember < 0) store = -1;
                else if (targetMember == member) store = index;
            }
            return store >= 0 && IsUnitValue(words, words[store + 2], store, -1, depth + 1);
        }

        static int GetMember(ReadOnlySpan<uint> words, uint id, int before)
        {
            int definition = FindDefinition(words, id, before);
            if (definition < 0 || (words[definition] & 0xffff) != 43 || words[definition] >> 16 != 4 ||
                words[definition + 3] > int.MaxValue) return -1;
            int type = FindDefinition(words, words[definition + 1], definition);
            return type >= 0 && (words[type] & 0xffff) == 21 && words[type] >> 16 == 4 && words[type + 2] == 32
                ? (int)words[definition + 3] : -1;
        }
    }

    private static bool IsSpirv15(ReadOnlySpan<byte> code)
    {
        if (code.Length < 20 || code.Length % sizeof(uint) != 0) return false;
        ReadOnlySpan<uint> words = MemoryMarshal.Cast<byte, uint>(code);
        return words[0] == 0x07230203 && words[1] >= 0x00010500;
    }

    private static bool IsDxil(ReadOnlySpan<byte> code)
    {
        if (code.Length < 32 || code.Length % sizeof(uint) != 0) return false;
        ReadOnlySpan<uint> words = MemoryMarshal.Cast<byte, uint>(code);
        if (words[0] != 0x43425844 || words[6] != code.Length || words[7] > words.Length - 8) return false;
        for (int index = 0; index < words[7]; ++index)
        {
            uint offset = words[8 + index];
            if (offset % sizeof(uint) != 0 || offset > code.Length - 8) return false;
            int part = (int)(offset / sizeof(uint));
            if (words[part] == 0x4c495844)
                return words[part + 1] >= 24 && words[part + 1] <= code.Length - offset - 8 && (words[part + 2] & 0xffff) == 0x66;
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StencilData
    {
        public GPUBufferView Output;
        public uint TextureIndex;
        public uint Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MeshData
    {
        public uint Seed;
        public uint Padding0;
        public uint Padding1;
        public uint Padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BufferViewEntry
    {
        public GPUBufferView Source;
        public GPUBufferView Destination;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BufferViewData
    {
        public GPUBufferView Entries;
        public uint EntryCount;
        public uint Seed;
        public uint TextureIndex;
        public uint SamplerIndex;
        public uint Padding0;
        public uint Padding1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CoordinateData
    {
        public float Depth;
        public uint TextureIndex;
        public uint SamplerIndex;
        public uint Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TopologyData
    {
        public GPUBufferView Vertices;
        public uint VertexCount;
        public uint Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BcData
    {
        public GPUBufferView Output;
        public uint Mip;
        public uint Slice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RootData
    {
        public GPUBufferView Output;
        public uint OutputIndex;
        public uint Seed;
        public fixed uint Values[60];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GenerateData
    {
        public GPUBufferView Destination;
        public GPUBufferView Output;
        public GPUBufferView Arguments;
        public uint FirstIndex;
        public uint Seed;
    }
}
