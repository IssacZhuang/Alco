using System.Numerics;
using Alco.Engine;
using Alco.Rendering;
using Alco;
using Alco.Graphics;
using Alco.ImGUI;

using static Alco.Rendering.Noise;


public class Game : GameEngine
{
    // Noise parameters are now managed directly by the _noise object

    // View parameters
    private float _scale = 10.0f;
    private Vector2 _offset = Vector2.Zero;

    // Texture and noise generation
    private const int TextureSize = 512;
    private Texture2D _noiseTexture;
    private Noise _noise;
    private Color32[] _pixelData;
    private bool _needsUpdate = true;

    // Parallel noise generation
    private NoiseGenerationTask _noiseTask;

    private ImGUILogger _logger;

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
        // Initialize noise generator with default seed
        _noise = new Noise(1337);

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

        _logger = new ImGUILogger();
        _logger.OnLogDoubleClick += (str) =>
        {
            Input.CopyToClipboard(str);
        };
        Log.Logger = _logger;
    }

    protected override void OnUpdate(float delta)
    {
        if (Input.IsKeyDown(KeyCode.Escape))
        {
            Stop();
        }

        if (Input.IsKeyDown(KeyCode.F11))
        {
            _logger.IsOpen = !_logger.IsOpen;
        }

        // Update noise if needed
        if (_needsUpdate)
        {
            GenerateNoise();
            _needsUpdate = false;
        }

        RenderImGUIContent();

        _logger.Draw();
    }

    // UpdateNoiseSettings method removed - settings are now applied directly via properties

    /// <summary>
    /// Generates noise texture using parallel processing for improved performance.
    /// </summary>
    private void GenerateNoise()
    {
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
            int seed = _noise.Seed;
            if (ImGui.SliderInt("Seed", ref seed, 0, 10000))
            {
                _noise.Seed = seed;
                _needsUpdate = true;
            }

            float frequency = _noise.Frequency;
            if (ImGui.SliderFloat("Frequency", ref frequency, 0.001f, 0.1f, "%.4f"))
            {
                _noise.Frequency = frequency;
                _needsUpdate = true;
            }

            NoiseType noiseType = _noise.NoiseType;
            if (ImGui.Combo("Noise Type", ref noiseType))
            {
                _noise.NoiseType = noiseType;
                _needsUpdate = true;
            }
        }

        // Fractal parameters
        if (ImGui.CollapsingHeader("Fractal Parameters"))
        {
            FractalType fractalType = _noise.FractalType;
            if (ImGui.Combo("Fractal Type", ref fractalType))
            {
                _noise.FractalType = fractalType;
                _needsUpdate = true;
            }

            if (_noise.FractalType != FractalType.None)
            {
                int octaves = _noise.FractalOctaves;
                if (ImGui.SliderInt("Octaves", ref octaves, 1, 8))
                {
                    _noise.FractalOctaves = octaves;
                    _needsUpdate = true;
                }

                float lacunarity = _noise.FractalLacunarity;
                if (ImGui.SliderFloat("Lacunarity", ref lacunarity, 0.5f, 4.0f))
                {
                    _noise.FractalLacunarity = lacunarity;
                    _needsUpdate = true;
                }

                float gain = _noise.FractalGain;
                if (ImGui.SliderFloat("Gain", ref gain, 0.0f, 1.0f))
                {
                    _noise.FractalGain = gain;
                    _needsUpdate = true;
                }

                float weightedStrength = _noise.FractalWeightedStrength;
                if (ImGui.SliderFloat("Weighted Strength", ref weightedStrength, 0.0f, 1.0f))
                {
                    _noise.FractalWeightedStrength = weightedStrength;
                    _needsUpdate = true;
                }

                if (_noise.FractalType == FractalType.PingPong)
                {
                    float pingPongStrength = _noise.FractalPingPongStrength;
                    if (ImGui.SliderFloat("Ping Pong Strength", ref pingPongStrength, 0.0f, 4.0f))
                    {
                        _noise.FractalPingPongStrength = pingPongStrength;
                        _needsUpdate = true;
                    }
                }
            }
        }

        // Cellular noise parameters
        if (_noise.NoiseType == NoiseType.Cellular && ImGui.CollapsingHeader("Cellular Parameters"))
        {
            CellularDistanceFunction cellularDistanceFunc = _noise.CellularDistanceFunction;
            if (ImGui.Combo("Distance Function", ref cellularDistanceFunc))
            {
                _noise.CellularDistanceFunction = cellularDistanceFunc;
                _needsUpdate = true;
            }

            CellularReturnType cellularReturnType = _noise.CellularReturnType;
            if (ImGui.Combo("Return Type", ref cellularReturnType))
            {
                _noise.CellularReturnType = cellularReturnType;
                _needsUpdate = true;
            }

            float cellularJitter = _noise.CellularJitter;
            if (ImGui.SliderFloat("Jitter", ref cellularJitter, 0.0f, 2.0f))
            {
                _noise.CellularJitter = cellularJitter;
                _needsUpdate = true;
            }
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
        _noise.Seed = 1337;
        _noise.Frequency = 0.01f;
        _noise.NoiseType = NoiseType.OpenSimplex2;
        _noise.FractalType = FractalType.None;
        _noise.FractalOctaves = 3;
        _noise.FractalLacunarity = 2.0f;
        _noise.FractalGain = 0.5f;
        _noise.FractalWeightedStrength = 0.0f;
        _noise.FractalPingPongStrength = 2.0f;
        _noise.CellularDistanceFunction = CellularDistanceFunction.EuclideanSq;
        _noise.CellularReturnType = CellularReturnType.Distance;
        _noise.CellularJitter = 1.0f;
        _scale = 10.0f;
        _offset = Vector2.Zero;
    }
}