using System.Numerics;
using Vocore.Graphics;

namespace Vocore.Engine;

public interface ICamera2D
{
    public Matrix3x2 ViewMatrix { get; }
    public Matrix3x2 ProjectionMatrix { get; }
    public Matrix3x2 ViewProjectionMatrix { get; }
}
