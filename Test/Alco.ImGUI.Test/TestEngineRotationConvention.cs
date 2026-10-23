using System.Numerics;

namespace Alco.ImGUI.Test;

/// <summary>
/// Pins the engine euler convention that the gizmo rotation display relies on.
/// </summary>
public class TestEngineRotationConvention
{
    /// <summary>Tests that a positive right-handed X rotation maps to a negative engine roll.</summary>
    [Test]
    public void Convention_AxisX_RhPositive_IsEngineNegative()
    {
        Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 30f * MathF.PI / 180f);
        Vector3 euler = math.euler(q);
        Assert.That(euler.X, Is.EqualTo(-30f).Within(0.01f), "roll");
        Assert.That(euler.Y, Is.EqualTo(0f).Within(0.01f), "pitch");
        Assert.That(euler.Z, Is.EqualTo(0f).Within(0.01f), "yaw");
    }

    /// <summary>Tests that a positive right-handed Y rotation maps to a negative engine pitch.</summary>
    [Test]
    public void Convention_AxisY_RhPositive_IsEngineNegative()
    {
        Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 30f * MathF.PI / 180f);
        Vector3 euler = math.euler(q);
        Assert.That(euler.X, Is.EqualTo(0f).Within(0.01f), "roll");
        Assert.That(euler.Y, Is.EqualTo(-30f).Within(0.01f), "pitch");
        Assert.That(euler.Z, Is.EqualTo(0f).Within(0.01f), "yaw");
    }

    /// <summary>Tests that a positive right-handed Z rotation maps to a positive engine yaw.</summary>
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
