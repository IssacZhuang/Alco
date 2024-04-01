using System;
using System.Collections.Generic;
using System.Numerics;
using Vocore.Graphics;

namespace Vocore.Rendering;

public interface ICamera
{
    /// <summary>
    /// The view projection matrix of the camera.
    /// </summary>
    /// <value> The GPU resource group containing the GPU buffer of the view projection matrix. </value>
    public GPUResourceGroup ViewProjectionBuffer { get; }
}


