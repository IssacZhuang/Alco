using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Effects.Test;

/// <summary>
/// Owns an isolated <see cref="GPUDevice"/>, <see cref="RenderingSystem"/>, and
/// <see cref="ShaderSystem"/> configured for <c>Alco.Effects</c> unit and GPU tests.
/// </summary>
internal sealed unsafe class EffectsTestHost : IDisposable
{
    /// <summary>Gets the backing GPU device.</summary>
    public GPUDevice Device { get; }

    /// <summary>Gets the rendering system tied to <see cref="Device"/>.</summary>
    public RenderingSystem RenderingSystem { get; }

    /// <summary>Gets the shader system preconfigured with test and built-in shader modules.</summary>
    public ShaderSystem Shaders { get; }

    /// <summary>Initializes a test host on the specified graphics backend.</summary>
    public EffectsTestHost(
        GraphicsBackend backend = GraphicsBackend.NoGPU,
        bool includeEffectsBuiltIns = true,
        IReadOnlyList<string>? extraShaderDirectories = null)
    {
        Device = Gpu.CreateDevice(new DeviceDesc { Backend = backend, Headless = true });
        ShaderCompilerOptions options = CreateCompilerOptions(extraShaderDirectories);
        RenderingSystem = new RenderingSystem(
            Device,
            textureHeapByteCount: 16UL * 1024 * 1024,
            bufferHeapByteCount: 16UL * 1024 * 1024,
            uploadStagingByteCount: 4UL * 1024 * 1024,
            shaderOptions: options);
        Shaders = RenderingSystem.Shaders;

        if (includeEffectsBuiltIns)
            AlcoEffectsShaders.EnsureBuiltInModulesLoaded(Shaders);
    }

    /// <summary>Creates compiler options resolving test shader files and any extra directories.</summary>
    public static ShaderCompilerOptions CreateCompilerOptions(IReadOnlyList<string>? extraShaderDirectories = null)
    {
        List<string> searchDirs = BuildSearchDirectories(extraShaderDirectories);
        Dictionary<string, string> slangFiles = CollectSlangFiles(searchDirs);

        ShaderFileResolver fileResolver = ShaderModuleResolver.Create(
            path => slangFiles.TryGetValue(path, out string? fullPath) ? File.OpenRead(fullPath) : null,
            () => slangFiles.Keys);

        return new ShaderCompilerOptions
        {
            SearchPaths = searchDirs,
            FileResolver = fileResolver,
        };
    }

    /// <summary>Resolves the effective GPU backend, honoring <c>ALCO_GRAPHICS_BACKEND</c> when <paramref name="requested"/> is <see cref="GraphicsBackend.Auto"/>.</summary>
    public static GraphicsBackend ResolveGpuBackend(GraphicsBackend requested = GraphicsBackend.Auto)
    {
        if (requested != GraphicsBackend.Auto)
            return requested;

        string? env = Environment.GetEnvironmentVariable("ALCO_GRAPHICS_BACKEND");
        if (!string.IsNullOrWhiteSpace(env) && Enum.TryParse(env.Trim(), ignoreCase: true, out GraphicsBackend parsed))
            return parsed;

        return GraphicsBackend.Auto;
    }

    /// <summary>Creates a real-GPU test host or skips cleanly via <see cref="Assert.Ignore(string)"/> if unsupported.</summary>
    public static EffectsTestHost CreateGpu(bool includeEffectsBuiltIns = true, IReadOnlyList<string>? extraShaderDirectories = null)
    {
        GraphicsBackend backend = ResolveGpuBackend();
        try
        {
            return new EffectsTestHost(backend, includeEffectsBuiltIns, extraShaderDirectories);
        }
        catch (GPUException ex)
        {
            Assert.Ignore($"GPU backend {backend} unavailable on this host: {ex.Message}");
            throw;
        }
    }

    /// <summary>Creates an RGBA8 offscreen color target suitable for render pass output and GPU readback.</summary>
    public Texture CreateColorTarget(int width, int height)
        => RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D((uint)width, (uint)height, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
        });

    /// <summary>Creates a D32F offscreen depth target for 3D particle/trail rendering.</summary>
    public Texture CreateDepthTarget(int width, int height)
        => RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D((uint)width, (uint)height, 1),
            Format = Format.D32Float,
            Usage = TextureUsage.DepthStencilAttachment,
        });

    /// <summary>Creates and synchronously uploads a solid-color RGBA8 <see cref="Image"/> texture.</summary>
    public Image CreateSolidImage(byte r, byte g, byte b, byte a, int size = 4)
    {
        Texture texture = RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D((uint)size, (uint)size, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        byte[] pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = a;
        }
        UploadRgba8Texture(texture, pixels);
        return new Image(texture, ownsResource: true);
    }

    /// <summary>Synchronously uploads tightly packed RGBA8 pixels into <paramref name="texture"/>.</summary>
    public void UploadRgba8Texture(Texture texture, ReadOnlySpan<byte> rgbaPixels)
    {
        var context = new RenderContext(RenderingSystem);
        try
        {
            context.BeginFrame();
            context.SetTextureData(texture, rgbaPixels);
            context.Submit().Wait();
        }
        finally
        {
            context.Destroy();
        }
    }

    /// <summary>Reads back tightly packed RGBA8 pixels from <paramref name="target"/> in a fresh transfer frame.</summary>
    public byte[] ReadBackRgba8(Texture target, int width, int height)
    {
        ulong byteCount = (ulong)width * (ulong)height * 4UL;
        GPUHeap readback = Device.AllocateGPUHeap(byteCount, MemoryType.Readback);
        var context = new RenderContext(RenderingSystem);
        try
        {
            context.BeginFrame();
            context.Barrier(Stage.AllCommands, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit().Wait();

            byte[] pixels = new byte[(int)byteCount];
            new ReadOnlySpan<byte>(readback.Range.Cpu, (int)byteCount).CopyTo(pixels);
            return pixels;
        }
        finally
        {
            context.Destroy();
            readback.Destroy();
        }
    }

    /// <summary>Records texture readback into an open recording frame, submits, waits, and returns RGBA8 bytes.</summary>
    public byte[] ReadBackRgba8AndSubmit(RenderContext context, Texture target, int width, int height)
    {
        ulong byteCount = (ulong)width * (ulong)height * 4UL;
        GPUHeap readback = Device.AllocateGPUHeap(byteCount, MemoryType.Readback);
        try
        {
            context.Barrier(Stage.ColorOutput, Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            context.CopyTextureToMemory(target.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit().Wait();

            byte[] pixels = new byte[(int)byteCount];
            new ReadOnlySpan<byte>(readback.Range.Cpu, (int)byteCount).CopyTo(pixels);
            return pixels;
        }
        finally
        {
            readback.Destroy();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Device.WaitIdle();
        RenderingSystem.Destroy();
        Device.Destroy();
    }

    private static List<string> BuildSearchDirectories(IReadOnlyList<string>? extra)
    {
        var dirs = new List<string>();
        string baseAssets = Path.Combine(AppContext.BaseDirectory, "Assets");
        string baseShaders = Path.Combine(baseAssets, "Shaders");
        if (Directory.Exists(baseShaders))
            dirs.Add(baseShaders);
        if (Directory.Exists(baseAssets))
            dirs.Add(baseAssets);
        dirs.Add(AppContext.BaseDirectory);

        if (extra != null)
        {
            foreach (string dir in extra)
            {
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    dirs.Add(dir);
            }
        }
        return dirs;
    }

    private static Dictionary<string, string> CollectSlangFiles(List<string> searchDirs)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in searchDirs)
        {
            foreach (string file in Directory.GetFiles(dir, "*.slang", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
                files.TryAdd(rel, file);
                string fileName = Path.GetFileName(file);
                files.TryAdd(fileName, file);
            }
        }
        return files;
    }
}
