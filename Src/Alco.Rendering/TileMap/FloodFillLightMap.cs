using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

public class FloodFillLightMap : AutoDisposable
{
    private readonly GPUDevice _device;
    private readonly RenderTexture _lightMapFront;
    private readonly RenderTexture _lightMapBack;
    private readonly DoubleBuffer<RenderTexture> _lightMaps;
    private readonly BitmapFloat16RGBA _lightMapCPU;

    private readonly ComputeMaterial _material;
    private readonly GPUCommandBuffer _command;

    private uint _shaderId_front;
    private uint _shaderId_back;

    private FloodFillLightingConstant _data;


    public int Iteration { get; set; } = 32;
    public float AttenuationSide
    {
        get => _data.AttenuationSide;
        set => _data.AttenuationSide = value;

    }

    public float AttenuationCorner
    {
        get => _data.AttenuationCorner;
        set => _data.AttenuationCorner = value;

    }

    public RenderTexture Texture => _lightMaps.Front;

    public int Width => _lightMapCPU.Width;
    public int Height => _lightMapCPU.Height;
    public string Name { get; }


    internal FloodFillLightMap(
        RenderingSystem renderingSystem,
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
        _material = computeDispatcher.CreateInstance();

        _device = renderingSystem.GraphicsDevice;
        _command = _device.CreateCommandBuffer();

        _data = new FloodFillLightingConstant
        {
            AttenuationSide = 0.1f,
            AttenuationCorner = 0.14141414f,
        };

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

        _lightMaps.Reset();
        _lightMaps.Front.ColorTextures[0].SetPixels(_lightMapCPU);
        _command.Begin();
        _material.ReflectionInfo.Size.GetDispatchCount((uint)Width, (uint)Height, 1, out uint groupX, out uint groupY, out uint groupZ);
        for (int i = 0; i < Iteration; i++)
        {
            _material.SetRenderTexture(_shaderId_front, _lightMaps.Front);
            _material.SetRenderTexture(_shaderId_back, _lightMaps.Back);
            _material.DispatchByGroupWithConstant(_command, groupX, groupY, groupZ, _data);
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
