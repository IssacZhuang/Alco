namespace Alco.Test;

using System;
using System.Numerics;
using Alco;
using TestFramework;

[TestFixture]
public class TestTransform2D()
{
    [Test]
    public void TestTransformRevert()
    {
        Transform2D parent = new Transform2D()
        {
            position = new Vector2(1, 2),
            rotation = Rotation2D.FromDegree(45),
            scale = new Vector2(2, 3)
        };

        Transform2D child = new Transform2D()
        {
            position = new Vector2(5, 6),
            rotation = Rotation2D.FromDegree(90),
            scale = new Vector2(8, 9)
        };

        Transform2D transformed = math.transform(parent, child);
        transformed = math.tolocal(parent, transformed);


        AssertExt.AreEqual(child, transformed);
    }

    [Test]
    public void TestTransformMatrix()
    {
        Transform2D parent = new Transform2D()
        {
            position = new Vector2(1, 2),
            rotation = Rotation2D.FromDegree(45),
            scale = new Vector2(2, 3)
        };

        Vector2 position = new Vector2(5, 6);
        //transform by hand
        Vector2 transformed1 = math.mul(parent.rotation, parent.scale * position) + parent.position;
        //transform by matrix
        Vector2 transformed2 = math.transform(parent.Matrix, position);

        AssertExt.AreEqual(transformed1, transformed2);
    }

}