using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private const int SsrBlueNoiseSize = 128;

    private Texture? _ssrBlueNoiseTex;
    private readonly Texture?[] _ssrHistoryTex = new Texture?[2];

    private Shader? _ssrBlueNoiseShader;
    private Shader? _ssrTraceShader;
    private Shader? _ssrResolveShader;
    private Shader? _ssrCompositeShader;
    private bool _ssrShadersLoaded;
    private bool _ssrBlueNoiseDirty = true;

    private Extent2D _ssrHistoryExtent;
    private int _submittedSsrHistoryIndex = -1;
    private ulong _submittedSsrDiscardVersion;
    private uint _ssrFrameCounter;

    private bool _recordedSsrEnabled;
    private bool _recordedSsrHalfRes;
    private bool _recordedSsrHistoryValid;
    private int _recordedCurrSsrHistoryIndex = -1;
    private int _recordedPrevSsrHistoryIndex = -1;
    private Extent2D _recordedSsrTraceExtent;
    private int _recordedSsrMaxSteps;
    private BufferSlice _recordedSsrViews;
    private World3DView _ssrPrevView;

    /// <summary>Gets or sets runtime settings for Hi-Z accelerated stochastic SSR.</summary>
    public World3DSsrSettings Ssr { get; set; } = new();

    /// <summary>Gets the recorded SSR telemetry for the latest frame.</summary>
    public World3DSsrStats LastRecordedSsr { get; private set; } =
        new(false, default, 0, false, false);

    private void PrepareSsr(RenderContext context, Extent2D viewport, in World3DView primaryView)
    {
        _recordedSsrEnabled = false;
        _recordedSsrHistoryValid = false;
        _recordedCurrSsrHistoryIndex = -1;
        _recordedPrevSsrHistoryIndex = -1;
        _recordedSsrTraceExtent = default;
        _recordedSsrMaxSteps = 0;
        _recordedSsrViews = default;

        World3DSsrSettings settings = Ssr;
        if (!_recordedWorld || !settings.Enabled || viewport.X == 0 || viewport.Y == 0)
        {
            _submittedSsrHistoryIndex = -1;
            LastRecordedSsr = new World3DSsrStats(false, default, 0, false, false);
            return;
        }

        EnsureSsrShaders();
        EnsureSsrTextures(settings, viewport, context.DiscardVersion,
            out Extent2D traceExtent, out bool historyValid, out int prevIdx, out int currIdx);

        ++_ssrFrameCounter;
        Span<World3DView> views = stackalloc World3DView[2];
        views[0] = primaryView;
        views[1] = historyValid ? _ssrPrevView : primaryView;
        _recordedSsrViews = context.UploadTransient<World3DView>(views);

        _recordedSsrEnabled = true;
        _recordedSsrHalfRes = settings.HalfResolution;
        _recordedSsrHistoryValid = historyValid;
        _recordedCurrSsrHistoryIndex = currIdx;
        _recordedPrevSsrHistoryIndex = prevIdx;
        _recordedSsrTraceExtent = traceExtent;
        _recordedSsrMaxSteps = Math.Clamp(settings.MaxSteps, 8, 128);

        LastRecordedSsr = new World3DSsrStats(
            true,
            traceExtent,
            _recordedSsrMaxSteps,
            historyValid,
            _recordedHiZBuildEnabled && _frameCurrHiZTexture != null);
    }

    private void ReconcileSsr(bool submitted)
    {
        if (submitted && _recordedWorld && _recordedSsrEnabled && _recordedCurrSsrHistoryIndex >= 0)
        {
            _ssrBlueNoiseDirty = false;
            _submittedSsrHistoryIndex = _recordedCurrSsrHistoryIndex;
            _submittedSsrDiscardVersion = _recordedDiscardVersion;
            _ssrPrevView = _recordedView;
        }
        else if (!submitted || !_recordedSsrEnabled || !_recordedWorld)
        {
            _submittedSsrHistoryIndex = -1;
        }
    }

    private void DisposeSsr()
    {
        _ssrBlueNoiseTex?.Dispose(); _ssrBlueNoiseTex = null;
        for (int i = 0; i < _ssrHistoryTex.Length; ++i)
        {
            _ssrHistoryTex[i]?.Dispose();
            _ssrHistoryTex[i] = null;
        }
        _ssrBlueNoiseDirty = true;
        _ssrHistoryExtent = default;
        _submittedSsrHistoryIndex = -1;
    }

    private void EnsureSsrShaders()
    {
        if (_ssrShadersLoaded)
            return;

        _ssrBlueNoiseShader = LoadSceneCompute<SsrBlueNoiseRoot>("World3DSsrBlueNoise");
        _ssrTraceShader = LoadSceneCompute<SsrTraceRoot>("World3DSsrTrace");
        _ssrResolveShader = LoadSceneCompute<SsrResolveRoot>("World3DSsrResolve");
        _ssrCompositeShader = LoadSceneCompute<SsrCompositeRoot>("World3DSsrComposite");
        _ssrShadersLoaded = true;
    }

    private void EnsureSsrTextures(World3DSsrSettings settings, Extent2D viewport, ulong discardVersion,
        out Extent2D traceExtent, out bool historyValid, out int prevIdx, out int currIdx)
    {
        if (_ssrBlueNoiseTex == null)
        {
            _ssrBlueNoiseTex = _system.CreateTexture(new TextureDesc
            {
                Type = TextureType.TwoD,
                Extent = new Extent3D(SsrBlueNoiseSize, SsrBlueNoiseSize, 1),
                MipLevels = 1,
                LayerCount = 1,
                Format = Format.Rgba16Float,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            });
            _ssrBlueNoiseDirty = true;
        }

        traceExtent = settings.HalfResolution
            ? new Extent2D(Math.Max(1u, (viewport.X + 1u) >> 1), Math.Max(1u, (viewport.Y + 1u) >> 1))
            : viewport;

        if (!_ssrHistoryExtent.Equals(traceExtent) || _ssrHistoryTex[0] == null || _ssrHistoryTex[1] == null)
        {
            for (int i = 0; i < 2; ++i)
            {
                _ssrHistoryTex[i]?.Dispose();
                _ssrHistoryTex[i] = _system.CreateTexture(new TextureDesc
                {
                    Type = TextureType.TwoD,
                    Extent = new Extent3D(traceExtent.X, traceExtent.Y, 1),
                    MipLevels = 1,
                    LayerCount = 1,
                    Format = Format.Rgba16Float,
                    Usage = TextureUsage.Sampled | TextureUsage.Storage,
                });
            }
            _ssrHistoryExtent = traceExtent;
            _submittedSsrHistoryIndex = -1;
        }

        historyValid = _submittedSsrHistoryIndex >= 0
            && _submittedSsrDiscardVersion == discardVersion
            && _ssrHistoryTex[_submittedSsrHistoryIndex] is { IsDisposed: false };
        prevIdx = historyValid ? _submittedSsrHistoryIndex : 0;
        currIdx = historyValid ? 1 - _submittedSsrHistoryIndex : 0;
    }

    private sealed class SsrPasses(World3DScene scene, RenderChain chain) : IRenderGraphNode
    {
        private const int PassBlueNoise = 0;
        private const int PassTrace = 1;
        private const int PassResolve = 2;
        private const int PassComposite = 3;

        private readonly World3DScene _scene = scene;
        private readonly RenderChain _chain = chain;
        private RGTexture _blueNoiseRG;
        private RGTexture _rawColorRG;
        private RGTexture _metaRG;
        private RGTexture _historyInRG;
        private RGTexture _historyOutRG;
        private RGTexture _hdrInRG;
        private RGTexture _hdrOutRG;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            _blueNoiseRG = default;
            _rawColorRG = default;
            _metaRG = default;
            _historyInRG = default;
            _historyOutRG = default;
            _hdrInRG = default;
            _hdrOutRG = default;

            World3DLightingDebugView dbg = _scene.LightingDebugView;
            bool allowSsr = dbg == World3DLightingDebugView.None
                || dbg == World3DLightingDebugView.SsrReflection
                || dbg == World3DLightingDebugView.SsrConfidence;
            GBufferTargets gb = _scene._gbufferTargets;
            if (!_scene._recordedWorld || !_scene._recordedSsrEnabled || !_chain.Current.IsValid
                || !gb.Depth.IsValid || _scene._recordedCurrSsrHistoryIndex < 0
                || !allowSsr)
                return;

            _hdrInRG = _chain.Current;
            _blueNoiseRG = builder.ImportTexture(_scene._ssrBlueNoiseTex!, "World3D.Ssr.BlueNoise");
            if (_scene._ssrBlueNoiseDirty)
            {
                RenderGraphPassBuilder bnPass = builder.AddPass("World3D.Ssr.BlueNoise", PassBlueNoise);
                bnPass.Write(_blueNoiseRG, RGUsage.ComputeWrite);
            }

            Extent2D trace = _scene._recordedSsrTraceExtent;
            Extent3D traceExt3 = new(trace.X, trace.Y, 1u);
            _rawColorRG = builder.CreateTexture(new RGTextureDesc { Name = "World3D.Ssr.RawColor", Extent = traceExt3, Format = Format.Rgba16Float });
            _metaRG = builder.CreateTexture(new RGTextureDesc { Name = "World3D.Ssr.Meta", Extent = traceExt3, Format = Format.Rgba16Float });
            _historyOutRG = builder.ImportTexture(_scene._ssrHistoryTex[_scene._recordedCurrSsrHistoryIndex]!, "World3D.Ssr.HistoryOut");

            RGBuffer views = builder.ImportBuffer(_scene._frameViews.Buffer!, "World3D.Views");
            RGBuffer ssrViews = builder.ImportBuffer(_scene._recordedSsrViews.Buffer!, "World3D.SsrViews");
            RenderGraphPassBuilder tracePass = builder.AddPass("World3D.Ssr.Trace", PassTrace);
            tracePass.Read(views, RGUsage.ComputeRead);
            if (!_scene._frameShadowData.IsEmpty)
                tracePass.Read(builder.ImportBuffer(_scene._frameShadowData.Buffer!, "World3D.ShadowFrame"), RGUsage.ComputeRead);
            tracePass.Read(_hdrInRG, RGUsage.ComputeRead);
            tracePass.Read(gb.Normal, RGUsage.ComputeRead);
            tracePass.Read(gb.Material, RGUsage.ComputeRead);
            tracePass.Read(gb.Depth, RGUsage.ComputeRead);
            tracePass.Read(_blueNoiseRG, RGUsage.ComputeRead);
            if (_scene._frameCurrHiZ.IsValid)
                tracePass.Read(_scene._frameCurrHiZ, RGUsage.ComputeRead);
            tracePass.Write(_rawColorRG, RGUsage.ComputeWrite);
            tracePass.Write(_metaRG, RGUsage.ComputeWrite);

            RenderGraphPassBuilder resolvePass = builder.AddPass("World3D.Ssr.Resolve", PassResolve);
            resolvePass.Read(ssrViews, RGUsage.ComputeRead);
            resolvePass.Read(_rawColorRG, RGUsage.ComputeRead);
            resolvePass.Read(_metaRG, RGUsage.ComputeRead);
            resolvePass.Read(gb.Normal, RGUsage.ComputeRead);
            resolvePass.Read(gb.Material, RGUsage.ComputeRead);
            resolvePass.Read(gb.Depth, RGUsage.ComputeRead);
            if (_scene._recordedSsrHistoryValid && _scene._recordedPrevSsrHistoryIndex >= 0)
            {
                _historyInRG = builder.ImportTexture(_scene._ssrHistoryTex[_scene._recordedPrevSsrHistoryIndex]!, "World3D.Ssr.HistoryIn");
                resolvePass.Read(_historyInRG, RGUsage.ComputeRead);
            }
            resolvePass.Write(_historyOutRG, RGUsage.ComputeWrite);

            _hdrOutRG = builder.CreateTexture(new RGTextureDesc
            {
                Name = "World3D.Ssr.HDR",
                Extent = builder.GetExtent(_hdrInRG),
                Format = Format.Rgba16Float,
            });
            RenderGraphPassBuilder compPass = builder.AddPass("World3D.Ssr.Composite", PassComposite);
            compPass.Read(views, RGUsage.ComputeRead);
            if (!_scene._frameShadowData.IsEmpty)
                compPass.Read(builder.ImportBuffer(_scene._frameShadowData.Buffer!, "World3D.ShadowFrame"), RGUsage.ComputeRead);
            compPass.Read(_hdrInRG, RGUsage.ComputeRead);
            compPass.Read(_historyOutRG, RGUsage.ComputeRead);
            compPass.Read(gb.Albedo, RGUsage.ComputeRead);
            compPass.Read(gb.Normal, RGUsage.ComputeRead);
            compPass.Read(gb.Material, RGUsage.ComputeRead);
            compPass.Read(gb.Depth, RGUsage.ComputeRead);
            if (_scene._recordedVoxelGiEnabled && _scene._frameGiOutputSpecularRG.IsValid)
                compPass.Read(_scene._frameGiOutputSpecularRG, RGUsage.ComputeRead);
            compPass.Write(_hdrOutRG, RGUsage.ComputeWrite);

            _chain.Advance(_hdrOutRG);
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            if (passId == PassBlueNoise)
            {
                SsrBlueNoiseRoot bnRoot = new(_scene._ssrBlueNoiseTex!.GetStorageId(0), SsrBlueNoiseSize, 0u, 0u, 0u);
                uint bnGroups = (SsrBlueNoiseSize + 7u) / 8u;
                compute.Dispatch(_scene._ssrBlueNoiseShader!, in bnRoot, new Extent3D(bnGroups, bnGroups, 1u));
                return;
            }

            GBufferTargets gb = _scene._gbufferTargets;
            World3DSsrSettings settings = _scene.Ssr;
            Extent2D trace = _scene._recordedSsrTraceExtent;
            Extent3D traceGroups = new((trace.X + 7u) / 8u, (trace.Y + 7u) / 8u, 1u);

            if (passId == PassTrace)
            {
                bool hizActive = _scene._frameCurrHiZ.IsValid && _scene._frameCurrHiZTexture != null;
                SsrTraceRoot traceRoot = new(
                    new Vector4(Math.Max(settings.MaxDistanceMeters, 1f), Math.Max(settings.ThicknessMeters, 0.02f),
                        Math.Clamp(settings.MaxRoughness, 0.05f, 1f), Math.Clamp(settings.EdgeFade, 0.01f, 0.5f)),
                    new Vector4(trace.X, trace.Y, hizActive ? _scene._recordedHiZMipCount : 0u, _scene._recordedSsrMaxSteps),
                    _scene._frameViews.View,
                    _scene._frameShadowData.View,
                    GPUTextureId.FromSampled(context.GetTexture(_hdrInRG)),
                    GPUTextureId.FromSampled(context.GetTexture(gb.Normal)),
                    GPUTextureId.FromSampled(context.GetTexture(gb.Material)),
                    GPUTextureId.FromSampled(context.GetTexture(gb.Depth)),
                    hizActive ? _scene._frameCurrHiZTexture!.SampledId : GPUTextureId.Invalid,
                    _scene._ssrBlueNoiseTex!.SampledId,
                    GPUTextureId.FromStorage(context.GetTexture(_rawColorRG)),
                    GPUTextureId.FromStorage(context.GetTexture(_metaRG)),
                    _scene._ssrFrameCounter,
                    hizActive ? 1u : 0u,
                    settings.RejectBackfaceHits ? 1u : 0u,
                    0u);
                compute.Dispatch(_scene._ssrTraceShader!, in traceRoot, traceGroups);
                return;
            }

            if (passId == PassResolve)
            {
                Texture histOut = _scene._ssrHistoryTex[_scene._recordedCurrSsrHistoryIndex]!;
                SsrResolveRoot resolveRoot = new(
                    _scene._recordedSsrViews.View,
                    GPUTextureId.FromSampled(context.GetTexture(_rawColorRG)),
                    GPUTextureId.FromSampled(context.GetTexture(_metaRG)),
                    GPUTextureId.FromSampled(context.GetTexture(gb.Normal)),
                    GPUTextureId.FromSampled(context.GetTexture(gb.Material)),
                    GPUTextureId.FromSampled(context.GetTexture(gb.Depth)),
                    _scene._recordedSsrHistoryValid ? _scene._ssrHistoryTex[_scene._recordedPrevSsrHistoryIndex]!.SampledId : GPUTextureId.Invalid,
                    histOut.GetStorageId(0),
                    trace.X,
                    trace.Y,
                    Math.Clamp(settings.TemporalBlend, 0f, 0.96f),
                    _scene._recordedSsrHistoryValid ? 1u : 0u);
                compute.Dispatch(_scene._ssrResolveShader!, in resolveRoot, traceGroups);
                return;
            }

            Extent3D full = context.GetExtent(_hdrOutRG);
            bool giSpecValid = _scene._recordedVoxelGiEnabled && _scene._frameGiOutputSpecularRG.IsValid;
            SsrCompositeRoot compRoot = new(
                _scene._frameViews.View,
                _scene._frameShadowData.View,
                GPUTextureId.FromSampled(context.GetTexture(_hdrInRG)),
                _scene._ssrHistoryTex[_scene._recordedCurrSsrHistoryIndex]!.SampledId,
                GPUTextureId.FromSampled(context.GetTexture(gb.Albedo)),
                GPUTextureId.FromSampled(context.GetTexture(gb.Normal)),
                GPUTextureId.FromSampled(context.GetTexture(gb.Material)),
                GPUTextureId.FromSampled(context.GetTexture(gb.Depth)),
                giSpecValid ? GPUTextureId.FromSampled(context.GetTexture(_scene._frameGiOutputSpecularRG)) : GPUTextureId.Invalid,
                GPUTextureId.FromStorage(context.GetTexture(_hdrOutRG)),
                trace.X,
                trace.Y,
                _scene._recordedSsrHalfRes ? 1u : 0u,
                Math.Max(settings.Intensity, 0f),
                Math.Clamp(settings.MaxRoughness, 0.05f, 1f),
                MathF.Max(0f, _scene.VoxelGi.IndirectSpecularIntensity),
                giSpecValid ? 1u : 0u,
                settings.DisableVoxelSpecularFallback ? 1u : 0u);
            compute.Dispatch(_scene._ssrCompositeShader!, in compRoot, new Extent3D((full.X + 7u) / 8u, (full.Y + 7u) / 8u, 1u));
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 20)]
    private readonly record struct SsrBlueNoiseRoot(
        GPUTextureId OutputTex,
        uint Size,
        uint Reserved0,
        uint Reserved1,
        uint Reserved2);

    [StructLayout(LayoutKind.Sequential, Size = 96)]
    private readonly record struct SsrTraceRoot(
        Vector4 SsrParams,
        Vector4 ScreenAndStepParams,
        GPUBufferView Views,
        GPUBufferView ShadowFrame,
        GPUTextureId HdrInTex,
        GPUTextureId NormalTex,
        GPUTextureId MaterialTex,
        GPUTextureId DepthTex,
        GPUTextureId HiZTex,
        GPUTextureId BlueNoiseTex,
        GPUTextureId OutRawColorTex,
        GPUTextureId OutMetaTex,
        uint FrameIndex,
        uint HiZActive,
        uint RejectBackface,
        uint Reserved0);

    [StructLayout(LayoutKind.Sequential, Size = 52)]
    private readonly record struct SsrResolveRoot(
        GPUBufferView Views,
        GPUTextureId RawColorTex,
        GPUTextureId MetaTex,
        GPUTextureId NormalTex,
        GPUTextureId MaterialTex,
        GPUTextureId DepthTex,
        GPUTextureId HistoryInTex,
        GPUTextureId HistoryOutTex,
        uint TraceWidth,
        uint TraceHeight,
        float TemporalBlend,
        uint HistoryValid);

    [StructLayout(LayoutKind.Sequential, Size = 80)]
    private readonly record struct SsrCompositeRoot(
        GPUBufferView Views,
        GPUBufferView ShadowFrame,
        GPUTextureId HdrInTex,
        GPUTextureId ResolvedSsrTex,
        GPUTextureId AlbedoTex,
        GPUTextureId NormalTex,
        GPUTextureId MaterialTex,
        GPUTextureId DepthTex,
        GPUTextureId GiSpecularTex,
        GPUTextureId HdrOutTex,
        uint TraceWidth,
        uint TraceHeight,
        uint HalfResolution,
        float Intensity,
        float MaxRoughness,
        float GiSpecularScale,
        uint GiSpecularValid,
        uint DisableVoxelFallback);
}
