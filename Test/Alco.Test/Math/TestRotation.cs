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
        float epsilon = 0.001f;
        Vector3 eulerAngles = new Vector3(12, 45, -45);

        Matrix4x4 rotationMatrix = Matrix4x4.CreateRotationX(radians(eulerAngles.X)) *
                                   Matrix4x4.CreateRotationY(radians(eulerAngles.Y)) *
                                   Matrix4x4.CreateRotationZ(radians(eulerAngles.Z));
        Quaternion quat = quaternion(rotationMatrix);

        float halfX = radians(eulerAngles.X) * 0.5f;
        float halfY = radians(eulerAngles.Y) * 0.5f;
        float halfZ = radians(eulerAngles.Z) * 0.5f;

        Quaternion zRot = new Quaternion(0, 0, sin(halfZ), cos(halfZ));
        Quaternion yRot = new Quaternion(0, sin(halfY), 0, cos(halfY));
        Quaternion xRot = new Quaternion(sin(halfX), 0, 0, cos(halfX));

        Quaternion quat2 = zRot * yRot * xRot;
        Assert.Multiple(() =>
        {
            Assert.That(quat.X, Is.EqualTo(quat2.X).Within(epsilon));
            Assert.That(quat.Y, Is.EqualTo(quat2.Y).Within(epsilon));
            Assert.That(quat.Z, Is.EqualTo(quat2.Z).Within(epsilon));
            Assert.That(quat.W, Is.EqualTo(quat2.W).Within(epsilon));
        });
    }

    [Test]
    public void TestQuaternionToEuler()
    {
        float epsilon = 0.001f;
        // Test case 1: Standard rotation
        Vector3 eulerAngles = new Vector3(12, 45, -45);
        Quaternion quat = quaternion(radians(eulerAngles));
        Vector3 decomposed = degrees(decompose(quat));
        Assert.Multiple(() =>
        {
            Assert.That(decomposed.X, Is.EqualTo(eulerAngles.X).Within(epsilon));
            Assert.That(decomposed.Y, Is.EqualTo(eulerAngles.Y).Within(epsilon));
            Assert.That(decomposed.Z, Is.EqualTo(eulerAngles.Z).Within(epsilon));
        });

        // Test case 2: Zero rotation
        eulerAngles = new Vector3(0, 0, 0);
        quat = quaternion(radians(eulerAngles));
        decomposed = degrees(decompose(quat));
        Assert.Multiple(() =>
        {
            Assert.That(decomposed.X, Is.EqualTo(eulerAngles.X).Within(epsilon));
            Assert.That(decomposed.Y, Is.EqualTo(eulerAngles.Y).Within(epsilon));
            Assert.That(decomposed.Z, Is.EqualTo(eulerAngles.Z).Within(epsilon));
        });

        // Test case 3: Rotation with gimbal lock consideration
        // When pitch (Y) is near 90 degrees, original representation isn't unique
        eulerAngles = new Vector3(90, 90, 90);
        quat = quaternion(radians(eulerAngles));
        decomposed = degrees(decompose(quat));
        // Instead of direct comparison, verify the quaternions are equivalent
        Quaternion recomposedQuaternion = quaternion(radians(decomposed));
        Assert.Multiple(() =>
        {
            Assert.That(quat.X, Is.EqualTo(recomposedQuaternion.X).Within(epsilon));
            Assert.That(quat.Y, Is.EqualTo(recomposedQuaternion.Y).Within(epsilon));
            Assert.That(quat.Z, Is.EqualTo(recomposedQuaternion.Z).Within(epsilon));
            Assert.That(quat.W, Is.EqualTo(recomposedQuaternion.W).Within(epsilon));
        });

        // Test case 4: Negative angles
        eulerAngles = new Vector3(-30, -60, -90);
        quat = quaternion(radians(eulerAngles));
        decomposed = degrees(decompose(quat));
        Assert.Multiple(() =>
        {
            Assert.That(decomposed.X, Is.EqualTo(eulerAngles.X).Within(epsilon));
            Assert.That(decomposed.Y, Is.EqualTo(eulerAngles.Y).Within(epsilon));
            Assert.That(decomposed.Z, Is.EqualTo(eulerAngles.Z).Within(epsilon));
        });

        // Test case 5: Mixed large angles
        eulerAngles = new Vector3(120, -45, 180);
        quat = quaternion(radians(eulerAngles));
        decomposed = degrees(decompose(quat));

        //-180 and 180 are equivalent rotations
        eulerAngles.Z = abs(eulerAngles.Z);
        decomposed.Z = abs(decomposed.Z);

        Assert.Multiple(() =>
        {
            Assert.That(decomposed.X, Is.EqualTo(eulerAngles.X).Within(epsilon));
            Assert.That(decomposed.Y, Is.EqualTo(eulerAngles.Y).Within(epsilon));
            Assert.That(decomposed.Z, Is.EqualTo(eulerAngles.Z).Within(epsilon));
        });
    }

    [Test]
    public void TestDecomposeFromMatrix()
    {
        float epsilon = 0.001f;

        Quaternion quat = quaternion(radians(new Vector3(12, 45, -45)));
        Matrix4x4 matrix = matrix4rotation(quat);
        Quaternion quat2 = quaternion(matrix);

        float dot = Quaternion.Dot(quat, quat2);
        Assert.That(abs(dot), Is.GreaterThan(1 - epsilon));

        //mix with translation
        quat = quaternion(radians(new Vector3(12, 23, 60)));
        Transform3D transform = new Transform3D(new Vector3(1, 2, 3), quat, Vector3.One);
        Matrix4x4 matrix2 = transform.Matrix;
        quat2 = quaternion(matrix2);

        dot = Quaternion.Dot(quat, quat2);
        Assert.That(abs(dot), Is.GreaterThan(1 - epsilon));


        //mix with scale
        quat = quaternion(radians(new Vector3(-41, 2, 87)));
        Transform3D transform2 = new Transform3D(new Vector3(1, 2, 3), quat, new Vector3(1, 2, 3));
        Matrix4x4 matrix3 = transform2.Matrix;
        Quaternion quaternion4 = quaternion(matrix3);

        dot = Quaternion.Dot(quat, quaternion4);
        Assert.That(abs(dot), Is.GreaterThan(1 - epsilon));

        //mix with translation and scale
        quat = quaternion(radians(new Vector3(65, -39, 11)));
        Transform3D transform3 = new Transform3D(new Vector3(1, 2, 3), quat, new Vector3(1, 2, 3));
        Matrix4x4 matrix4 = transform3.Matrix;
        Quaternion quaternion5 = quaternion(matrix4);

        dot = Quaternion.Dot(quat, quaternion5);
        Assert.That(abs(dot), Is.GreaterThan(1 - epsilon));
    }
}
