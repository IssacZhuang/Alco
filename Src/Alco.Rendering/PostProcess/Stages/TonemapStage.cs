
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Post-process stage that resolves the HDR scene texture into the destination with a tone
/// mapping operator. Supports switching between operators at runtime; the
/// <see cref="TonemapType.Linear"/> operator is a plain copy (no tone mapping).
/// </summary>
public sealed class TonemapStage : PostProcessStage
{
    private readonly RenderingSystem _rendering;
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Shader _reinhardShader;
    private readonly Shader _uncharted2Shader;
    private readonly Shader _filmicShader;
    private readonly Shader _acesShader;
    private readonly Shader _neutralShader;
    private readonly Shader _agxShader;

    private TonemapType _operator = TonemapType.Reinhard;
    private Material? _material;
    private GraphicsBuffer? _dataBuffer;
    private RenderTexture? _boundSource;

    private ReinhardTonemapData _reinhardData = ReinhardTonemapData.Default;
    private Uncharted2TonemapData _uncharted2Data = Uncharted2TonemapData.Default;
    private FilmicTonemapData _filmicData = FilmicTonemapData.Default;
    private ACESTonemapData _acesData = ACESTonemapData.Default;
    private NeutralTonemapData _neutralData = NeutralTonemapData.Default;
    private AgXTonemapData _agxData = AgXTonemapData.Default;

    /// <inheritdoc />
    public override int Order => 1000;

    /// <summary>
    /// The active tone mapping operator. Switching recreates the internal material and
    /// uploads the operator's parameter set.
    /// </summary>
    public TonemapType Operator
    {
        get => _operator;
        set
        {
            if (_operator == value)
            {
                return;
            }
            _operator = value;
            ApplyCurrentOperator();
        }
    }

    /// <summary>
    /// Reinhard tone mapping parameters. Uploaded immediately when Reinhard is active.
    /// </summary>
    public ReinhardTonemapData ReinhardData
    {
        get => _reinhardData;
        set
        {
            _reinhardData = value;
            if (_operator == TonemapType.Reinhard)
            {
                _dataBuffer?.UpdateBuffer(_reinhardData);
            }
        }
    }

    /// <summary>
    /// Uncharted 2 filmic tone mapping parameters. Uploaded immediately when Uncharted 2 is active.
    /// </summary>
    public Uncharted2TonemapData Uncharted2Data
    {
        get => _uncharted2Data;
        set
        {
            _uncharted2Data = value;
            if (_operator == TonemapType.Uncharted2)
            {
                _dataBuffer?.UpdateBuffer(_uncharted2Data);
            }
        }
    }

    /// <summary>
    /// Filmic tone mapping parameters. Uploaded immediately when Filmic is active.
    /// </summary>
    public FilmicTonemapData FilmicData
    {
        get => _filmicData;
        set
        {
            _filmicData = value;
            if (_operator == TonemapType.Filmic)
            {
                _dataBuffer?.UpdateBuffer(_filmicData);
            }
        }
    }

    /// <summary>
    /// ACES tone mapping parameters. Uploaded immediately when ACES is active.
    /// </summary>
    public ACESTonemapData ACESData
    {
        get => _acesData;
        set
        {
            _acesData = value;
            if (_operator == TonemapType.ACES)
            {
                _dataBuffer?.UpdateBuffer(_acesData);
            }
        }
    }

    /// <summary>
    /// Neutral tone mapping parameters. Uploaded immediately when Neutral is active.
    /// </summary>
    public NeutralTonemapData NeutralData
    {
        get => _neutralData;
        set
        {
            _neutralData = value;
            if (_operator == TonemapType.Neutral)
            {
                _dataBuffer?.UpdateBuffer(_neutralData);
            }
        }
    }

    /// <summary>
    /// AgX tone mapping parameters. Uploaded immediately when AgX is active.
    /// </summary>
    public AgXTonemapData AgXData
    {
        get => _agxData;
        set
        {
            _agxData = value;
            if (_operator == TonemapType.AgX)
            {
                _dataBuffer?.UpdateBuffer(_agxData);
            }
        }
    }

    /// <summary>
    /// Creates the stage with the default Reinhard operator. The shaders stay owned by the
    /// caller; the stage creates its materials and buffers lazily per operator.
    /// </summary>
    public TonemapStage(
        RenderingSystem rendering,
        Shader reinhardShader,
        Shader uncharted2Shader,
        Shader filmicShader,
        Shader acesShader,
        Shader neutralShader,
        Shader agxShader)
    {
        _rendering = rendering;
        _renderContext = rendering.CreateRenderContext();
        _fullScreenMesh = rendering.MeshFullScreen;

        _reinhardShader = reinhardShader;
        _uncharted2Shader = uncharted2Shader;
        _filmicShader = filmicShader;
        _acesShader = acesShader;
        _neutralShader = neutralShader;
        _agxShader = agxShader;

        ApplyCurrentOperator();
    }

    /// <inheritdoc />
    public override void Apply(PostProcessContext context)
    {
        if (_operator == TonemapType.Linear || _material == null)
        {
            context.Chain.Blit(context.Source, context.Destination);
            return;
        }

        if (!ReferenceEquals(_boundSource, context.Source))
        {
            _material.SetRenderTexture(ShaderResourceId.Texture, context.Source);
            _boundSource = context.Source;
        }

        _renderContext.Begin(context.Destination);
        _renderContext.Draw(_fullScreenMesh, _material);
        _renderContext.End();
    }

    private void ApplyCurrentOperator()
    {
        _material?.Dispose();
        _dataBuffer?.Dispose();
        _material = null;
        _dataBuffer = null;
        _boundSource = null;

        switch (_operator)
        {
            case TonemapType.Reinhard:
                CreateOperatorResources(_reinhardShader, (uint)Unsafe.SizeOf<ReinhardTonemapData>());
                _dataBuffer!.UpdateBuffer(_reinhardData);
                break;
            case TonemapType.Uncharted2:
                CreateOperatorResources(_uncharted2Shader, (uint)Unsafe.SizeOf<Uncharted2TonemapData>());
                _dataBuffer!.UpdateBuffer(_uncharted2Data);
                break;
            case TonemapType.Filmic:
                CreateOperatorResources(_filmicShader, (uint)Unsafe.SizeOf<FilmicTonemapData>());
                _dataBuffer!.UpdateBuffer(_filmicData);
                break;
            case TonemapType.ACES:
                CreateOperatorResources(_acesShader, (uint)Unsafe.SizeOf<ACESTonemapData>());
                _dataBuffer!.UpdateBuffer(_acesData);
                break;
            case TonemapType.Neutral:
                CreateOperatorResources(_neutralShader, (uint)Unsafe.SizeOf<NeutralTonemapData>());
                _dataBuffer!.UpdateBuffer(_neutralData);
                break;
            case TonemapType.AgX:
                CreateOperatorResources(_agxShader, (uint)Unsafe.SizeOf<AgXTonemapData>());
                _dataBuffer!.UpdateBuffer(_agxData);
                break;
            case TonemapType.Linear:
                // No resources: Apply falls back to a plain blit.
                break;
        }
    }

    private void CreateOperatorResources(Shader shader, uint dataSize)
    {
        _material = _rendering.CreateMaterial(shader);
        _dataBuffer = _rendering.CreateGraphicsBuffer(dataSize, "tonemap_data");
        _material.SetBuffer(ShaderResourceId.Data, _dataBuffer);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _material?.Dispose();
            _dataBuffer?.Dispose();
            _renderContext.Dispose();
        }
    }
}
