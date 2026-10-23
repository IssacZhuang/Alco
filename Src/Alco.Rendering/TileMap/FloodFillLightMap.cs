using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A tile light map that propagates lighting and opacity across a grid with iterative flood fill.
/// </summary>
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


    /// <summary>The number of flood-fill iterations performed per compute pass.</summary>
    public int Iteration { get; set; } = 32;
    /// <summary>The light attenuation applied per step to side-neighbor propagation.</summary>
    public float AttenuationSide { get; set; } = 0.1f;
    /// <summary>The light attenuation applied per step to diagonal propagation.</summary>
    public float AttenuationCorner { get; set; } = 0.14141414f;

    /// <summary>The multiplier applied to both side and corner attenuation.</summary>
    public float AttenuationMultiplier { get; set; } = 1f;

    /// <summary>The front light map texture holding the current propagation result.</summary>
    public RenderTexture Texture => _lightMaps.Front;

    /// <summary>The texture storing per-tile opacity obstacles.</summary>
    public RenderTexture OpacityMap => _opacityMap;

    /// <summary>The map width in texels.</summary>
    public int Width => _lightMapCPU.Width;
    /// <summary>The map height in texels.</summary>
    public int Height => _lightMapCPU.Height;
    /// <summary>The name of the light map, used for diagnostics.</summary>
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

    /// <summary>Clears the entire CPU light map to the given color.</summary>
    /// <param name="color">The color to fill with.</param>
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
    /// registrations changed without a known region; opacity is event-driven and always
    /// uploaded in full by this path.
    /// </summary>
    public void UploadOpacityMap()
    {
        _opacityMap.ColorTextures[0].SetPixels(_opacityMapCPU);
        _isResultDirty = true;
    }

    /// <summary>
    /// Clears a sub-rectangle of the CPU opacity map to fully transparent, row-wise. Combined with
    /// <see cref="UploadOpacityMapRegion"/> this rebuilds only the changed obstacles' texels.
    /// </summary>
    /// <param name="x">The x origin of the region, in texels.</param>
    /// <param name="y">The y origin of the region, in texels.</param>
    /// <param name="width">The width of the region, in texels.</param>
    /// <param name="height">The height of the region, in texels.</param>
    public void ClearOpacityRegion(int x, int y, int width, int height)
    {
        _opacityMapCPU.Fill(x, y, width, height, Color32.White);
    }

    /// <summary>
    /// Uploads a sub-rectangle of the CPU opacity map into the opacity texture and marks the
    /// computed result dirty. Texels outside the region keep their last values — callers must
    /// have rebuilt the region's CPU opacity (clear + obstacle stamps) beforehand.
    /// </summary>
    /// <param name="x">The x origin of the region, in texels.</param>
    /// <param name="y">The y origin of the region, in texels.</param>
    /// <param name="width">The width of the region, in texels.</param>
    /// <param name="height">The height of the region, in texels.</param>
    public void UploadOpacityMapRegion(int x, int y, int width, int height)
    {
        _opacityMap.ColorTextures[0].SetPixels(_opacityMapCPU, x, y, width, height);
        _isResultDirty = true;
    }

    /// <summary>Marks the GPU textures as needing a full re-upload on the next reset.</summary>
    public void SetDirty()
    {
        _isTextureDirty = true;
    }

    /// <summary>Resets the CPU opacity map to fully opaque white.</summary>
    public void ResetOpacity()
    {
        _opacityMapCPU.Fill(Color32.White);
    }

    /// <summary>Adds light to the texel at the given coordinates.</summary>
    /// <param name="x">The x coordinate of the texel.</param>
    /// <param name="y">The y coordinate of the texel.</param>
    /// <param name="light">The light value to add.</param>
    public void AddLight(int x, int y, Half4 light)
    {
        _lightMapCPU[x, y] = _lightMapCPU[x, y] + light;
    }

    /// <summary>Sets the light value of the texel at the given coordinates, replacing any existing value.</summary>
    /// <param name="x">The x coordinate of the texel.</param>
    /// <param name="y">The y coordinate of the texel.</param>
    /// <param name="light">The light value to set.</param>
    public void SetLight(int x, int y, Half4 light)
    {
        _lightMapCPU[x, y] = light;
    }

    /// <summary>Clears the CPU opacity map to fully opaque white.</summary>
    public void ClearOpacityMap()
    {
        _opacityMapCPU.Fill(Color32.White);
    }


    /// <summary>Sets the opacity of the texel at the given coordinates.</summary>
    /// <param name="x">The x coordinate of the texel.</param>
    /// <param name="y">The y coordinate of the texel.</param>
    /// <param name="opacity">The opacity value to set.</param>
    public void SetOpacity(int x, int y, Color32 opacity)
    {
        _opacityMapCPU[x, y] = opacity;
    }

    /// <summary>Runs the full-map flood-fill computation when the result is dirty.</summary>
    /// <param name="computePass">The compute pass to record the dispatches to.</param>
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
    /// Runs the flood-fill iterations over a sub-rectangle, ignoring the result-dirty gate;
    /// texels outside the region keep their values, so the region must extend at least
    /// <see cref="Iteration"/> texels beyond any sampled area.
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

    /// <summary>Uploads dirty CPU maps to the GPU textures and marks the computed result dirty.</summary>
    /// <param name="force">Whether to upload even when the dirty flag is not set.</param>
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
