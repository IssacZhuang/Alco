namespace Vocore.Engine;

public interface ICamera2D
{
    public Matrix3x3 ViewMatrix { get; }
    public Matrix3x3 ProjectionMatrix { get; }
    public Matrix3x3 ViewProjectionMatrix { get; }
}
