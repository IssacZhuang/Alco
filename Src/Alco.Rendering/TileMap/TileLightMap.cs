using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

public class TileLightMap : AutoDisposable
{
    private struct Data
    {
        public float attenuationSide;
        public float attenuationCorner;
    }
    private readonly GPUDevice _device;
    private readonly RenderTexture _lightMapFront;
    private readonly RenderTexture _lightMapBack;
    private readonly DoubleBuffer<RenderTexture> _lightMaps;
    private readonly BitmapFloat16RGBA _lightMapCPU;
    private readonly TileMapHeightBuffer _heightBuffer;
    private readonly GraphicsValueBuffer<Data> _dataBuffer;
    private readonly ComputeMaterial _material;
    private readonly GPUCommandBuffer _command;
    private bool _isDirty = true;

    private uint _shaderId_front;
    private uint _shaderId_back;


    public int Iteration { get; set; } = 32;
    public float AttenuationSide
    {
        get => _dataBuffer.Value.attenuationSide;
        set {
            _dataBuffer.Value.attenuationSide = value;
            _isDirty = true;
        }
    }

    public float AttenuationCorner
    {
        get => _dataBuffer.Value.attenuationCorner;
        set {
            _dataBuffer.Value.attenuationCorner = value;
            _isDirty = true;
        }
    }

    public RenderTexture LightMap => _lightMaps.Front;

    public int Width => _lightMapCPU.Width;
    public int Height => _lightMapCPU.Height;
    public string Name { get; }


    internal TileLightMap(
        RenderingSystem renderingSystem,
        TileMapHeightBuffer heightBuffer,
        ComputeMaterial computeDispatcher,
        int width,
        int height,
        string name = "tile_light_map"
        )
    {
        _lightMapFront = renderingSystem.CreateRenderTexture(renderingSystem.PrefferedLightMapPass, (uint)width, (uint)height, "tile_light_map");
        _lightMapBack = renderingSystem.CreateRenderTexture(renderingSystem.PrefferedLightMapPass, (uint)width, (uint)height, "tile_light_map");
        _lightMaps = new DoubleBuffer<RenderTexture>(_lightMapFront, _lightMapBack);
        _lightMapCPU = new BitmapFloat16RGBA(width, height);
        _heightBuffer = heightBuffer;
        _material = computeDispatcher.CreateInstance();

        _device = renderingSystem.GraphicsDevice;
        _command = _device.CreateCommandBuffer();

        _dataBuffer = renderingSystem.CreateGraphicsValueBuffer<Data>();
        _dataBuffer.Value.attenuationSide = 0.1f;
        _dataBuffer.Value.attenuationCorner = 0.14141414f;
        _dataBuffer.UpdateBuffer();
        _material.SetBuffer(ShaderResourceId.Data, _dataBuffer);

        _shaderId_front = _material.GetResourceId(ShaderResourceId.FrontBuffer);
        _shaderId_back = _material.GetResourceId(ShaderResourceId.BackBuffer);

        Name = name;

    }



    public void Reset()
    {
        _lightMapCPU.Clear();
    }

    public void AddLight(int x, int y, Half4 light)
    {
        _lightMapCPU[x, y] = _lightMapCPU[x, y] + light;
    }

    public void SetLight(int x, int y, Half4 light)
    {
        _lightMapCPU[x, y] = light;
    }

    public void Render()
    {
        if(_isDirty){
            _dataBuffer.UpdateBuffer();
            _isDirty = false;
        }
        _lightMaps.Reset();
        _lightMaps.Front.ColorTextures[0].SetPixels(_lightMapCPU);
        _command.Begin();
        for (int i = 0; i < Iteration; i++)
        {
            _material.SetRenderTexture(_shaderId_front, _lightMaps.Front);
            _material.SetRenderTexture(_shaderId_back, _lightMaps.Back);
            _material.Dispatch(_command, 5, 5, 1);
            _lightMaps.Swap();
        }
        _command.End();
        _device.Submit(_command);
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lightMapFront.Dispose();
            _lightMapBack.Dispose();
            _lightMapCPU.Dispose();
        }
    }

}
