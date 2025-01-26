using System.Numerics;

namespace Alco.Rendering;

public interface ICameraData
{
    Matrix4x4 ViewProjectionMatrix { get; }
}