using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

public class FloodFillLightMap : AutoDisposable
{
    private readonly RenderTexture _lightMapFront;
    private readonly RenderTexture _lightMapBack;
    private readonly DoubleBuffer<RenderTexture> _lightMaps;
    private readonly BitmapFloat16RGBA _lightMapCPU;

    private readonly RenderTexture _opacityMap;
    private readonly BitmapUIntRGBA _opacityMapCPU;

    private readonly ComputeMaterial _material;

    private uint _shaderId_front;
    private uint _shaderId_back;

    private bool _isTextureDirty = false;
    private bool _isResultDirty = false;


    public int Iteration { get; set; } = 32;
    public float AttenuationSide { get; set; } = 0.1f;
    public float AttenuationCorner { get; set; } = 0.14141414f;

    public float AttenuationMultiplier { get; set; } = 1f;

    public RenderTexture Texture => _lightMaps.Front;

    public RenderTexture OpacityMap => _opacityMap;

    public int Width => _lightMapCPU.Width;
    public int Height => _lightMapCPU.Height;
    public string Name { get; }


    internal FloodFillLightMap(
        RenderingSystem renderingSystem,
        ComputeMaterial material,
        int width,
        int height,
        string name = "tile_light_map"
        )
    {
        _lightMapFront = renderingSystem.CreateRenderTexture(renderingSystem.PreferredLightMapPass, (uint)width, (uint)height, "tile_light_map");
        _lightMapBack = renderingSystem.CreateRenderTexture(renderingSystem.PreferredLightMapPass, (uint)width, (uint)height, "tile_light_map");
        _lightMaps = new DoubleBuffer<RenderTexture>(_lightMapFront, _lightMapBack);
        _lightMapCPU = new BitmapFloat16RGBA(width, height, new Half4(0, 0, 0, 0));
        _material = material.CreateInstance();

        _opacityMap = renderingSystem.CreateRenderTexture(renderingSystem.PreferredRGBATexturePass, (uint)width, (uint)height, "tile_opacity_map");
        _opacityMapCPU = new BitmapUIntRGBA(width, height, Color32.White);



        _shaderId_front = _material.GetResourceId(ShaderResourceId.FrontBuffer);
        _shaderId_back = _material.GetResourceId(ShaderResourceId.BackBuffer);

        _material.TrySetRenderTexture(ShaderResourceId.OpacityMap, _opacityMap);

        Name = name;

    }

    public void ClearLightMap(Vector4 color)
    {
        _lightMapCPU.Fill(color);
    }

    /// <summary>
    /// Clears a sub-rectangle of the CPU light map to a color, row-wise. Combined with
    /// <see cref="UploadLightMapRegion"/> this rebuilds only the compute region's input state.
    /// </summary>
    /// <param name="x">The x origin of the region, in texels.</param>
    /// <param name="y">The y origin of the region, in texels.</param>
    /// <param name="width">The width of the region, in texels.</param>
    /// <param name="height">The height of the region, in texels.</param>
    /// <param name="color">The color to fill with.</param>
    public void ClearLightMap(int x, int y, int width, int height, Vector4 color)
    {
        _lightMapCPU.Fill(x, y, width, height, color);
    }

    /// <summary>
    /// Uploads the whole CPU light map into the front texture and marks the computed result dirty.
    /// Use for full-map recomputes (warm-up, snapshots); the region variant for per-tick updates.
    /// </summary>
    public void UploadLightMap()
    {
        _lightMaps.Front.ColorTextures[0].SetPixels(_lightMapCPU);
        _isResultDirty = true;
    }

    /// <summary>
    /// Uploads a sub-rectangle of the CPU light map into the front texture and marks the computed
    /// result dirty. Texels outside the region keep their last computed values — callers must
    /// have rebuilt the region's CPU input (clear + light stamps) beforehand.
    /// </summary>
    /// <param name="x">The x origin of the region, in texels.</param>
    /// <param name="y">The y origin of the region, in texels.</param>
    /// <param name="width">The width of the region, in texels.</param>
    /// <param name="height">The height of the region, in texels.</param>
    public void UploadLightMapRegion(int x, int y, int width, int height)
    {
        _lightMaps.Front.ColorTextures[0].SetPixels(_lightMapCPU, x, y, width, height);
        _isResultDirty = true;
    }

    /// <summary>
    /// Uploads the whole CPU opacity map into the opacity texture. Called when obstacle
    /// registrations changed; opacity is event-driven and always uploaded in full.
    /// </summary>
    public void UploadOpacityMap()
    {
        _opacityMap.ColorTextures[0].SetPixels(_opacityMapCPU);
        _isResultDirty = true;
    }

    public void SetDirty()
    {
        _isTextureDirty = true;
    }

    public void ResetOpacity()
    {
        _opacityMapCPU.Fill(Color32.White);
    }

    public void AddLight(int x, int y, Half4 light)
    {
        _lightMapCPU[x, y] = _lightMapCPU[x, y] + light;
    }

    public void SetLight(int x, int y, Half4 light)
    {
        _lightMapCPU[x, y] = light;
    }

    public void ClearOpacityMap()
    {
        _opacityMapCPU.Fill(Color32.White);
    }


    public void SetOpacity(int x, int y, Color32 opacity)
    {
        _opacityMapCPU[x, y] = opacity;
    }

    public void Compute(GPUCommandBuffer.ComputePass computePass)
    {
        ResetTexture();

        if (_isResultDirty)
        {
            _isResultDirty = false;
            RunFloodFill(computePass, 0, 0, Width, Height);
        }
    }

    /// <summary>
    /// Runs the flood-fill iterations over a sub-rectangle only, ignoring the result-dirty gate:
    /// region callers upload fresh input state for the region beforehand, so the propagation
    /// always runs. Texels outside the region are not dispatched and keep their last computed
    /// values; the region must therefore be expanded by at least <see cref="Iteration"/> texels
    /// beyond any sampled area, since border texels read one-step-old neighbors each iteration.
    /// </summary>
    /// <param name="computePass">The compute pass to record the dispatches to.</param>
    /// <param name="x">The x origin of the region, in texels.</param>
    /// <param name="y">The y origin of the region, in texels.</param>
    /// <param name="width">The width of the region, in texels.</param>
    /// <param name="height">The height of the region, in texels.</param>
    public void ComputeRegion(GPUCommandBuffer.ComputePass computePass, int x, int y, int width, int height)
    {
        _isResultDirty = false;
        RunFloodFill(computePass, x, y, width, height);
    }

    private void RunFloodFill(GPUCommandBuffer.ComputePass computePass, int x, int y, int width, int height)
    {
        FloodFillLightingConstant constant = new FloodFillLightingConstant
        {
            RectOrigin = new int2(x, y),
            RectSize = new int2(width, height),
            AttenuationCorner = AttenuationCorner * AttenuationMultiplier,
            AttenuationSide = AttenuationSide * AttenuationMultiplier,
        };

        _material.ReflectionInfo.Size.GetDispatchCount((uint)width, (uint)height, 1, out uint groupX, out uint groupY, out uint groupZ);
        for (int i = 0; i < Iteration; i++)
        {
            _material.SetRenderTexture(_shaderId_front, _lightMaps.Front);
            _material.SetRenderTexture(_shaderId_back, _lightMaps.Back);
            _material.DispatchByGroupWithConstant(computePass, groupX, groupY, groupZ, constant);
            _lightMaps.Swap();
        }
    }

    public void ResetTexture(bool force = false)
    {
        if (!_isTextureDirty && !force)
        {
            return;
        }

        _lightMaps.Reset();

        _lightMaps.Front.ColorTextures[0].SetPixels(_lightMapCPU);
        _opacityMap.ColorTextures[0].SetPixels(_opacityMapCPU);

        _isTextureDirty = false;
        _isResultDirty = true;
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
