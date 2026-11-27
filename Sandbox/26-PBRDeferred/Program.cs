using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.World3D;

if (!SandboxOptions.TryParse(args, out SandboxOptions options, out string? error))
{
    Log.Error(error);
    Console.WriteLine(SandboxOptions.Usage);
    return 2;
}
if (options.Help)
{
    Console.WriteLine(SandboxOptions.Usage);
    return 0;
}

if (!options.TryResolveBistro(out string? bistroDirectory, out error) || !options.TryGetCamera(out World3DCamera camera, out error))
{
    Log.Error(error);
    return 2;
}

var setting = new GameEngineSetting
{
    StopWhenError = true,
    View = new ViewSetting((int)options.Width, (int)options.Height, "World3D | PBR Deferred"),
    Platform = options.Headless || options.Backend == GraphicsBackend.NoGPU ? new ConsolePlatform() : null,
    Audio = AudioSetting.NoAudio,
    Graphics = GraphicsSetting.Default with { Backend = options.Backend },
};
try
{
    int exitCode;
    using (var game = new Game(setting, options, bistroDirectory, camera))
    {
        game.Run();
        exitCode = game.ExitCode;
    }
    AllocationTracker.CheckAllocated();
    return exitCode;
}
catch (Exception ex)
{
    Log.Error("[World3D sandbox] ", ex);
    return 1;
}

/// <summary>Deterministic startup and capture options for the World3D sandbox.</summary>
internal sealed record SandboxOptions
{
    private const string ProjectFolder = "26-PBRDeferred";
    private const int DefaultFrames = 3;

    /// <summary>Gets the supported command-line switches.</summary>
    public const string Usage = "26-PBRDeferred [--backend=VulkanCompact] [--interior | --procedural] " +
        "[--asset-root=<parent of Bistro>] [--require-bistro] [--bistro-legacy-metallic] [--software-bc] " +
        "[--pos=x,y,z] [--look=x,y,z] [--size=1280x720] [--headless] [--frames=N] [--capture=<path.png>]\n" +
        "Bistro loads the exterior and interior together. --interior starts the camera indoors.";

    /// <summary>Gets the requested graphics backend, VulkanCompact by default.</summary>
    public GraphicsBackend Backend { get; private init; } = GraphicsBackend.VulkanCompact;
    /// <summary>Gets whether the default camera starts inside the Bistro.</summary>
    public bool Interior { get; private init; }
    /// <summary>Gets whether to use the explicit synthetic scene.</summary>
    public bool Procedural { get; private init; }
    /// <summary>Gets whether the real Bistro import and its first deferred draw are required.</summary>
    public bool RequireBistro { get; private init; }
    /// <summary>Gets whether to compare Bistro's legacy implicit dielectric factor.</summary>
    public bool LegacyMetallic { get; private init; }
    /// <summary>Gets whether to force bounded software BC decoding even on capable devices.</summary>
    public bool SoftwareBc { get; private init; }
    /// <summary>Gets whether to render into an offscreen GPU target.</summary>
    public bool Headless { get; private init; }
    /// <summary>Gets whether to print usage and exit without initializing the engine.</summary>
    public bool Help { get; private init; }
    /// <summary>Gets the requested initial viewport width.</summary>
    public uint Width { get; private init; } = 1280;
    /// <summary>Gets the requested initial viewport height.</summary>
    public uint Height { get; private init; } = 720;
    /// <summary>Gets the number of ready frames to submit before stopping, if finite.</summary>
    public int? Frames { get; private init; }
    /// <summary>Gets the path for a PNG of the final submitted offscreen frame.</summary>
    public string? CapturePath { get; private init; }

    private string? AssetRoot { get; init; }
    private Vector3? Position { get; init; }
    private Vector3? Look { get; init; }

    /// <summary>Parses and validates sandbox options without opening any assets or GPU resources.</summary>
    public static bool TryParse(string[] args, out SandboxOptions options, [NotNullWhen(false)] out string? error)
    {
        options = new SandboxOptions();
        error = null;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            int equals = arg.IndexOf('=');
            string key = (equals < 0 ? arg : arg[..equals]).ToLowerInvariant();
            string value = equals < 0 ? "" : arg[(equals + 1)..];
            if (equals < 0 && key == "--asset-root" && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                value = args[++i];

            switch (key)
            {
                case "--help" or "-h" when equals < 0: options = options with { Help = true }; break;
                case "--interior" when equals < 0: options = options with { Interior = true }; break;
                case "--procedural" when equals < 0: options = options with { Procedural = true }; break;
                case "--require-bistro" when equals < 0: options = options with { RequireBistro = true }; break;
                case "--bistro-legacy-metallic" when equals < 0: options = options with { LegacyMetallic = true }; break;
                case "--software-bc" when equals < 0: options = options with { SoftwareBc = true }; break;
                case "--headless" when equals < 0: options = options with { Headless = true }; break;
                case "--backend" when Enum.TryParse(value, true, out GraphicsBackend backend) && Enum.IsDefined(backend):
                    options = options with { Backend = backend };
                    break;
                case "--asset-root" when TryGetFullPath(value, out string? assetRoot):
                    options = options with { AssetRoot = assetRoot };
                    break;
                case "--capture" or "--screenshot" when TryGetFullPath(value, out string? capture) &&
                    Path.GetExtension(capture).Equals(".png", StringComparison.OrdinalIgnoreCase):
                    options = options with { CapturePath = capture, Headless = true };
                    break;
                case "--frames" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int frames) && frames > 0:
                    options = options with { Frames = frames };
                    break;
                case "--size" when TryParseSize(value, out uint width, out uint height):
                    options = options with { Width = width, Height = height };
                    break;
                case "--pos" when TryParseVector(value, out Vector3 position): options = options with { Position = position }; break;
                case "--look" when TryParseVector(value, out Vector3 look): options = options with { Look = look }; break;
                default:
                    error = $"Unknown or invalid option '{arg}'.";
                    return false;
            }
        }

        if (options.Procedural && (options.RequireBistro || options.Interior || options.LegacyMetallic || options.AssetRoot != null))
        {
            error = "--procedural cannot be combined with Bistro scene or validation options.";
            return false;
        }
        if (options.Backend == GraphicsBackend.NoGPU && (options.RequireBistro || options.CapturePath != null))
        {
            error = "--require-bistro and --capture require a real graphics backend.";
            return false;
        }
        if ((options.Headless || options.Backend == GraphicsBackend.NoGPU) && options.Frames == null)
            options = options with { Frames = DefaultFrames };
        return true;
    }

    /// <summary>Resolves the directory containing both Bistro glTF files, unless procedural was requested.</summary>
    public bool TryResolveBistro(out string? directory, [NotNullWhen(false)] out string? error)
    {
        directory = null;
        error = null;
        if (Procedural)
            return true;
        string packaged = Path.Combine(AppContext.BaseDirectory, "Assets");
        string root = AssetRoot ?? FindSourceAssets(AppContext.BaseDirectory) ??
            (Directory.Exists(Path.Combine(packaged, "Bistro")) ? packaged : FindSourceAssets(Environment.CurrentDirectory) ?? packaged);
        string bistro = Path.Combine(root, "Bistro");
        foreach (string filename in BistroImporter.SceneFiles)
        {
            string path = Path.Combine(bistro, filename);
            if (!File.Exists(path))
            {
                error = $"Bistro scene not found at '{path}'. Use --asset-root=<parent of Bistro> or explicitly select --procedural.";
                return false;
            }
        }
        directory = bistro;
        return true;
    }

    /// <summary>Creates the startup World3D camera from the default pose and any command-line overrides.</summary>
    public bool TryGetCamera(out World3DCamera camera, [NotNullWhen(false)] out string? error)
    {
        (Vector3 eye, Vector3 target) = Procedural ? (new Vector3(8, 10, 6), new Vector3(0, 0, 0.7f)) : Interior
            ? (new Vector3(7.8f, 5.4f, 2.1f), new Vector3(0.3f, 6.2f, 1.85f))
            : (new Vector3(-9, -4, 3.5f), new Vector3(-0.3f, 4.3f, 3));
        eye = Position ?? eye;
        target = Look ?? target;
        Vector3 direction = target - eye;
        float length = direction.LengthSquared();
        camera = default;
        error = null;
        if (!float.IsFinite(length) || length <= 1e-12f)
        {
            error = "The camera position and look target must be finite and distinct.";
            return false;
        }
        Vector3 forward = direction / MathF.Sqrt(length);
        Vector3 worldUp = MathF.Abs(forward.Z) < 0.9999f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 right = Vector3.Normalize(Vector3.Cross(worldUp, forward));
        Vector3 up = Vector3.Cross(forward, right);
        Quaternion rotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            forward.X, forward.Y, forward.Z, 0, right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, 0, 0, 0, 1));
        camera = new World3DCamera(new World3DPosition(eye.X, eye.Y, eye.Z), rotation, 0.83f, 0.1f);
        if (!World3DView.TryCreate(camera, new Extent2D(Width, Height), null, out _))
        {
            error = "The camera lies outside the supported World3D coordinate range.";
            return false;
        }
        return true;
    }

    private static string? FindSourceAssets(string start)
    {
        for (DirectoryInfo? directory = new(start); directory != null; directory = directory.Parent)
        {
            string direct = directory.FullName;
            if (IsSourceProject(direct))
                return Path.Combine(direct, "Assets");
            string sandbox = Path.Combine(direct, "Sandbox", ProjectFolder);
            if (IsSourceProject(sandbox))
                return Path.Combine(sandbox, "Assets");
            string engine = Path.Combine(direct, "Alco", "Sandbox", ProjectFolder);
            if (IsSourceProject(engine))
                return Path.Combine(engine, "Assets");
        }
        return null;
    }

    private static bool IsSourceProject(string path) => File.Exists(Path.Combine(path, ProjectFolder + ".csproj"));

    private static bool TryGetFullPath(string value, [NotNullWhen(true)] out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            path = Path.GetFullPath(value);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryParseSize(string value, out uint width, out uint height)
    {
        width = height = 0;
        int separator = value.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        return separator > 0 && uint.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out width) &&
            uint.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out height) &&
            width is >= 64 and <= 3840 && height is >= 64 and <= 2160;
    }

    private static bool TryParseVector(string value, out Vector3 result)
    {
        result = default;
        string[] parts = value.Split(',');
        if (parts.Length != 3 || !Parse(parts[0], out float x) || !Parse(parts[1], out float y) || !Parse(parts[2], out float z))
            return false;
        result = new Vector3(x, y, z);
        return true;

        static bool Parse(string part, out float component) =>
            float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out component) && float.IsFinite(component);
    }
}
