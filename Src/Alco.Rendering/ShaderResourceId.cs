using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

public static class ShaderResourceId
{
    public const string Camera = "camera";
    public const string Texture = "texture";
    public const string Mask = "mask";
    public const string LightMap = "lightMap";
    public const string OpacityMap = "opacityMap";
    public const string Data = "data";
    public const string Font = "font";
    public const string TextBuffer = "textBuffer";

    public const string FrontBuffer = "frontBuffer";
    public const string BackBuffer = "backBuffer";

    public const string GlobalRenderData = "globalRenderData";
    public const string SpriteData = "spriteData";
    public const string TileData = "tileData";
    public const string ColorData = "colorData";
    public const string HeightData = "heightData";

    public const string PositionData = "positionData";
    public const string TileIdData = "tileIdData";
    public const string TileSetData = "tileSetData";

    public const string TileMap = "tileMap";

    public const string Input = "input";
    public const string Output = "output";

    public const string Particles = "particles";
    public const string Instances = "instances";

    public const string GaussianKernel = "gaussianKernel";

    public const string PointLights = "pointLights";

    public const string TrailPoints = "trailPoints";
    public const string TrailParams = "trailParams";
    public const string TrailGlobals = "trailGlobals";
}