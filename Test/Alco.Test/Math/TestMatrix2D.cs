using NUnit.Framework;
using System.Numerics;
using Alco;

using static Alco.math;

namespace Alco.Test;

[TestFixture]
public class TestMatrix2D
{
    [Test]
    public void TestMatrix4trs()
    {
        Vector2 position = new Vector2(1, 2);
        Rotation2D rotation = new Rotation2D(0.5f, 0.8660254f); // 60 degrees
        Vector2 scale = new Vector2(3, 4);

        Matrix4x4 resultOptimized = matrix4trs(position, rotation, scale);
        Matrix4x4 resultOriginal = matrix4scale(scale) * matrix4rotation(rotation) * matrix4translation(position);

        Assert.That(resultOptimized, Is.EqualTo(resultOriginal).Using<Matrix4x4>((m1, m2) => m1.Equals(m2) ? 0 : 1));
    }

    [Test]
    public void TestMatrix4tr()
    {
        Vector2 position = new Vector2(1, 2);
        Rotation2D rotation = new Rotation2D(0.5f, 0.8660254f); // 60 degrees

        Matrix4x4 resultOptimized = matrix4tr(position, rotation);
        Matrix4x4 resultOriginal = matrix4rotation(rotation) * matrix4translation(position);

        Assert.That(resultOptimized, Is.EqualTo(resultOriginal).Using<Matrix4x4>((m1, m2) => m1.Equals(m2) ? 0 : 1));
    }

    [Test]
    public void TestMatrix4ts()
    {
        Vector2 position = new Vector2(1, 2);
        Vector2 scale = new Vector2(3, 4);

        Matrix4x4 resultOptimized = matrix4ts(position, scale);
        Matrix4x4 resultOriginal = matrix4scale(scale) * matrix4translation(position);

        Assert.That(resultOptimized, Is.EqualTo(resultOriginal).Using<Matrix4x4>((m1, m2) => m1.Equals(m2) ? 0 : 1));
    }

    [Test]
    public void TestMatrix4rs()
    {
        Rotation2D rotation = new Rotation2D(0.5f, 0.8660254f); // 60 degrees
        Vector2 scale = new Vector2(3, 4);

        Matrix4x4 resultOptimized = matrix4rs(rotation, scale);
        Matrix4x4 resultOriginal = matrix4scale(scale) * matrix4rotation(rotation);

        Assert.That(resultOptimized, Is.EqualTo(resultOriginal).Using<Matrix4x4>((m1, m2) => m1.Equals(m2) ? 0 : 1));
    }
}