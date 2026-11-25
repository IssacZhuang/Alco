using System.Runtime.CompilerServices;

namespace Alco.GUI.Test;

/// <summary>
/// Regression tests for <see cref="Canvas"/> disposal and finalization safety.
/// </summary>
[TestFixture]
public class CanvasDisposeTests
{
    /// <summary>
    /// Verifies that finalizing an uninitialized <see cref="Canvas"/> instance does not throw.
    /// </summary>
    [Test]
    public void Finalize_UninitializedCanvas_DoesNotThrow()
    {
        WeakReference<Canvas> weakRef = CreateUninitializedCanvas();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(weakRef.TryGetTarget(out _), Is.False);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Canvas> CreateUninitializedCanvas()
    {
        Canvas canvas = (Canvas)RuntimeHelpers.GetUninitializedObject(typeof(Canvas));
        return new WeakReference<Canvas>(canvas);
    }
}
