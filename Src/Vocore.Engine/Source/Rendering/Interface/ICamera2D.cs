using System.Numerics;

namespace Vocore.Engine;

public interface ICamera2D
{
    public Matrix4x4 ViewMatrix { get; }
    public Matrix4x4 ProjectionMatrix { get; }
    public Matrix4x4 ViewProjectionMatrix { get; }
}
