using System.Numerics;
using static Alco.ImGUI.Test.GizmoTestSupport;

namespace Alco.ImGUI.Test;

/// <summary>
/// U11: the IsUsing state machine — hover, activation, drag and release — driven
/// by injected mouse sequences, and the single-active-handle rule.
/// </summary>
[TestFixture]
public class TestGizmoStateMachine
{
    [Test]
    public void U11_Hover_Press_Drag_Release()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;
        const GizmoOperation op = GizmoOperation.Translate;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.World, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 onAxis = ToScreen(new Vector3(0.55f * sf, 0f, 0f), view, projection);

        // Hover: not using, hover reported.
        Frame(ctx, onAxis, false, view, projection, op, GizmoMode.World, ref model);
        Assert.That(ctx.Using, Is.False);
        Assert.That(ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.MoveX));

        // Press: activates the drag, does not modify yet.
        Assert.That(Frame(ctx, onAxis, true, view, projection, op, GizmoMode.World, ref model), Is.False);
        Assert.That(ctx.Using, Is.True);
        Assert.That(ctx.CurrentOperation, Is.EqualTo(GizmoMoveType.MoveX));

        // Drag: modifies and keeps the active state across frames.
        Assert.That(Frame(ctx, onAxis + new Vector2(30f, 0f), true, view, projection, op, GizmoMode.World, ref model), Is.True);
        Assert.That(ctx.Using, Is.True);
        float afterFirstDrag = model.M41;
        Assert.That(afterFirstDrag, Is.GreaterThan(0f));

        Assert.That(Frame(ctx, onAxis + new Vector2(60f, 0f), true, view, projection, op, GizmoMode.World, ref model), Is.True);
        Assert.That(ctx.Using, Is.True);
        Assert.That(model.M41, Is.GreaterThan(afterFirstDrag));

        // Release: state resets on the frame the button goes up.
        Frame(ctx, onAxis + new Vector2(60f, 0f), false, view, projection, op, GizmoMode.World, ref model);
        Assert.That(ctx.Using, Is.False);

        // After release, hover works again without a drag (object moved, so aim at its new axis handle).
        Vector2 onAxisMoved = ToScreen(new Vector3(model.M41 + 0.55f * sf, 0f, 0f), view, projection);
        Assert.That(Frame(ctx, onAxisMoved, false, view, projection, op, GizmoMode.World, ref model), Is.False);
        Assert.That(ctx.Using, Is.False);
        Assert.That(ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.MoveX));
    }

    [Test]
    public void U11_PressWithoutHover_DoesNotActivate()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, GizmoOperation.Translate, GizmoMode.World, ref model);
        bool manipulated = Frame(ctx, new Vector2(5f, 5f), true, view, projection, GizmoOperation.Translate, GizmoMode.World, ref model);

        Assert.That(manipulated, Is.False);
        Assert.That(ctx.Using, Is.False);
    }

    [Test]
    public void U11_SingleActiveHandle_DragIgnoresOtherHandles()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;
        const GizmoOperation op = GizmoOperation.Translate;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.World, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 onX = ToScreen(new Vector3(0.55f * sf, 0f, 0f), view, projection);
        Vector2 onY = ToScreen(new Vector3(0f, 0.55f * sf, 0f), view, projection);

        // Press on X, then drag onto the Y axis handle.
        Frame(ctx, onX, true, view, projection, op, GizmoMode.World, ref model);
        Assert.That(ctx.Using, Is.True);
        Assert.That(ctx.CurrentOperation, Is.EqualTo(GizmoMoveType.MoveX));

        Frame(ctx, onY, true, view, projection, op, GizmoMode.World, ref model);
        // Still the X drag: no re-activation, hover does not switch.
        Assert.That(ctx.CurrentOperation, Is.EqualTo(GizmoMoveType.MoveX));
        Assert.That(ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.MoveX));
    }

    [Test]
    public void U11_IsOver_ReflectsHoverAndUsing()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;

        bool IsOver() => ctx.FrameHoverType != GizmoMoveType.None || ctx.Using;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, GizmoOperation.Translate, GizmoMode.World, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 onAxis = ToScreen(new Vector3(0.55f * sf, 0f, 0f), view, projection);
        Assert.That(IsOver(), Is.False);

        Frame(ctx, onAxis, false, view, projection, GizmoOperation.Translate, GizmoMode.World, ref model);
        Assert.That(IsOver(), Is.True);

        Frame(ctx, onAxis, true, view, projection, GizmoOperation.Translate, GizmoMode.World, ref model);
        Assert.That(IsOver(), Is.True);

        Frame(ctx, onAxis, false, view, projection, GizmoOperation.Translate, GizmoMode.World, ref model);
        Assert.That(IsOver(), Is.True, "hover remains after release while still over the handle");
    }
}
