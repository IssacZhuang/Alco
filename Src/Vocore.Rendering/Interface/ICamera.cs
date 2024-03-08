using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vocore.Rendering;

public interface ICamera
{
    public Matrix4x4 ViewMatrix { get; }
    public Matrix4x4 ProjectionMatrix { get; }
    public Matrix4x4 ViewProjectionMatrix { get; }
}


