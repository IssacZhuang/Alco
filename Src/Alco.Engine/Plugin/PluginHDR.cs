using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// HDR post-process plugin that applies a tone mapping operator on the main render target.
/// Supports switching between Reinhard and Uncharted 2 filmic tone mapping.
/// </summary>
public class PluginHDR : BaseEnginePlugin
{
    /// <summary>
    /// Supported tone mapping operators.
    /// </summary>
    public enum TonemapType
    {
        /// <summary>
        /// Linear tonemapping (no tonemap), directly blits the HDR buffer.
        /// </summary>
        Linear,
        Reinhard,
        Uncharted2,
        ReinhardJodie,
        Filmic,
        ACES,
    }

    private Shader? _shader;
    private Material? _material;
    private GraphicsBuffer? _dataBuffer;

    private GameEngine? _engine;
    private TonemapType _tonemapType = TonemapType.Reinhard;
    private ReinhardToneMapData _reinhardData = ReinhardToneMapData.Default;
    private Uncharted2ToneMapData _uncharted2Data = Uncharted2ToneMapData.Default;
    private ReinhardJodieToneMapData _reinhardJodieData = ReinhardJodieToneMapData.Default;
    private FilmicToneMapData _filmicData = FilmicToneMapData.Default;
    private ACESToneMapData _acesData = ACESToneMapData.Default;

    /// <summary>
    /// The execution order of the plugin. Runs early in the post process chain.
    /// </summary>
    public override int Order => -900;

    /// <summary>
    /// Current tone mapping operator. Changing this will recreate internal resources and re-bind the HDR pass.
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
            ApplyCurrentTonemap();
        }
    }

    /// <summary>
    /// Reinhard tone mapping data. If the current <see cref="Tonemap"/> is <see cref="TonemapType.Reinhard"/>,
    /// it updates the GPU buffer immediately.
    /// </summary>
    public ReinhardToneMapData ReinhardData
    {
        get => _reinhardData;
        set
        {
            _reinhardData = value;
            if (_tonemapType == TonemapType.Reinhard)
            {
                _dataBuffer?.UpdateBuffer(_reinhardData);
            }
        }
    }

    /// <summary>
    /// Reinhard-Jodie tone mapping parameters. If the current <see cref="Tonemap"/> is
    /// <see cref="TonemapType.ReinhardJodie"/> or <see cref="TonemapType.ACES"/> (reuses exposure/gamma),
    /// it updates the GPU buffer immediately.
    /// </summary>
    public ReinhardJodieToneMapData ReinhardJodieData
    {
        get => _reinhardJodieData;
        set
        {
            _reinhardJodieData = value;
            if (_tonemapType == TonemapType.ReinhardJodie || _tonemapType == TonemapType.ACES)
            {
                _dataBuffer?.UpdateBuffer(_reinhardJodieData);
            }
        }
    }

    /// <summary>
    /// Filmic tone mapping parameters. If the current <see cref="Tonemap"/> is <see cref="TonemapType.Filmic"/>,
    /// it updates the GPU buffer immediately.
    /// </summary>
    public FilmicToneMapData FilmicData
    {
        get => _filmicData;
        set
        {
            _filmicData = value;
            if (_tonemapType == TonemapType.Filmic)
            {
                _dataBuffer?.UpdateBuffer(_filmicData);
            }
        }
    }

    /// <summary>
    /// ACES tone mapping parameters. If the current <see cref="Tonemap"/> is <see cref="TonemapType.ACES"/>,
    /// it updates the GPU buffer immediately.
    /// </summary>
    public ACESToneMapData ACESData
    {
        get => _acesData;
        set
        {
            _acesData = value;
            if (_tonemapType == TonemapType.ACES)
            {
                _dataBuffer?.UpdateBuffer(_acesData);
            }
        }
    }

    /// <summary>
    /// Uncharted 2 filmic tone mapping data. If the current <see cref="Tonemap"/> is <see cref="TonemapType.Uncharted2"/>,
    /// it updates the GPU buffer immediately.
    /// </summary>
    public Uncharted2ToneMapData Uncharted2Data
    {
        get => _uncharted2Data;
        set
        {
            _uncharted2Data = value;
            if (_tonemapType == TonemapType.Uncharted2)
            {
                _dataBuffer?.UpdateBuffer(_uncharted2Data);
            }
        }
    }

    /// <summary>
    /// Alias of <see cref="ReinhardData"/> for backward compatibility.
    /// Only affects rendering when <see cref="Tonemap"/> is <see cref="TonemapType.Reinhard"/>.
    /// </summary>
    public ReinhardToneMapData Data
    {
        get => _reinhardData;
        set
        {
            _reinhardData = value;
            if (_tonemapType == TonemapType.Reinhard)
            {
                _dataBuffer?.UpdateBuffer(_reinhardData);
            }
        }
    }

    public PluginHDR()
    {
        _reinhardData = ReinhardToneMapData.Default;
    }

    /// <summary>
    /// Initializes the plugin with Reinhard parameters.
    /// </summary>
    /// <param name="maxLuminance">Max luminance used by the Reinhard operator.</param>
    /// <param name="gamma">Gamma correction value.</param>
    public PluginHDR(float maxLuminance, float gamma)
    {
        _reinhardData = ReinhardToneMapData.Default;
        _reinhardData.MaxLuminance = maxLuminance;
        _reinhardData.Gamma = gamma;
    }

    /// <summary>
    /// Called after engine initialization. Sets up the tone mapping material on the main HDR pass.
    /// </summary>
    public unsafe override void OnPostInitialize(GameEngine engine)
    {
        _engine = engine;
        ApplyCurrentTonemap();
    }

    /// <summary>
    /// Dispose internal GPU resources.
    /// </summary>
    public override void Dispose()
    {
        _shader?.Dispose();
        _material?.Dispose();
        _dataBuffer?.Dispose();
    }

    private unsafe void ApplyCurrentTonemap()
    {
        if (_engine == null)
        {
            return;
        }

        RenderingSystem rendering = _engine.RenderingSystem;

        _shader?.Dispose();
        _material?.Dispose();
        _dataBuffer?.Dispose();
        _shader = null;
        _material = null;
        _dataBuffer = null;

        switch (_tonemapType)
        {
            case TonemapType.Reinhard:
                _shader = _engine.AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_ReinhardLuminanceTonemap);
                _material = rendering.CreateMaterial(_shader);
                _dataBuffer = rendering.CreateGraphicsBuffer((uint)sizeof(ReinhardToneMapData), "hdr_tonemap_data");
                _dataBuffer.UpdateBuffer(_reinhardData);
                _material.SetBuffer(ShaderResourceId.Data, _dataBuffer);
                break;
            case TonemapType.Uncharted2:
                _shader = _engine.AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_Uncharted2Tonemap);
                _material = rendering.CreateMaterial(_shader);
                _dataBuffer = rendering.CreateGraphicsBuffer((uint)sizeof(Uncharted2ToneMapData), "hdr_tonemap_data");
                _dataBuffer.UpdateBuffer(_uncharted2Data);
                _material.SetBuffer(ShaderResourceId.Data, _dataBuffer);
                break;
            case TonemapType.Linear:
                _shader = _engine.AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_Blit);
                _material = rendering.CreateMaterial(_shader);
                // No data buffer required for linear blit
                break;
            case TonemapType.ReinhardJodie:
                _shader = _engine.AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_ReinhardJodieTonemap);
                _material = rendering.CreateMaterial(_shader);
                _dataBuffer = rendering.CreateGraphicsBuffer((uint)sizeof(ReinhardJodieToneMapData), "hdr_tonemap_data");
                _dataBuffer.UpdateBuffer(_reinhardJodieData);
                _material.SetBuffer(ShaderResourceId.Data, _dataBuffer);
                break;
            case TonemapType.Filmic:
                _shader = _engine.AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_FilmicTonemap);
                _material = rendering.CreateMaterial(_shader);
                _dataBuffer = rendering.CreateGraphicsBuffer((uint)sizeof(FilmicToneMapData), "hdr_tonemap_data");
                _dataBuffer.UpdateBuffer(_filmicData);
                _material.SetBuffer(ShaderResourceId.Data, _dataBuffer);
                break;
            case TonemapType.ACES:
                _shader = _engine.AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_ACESTonemap);
                _material = rendering.CreateMaterial(_shader);
                _dataBuffer = rendering.CreateGraphicsBuffer((uint)sizeof(ACESToneMapData), "hdr_tonemap_data");
                _dataBuffer.UpdateBuffer(_acesData);
                _material.SetBuffer(ShaderResourceId.Data, _dataBuffer);
                break;
        }

        _engine.MainRenderTarget.SetAttachmentLayout(rendering.PrefferedHDRPass, _material!);
    }
}