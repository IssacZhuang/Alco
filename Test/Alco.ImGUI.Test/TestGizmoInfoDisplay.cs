using System.Numerics;

namespace Alco.ImGUI.Test;

/// <summary>
/// Verifies the gizmo drag info display: translation delta shown in the caller's
/// authoring unit (InfoUnitScale) and rotation angle shown in the engine's euler
/// sign convention (EngineRotationSign).
/// </summary>
public class TestGizmoInfoDisplay
{
    [Test]
    public void InfoUnitScale_DefaultsToOne()
    {
        GizmoContext ctx = GizmoTestSupport.CreateContext();
        Assert.That(ctx.InfoUnitScale, Is.EqualTo(1f));
    }

    [Test]
    public void InfoUnitScale_ResetsOnBeginFrame()
    {
        GizmoContext ctx = GizmoTestSupport.CreateContext();
        ctx.InfoUnitScale = 2f;
        ctx.BeginFrame(GizmoTestSupport.Viewport, new GizmoInput(Vector2.Zero, false, Vector2.Zero));
        Assert.That(ctx.InfoUnitScale, Is.EqualTo(1f));
    }

    [Test]
    public void InfoUnitScale_FacadeRoundTrip()
    {
        Gizmo.InfoUnitScale = 2f;
        Assert.That(Gizmo.InfoUnitScale, Is.EqualTo(2f));
        Gizmo.InfoUnitScale = 1f;
    }

    [Test]
    public void EngineRotationSign_MatchesEulerConvention()
    {
        Assert.That(GizmoDraw.EngineRotationSign(GizmoMoveType.RotateX), Is.EqualTo(-1f));
        Assert.That(GizmoDraw.EngineRotationSign(GizmoMoveType.RotateY), Is.EqualTo(-1f));
        Assert.That(GizmoDraw.EngineRotationSign(GizmoMoveType.RotateZ), Is.EqualTo(1f));
        Assert.That(GizmoDraw.EngineRotationSign(GizmoMoveType.RotateScreen), Is.EqualTo(1f));
    }
}
