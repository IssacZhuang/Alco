using System.Diagnostics;
using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;

public class Game : GameEngine
{
    private const float ViewWidth = 1280;
    private const float ViewHeight = 720;
    private static readonly uint[] RasterSizes = { 48, 128, 384 };

    private readonly string? _screenshotPath;
    private readonly int _screenshotFrames;

    private Camera2DBuffer _camera = null!;
    private GraphicsMaterial _materialText = null!;
    private GraphicsMaterial _materialSprite = null!;
    private Font _font = null!;
    private RenderContext _renderContext = null!;
    private TextRenderer _textRenderer = null!;
    private SpriteRenderer _spriteRenderer = null!;
    private SvgRenderer _svgRenderer = null!;

    private Texture2D _gradientCard = null!;
    private Texture2D _strokeShowcase = null!;
    private Texture2D _donut = null!;
    private Texture2D _overlap = null!;
    private Texture2D _spinner = null!;
    private Texture2D _clockSmall = null!;
    private Texture2D _clockLarge = null!;

    private readonly List<(string Name, string Svg)> _artwork = [];
    private Texture2D?[] _artworkTextures = [];
    private int _rasterSizeIndex = 1;
    private double _lastRasterizeMs;
    private float _time;
    private int _frameCount;
    private Task<RenderCaptureResult>? _screenshotRequest;
    private bool _screenshotSaved;
    private SwapchainCaptureSystem? _swapchainCapture;

    public Game(GameEngineSetting setting, string[] args) : base(setting)
    {
        _screenshotPath = GetArgValue(args, "--screenshot=");
        _screenshotFrames = int.TryParse(GetArgValue(args, "--frames="), out int frames) ? frames : 60;
        if (_screenshotPath != null)
        {
            _swapchainCapture = SwapchainCapture;
        }

        _camera = RenderingSystem.CreateCamera2D(ViewWidth, ViewHeight, 100);

        _materialText = RenderingSystem.CreateGraphicsMaterial(BuiltInAssets.Shader_Text);
        _materialText.SetBuffer(ShaderResourceId.Camera, _camera);

        _materialSprite = RenderingSystem.CreateGraphicsMaterial(BuiltInAssets.Shader_Sprite, "sprite", false);
        // SVG textures are premultiplied alpha
        _materialSprite.BlendState = BlendState.PremultipliedAlpha;
        _materialSprite.SetBuffer(ShaderResourceId.Camera, _camera);

        _font = BuiltInAssets.Font_Default;

        _renderContext = RenderingSystem.CreateRenderContext();
        _textRenderer = RenderingSystem.CreateTextRenderer(_renderContext, _materialText);
        _spriteRenderer = RenderingSystem.CreateSpriteRenderer(_renderContext, _materialSprite);

        _svgRenderer = RenderingSystem.CreateSvgRenderer();

        _gradientCard = Rasterize(GradientCardSvg, 128, 128);
        _strokeShowcase = Rasterize(StrokeShowcaseSvg, 128, 128);
        _donut = Rasterize(DonutSvg, 128, 128);
        _overlap = Rasterize(OverlapSvg, 128, 128);
        _spinner = Rasterize(SpinnerSvg, 128, 128);
        _clockSmall = Rasterize(File.ReadAllText(AssetPath("Clock.svg")), 48, 48);
        _clockLarge = Rasterize(File.ReadAllText(AssetPath("Clock.svg")), 384, 384);

        string svgDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Svg");
        foreach (string file in Directory.GetFiles(svgDir, "*.svg").Order())
        {
            _artwork.Add((Path.GetFileNameWithoutExtension(file), File.ReadAllText(file)));
        }
        RasterizeArtwork();
    }

    private string AssetPath(string file) => Path.Combine(AppContext.BaseDirectory, "Assets", "Svg", file);

    private Texture2D Rasterize(string svg, uint width, uint height)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        Texture2D texture = _svgRenderer.GetTexture(svg, width, height);
        stopwatch.Stop();
        _lastRasterizeMs = stopwatch.Elapsed.TotalMilliseconds;
        return texture;
    }

    private void RasterizeArtwork()
    {
        foreach (Texture2D? texture in _artworkTextures)
        {
            texture?.Dispose();
        }

        uint size = RasterSizes[_rasterSizeIndex];
        _artworkTextures = new Texture2D[_artwork.Count];
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < _artwork.Count; i++)
        {
            _artworkTextures[i] = _svgRenderer.GetTexture(_artwork[i].Svg, size, size);
        }
        stopwatch.Stop();
        _lastRasterizeMs = stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, _artwork.Count);
        Console.WriteLine($"Rasterized {_artwork.Count} icons at {size}px in {stopwatch.Elapsed.TotalMilliseconds:F1} ms total");
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (Input.IsKeyDown(KeyCode.Space))
        {
            _rasterSizeIndex = (_rasterSizeIndex + 1) % RasterSizes.Length;
            RasterizeArtwork();
        }

        _time += delta;

        if (MainPresenter.FrameBuffer is not { } frameBuffer) return;

        using (RenderFrameScope frame = _renderContext.BeginFrame())
        using (RenderPassScope pass = _renderContext.BeginPass(frameBuffer, new ColorFloat(0.07f, 0.08f, 0.11f)))
        {
            _textRenderer.DrawText(_font, "SvgRenderer - GPU vector rasterization", 24,
                new Vector2(-ViewWidth / 2 + 16, ViewHeight / 2 - 12), Rotation2D.Identity, Pivot.LeftTop, new Vector4(1, 1, 1, 1));

            DrawFeatureRow();
            DrawArtworkRows();
            DrawSpinner();
            DrawZoomCompare();
            DrawStats();
        }

        _frameCount++;
        if (_screenshotPath != null && _screenshotRequest == null && !_screenshotSaved && _frameCount >= _screenshotFrames)
        {
            _screenshotRequest = _swapchainCapture!.RequestCaptureAsync();
        }

        if (_screenshotRequest is { IsCompletedSuccessfully: true })
        {
            RenderCaptureResult result = _screenshotRequest.Result;
            if (result.Success && result.PngBytes != null)
            {
                File.WriteAllBytes(_screenshotPath!, result.PngBytes);
                Console.WriteLine($"Screenshot saved to {_screenshotPath}");
            }
            else
            {
                Console.WriteLine($"Screenshot failed: {result.Error}");
            }

            _screenshotSaved = true;
            Stop();
        }
        else if (_screenshotRequest is { IsFaulted: true })
        {
            Console.WriteLine($"Screenshot failed: {_screenshotRequest.Exception?.GetBaseException().Message}");
            Stop();
        }
    }

    private void DrawFeatureRow()
    {
        float y = 240;
        Span<(Texture2D Texture, string Label)> tiles =
        [
            (_gradientCard, "linear + radial gradient"),
            (_strokeShowcase, "strokes: caps / joins / dash"),
            (_donut, "fill-rule evenodd"),
            (_overlap, "translucent overlap"),
        ];

        for (int i = 0; i < tiles.Length; i++)
        {
            float x = -480 + i * 320;
            _spriteRenderer.Draw(tiles[i].Texture, new Vector2(x, y), Rotation2D.Identity, Vector2.One * 120, ColorFloat.White);
            _textRenderer.DrawText(_font, tiles[i].Label, 12, new Vector2(x, y - 78), Rotation2D.Identity, Pivot.CenterTop, new Vector4(0.7f, 0.75f, 0.8f, 1));
        }
    }

    private void DrawArtworkRows()
    {
        const float displaySize = 110;
        const float rowTop = 60;
        const float rowBottom = -110;

        int topCount = Math.Min(8, _artworkTextures.Length);
        for (int i = 0; i < topCount; i++)
        {
            float x = -490 + i * 140;
            _spriteRenderer.Draw(_artworkTextures[i]!, new Vector2(x, rowTop), Rotation2D.Identity, Vector2.One * displaySize, ColorFloat.White);
            _textRenderer.DrawText(_font, _artwork[i].Name, 12, new Vector2(x, rowTop - 72), Rotation2D.Identity, Pivot.CenterTop, new Vector4(0.7f, 0.75f, 0.8f, 1));
        }

        for (int i = topCount; i < _artworkTextures.Length; i++)
        {
            float x = -490 + (i - topCount) * 140;
            _spriteRenderer.Draw(_artworkTextures[i]!, new Vector2(x, rowBottom), Rotation2D.Identity, Vector2.One * displaySize, ColorFloat.White);
            _textRenderer.DrawText(_font, _artwork[i].Name, 12, new Vector2(x, rowBottom - 72), Rotation2D.Identity, Pivot.CenterTop, new Vector4(0.7f, 0.75f, 0.8f, 1));
        }
    }

    private void DrawSpinner()
    {
        // A rasterized arc rotating every frame shows animated vector content.
        Vector2 position = new(230, -110);
        _spriteRenderer.Draw(_spinner, position, Rotation2D.FromRadians(_time * 2.5f), Vector2.One * 130, ColorFloat.White);
        _textRenderer.DrawText(_font, "animated (rotation)", 12, new Vector2(position.X, position.Y - 86), Rotation2D.Identity, Pivot.CenterTop, new Vector4(0.7f, 0.75f, 0.8f, 1));
    }

    private void DrawZoomCompare()
    {
        // Same clock rasterized at 48px and 384px, both displayed at 140px:
        // the crispness gap is the resolution-independence payoff.
        _spriteRenderer.Draw(_clockSmall, new Vector2(400, -110), Rotation2D.Identity, Vector2.One * 140, ColorFloat.White);
        _spriteRenderer.Draw(_clockLarge, new Vector2(545, -110), Rotation2D.Identity, Vector2.One * 140, ColorFloat.White);
        _textRenderer.DrawText(_font, "raster 48px", 12, new Vector2(400, -110 - 88), Rotation2D.Identity, Pivot.CenterTop, new Vector4(0.7f, 0.75f, 0.8f, 1));
        _textRenderer.DrawText(_font, "raster 384px", 12, new Vector2(545, -110 - 88), Rotation2D.Identity, Pivot.CenterTop, new Vector4(0.7f, 0.75f, 0.8f, 1));
    }

    private void DrawStats()
    {
        string stats = $"FPS {FrameRate}   |   {_artwork.Count} icons rasterized at {RasterSizes[_rasterSizeIndex]}px in {_lastRasterizeMs:F2} ms each   |   SPACE: cycle raster size   ESC: quit";
        _textRenderer.DrawText(_font, stats, 14, new Vector2(-ViewWidth / 2 + 16, -ViewHeight / 2 + 8), Rotation2D.Identity, Pivot.LeftBottom, new Vector4(0.8f, 0.85f, 0.9f, 1));
    }

    protected override void OnStop()
    {
        foreach (Texture2D? texture in _artworkTextures)
        {
            texture?.Dispose();
        }
        _gradientCard.Dispose();
        _strokeShowcase.Dispose();
        _donut.Dispose();
        _overlap.Dispose();
        _spinner.Dispose();
        _clockSmall.Dispose();
        _clockLarge.Dispose();
        _svgRenderer.Dispose();
        _textRenderer.Dispose();
    }

    private static string? GetArgValue(string[] args, string prefix)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..];
            }
        }
        return null;
    }

    // ── demo SVG strings (GetTexture takes the SVG text directly) ─────────────

    private const string GradientCardSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128">
          <defs>
            <linearGradient id="lin" x1="0%" y1="0%" x2="100%" y2="100%">
              <stop offset="0%" stop-color="#FFD54F"/>
              <stop offset="100%" stop-color="#FF5252"/>
            </linearGradient>
            <radialGradient id="rad" cx="50%" cy="42%" r="60%">
              <stop offset="0%" stop-color="#FFFFFF"/>
              <stop offset="100%" stop-color="#26C6DA"/>
            </radialGradient>
          </defs>
          <rect x="8" y="8" width="112" height="112" rx="18" fill="url(#lin)"/>
          <circle cx="64" cy="58" r="34" fill="url(#rad)"/>
          <rect x="30" y="100" width="68" height="8" rx="4" fill="#FFFFFF" fill-opacity="0.85"/>
        </svg>
        """;

    private const string StrokeShowcaseSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128">
          <path d="M 16 100 C 30 40, 55 40, 64 72 S 100 105, 112 40"
                fill="none" stroke="#FFB74D" stroke-width="9"
                stroke-linecap="round" stroke-linejoin="round"/>
          <path d="M 16 116 L 112 116" fill="none" stroke="#81C784"
                stroke-width="7" stroke-linecap="butt" stroke-dasharray="14 9"/>
          <path d="M 20 20 L 45 14 L 70 20 L 95 14" fill="none"
                stroke="#64B5F6" stroke-width="6" stroke-linejoin="miter"/>
        </svg>
        """;

    private const string DonutSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128">
          <path d="M 64 14 A 50 50 0 1 1 63.9 14 Z M 64 38 A 26 26 0 1 0 64.1 38 Z"
                fill="#BA68C8" fill-rule="evenodd"/>
        </svg>
        """;

    private const string OverlapSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128">
          <circle cx="48" cy="56" r="34" fill="#FF0000" fill-opacity="0.5"/>
          <circle cx="80" cy="56" r="34" fill="#00FF00" fill-opacity="0.5"/>
          <circle cx="64" cy="84" r="34" fill="#0000FF" fill-opacity="0.5"/>
        </svg>
        """;

    private const string SpinnerSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128">
          <path d="M 64 16 A 48 48 0 1 1 30.06 30.06"
                fill="none" stroke="#FFFFFF" stroke-width="11" stroke-linecap="round"/>
        </svg>
        """;
}
