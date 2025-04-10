using System.Numerics;
using Alco.Rendering;

namespace Alco.GUI;

public interface IUIMask
{
    Texture2D? MaskTexture { get; }
    Matrix4x4 MaskTransform { get; }
    Rect MaskTextureUvRect { get; }
}

