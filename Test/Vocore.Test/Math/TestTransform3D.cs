namespace Vocore.Test;

using System;
using System.Numerics;
using Vocore;
using TestFramework;

[TestFixture]
public class TestTransform3D()
{
    [Test]
    public void TestTransform()
    {
        Transform3D parent = new Transform3D()
        {
            position = new Vector3(1, 2, 3),
            rotation = math.euler(0.1f, 0.2f, 0.3f),
            scale = new Vector3(2, 3, 4)
        };

        Transform3D child = new Transform3D()
        {
            position = new Vector3(5, 6, 7),
            rotation = math.euler(0.4f, 0.5f, 0.6f),
            scale = new Vector3(8, 9, 10)
        };

        Transform3D transformed = math.transform(parent, child);
        transformed = math.transform(math.inverse(parent), transformed);


        AssertExt.AreEqual(child, transformed);
    }

}