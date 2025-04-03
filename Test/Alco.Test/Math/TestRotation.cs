using NUnit.Framework;
using System.Numerics;
using Alco;

using static Alco.math;

namespace Alco.Test;

[TestFixture]
public class TestRotation
{
    [Test]
    public void TestEuler()
    {
        Vector3 eulerAngles = new Vector3(12, 45, -45);

        Matrix4x4 rotationMatrix = Matrix4x4.CreateRotationX(radians(eulerAngles.X)) *
                                   Matrix4x4.CreateRotationY(radians(eulerAngles.Y)) *
                                   Matrix4x4.CreateRotationZ(radians(eulerAngles.Z));
        Quaternion quaternion = Quaternion.CreateFromRotationMatrix(rotationMatrix);

        float halfX = radians(eulerAngles.X) * 0.5f;
        float halfY = radians(eulerAngles.Y) * 0.5f;
        float halfZ = radians(eulerAngles.Z) * 0.5f;

        Quaternion zRot = new Quaternion(0, 0, sin(halfZ), cos(halfZ));
        Quaternion yRot = new Quaternion(0, sin(halfY), 0, cos(halfY));
        Quaternion xRot = new Quaternion(sin(halfX), 0, 0, cos(halfX));

        Quaternion quaternion2 = zRot * yRot * xRot;

        AssertExt.AreEqual(quaternion, quaternion2);
    }
}
