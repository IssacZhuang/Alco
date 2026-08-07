using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// HDR post-process plugin that registers a <see cref="TonemapStage"/> on the main render
/// pipeline. Supports switching between tone mapping operators at runtime.
/// </summary>
public class PluginHDR : BaseEnginePlugin
{
    private TonemapStage? _stage;

    private TonemapType _tonemapType = TonemapType.Reinhard;
    private ReinhardTonemapData _reinhardData = ReinhardTonemapData.Default;
    private Uncharted2TonemapData _uncharted2Data = Uncharted2TonemapData.Default;
    private FilmicTonemapData _filmicData = FilmicTonemapData.Default;
    private ACESTonemapData _acesData = ACESTonemapData.Default;
    private NeutralTonemapData _neutralData = NeutralTonemapData.Default;
    private AgXTonemapData _agxData = AgXTonemapData.Default;

    /// <summary>
    /// The execution order of the plugin. Runs early in the post process chain.
    /// </summary>
    public override int Order => -900;

    /// <summary>
    /// Current tone mapping operator. Changing this switches the operator of the registered stage.
    /// </summary>
    public TonemapType Tonemap
    {
        get => _tonemapType;
        set
        {
            if (_tonemapType == value)
            {
                return;
            }
            _tonemapType = value;
            if (_stage != null)
            {
                _stage.Operator = value;
            }
        }
    }

    /// <summary>
    /// Reinhard tone mapping data. Uploaded to the GPU immediately while
    /// <see cref="Tonemap"/> is <see cref="TonemapType.Reinhard"/>.
    /// </summary>
    public ReinhardTonemapData ReinhardData
    {
        get => _reinhardData;
        set
        {
            _reinhardData = value;
            if (_stage != null)
            {
                _stage.ReinhardData = value;
            }
        }
    }

    /// <summary>
    /// Filmic tone mapping parameters. Uploaded to the GPU immediately while
    /// <see cref="Tonemap"/> is <see cref="TonemapType.Filmic"/>.
    /// </summary>
    public FilmicTonemapData FilmicData
    {
        get => _filmicData;
        set
        {
            _filmicData = value;
            if (_stage != null)
            {
                _stage.FilmicData = value;
            }
        }
    }

    /// <summary>
    /// ACES tone mapping parameters. Uploaded to the GPU immediately while
    /// <see cref="Tonemap"/> is <see cref="TonemapType.ACES"/>.
    /// </summary>
    public ACESTonemapData ACESData
    {
        get => _acesData;
        set
        {
            _acesData = value;
            if (_stage != null)
            {
                _stage.ACESData = value;
            }
        }
    }

    /// <summary>
    /// Neutral tone mapping parameters. Uploaded to the GPU immediately while
    /// <see cref="Tonemap"/> is <see cref="TonemapType.Neutral"/>.
    /// </summary>
    public NeutralTonemapData NeutralData
    {
        get => _neutralData;
        set
        {
            _neutralData = value;
            if (_stage != null)
            {
                _stage.NeutralData = value;
            }
        }
    }

    /// <summary>
    /// Uncharted 2 filmic tone mapping data. Uploaded to the GPU immediately while
    /// <see cref="Tonemap"/> is <see cref="TonemapType.Uncharted2"/>.
    /// </summary>
    public Uncharted2TonemapData Uncharted2Data
    {
        get => _uncharted2Data;
        set
        {
            _uncharted2Data = value;
            if (_stage != null)
            {
                _stage.Uncharted2Data = value;
            }
        }
    }

    /// <summary>
    /// AgX tone mapping parameters. Uploaded to the GPU immediately while
    /// <see cref="Tonemap"/> is <see cref="TonemapType.AgX"/>.
    /// </summary>
    public AgXTonemapData AgXData
    {
        get => _agxData;
        set
        {
            _agxData = value;
            if (_stage != null)
            {
                _stage.AgXData = value;
            }
        }
    }

    /// <summary>
    /// Alias of <see cref="ReinhardData"/> for backward compatibility.
    /// Only affects rendering when <see cref="Tonemap"/> is <see cref="TonemapType.Reinhard"/>.
    /// </summary>
    public ReinhardTonemapData Data
    {
        get => _reinhardData;
        set => ReinhardData = value;
    }

    public PluginHDR()
    {
        _reinhardData = ReinhardTonemapData.Default;
    }

    /// <summary>
    /// Initializes the plugin with Reinhard parameters.
    /// </summary>
    /// <param name="maxLuminance">Max luminance used by the Reinhard operator.</param>
    /// <param name="gamma">Gamma correction value.</param>
    public PluginHDR(float maxLuminance, float gamma)
    {
        _reinhardData = ReinhardTonemapData.Default;
        _reinhardData.MaxLuminance = maxLuminance;
        _reinhardData.Gamma = gamma;
    }

    /// <summary>
    /// Called after engine initialization. Registers the tone mapping stage on the main pipeline.
    /// </summary>
    public override void OnPostInitialize(GameEngine engine)
    {
        BuiltInAssets assets = engine.BuiltInAssets;
        _stage = new TonemapStage(
            engine.RenderingSystem,
            assets.Shader_ReinhardLuminanceTonemap,
            assets.Shader_Uncharted2Tonemap,
            assets.Shader_FilmicTonemap,
            assets.Shader_ACESTonemap,
            assets.Shader_NeutralTonemap,
            assets.Shader_AgXTonemap);
        _stage.Operator = _tonemapType;
        _stage.ReinhardData = _reinhardData;
        _stage.Uncharted2Data = _uncharted2Data;
        _stage.FilmicData = _filmicData;
        _stage.ACESData = _acesData;
        _stage.NeutralData = _neutralData;
        _stage.AgXData = _agxData;
        engine.MainPipeline.PostProcess.Add(_stage);
    }
}
