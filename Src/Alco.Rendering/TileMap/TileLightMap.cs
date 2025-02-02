using System.Numerics;

namespace Alco.Rendering;

public class TileLightMap : AutoDisposable
{
    private readonly RenderTexture _lightMapFront;
    private readonly RenderTexture _lightMapBack;
    private readonly DoubleBuffer<RenderTexture> _lightMap;
    private readonly BitmapFloat16RGBA _lightMapCPU;
    private readonly TileMapHeightBuffer _heightBuffer;
    private readonly ComputeMaterial _computeDispatcher;

    private uint _shaderid_front;
    private uint _shaderid_back;

    public int Width => _lightMapCPU.Width;
    public int Height => _lightMapCPU.Height;


    internal TileLightMap(
        RenderingSystem renderingSystem,
        TileMapHeightBuffer heightBuffer,
        ComputeMaterial computeDispatcher,
        int width,
        int height
        )
    {
        _lightMapFront = renderingSystem.CreateRenderTexture(renderingSystem.PrefferedLightMapPass, (uint)width, (uint)height, "tile_light_map");
        _lightMapBack = renderingSystem.CreateRenderTexture(renderingSystem.PrefferedLightMapPass, (uint)width, (uint)height, "tile_light_map");
        _lightMap = new DoubleBuffer<RenderTexture>(_lightMapFront, _lightMapBack);
        _lightMapCPU = new BitmapFloat16RGBA(width, height);
        _heightBuffer = heightBuffer;
        _computeDispatcher = computeDispatcher.CreateInstance();

        _shaderid_front = _computeDispatcher.GetResourceId(ShaderResourceId.FrontBuffer);
        _shaderid_back = _computeDispatcher.GetResourceId(ShaderResourceId.BackBuffer);

    }



    public void Reset()
    {
        _lightMapCPU.Clear();
    }

    public void AddLight(int x, int y, Half4 light)
    {
        _lightMapCPU[x, y] = _lightMapCPU[x, y] + light;
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
