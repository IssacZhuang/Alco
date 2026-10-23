using System.Numerics;
using Alco.Rendering;

namespace Alco.GUI;

/// <summary>
/// Provides the texture, transform, and UV rectangle used to mask a UI node's subtree.
/// </summary>
public interface IUIMask
{
    /// <summary>
    /// Gets the texture sampled by the mask.
    /// </summary>
    Texture2D? MaskTexture { get; }
    /// <summary>
    /// Gets the transform applied to the mask geometry.
    /// </summary>
    Transform2D MaskTransform { get; }
    /// <summary>
    /// Gets the UV rectangle sampled from <see cref="MaskTexture"/>.
    /// </summary>
    Rect MaskTextureUvRect { get; }
}

