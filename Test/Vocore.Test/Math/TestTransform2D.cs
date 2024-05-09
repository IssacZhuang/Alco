namespace Vocore.Test;

using System;
using System.Numerics;
using Vocore;
using TestFramework;

[TestFixture]
public class TestTransform2D()
{
    [Test]
    public void TestTransform()
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
        transformed = math.transform(math.inverse(parent), transformed);


        AssertExt.AreEqual(child, transformed);
    }

}