using System.Globalization;
using System.Runtime.InteropServices;
using Alco.Rendering;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Perfolizer.Mathematics.OutlierDetection;
using StbImageSharp;
using StbImageWriteSharp;

namespace Alco.Benchmark;

/// <summary>Compares PNG encoders that return an owned byte array from predecoded RGBA8 pixels.</summary>
[Config(typeof(PngBenchmarkConfig))]
public unsafe class BenchmarkImageEncode
{
    private const string PngSizePrefix = "// Alco PNG bytes: ";
    private (byte[] Pixels, int Width, int Height) _image = ([], 0, 0);
    private int _outputBytes;

    /// <summary>Identifies the RGBA8 input fixture.</summary>
    public enum Fixture
    {
        /// <summary>The existing 32×32 grayscale PNG fixture.</summary>
        TinyGray,
        /// <summary>The existing 32×32 RGB PNG fixture.</summary>
        TinyRgb,
        /// <summary>The existing 512×768 RGBA wall atlas.</summary>
        WallAtlas,
        /// <summary>A 1920×1080 screenshot from ALCO_BENCH_PNG_1080P or the generated scene.</summary>
        Screenshot1080p,
        /// <summary>A 3840×2160 screenshot from ALCO_BENCH_PNG_4K or the generated scene.</summary>
        Screenshot4K,
        /// <summary>Deterministic random RGBA8 pixels at 1920×1080.</summary>
        HighEntropy1080p,
    }

    /// <summary>Gets or sets the image to encode.</summary>
    [Params(Fixture.TinyGray, Fixture.TinyRgb, Fixture.WallAtlas, Fixture.Screenshot1080p,
        Fixture.Screenshot4K, Fixture.HighEntropy1080p)]
    public Fixture Image { get; set; }

    /// <summary>Loads or generates one fixture outside the measured workload.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _image = Image switch
        {
            Fixture.TinyGray => Decode(AssetPath("png-small.png")),
            Fixture.TinyRgb => Decode(AssetPath("png-large.png")),
            Fixture.WallAtlas => Decode(AssetPath("wall.png")),
            Fixture.Screenshot1080p => LoadScreenshot("ALCO_BENCH_PNG_1080P", 1920, 1080),
            Fixture.Screenshot4K => LoadScreenshot("ALCO_BENCH_PNG_4K", 3840, 2160),
            Fixture.HighEntropy1080p => CreateNoise(1920, 1080),
            _ => throw new ArgumentOutOfRangeException(nameof(Image)),
        };
    }

    /// <summary>Encodes an owned file using the previous STB writer and a memory stream.</summary>
    [Benchmark(Baseline = true, Description = "STB")]
    public byte[] Stb()
    {
        using var destination = new MemoryStream();
        fixed (byte* rgba = _image.Pixels)
        {
            new ImageWriter().WritePng(rgba, _image.Width, _image.Height, ColorComponents.RedGreenBlueAlpha, destination);
        }
        return RecordSize(destination.ToArray());
    }

    /// <summary>Encodes an owned file using the Fast preset.</summary>
    [Benchmark]
    public byte[] Fast() => RecordSize(ImageEncodeUtility.EncodePng(_image.Pixels, _image.Width, _image.Height, PngEncodeMode.Fast));

    /// <summary>Encodes an owned file using the Balanced preset.</summary>
    [Benchmark]
    public byte[] Balanced() => RecordSize(ImageEncodeUtility.EncodePng(_image.Pixels, _image.Width, _image.Height, PngEncodeMode.Balanced));

    /// <summary>Reports the actual output length to the benchmark host outside the timed iterations.</summary>
    [GlobalCleanup]
    public void ReportOutputSize() => Console.WriteLine(PngSizePrefix + _outputBytes.ToString(CultureInfo.InvariantCulture));

    private byte[] RecordSize(byte[] png)
    {
        _outputBytes = png.Length;
        return png;
    }

    private static string AssetPath(string name) => Path.Combine(AppContext.BaseDirectory, "Files", "Image", name);

    private static (byte[] Pixels, int Width, int Height) Decode(string path)
    {
        byte* pixels = ImageDecodeUtility.DecodeAuto(File.ReadAllBytes(path), out int width, out int height);
        try
        {
            return (new ReadOnlySpan<byte>(pixels, checked(width * height * 4)).ToArray(), width, height);
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }

    private static (byte[] Pixels, int Width, int Height) LoadScreenshot(string variable, int width, int height)
    {
        var path = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(path))
            return CreateScreenshot(width, height);

        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException($"{variable} must be an absolute path to a PNG or JPEG screenshot.");

        var screenshot = Decode(path);
        if (screenshot.Width != width || screenshot.Height != height)
            throw new InvalidOperationException($"{variable} must be {width}×{height}; '{path}' is {screenshot.Width}×{screenshot.Height}.");

        return screenshot;
    }

    private static (byte[] Pixels, int Width, int Height) CreateScreenshot(int width, int height)
    {
        // A textured road with surrounding terrain, broad lighting/shadow, and sharp HUD edges.
        // Sample the detail texture at native pixel density rather than upscaling the 1080p frame.
        var (texture, textureWidth, textureHeight) = Decode(AssetPath("jpeg-real.jpg"));
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int textureRow = y % textureHeight * textureWidth;
            int roadLeft = width / 5 + y / 3;
            int roadRight = width * 4 / 5 - y / 6;
            for (int x = 0; x < width; x++)
            {
                int source = (textureRow + (x + y / 3) % textureWidth) * 4;
                int destination = (y * width + x) * 4;
                int light = 175 + (x + y) * 80 / (width + height) - (x + y / 2 > width * 3 / 4 ? 35 : 0);
                bool onRoad = x >= roadLeft && x < roadRight;
                int grain = texture[source + 1] / 6;
                int red = onRoad ? texture[source] : 39 + grain;
                int green = onRoad ? texture[source + 1] : 61 + grain;
                int blue = onRoad ? texture[source + 2] : 32 + grain;

                if (y >= height * 7 / 8 && x >= width / 4 && x < width * 3 / 4)
                {
                    int slotX = (x - width / 4) % (width / 16);
                    int slotY = y - height * 7 / 8;
                    bool border = slotX < width / 400 || slotY < height / 180;
                    bool icon = slotX >= width / 48 && slotX < width / 24 && slotY >= height / 30 && slotY < height / 12;
                    red = border ? 28 : icon ? 142 : 54;
                    green = border ? 32 : icon ? 116 : 61;
                    blue = border ? 36 : icon ? 64 : 66;
                    light = 255;
                }

                pixels[destination] = (byte)(red * light / 255);
                pixels[destination + 1] = (byte)(green * light / 255);
                pixels[destination + 2] = (byte)(blue * light / 255);
                pixels[destination + 3] = 255;
            }
        }
        return (pixels, width, height);
    }

    private static (byte[] Pixels, int Width, int Height) CreateNoise(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        new Random(0x504E47).NextBytes(pixels);
        return (pixels, width, height);
    }

    private sealed class PngBenchmarkConfig : ManualConfig
    {
        /// <summary>Sets up an image-latency job and report independent of the framework's batched job.</summary>
        public PngBenchmarkConfig()
        {
            Add(DefaultConfig.Instance);
            UnionRule = ConfigUnionRule.AlwaysUseLocal;
            AddJob(Job.Default.WithId("PNG")
                .RunOncePerIteration()
                .WithWarmupCount(3)
                .WithIterationCount(20)
                .WithOutlierMode(OutlierMode.DontRemove));
            AddDiagnoser(MemoryDiagnoser.Default);
            AddColumn(StatisticColumn.P95, new PngSizeColumn());
        }
    }

    private sealed class PngSizeColumn : IColumn
    {
        string IColumn.Id => nameof(PngSizeColumn);
        string IColumn.ColumnName => "PNG bytes";
        bool IColumn.AlwaysShow => true;
        ColumnCategory IColumn.Category => ColumnCategory.Custom;
        int IColumn.PriorityInCategory => 0;
        bool IColumn.IsNumeric => true;
        UnitType IColumn.UnitType => UnitType.Dimensionless;
        string IColumn.Legend => "Length of the returned PNG file in bytes.";

        bool IColumn.IsAvailable(Summary summary) => true;
        bool IColumn.IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
        string IColumn.GetValue(Summary summary, BenchmarkCase benchmarkCase) => GetValue(summary, benchmarkCase);
        string IColumn.GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);

        private static string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            if (summary[benchmarkCase] is not { Success: true } report)
                return "NA";

            foreach (var run in report.ExecuteResults)
            {
                // The teardown writes to stdout, not BenchmarkDotNet's measurement pipe.
                foreach (string line in run.StandardOutput)
                {
                    if (line.StartsWith(PngSizePrefix, StringComparison.Ordinal))
                        return line[PngSizePrefix.Length..];
                }
            }

            return "NA";
        }
    }
}
