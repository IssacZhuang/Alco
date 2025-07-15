using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;
using Alco.ImGUI;
using FastNoiseLite;
using static FastNoiseLite.FastNoiseLite;

public class Game : GameEngine
{
    // Noise parameters
    private int _seed = 1337;
    private float _frequency = 0.01f;
    private NoiseType _noiseType = NoiseType.OpenSimplex2;
    private FractalType _fractalType = FractalType.None;
    private int _octaves = 3;
    private float _lacunarity = 2.0f;
    private float _gain = 0.5f;
    private float _weightedStrength = 0.0f;
    private float _pingPongStrength = 2.0f;

    // Cellular noise parameters
    private CellularDistanceFunction _cellularDistanceFunc = CellularDistanceFunction.EuclideanSq;
    private CellularReturnType _cellularReturnType = CellularReturnType.Distance;
    private float _cellularJitter = 1.0f;

    // View parameters
    private float _scale = 10.0f;
    private Vector2 _offset = Vector2.Zero;

    // Texture and noise generation
    private const int TextureSize = 512;
    private Texture2D _noiseTexture;
    private FastNoiseLite.FastNoiseLite _noise;
    private Color32[] _pixelData;
    private bool _needsUpdate = true;

    // Parallel noise generation
    private NoiseGenerationTask _noiseTask;

    /// <summary>
    /// Parallel noise generation task that extends ReuseableBatchTask2D for optimized performance.
    /// </summary>
    private class NoiseGenerationTask : ReuseableBatchTask2D
    {
        private readonly Game _game;

        /// <summary>
        /// Initializes a new instance of the NoiseGenerationTask class.
        /// </summary>
        /// <param name="game">The parent game instance.</param>
        public NoiseGenerationTask(Game game) : base()
        {
            _game = game;
        }

        /// <summary>
        /// Executes noise generation for a specific pixel coordinate.
        /// </summary>
        /// <param name="x">The X coordinate of the pixel.</param>
        /// <param name="y">The Y coordinate of the pixel.</param>
        protected override void ExecuteCore(int x, int y)
        {
            // Calculate noise coordinates
            float noiseX = (x / (float)TextureSize - 0.5f) * _game._scale + _game._offset.X;
            float noiseY = (y / (float)TextureSize - 0.5f) * _game._scale + _game._offset.Y;

            // Generate noise value
            float noiseValue = _game._noise.GetNoise(noiseX, noiseY);

            // Normalize from [-1, 1] to [0, 1]
            noiseValue = (noiseValue + 1.0f) * 0.5f;

            // Convert to grayscale color
            byte grayValue = (byte)(noiseValue * 255);
            _game._pixelData[y * TextureSize + x] = new Color32(grayValue, grayValue, grayValue, 255);
        }
    }

    public Game(GameEngineSetting setting) : base(setting)
    {
        // Initialize noise generator
        _noise = new FastNoiseLite.FastNoiseLite(_seed);
        UpdateNoiseSettings();

        // Create texture and pixel data
        _noiseTexture = RenderingSystem.CreateTexture2D(
            TextureSize,
            TextureSize,
            ImageLoadOption.Default
        );
        _pixelData = new Color32[TextureSize * TextureSize];

        // Initialize parallel noise generation task
        _noiseTask = new NoiseGenerationTask(this);

        // Generate initial noise
        GenerateNoise();
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        // Update noise if needed
        if (_needsUpdate)
        {
            GenerateNoise();
            _needsUpdate = false;
        }

        RenderImGUIContent();
    }

    /// <summary>
    /// Updates the noise generator settings with current parameter values.
    /// </summary>
    private void UpdateNoiseSettings()
    {
        _noise.SetSeed(_seed);
        _noise.SetFrequency(_frequency);
        _noise.SetNoiseType(_noiseType);
        _noise.SetFractalType(_fractalType);
        _noise.SetFractalOctaves(_octaves);
        _noise.SetFractalLacunarity(_lacunarity);
        _noise.SetFractalGain(_gain);
        _noise.SetFractalWeightedStrength(_weightedStrength);
        _noise.SetFractalPingPongStrength(_pingPongStrength);
        _noise.SetCellularDistanceFunction(_cellularDistanceFunc);
        _noise.SetCellularReturnType(_cellularReturnType);
        _noise.SetCellularJitter(_cellularJitter);
    }

    /// <summary>
    /// Generates noise texture using parallel processing for improved performance.
    /// </summary>
    private void GenerateNoise()
    {
        UpdateNoiseSettings();

        // Use parallel processing to generate noise
        _noiseTask.RunParallel(TextureSize, TextureSize);

        // Update texture with generated pixel data
        _noiseTexture.SetPixels(_pixelData);
    }

    private void RenderImGUIContent()
    {

        ImGui.Begin("Noise Generator");

        // Show current framerate
        FixedString8 strFramerate = new FixedString8();
        strFramerate.Append(FrameRate);
        ImGui.Text("FPS: ");
        ImGui.SameLine();
        ImGui.Text(strFramerate);

        ImGui.Separator();

        // Basic parameters
        if (ImGui.CollapsingHeader("Basic Parameters", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.SliderInt("Seed", ref _seed, 0, 10000))
                _needsUpdate = true;

            if (ImGui.SliderFloat("Frequency", ref _frequency, 0.001f, 0.1f, "%.4f"))
                _needsUpdate = true;

            if (ImGui.Combo("Noise Type", ref _noiseType))
            {
                _needsUpdate = true;
            }
        }

        // Fractal parameters
        if (ImGui.CollapsingHeader("Fractal Parameters"))
        {
            if (ImGui.Combo("Fractal Type", ref _fractalType))
            {
                _needsUpdate = true;
            }

            if (_fractalType != FractalType.None)
            {
                if (ImGui.SliderInt("Octaves", ref _octaves, 1, 8))
                    _needsUpdate = true;

                if (ImGui.SliderFloat("Lacunarity", ref _lacunarity, 0.5f, 4.0f))
                    _needsUpdate = true;

                if (ImGui.SliderFloat("Gain", ref _gain, 0.0f, 1.0f))
                    _needsUpdate = true;

                if (ImGui.SliderFloat("Weighted Strength", ref _weightedStrength, 0.0f, 1.0f))
                    _needsUpdate = true;

                if (_fractalType == FractalType.PingPong)
                {
                    if (ImGui.SliderFloat("Ping Pong Strength", ref _pingPongStrength, 0.0f, 4.0f))
                        _needsUpdate = true;
                }
            }
        }

        // Cellular noise parameters
        if (_noiseType == NoiseType.Cellular && ImGui.CollapsingHeader("Cellular Parameters"))
        {
            if (ImGui.Combo("Distance Function", ref _cellularDistanceFunc))
            {
                _needsUpdate = true;
            }

            if (ImGui.Combo("Return Type", ref _cellularReturnType))
            {
                _needsUpdate = true;
            }

            if (ImGui.SliderFloat("Jitter", ref _cellularJitter, 0.0f, 2.0f))
                _needsUpdate = true;
        }

        // View parameters
        if (ImGui.CollapsingHeader("View Parameters", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.SliderFloat("Scale", ref _scale, 1.0f, 100.0f))
                _needsUpdate = true;

            if (ImGui.SliderFloat2("Offset", ref _offset, -50.0f, 50.0f))
                _needsUpdate = true;
        }

        ImGui.Separator();

        // Control buttons
        if (ImGui.Button("Generate New"))
            _needsUpdate = true;

        ImGui.SameLine();
        if (ImGui.Button("Reset to Default"))
        {
            ResetToDefault();
            _needsUpdate = true;
        }


        ImGui.Separator();

        // Display the noise texture
        ImGui.Text("Generated Noise:");
        Vector2 imageSize = new Vector2(256, 256);
        ImGui.Image(_noiseTexture, imageSize);

        ImGui.End();

    }

    private void ResetToDefault()
    {
        _seed = 1337;
        _frequency = 0.01f;
        _noiseType = NoiseType.OpenSimplex2;
        _fractalType = FractalType.None;
        _octaves = 3;
        _lacunarity = 2.0f;
        _gain = 0.5f;
        _weightedStrength = 0.0f;
        _pingPongStrength = 2.0f;
        _cellularDistanceFunc = CellularDistanceFunction.EuclideanSq;
        _cellularReturnType = CellularReturnType.Distance;
        _cellularJitter = 1.0f;
        _scale = 10.0f;
        _offset = Vector2.Zero;
    }
}