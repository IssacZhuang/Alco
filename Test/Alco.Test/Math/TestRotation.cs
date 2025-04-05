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

    [Test]
    public void TestDecompose()
    {
        // Test case 1: Standard rotation
        Vector3 eulerAngles = new Vector3(12, 45, -45);
        Quaternion quaternion = euler(radians(eulerAngles));
        Vector3 decomposed = degree(decompose(quaternion));
        AssertExt.AreEqual(decomposed, eulerAngles);

        // Test case 2: Zero rotation
        eulerAngles = new Vector3(0, 0, 0);
        quaternion = euler(radians(eulerAngles));
        decomposed = degree(decompose(quaternion));
        AssertExt.AreEqual(decomposed, eulerAngles);

        // Test case 3: Rotation with gimbal lock consideration
        // When pitch (Y) is near 90 degrees, original representation isn't unique
        eulerAngles = new Vector3(90, 90, 90);
        quaternion = euler(radians(eulerAngles));
        decomposed = degree(decompose(quaternion));
        // Instead of direct comparison, verify the quaternions are equivalent
        Quaternion recomposedQuaternion = euler(radians(decomposed));
        AssertExt.AreEqual(quaternion, recomposedQuaternion);

        // Test case 4: Negative angles
        eulerAngles = new Vector3(-30, -60, -90);
        quaternion = euler(radians(eulerAngles));
        decomposed = degree(decompose(quaternion));
        AssertExt.AreEqual(decomposed, eulerAngles);

        // Test case 5: Mixed large angles
        eulerAngles = new Vector3(120, -45, 180);
        quaternion = euler(radians(eulerAngles));
        decomposed = degree(decompose(quaternion));

        //-180 and 180 are equivalent rotations
        eulerAngles.Z = abs(eulerAngles.Z);
        decomposed.Z = abs(decomposed.Z);

        AssertExt.AreEqual(decomposed, eulerAngles);
    }
}
