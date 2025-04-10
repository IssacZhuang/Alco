using System.Numerics;

namespace Alco.Rendering;

public interface ICameraData
{
    Matrix4x4 ViewMatrix { get; }
    Matrix4x4 ProjectionMatrix { get; }
    Matrix4x4 ViewProjectionMatrix { get; }
}