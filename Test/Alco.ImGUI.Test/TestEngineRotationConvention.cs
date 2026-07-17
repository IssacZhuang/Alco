using System.Numerics;

namespace Alco.ImGUI.Test;

/// <summary>
/// Pins the engine euler convention the gizmo's rotation info display relies on:
/// <see cref="math.euler"/> decomposes to Roll(X)/Pitch(Y)/Yaw(Z) where engine-positive
/// is RH-negative about X and Y but RH-positive about Z (UE-style left-handed).
/// The gizmo display negates the raw plane angle for X/Y so the text matches the
/// inspector's euler readout.
/// </summary>
public class TestEngineRotationConvention
{
    [Test]
    public void Convention_AxisX_RhPositive_IsEngineNegative()
    {
        Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 30f * MathF.PI / 180f);
        Vector3 euler = math.euler(q);
        Assert.That(euler.X, Is.EqualTo(-30f).Within(0.01f), "roll");
        Assert.That(euler.Y, Is.EqualTo(0f).Within(0.01f), "pitch");
        Assert.That(euler.Z, Is.EqualTo(0f).Within(0.01f), "yaw");
    }

    [Test]
    public void Convention_AxisY_RhPositive_IsEngineNegative()
    {
        Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 30f * MathF.PI / 180f);
        Vector3 euler = math.euler(q);
        Assert.That(euler.X, Is.EqualTo(0f).Within(0.01f), "roll");
        Assert.That(euler.Y, Is.EqualTo(-30f).Within(0.01f), "pitch");
        Assert.That(euler.Z, Is.EqualTo(0f).Within(0.01f), "yaw");
    }

    [Test]
    public void Convention_AxisZ_RhPositive_IsEnginePositive()
    {
        Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 30f * MathF.PI / 180f);
        Vector3 euler = math.euler(q);
        Assert.That(euler.X, Is.EqualTo(0f).Within(0.01f), "roll");
        Assert.That(euler.Y, Is.EqualTo(0f).Within(0.01f), "pitch");
        Assert.That(euler.Z, Is.EqualTo(30f).Within(0.01f), "yaw");
    }
}
