using System.Numerics;

namespace Vocore.Rendering;

public interface ICameraData
{
    Matrix4x4 ViewProjectionMatrix { get; }
}