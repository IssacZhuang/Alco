using System.Numerics;
using Alco.Rendering;

namespace Alco.GUI;

public interface IUIMask
{
    Texture2D? MaskTexture { get; }
    Transform2D MaskTransform { get; }
    Rect MaskTextureUvRect { get; }
}

