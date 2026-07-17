using System.Numerics;
using static Alco.ImGUI.Test.GizmoTestSupport;

namespace Alco.ImGUI.Test;

/// <summary>
/// GC pressure regression: the per-frame gizmo path must not allocate managed memory.
/// Pumps hover/activate/drag frames through the headless core and asserts zero
/// allocated bytes after warm-up (first calls may allocate static/JIT state).
/// </summary>
[TestFixture]
public class TestGizmoAllocations
{
    [Test]
    public void TranslateDrag_CoreFrames_AllocateNothing()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;
        const GizmoOperation op = GizmoOperation.Translate;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.World, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0.55f * sf, 0f, 0f), view, projection);

        // Warm-up: activate and drag once so any one-time allocations happen before measuring.
        Frame(ctx, grab, false, view, projection, op, GizmoMode.World, ref model);
        Frame(ctx, grab, true, view, projection, op, GizmoMode.World, ref model);
        Frame(ctx, grab + new Vector2(40f, 0f), true, view, projection, op, GizmoMode.World, ref model);

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 16; i++)
        {
            Frame(ctx, grab + new Vector2(40f + i, 0f), true, view, projection, op, GizmoMode.World, ref model);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.That(allocated, Is.EqualTo(0), "per-frame core path must not allocate");
    }

    [Test]
    public void RotateDrag_CoreFrames_AllocateNothing()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;
        const GizmoOperation op = GizmoOperation.Rotate;

        // Find a point on the Z rotation ring (screen-projected circle around the origin).
        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.World, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0f, 0f, 0f), view, projection);
        // Ring radius in world units ~ ScreenFactor * 1.2; grab along +Y of the ring in world space.
        grab = ToScreen(new Vector3(0f, 1.2f * sf, 0f), view, projection);

        Frame(ctx, grab, false, view, projection, op, GizmoMode.World, ref model);
        Frame(ctx, grab, true, view, projection, op, GizmoMode.World, ref model);
        Frame(ctx, grab + new Vector2(20f, 10f), true, view, projection, op, GizmoMode.World, ref model);

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 16; i++)
        {
            Frame(ctx, grab + new Vector2(20f + i, 10f), true, view, projection, op, GizmoMode.World, ref model);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.That(allocated, Is.EqualTo(0), "per-frame core path must not allocate");
    }
}
