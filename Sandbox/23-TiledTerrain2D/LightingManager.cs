

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

public class LightingManager
{
    private readonly FloodFillLightMap _lightMap;
    private readonly UnorderedList<ILight> _lights = new();
    private readonly UnorderedList<IObstacle> _obstacles = new();

    private bool _isLightMapDirty = false;
    private bool _isOpacityMapDirty = false;

    public RenderTexture LightMap => _lightMap.Texture;


    public Vector4 AmbientColor { get; set; } = new Vector4(0.4f, 0.4f, 0.4f, 1.0f);

    public LightingManager(GameEngine engine, TileMapHeightBuffer heightBuffer, int width, int height)
    {
        RenderingSystem rendering = engine.RenderingSystem;
        BuiltInAssets builtInAssets = engine.BuiltInAssets;
        ComputeMaterial computeMaterial = rendering.CreateComputeMaterial(builtInAssets.Shader_TileLighting);
        computeMaterial.SetBuffer(ShaderResourceId.HeightData, heightBuffer);
        _lightMap = rendering.CreateTileLightMap(computeMaterial, width, height);
    }

    public void AddLight(ILight light)
    {
        _lights.Add(light);
        SetLightMapDirty();
    }

    public void RemoveLight(ILight light)
    {
        _lights.Remove(light);
        SetLightMapDirty();
    }

    public void AddObstacle(IObstacle obstacle)
    {
        _obstacles.Add(obstacle);
        SetOpacityMapDirty();
    }

    public void RemoveObstacle(IObstacle obstacle)
    {
        _obstacles.Remove(obstacle);
        SetOpacityMapDirty();
    }

    public void SetLightMapDirty()
    {
        _isLightMapDirty = true;
    }

    public void SetOpacityMapDirty()
    {
        _isOpacityMapDirty = true;
    }

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

        _lightMap.Render();
    }

}
