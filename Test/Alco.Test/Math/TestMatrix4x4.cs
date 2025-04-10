using NUnit.Framework;
using System.Numerics;
using Alco;

using static Alco.math;

namespace Alco.Test;

[TestFixture]
public class TestMatrix4x4
{
    [Test]
    public void TestDecomposeTransform3DComponents()
    {
        float epsilon = 0.0001f;

        // Create a matrix using TRS
        Vector3 originalPosition = new Vector3(1, 2, 3);
        Quaternion originalRotation = quaternion(30, 45, 60).Normalize();
        Vector3 originalScale = new Vector3(2, 3, 4);

        Matrix4x4 matrix = matrix4trs(originalPosition, originalRotation, originalScale);

        // Decompose the matrix
        Vector3 scale;
        Quaternion rotation;
        Vector3 translation;
        decompose(matrix, out scale, out rotation, out translation);

        // Check if decomposed values match the original values
        Assert.Multiple(() =>
        {
            Assert.That(originalPosition.X, Is.EqualTo(translation.X).Within(epsilon));
            Assert.That(originalPosition.Y, Is.EqualTo(translation.Y).Within(epsilon));
            Assert.That(originalPosition.Z, Is.EqualTo(translation.Z).Within(epsilon));
        });

        Assert.True(MathHelper.Equal(originalRotation, rotation, epsilon), "Rotation is not equal: expected " + originalRotation + " but got " + rotation);
        
        Assert.Multiple(() =>
        {
            Assert.That(originalScale.X, Is.EqualTo(scale.X).Within(epsilon));
            Assert.That(originalScale.Y, Is.EqualTo(scale.Y).Within(epsilon));
            Assert.That(originalScale.Z, Is.EqualTo(scale.Z).Within(epsilon));
        });


        // //negate scale
        // originalScale = new Vector3(-2, 4, -4);
        // matrix = matrix4trs(originalPosition, originalRotation, originalScale);
        // decompose(matrix, out scale, out rotation, out translation);

        // Assert.Multiple(() =>
        // {
        //     Assert.That(originalPosition.X, Is.EqualTo(translation.X).Within(epsilon));
        //     Assert.That(originalPosition.Y, Is.EqualTo(translation.Y).Within(epsilon));
        //     Assert.That(originalPosition.Z, Is.EqualTo(translation.Z).Within(epsilon));
        // });

        // Assert.True(MathHelper.Equal(originalRotation, rotation, epsilon), "Rotation is not equal: expected " + originalRotation + " but got " + rotation);

        // Assert.Multiple(() =>
        // {
        //     Assert.That(originalScale.X, Is.EqualTo(scale.X).Within(epsilon));
        //     Assert.That(originalScale.Y, Is.EqualTo(scale.Y).Within(epsilon));
        //     Assert.That(originalScale.Z, Is.EqualTo(scale.Z).Within(epsilon));
        // });


    }

    [Test]
    public void TestDecomposeTransform3DEuler()
    {
        float epsilon = 0.0001f;

        // Create a matrix using TRS
        Vector3 originalPosition = new Vector3(1, 2, 3);
        Vector3 originalEuler = new Vector3(30, 45, 60);
        Quaternion originalRotation = quaternion(originalEuler);
        Vector3 originalScale = new Vector3(2, 3, 4);

        Matrix4x4 matrix = matrix4trs(originalPosition, originalRotation, originalScale);

        // Decompose the matrix
        Vector3 scale;
        Vector3 eulerAngles;
        Vector3 translation;
        decompose(matrix, out scale, out eulerAngles, out translation);

        // Check if decomposed values match the original values
        Assert.Multiple(() =>
        {
            Assert.That(originalPosition.X, Is.EqualTo(translation.X).Within(epsilon));
            Assert.That(originalPosition.Y, Is.EqualTo(translation.Y).Within(epsilon));
            Assert.That(originalPosition.Z, Is.EqualTo(translation.Z).Within(epsilon));
        });

        // Euler angles may differ slightly due to conversion, so use a larger tolerance
        float eulerEpsilon = 0.5f;
        Assert.Multiple(() =>
        {
            Assert.That(originalEuler.X, Is.EqualTo(eulerAngles.X).Within(eulerEpsilon));
            Assert.That(originalEuler.Y, Is.EqualTo(eulerAngles.Y).Within(eulerEpsilon));
            Assert.That(originalEuler.Z, Is.EqualTo(eulerAngles.Z).Within(eulerEpsilon));
        });

        Assert.Multiple(() =>
        {
            Assert.That(originalScale.X, Is.EqualTo(scale.X).Within(epsilon));
            Assert.That(originalScale.Y, Is.EqualTo(scale.Y).Within(epsilon));
            Assert.That(originalScale.Z, Is.EqualTo(scale.Z).Within(epsilon));
        });
    }

    // [Test]
    // public void TestDecomposeTransform2DComponents()
    // {
    //     float epsilon = 0.0001f;

    //     // Create a matrix using TRS for 2D
    //     Vector2 originalPosition = new Vector2(1, 2);
    //     float angleRadians = radians(45.0f);
    //     Rotation2D originalRotation = new Rotation2D(angleRadians);
    //     Vector2 originalScale = new Vector2(2, 3);

    //     Matrix4x4 matrix = matrix4trs(originalPosition, originalRotation, originalScale);

    //     // Decompose the matrix
    //     Vector2 scale;
    //     Rotation2D rotation;
    //     Vector2 translation;
    //     decompose(matrix, out scale, out rotation, out translation);

    //     // Check if decomposed values match the original values
    //     Assert.Multiple(() =>
    //     {
    //         Assert.That(originalPosition.X, Is.EqualTo(translation.X).Within(epsilon));
    //         Assert.That(originalPosition.Y, Is.EqualTo(translation.Y).Within(epsilon));
    //     });

    //     Assert.Multiple(() =>
    //     {
    //         Assert.That(originalRotation.C, Is.EqualTo(rotation.C).Within(epsilon));
    //         Assert.That(originalRotation.S, Is.EqualTo(rotation.S).Within(epsilon));
    //     });

    //     Assert.Multiple(() =>
    //     {
    //         Assert.That(originalScale.X, Is.EqualTo(scale.X).Within(epsilon));
    //         Assert.That(originalScale.Y, Is.EqualTo(scale.Y).Within(epsilon));
    //     });
    // }

    // [Test]
    // public void TestDecomposeTransform2DAngle()
    // {
    //     float epsilon = 0.0001f;

    //     // Create a matrix using TRS for 2D
    //     Vector2 originalPosition = new Vector2(1, 2);
    //     float originalAngle = 45.0f; // degrees
    //     Rotation2D originalRotation = new Rotation2D(originalAngle);
    //     Vector2 originalScale = new Vector2(2, 3);

    //     Matrix4x4 matrix = matrix4trs(originalPosition, originalRotation, originalScale);

    //     // Decompose the matrix
    //     Vector2 scale;
    //     float angle;
    //     Vector2 translation;
    //     decompose(matrix, out scale, out angle, out translation);

    //     // Check if decomposed values match the original values
    //     Assert.Multiple(() =>
    //     {
    //         Assert.That(originalPosition.X, Is.EqualTo(translation.X).Within(epsilon));
    //         Assert.That(originalPosition.Y, Is.EqualTo(translation.Y).Within(epsilon));
    //     });

    //     Assert.That(originalAngle, Is.EqualTo(angle).Within(0.1f));

    //     Assert.Multiple(() =>
    //     {
    //         Assert.That(originalScale.X, Is.EqualTo(scale.X).Within(epsilon));
    //         Assert.That(originalScale.Y, Is.EqualTo(scale.Y).Within(epsilon));
    //     });
    // }
}