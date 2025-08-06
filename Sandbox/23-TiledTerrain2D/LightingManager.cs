using System.Numerics;
using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;

public interface ILight
{
    int2 Position { get; }
    Vector4 Color { get; }
}

public interface IObstacle
{
    int2 Position { get; }
    Color32 Opacity { get; }
}

/// <summary>
/// Manages lighting calculations and post-processing for tile-based terrain rendering.
/// Integrates flood fill lighting with Gaussian blur for smooth light distribution.
/// </summary>
public class LightingManager : AutoDisposable
{
    private readonly FloodFillLightMap _lightMap;
    private readonly GaussianBlur _gaussianBlur;
    private readonly RenderTexture _blurTexture;
    private readonly UnorderedList<ILight> _lights = new();
    private readonly UnorderedList<IObstacle> _obstacles = new();

    private bool _isLightMapDirty = false;
    private bool _isOpacityMapDirty = false;

    // Command buffer for compute operations
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly GPUDevice _graphicsDevice;

    /// <summary>
    /// Gets the final blurred light map texture for rendering.
    /// </summary>
    public RenderTexture LightMap => _blurTexture;

    /// <summary>
    /// Gets or sets the ambient color applied to the light map.
    /// </summary>
    public Vector4 AmbientColor { get; set; } = new Vector4(0.4f, 0.4f, 0.4f, 1.0f);

    /// <summary>
    /// Initializes a new instance of the LightingManager with integrated Gaussian blur.
    /// </summary>
    /// <param name="engine">The game engine instance.</param>
    /// <param name="heightBuffer">The height buffer for terrain elevation data.</param>
    /// <param name="width">Width of the light map.</param>
    /// <param name="height">Height of the light map.</param>
    public LightingManager(GameEngine engine, int width, int height)
    {
        RenderingSystem rendering = engine.RenderingSystem;
        BuiltInAssets builtInAssets = engine.BuiltInAssets;

        // Store graphics device reference
        _graphicsDevice = rendering.GraphicsDevice;

        // Create compute material for lighting
        ComputeMaterial computeMaterial = rendering.CreateComputeMaterial(builtInAssets.Shader_FloodFillLighting);
        _lightMap = rendering.CreateTileLightMap(computeMaterial, width, height);

        // Create Gaussian blur
        ComputeMaterial gaussianBlurMaterial = rendering.CreateComputeMaterial(builtInAssets.Shader_GaussianBlurRGBA16F);
        _blurTexture = rendering.CreateRenderTexture(rendering.PrefferedLightMapPass, (uint)width, (uint)height);

        // Initialize with default kernel values
        float blurCenter = 4f;
        float blurSide = 3f;
        float blurCorner = 2f;
        _gaussianBlur = rendering.CreateGaussianBlur(gaussianBlurMaterial, 3, 3, CreateKernel(blurCenter, blurSide, blurCorner));

        // Create command buffer for compute operations
        _commandBuffer = _graphicsDevice.CreateCommandBuffer();
    }

    /// <summary>
    /// Adds a light source to the lighting system.
    /// </summary>
    /// <param name="light">The light source to add.</param>
    public void AddLight(ILight light)
    {
        _lights.Add(light);
        SetLightMapDirty();
    }

    /// <summary>
    /// Removes a light source from the lighting system.
    /// </summary>
    /// <param name="light">The light source to remove.</param>
    public void RemoveLight(ILight light)
    {
        _lights.Remove(light);
        SetLightMapDirty();
    }

    /// <summary>
    /// Adds an obstacle that blocks light.
    /// </summary>
    /// <param name="obstacle">The obstacle to add.</param>
    public void AddObstacle(IObstacle obstacle)
    {
        _obstacles.Add(obstacle);
        SetOpacityMapDirty();
    }

    /// <summary>
    /// Removes an obstacle from the lighting system.
    /// </summary>
    /// <param name="obstacle">The obstacle to remove.</param>
    public void RemoveObstacle(IObstacle obstacle)
    {
        _obstacles.Remove(obstacle);
        SetOpacityMapDirty();
    }

    /// <summary>
    /// Marks the light map as dirty, requiring recalculation.
    /// </summary>
    public void SetLightMapDirty()
    {
        _isLightMapDirty = true;
    }

    /// <summary>
    /// Marks the opacity map as dirty, requiring recalculation.
    /// </summary>
    public void SetOpacityMapDirty()
    {
        _isOpacityMapDirty = true;
    }

    /// <summary>
    /// Renders the lighting calculations using internal command buffer.
    /// </summary>
    public void Render()
    {
        if (!_isLightMapDirty && !_isOpacityMapDirty)
        {
            return;
        }

        if (_isLightMapDirty)
        {
            _lightMap.ClearLightMap(AmbientColor);
            for (int i = 0; i < _lights.Count; i++)
            {
                ILight light = _lights[i];
                _lightMap.AddLight(light.Position.X, light.Position.Y, light.Color);
            }
            _lightMap.SetDirty();
            _isLightMapDirty = false;
        }

        if (_isOpacityMapDirty)
        {
            _lightMap.ClearOpacityMap();
            for (int i = 0; i < _obstacles.Count; i++)
            {
                IObstacle obstacle = _obstacles[i];
                _lightMap.SetOpacity(obstacle.Position.X, obstacle.Position.Y, obstacle.Opacity);
            }
            _lightMap.SetDirty();
            _isOpacityMapDirty = false;
        }

        // Execute compute operations using internal command buffer
        _commandBuffer.Begin();
        using (var computePass = _commandBuffer.BeginCompute())
        {
            // Render the flood fill lighting
            _lightMap.Compute(computePass);

            // Apply Gaussian blur to the light map
            _gaussianBlur.Compute(computePass, _lightMap.Texture, _blurTexture);
        }
        _commandBuffer.End();
        _graphicsDevice.Submit(_commandBuffer);
    }

    /// <summary>
    /// Creates a 3x3 Gaussian blur kernel with the specified weights.
    /// </summary>
    /// <param name="center">Center weight.</param>
    /// <param name="side">Side weight.</param>
    /// <param name="corner">Corner weight.</param>
    /// <returns>Array of kernel weights.</returns>
    private static float[] CreateKernel(float center, float side, float corner)
    {
        return [
            corner, side, corner,
            side, center, side,
            corner, side, corner
        ];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _blurTexture?.Dispose();
            _gaussianBlur?.Dispose();
            _commandBuffer?.Dispose();
        }
    }
}
