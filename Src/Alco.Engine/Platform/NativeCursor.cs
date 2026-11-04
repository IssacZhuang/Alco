using System;

namespace Alco.Engine;

/// <summary>
/// An OS-level cursor image owned by the platform. Dispose it after it is no longer the
/// active cursor. Creating and disposing native cursors must happen on the main thread.
/// </summary>
public abstract class NativeCursor : IDisposable
{
    /// <summary>
    /// Releases the native cursor resource.
    /// </summary>
    public abstract void Dispose();
}
