using NUnit.Framework;
using System.Numerics;
using Vocore;

using static Vocore.math;

namespace Vocore.Test;

[TestFixture]
public class TestMatrix3D
{
    [Test]
    public void TestMatrix4trs()
    {
        Vector3 position = new Vector3(1, 2, 0);
        Quaternion rotation = euler(0.5f, 0.8660254f, 0);
        Vector3 scale = new Vector3(3, 4, 1);

        Matrix4x4 resultOptimized = matrix4trs(position, rotation, scale);
        Matrix4x4 resultOriginal = matrix4scale(scale) * matrix4rotation(rotation) * matrix4translation(position);

        Assert.That(resultOptimized, Is.EqualTo(resultOriginal).Using<Matrix4x4>((m1, m2) => m1.Equals(m2) ? 0 : 1));
    }

    [Test]
    public void TestMatrix4tr()
    {
        Vector3 position = new Vector3(1, 2, 0);
        Quaternion rotation = euler(0.5f, 0.8660254f, 0); 

        Matrix4x4 resultOptimized = matrix4tr(position, rotation);
        Matrix4x4 resultOriginal = matrix4rotation(rotation) * matrix4translation(position);

        Assert.That(resultOptimized, Is.EqualTo(resultOriginal).Using<Matrix4x4>((m1, m2) => m1.Equals(m2) ? 0 : 1));
    }

    [Test]
    public void TestMatrix4ts()
    {
        Vector3 position = new Vector3(1, 2, 0);
        Vector3 scale = new Vector3(3, 4, 1);

        Matrix4x4 resultOptimized = matrix4ts(position, scale);
        Matrix4x4 resultOriginal = matrix4scale(scale) * matrix4translation(position);

        Assert.That(resultOptimized, Is.EqualTo(resultOriginal).Using<Matrix4x4>((m1, m2) => m1.Equals(m2) ? 0 : 1));
    }

    [Test]
    public void TestMatrix4rs()
    {
        Quaternion rotation = euler(0.5f, 0.8660254f, 0); 
        Vector3 scale = new Vector3(3, 4, 1);

        Matrix4x4 resultOptimized = matrix4rs(rotation, scale);
        Matrix4x4 resultOriginal = matrix4scale(scale) * matrix4rotation(rotation);

        Assert.That(resultOptimized, Is.EqualTo(resultOriginal).Using<Matrix4x4>((m1, m2) => m1.Equals(m2) ? 0 : 1));
    }
}