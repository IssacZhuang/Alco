using System.Numerics;

namespace Alco.Rendering;

public class TileLightMap : AutoDisposable
{
    private readonly RenderTexture _lightMap;
    private readonly BitmapFloat16RGBA _lightMapCPU;
    private readonly TileMapHeightBuffer _heightBuffer;

    public uint Width => _lightMap.Width;
    public uint Height => _lightMap.Height;

    internal TileLightMap(
        RenderingSystem renderingSystem,
        TileMapHeightBuffer heightBuffer,
        int width,
        int height
        )
    {
        _lightMap = renderingSystem.CreateRenderTexture(renderingSystem.PrefferedLightMapPass, (uint)width, (uint)height, "tile_light_map");
        _lightMapCPU = new BitmapFloat16RGBA(width, height);
        _heightBuffer = heightBuffer;
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
            _lightMap.Dispose();
            _lightMapCPU.Dispose();
        }
    }

}
