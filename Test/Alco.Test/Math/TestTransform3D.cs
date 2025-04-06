namespace Alco.Test;

using System;
using System.Numerics;
using Alco;
using TestFramework;

[TestFixture]
public class TestTransform3D()
{
    [Test]
    public void TestTransformRevert()
    {
        Transform3D parent = new Transform3D()
        {
            Position = new Vector3(1, 2, 3),
            Rotation = math.quaternion(10, 20, 30),
            Scale = new Vector3(2, 3, 4)
        };

        Transform3D child = new Transform3D()
        {
            Position = new Vector3(5, 6, 7),
            Rotation = math.quaternion(40, 50, 60),
            Scale = new Vector3(8, 9, 10)
        };

        Transform3D transformed = math.transform(parent, child);
        transformed = math.tolocal(parent, transformed);

        AssertExt.AreEqual(child, transformed);
    }

    [Test]
    public void TestTransformMatrix()
    {
        Transform3D parent = new Transform3D()
        {
            Position = new Vector3(1, 2, 3),
            Rotation = math.quaternion(10, 20, 30),
            Scale = new Vector3(2, 3, 4)
        };

        Vector3 position = new Vector3(5, 6, 7);
        //transform by hand
        Vector3 transformed1 = math.mul(parent.Rotation, parent.Scale * position) + parent.Position;
        //transform by matrix
        Vector3 transformed2 = math.transform(parent.Matrix, position);

        AssertExt.AreEqual(transformed1, transformed2);
    }

}