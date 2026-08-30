using System.Numerics;
using static Alco.ImGUI.Test.GizmoTestSupport;

namespace Alco.ImGUI.Test;

/// <summary>
/// Bounds manipulation: camera-facing face selection, corner (two-axis) and
/// edge-midpoint (single-axis) resize drags around the fixed pivot, snapping,
/// and the mutual exclusion with the gizmo handle drags.
/// </summary>
[TestFixture]
public class TestGizmoBounds
{
    /// <summary>
    /// Camera looking straight at the min-X face of the unit box (0,0,0)-(1,1,1):
    /// the only visible face is X, so corner anchors resize Y/Z.
    /// </summary>
    private static (Matrix4x4 View, Matrix4x4 Projection, GizmoContext Ctx) CreateFaceXSetup()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(-10f, 0.5f, 0.5f), new Vector3(0f, 0.5f, 0.5f), Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        BoundingBox3D bounds = new BoundingBox3D(Vector3.Zero, Vector3.One);
        Frame(ctx, new Vector2(5f, 5f), false, view, projection, ref bounds);
        return (view, projection, ctx);
    }

    [Test]
    public void CornerDrag_ResizesTwoAxes_AroundOppositeCorner()
    {
        (Matrix4x4 view, Matrix4x4 projection, GizmoContext ctx) = CreateFaceXSetup();
        BoundingBox3D bounds = new BoundingBox3D(Vector3.Zero, Vector3.One);

        // Corner 0 of the min-X face is the (0, 0, 0) corner; opposite pivot is (0, 1, 1).
        Vector2 grab = ToScreen(Vector3.Zero, view, projection);
        Frame(ctx, grab, false, view, projection, ref bounds);
        Assert.That(Frame(ctx, grab, true, view, projection, ref bounds), Is.False, "activation frame must not modify");
        Assert.That(ctx.UsingBounds, Is.True);
        Assert.That(ctx.BoundsPivot, Is.EqualTo(new Vector3(0f, 1f, 1f)));

        // Drag the grabbed corner to (0, 3, 3): Y/Z sizes double around the fixed pivot.
        bool manipulated = Frame(ctx, ToScreen(new Vector3(0f, 3f, 3f), view, projection), true, view, projection, ref bounds);
        Assert.That(manipulated, Is.True);
        AreEqual(new Vector3(0f, -1f, -1f), bounds.Min, 0.05f);
        AreEqual(new Vector3(1f, 1f, 1f), bounds.Max, 0.05f);
    }

    [Test]
    public void CornerDrag_PastPivot_ShrinksBox()
    {
        (Matrix4x4 view, Matrix4x4 projection, GizmoContext ctx) = CreateFaceXSetup();
        BoundingBox3D bounds = new BoundingBox3D(Vector3.Zero, Vector3.One);

        Vector2 grab = ToScreen(Vector3.Zero, view, projection);
        Frame(ctx, grab, true, view, projection, ref bounds);
        Frame(ctx, ToScreen(new Vector3(0f, 1.5f, 1.5f), view, projection), true, view, projection, ref bounds);
        AreEqual(new Vector3(0f, 0.5f, 0.5f), bounds.Min, 0.05f);
        AreEqual(new Vector3(1f, 1f, 1f), bounds.Max, 0.05f);
    }

    [Test]
    public void EdgeMidpointDrag_ResizesSingleAxis()
    {
        (Matrix4x4 view, Matrix4x4 projection, GizmoContext ctx) = CreateFaceXSetup();
        BoundingBox3D bounds = new BoundingBox3D(Vector3.Zero, Vector3.One);

        // Edge 0 midpoint of the min-X face: (0, 0, 0.5); dragging resizes Y only.
        Vector2 grab = ToScreen(new Vector3(0f, 0f, 0.5f), view, projection);
        Frame(ctx, grab, true, view, projection, ref bounds);
        Assert.That(ctx.UsingBounds, Is.True);
        Assert.That(ctx.BoundsAxis0, Is.EqualTo(1));
        Assert.That(ctx.BoundsAxis1, Is.EqualTo(-1));
        Assert.That(ctx.BoundsPivot, Is.EqualTo(new Vector3(0f, 1f, 0.5f)));

        bool manipulated = Frame(ctx, ToScreen(new Vector3(0f, 3f, 0.5f), view, projection), true, view, projection, ref bounds);
        Assert.That(manipulated, Is.True);
        AreEqual(new Vector3(0f, -1f, 0f), bounds.Min, 0.05f);
        AreEqual(new Vector3(1f, 1f, 1f), bounds.Max, 0.05f);
    }

    [Test]
    public void Snap_AppliesToResultingSize()
    {
        (Matrix4x4 view, Matrix4x4 projection, GizmoContext ctx) = CreateFaceXSetup();
        BoundingBox3D bounds = new BoundingBox3D(Vector3.Zero, Vector3.One);

        Vector2 grab = ToScreen(Vector3.Zero, view, projection);
        Frame(ctx, grab, true, view, projection, ref bounds);
        // Raw ratios 1.6 and 2.2 snap to a 2x size on both axes.
        Frame(ctx, ToScreen(new Vector3(0f, 2.6f, 3.2f), view, projection), true, view, projection, ref bounds, new Vector3(1f));
        AreEqual(new Vector3(0f, -1f, -1f), bounds.Min, 0.05f);
        AreEqual(new Vector3(1f, 1f, 1f), bounds.Max, 0.05f);
    }

    [Test]
    public void MissClick_DoesNotActivate()
    {
        (Matrix4x4 view, Matrix4x4 projection, GizmoContext ctx) = CreateFaceXSetup();
        BoundingBox3D bounds = new BoundingBox3D(Vector3.Zero, Vector3.One);

        Vector2 grab = ToScreen(Vector3.Zero, view, projection);
        Frame(ctx, grab + new Vector2(25f, 25f), true, view, projection, ref bounds);
        Assert.That(ctx.UsingBounds, Is.False);
    }

    [Test]
    public void DragStateMachine_SolvesWhileHeld_StopsOnRelease()
    {
        (Matrix4x4 view, Matrix4x4 projection, GizmoContext ctx) = CreateFaceXSetup();
        BoundingBox3D bounds = new BoundingBox3D(Vector3.Zero, Vector3.One);

        Vector2 grab = ToScreen(Vector3.Zero, view, projection);
        Frame(ctx, grab, true, view, projection, ref bounds);
        Assert.That(ctx.UsingBounds, Is.True);
        // While dragging, only the captured face stays drawn.
        Assert.That(ctx.BoundsCallFaceCount, Is.EqualTo(1));
        Assert.That(ctx.BoundsCallFaceAxis[0], Is.EqualTo(0));

        Frame(ctx, ToScreen(new Vector3(0f, 3f, 3f), view, projection), true, view, projection, ref bounds);
        bool solved = Frame(ctx, ToScreen(new Vector3(0f, 3f, 3f), view, projection), false, view, projection, ref bounds);
        Assert.That(solved, Is.False, "release frame with unchanged mouse must not modify");
        Assert.That(ctx.UsingBounds, Is.False);
        // The resized box persists after release.
        AreEqual(new Vector3(0f, -1f, -1f), bounds.Min, 0.05f);
    }

    [Test]
    public void Box2D_CornerDrag_ResizesXYOnly()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        BoundingBox2D bounds = new BoundingBox2D(new Vector2(-1f), new Vector2(1f));

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, ref bounds);
        Vector2 grab = ToScreen(new Vector3(-1f, -1f, 0f), view, projection);
        Frame(ctx, grab, true, view, projection, ref bounds);
        Assert.That(ctx.UsingBounds, Is.True);
        Assert.That(ctx.BoundsInfoComponentCount, Is.EqualTo(2));

        bool manipulated = Frame(ctx, ToScreen(new Vector3(-3f, -3f, 0f), view, projection), true, view, projection, ref bounds);
        Assert.That(manipulated, Is.True);
        AreEqual(new Vector2(-3f, -3f), bounds.Min, 0.05f);
        AreEqual(new Vector2(1f, 1f), bounds.Max, 0.05f);
    }

    [Test]
    public void Box2D_EdgeMidpointDrag_ResizesSingleAxis()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        BoundingBox2D bounds = new BoundingBox2D(new Vector2(-1f), new Vector2(1f));

        // Edge 0 midpoint of the Z face: (-1, 0, 0); dragging resizes X only.
        Vector2 grab = ToScreen(new Vector3(-1f, 0f, 0f), view, projection);
        Frame(ctx, grab, true, view, projection, ref bounds);
        Assert.That(ctx.UsingBounds, Is.True);
        Assert.That(ctx.BoundsAxis0, Is.EqualTo(0));
        Assert.That(ctx.BoundsAxis1, Is.EqualTo(-1));

        bool manipulated = Frame(ctx, ToScreen(new Vector3(-3f, 0f, 0f), view, projection), true, view, projection, ref bounds);
        Assert.That(manipulated, Is.True);
        AreEqual(new Vector2(-3f, -1f), bounds.Min, 0.05f);
        AreEqual(new Vector2(1f, 1f), bounds.Max, 0.05f);
    }

    [Test]
    public void GizmoDragActive_BoundsNotInteractive()
    {
        (Matrix4x4 view, Matrix4x4 projection, GizmoContext ctx) = CreateFaceXSetup();
        BoundingBox3D bounds = new BoundingBox3D(Vector3.Zero, Vector3.One);

        ctx.Using = true;
        Vector2 grab = ToScreen(Vector3.Zero, view, projection);
        bool manipulated = Frame(ctx, grab, true, view, projection, ref bounds);
        Assert.That(manipulated, Is.False);
        Assert.That(ctx.UsingBounds, Is.False);
        Assert.That(ctx.CallBoundsValid, Is.False, "bounds display is suppressed while a gizmo drag is active");
        ctx.Using = false;
    }

    [Test]
    public void GizmoHandleHovered_AnchorActivationSuppressed()
    {
        (Matrix4x4 view, Matrix4x4 projection, GizmoContext ctx) = CreateFaceXSetup();
        BoundingBox3D bounds = new BoundingBox3D(Vector3.Zero, Vector3.One);

        Vector2 grab = ToScreen(Vector3.Zero, view, projection);
        ctx.BeginFrame(Viewport, new GizmoInput(grab, true, Vector2.Zero));
        ctx.FrameHoverType = GizmoMoveType.MoveX;
        bool manipulated = GizmoCore.ManipulateBounds(ctx, view, projection, ref bounds, null, 3);
        Assert.That(manipulated, Is.False);
        Assert.That(ctx.UsingBounds, Is.False);
    }

    [Test]
    public void BoundsDragActive_GizmoHandlesSuppressed()
    {
        (Matrix4x4 view, Matrix4x4 projection, GizmoContext ctx) = CreateFaceXSetup();

        ctx.UsingBounds = true;
        ctx.BeginFrame(Viewport, new GizmoInput(new Vector2(5f, 5f), true, Vector2.Zero));
        Matrix4x4 model = Matrix4x4.Identity;
        bool manipulated = GizmoCore.Manipulate(ctx, view, projection, GizmoOperation.Translate, GizmoMode.World, ref model, out _, null);
        Assert.That(manipulated, Is.False);
        Assert.That(ctx.CallValid, Is.False, "gizmo handles are not drawn while a bounds drag is active");
        ctx.UsingBounds = false;
    }

    [Test]
    public void CenterBehindCamera_NoDisplayNoInteraction()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(-10f, 0.5f, 0.5f), new Vector3(0f, 0.5f, 0.5f), Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        BoundingBox3D bounds = new BoundingBox3D(new Vector3(-30f, 0f, 0f), new Vector3(-29f, 1f, 1f));

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, ref bounds);
        bool manipulated = Frame(ctx, new Vector2(400f, 300f), true, view, projection, ref bounds);
        Assert.That(manipulated, Is.False);
        Assert.That(ctx.CallBoundsValid, Is.False);
        Assert.That(ctx.UsingBounds, Is.False);
    }
}
