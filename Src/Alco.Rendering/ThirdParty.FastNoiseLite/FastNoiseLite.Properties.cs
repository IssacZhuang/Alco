
namespace Alco.Rendering;

// getter, setter of FastNoiseLite for better usability and json serialization


public partial class Noise
{
    /// <summary>
    /// Gets or sets seed used for all noise types
    /// </summary>
    /// <remarks>
    /// Default: 1337
    /// </remarks>
    public int Seed
    {
        get => mSeed;
        set => SetSeed(value);
    }

    /// <summary>
    /// Gets or sets frequency for all noise types
    /// </summary>
    /// <remarks>
    /// Default: 0.01
    /// </remarks>
    public float Frequency
    {
        get => mFrequency;
        set => SetFrequency(value);
    }

    /// <summary>
    /// Gets or sets noise algorithm used for GetNoise(...)
    /// </summary>
    /// <remarks>
    /// Default: OpenSimplex2
    /// </remarks>
    public NoiseType NoiseType
    {
        get => mNoiseType;
        set => SetNoiseType(value);
    }

    /// <summary>
    /// Gets or sets domain rotation type for 3D Noise and 3D DomainWarp.
    /// Can aid in reducing directional artifacts when sampling a 2D plane in 3D
    /// </summary>
    /// <remarks>
    /// Default: None
    /// </remarks>
    public RotationType3D RotationType3D
    {
        get => mRotationType3D;
        set => SetRotationType3D(value);
    }

    /// <summary>
    /// Gets or sets method for combining octaves in all fractal noise types
    /// </summary>
    /// <remarks>
    /// Default: None
    /// Note: FractalType.DomainWarp... only affects DomainWarp(...)
    /// </remarks>
    public FractalType FractalType
    {
        get => mFractalType;
        set => SetFractalType(value);
    }

    /// <summary>
    /// Gets or sets octave count for all fractal noise types 
    /// </summary>
    /// <remarks>
    /// Default: 3
    /// </remarks>
    public int FractalOctaves
    {
        get => mOctaves;
        set => SetFractalOctaves(value);
    }

    /// <summary>
    /// Gets or sets octave lacunarity for all fractal noise types
    /// </summary>
    /// <remarks>
    /// Default: 2.0
    /// </remarks>
    public float FractalLacunarity
    {
        get => mLacunarity;
        set => SetFractalLacunarity(value);
    }

    /// <summary>
    /// Gets or sets octave gain for all fractal noise types
    /// </summary>
    /// <remarks>
    /// Default: 0.5
    /// </remarks>
    public float FractalGain
    {
        get => mGain;
        set => SetFractalGain(value);
    }

    /// <summary>
    /// Gets or sets octave weighting for all none DomainWarp fratal types
    /// </summary>
    /// <remarks>
    /// Default: 0.0
    /// Note: Keep between 0...1 to maintain -1...1 output bounding
    /// </remarks>
    public float FractalWeightedStrength
    {
        get => mWeightedStrength;
        set => SetFractalWeightedStrength(value);
    }

    /// <summary>
    /// Gets or sets strength of the fractal ping pong effect
    /// </summary>
    /// <remarks>
    /// Default: 2.0
    /// </remarks>
    public float FractalPingPongStrength
    {
        get => mPingPongStrength;
        set => SetFractalPingPongStrength(value);
    }

    /// <summary>
    /// Gets or sets distance function used in cellular noise calculations
    /// </summary>
    /// <remarks>
    /// Default: EuclideanSq
    /// </remarks>
    public CellularDistanceFunction CellularDistanceFunction
    {
        get => mCellularDistanceFunction;
        set => SetCellularDistanceFunction(value);
    }

    /// <summary>
    /// Gets or sets return type from cellular noise calculations
    /// </summary>
    /// <remarks>
    /// Default: Distance
    /// </remarks>
    public CellularReturnType CellularReturnType
    {
        get => mCellularReturnType;
        set => SetCellularReturnType(value);
    }

    /// <summary>
    /// Gets or sets the maximum distance a cellular point can move from it's grid position
    /// </summary>
    /// <remarks>
    /// Default: 1.0
    /// Note: Setting this higher than 1 will cause artifacts
    /// </remarks> 
    public float CellularJitter
    {
        get => mCellularJitterModifier;
        set => SetCellularJitter(value);
    }

    /// <summary>
    /// Gets or sets the warp algorithm when using DomainWarp(...)
    /// </summary>
    /// <remarks>
    /// Default: OpenSimplex2
    /// </remarks>
    public DomainWarpType DomainWarpType
    {
        get => mDomainWarpType;
        set => SetDomainWarpType(value);
    }

    /// <summary>
    /// Gets or sets the maximum warp distance from original position when using DomainWarp(...)
    /// </summary>
    /// <remarks>
    /// Default: 1.0
    /// </remarks>
    public float DomainWarpAmp
    {
        get => mDomainWarpAmp;
        set => SetDomainWarpAmp(value);
    }

    /// <summary>
    /// Creates a deep copy of this Noise instance with all the same configuration settings
    /// </summary>
    /// <returns>A new Noise instance with identical configuration</returns>
    public Noise Clone()
    {
        var clone = new Noise(this.Seed)
        {
            // Copy all configuration properties
            Frequency = this.Frequency,
            NoiseType = this.NoiseType,
            RotationType3D = this.RotationType3D,
            FractalType = this.FractalType,
            FractalOctaves = this.FractalOctaves,
            FractalLacunarity = this.FractalLacunarity,
            FractalGain = this.FractalGain,
            FractalWeightedStrength = this.FractalWeightedStrength,
            FractalPingPongStrength = this.FractalPingPongStrength,
            CellularDistanceFunction = this.CellularDistanceFunction,
            CellularReturnType = this.CellularReturnType,
            CellularJitter = this.CellularJitter,
            DomainWarpType = this.DomainWarpType,
            DomainWarpAmp = this.DomainWarpAmp
        };

        return clone;
    }
}