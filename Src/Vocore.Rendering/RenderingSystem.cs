namespace Vocore.Rendering;

using System.Runtime.CompilerServices;
using Vocore.Graphics;

/// <summary>
/// The facility to manage rendering resource and perform rendering.
/// <br/>It is a high-level encapsulation of <see cref="GPUDevice"/>.
/// </summary>
public partial class RenderingSystem
{
    private GPUDevice _device;

    public RenderingSystem(GPUDevice device)
    {
        _device = device;
    }
}