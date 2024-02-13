using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine;

/// <summary>
/// The high level encapsulation of the graphics functionality.
/// </summary>
public static partial class RenderingService
{
#pragma warning disable CS8618
    public static GPUDevice GraphicsDevice
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
        internal set;
    }
#pragma warning restore CS8618


}