using NUnit.Framework;
using System.Numerics;
using Alco;

using static Alco.math;

namespace Alco.Test;

/// <summary>Tests composed 3D transformation matrix helpers.</summary>
[TestFixture]
public class TestMatrix3D
{
    /// <summary>Tests that matrix4trs matches separate scale, rotation and translation composition.</summary>
    [Test]
    public void TestMatrix4trs()
    {
        Vector3 position = new Vector3(1, 2, 0);
        Quaternion rotation = quaternion(23, 45, 56);
        Vector3 scale = new Vector3(3, 4, 1);

        Matrix4x4 resultOptimized = matrix4trs(position, rotation, scale);
        Matrix4x4 resultOriginal = matrix4scale(scale) * matrix4rotation(rotation) * matrix4translation(position);

        Assert.That(resultOptimized, Is.EqualTo(resultOriginal).Using<Matrix4x4>((m1, m2) => m1.Equals(m2) ? 0 : 1));
    }

    /// <summary>Tests that matrix4tr matches separate rotation and translation composition.</summary>
    [Test]
    public void TestMatrix4tr()
    {
        Vector3 position = new Vector3(1, 2, 0);
        Quaternion rotation = quaternion(23, 45, 56);

        Matrix4x4 resultOptimized = matrix4tr(position, rotation);
        Matrix4x4 resultOriginal = matrix4rotation(rotation) * matrix4translation(position);

        Assert.That(resultOptimized, Is.EqualTo(resultOriginal).Using<Matrix4x4>((m1, m2) => m1.Equals(m2) ? 0 : 1));
    }

    /// <summary>Tests that matrix4ts matches separate scale and translation composition.</summary>
    [Test]
    public void TestMatrix4ts()
    {
        Vector3 position = new Vector3(1, 2, 0);
        Vector3 scale = new Vector3(3, 4, 1);

        Matrix4x4 resultOptimized = matrix4ts(position, scale);
        Matrix4x4 resultOriginal = matrix4scale(scale) * matrix4translation(position);

        Assert.That(resultOptimized, Is.EqualTo(resultOriginal).Using<Matrix4x4>((m1, m2) => m1.Equals(m2) ? 0 : 1));
    }

    /// <summary>Tests that matrix4rs matches separate scale and rotation composition.</summary>
    [Test]
    public void TestMatrix4rs()
    {
        Quaternion rotation = quaternion(23, 45, 56);
        Vector3 scale = new Vector3(3, 4, 1);

        Matrix4x4 resultOptimized = matrix4rs(rotation, scale);
        Matrix4x4 resultOriginal = matrix4scale(scale) * matrix4rotation(rotation);

        Assert.That(resultOptimized, Is.EqualTo(resultOriginal).Using<Matrix4x4>((m1, m2) => m1.Equals(m2) ? 0 : 1));
    }
}