using NUnit.Framework;
using System.Numerics;
using Alco;

using static Alco.math;

namespace Alco.Test;

[TestFixture]
public class TestMatrix4x4
{
    [Test]
    public void TestDecomposeTransform3D()
    {
        // Create a matrix using TRS
        Vector3 originalPosition = new Vector3(1, 2, 3);
        Quaternion originalRotation = euler(30, 45, 60);
        Vector3 originalScale = new Vector3(2, 3, 4);

        Matrix4x4 matrix = matrix4trs(originalPosition, originalRotation, originalScale);

        // Decompose the matrix
        Transform3D transform = new Transform3D();
        decompose(matrix, out transform);

        // Check if decomposed values match the original values
        Assert.That(transform.Position, Is.EqualTo(originalPosition).Within(0.0001f));
        Assert.That(transform.Rotation, Is.EqualTo(originalRotation).Using<Quaternion>((q1, q2) =>
            (abs(q1.X - q2.X) < 0.0001f &&
             abs(q1.Y - q2.Y) < 0.0001f &&
             abs(q1.Z - q2.Z) < 0.0001f &&
             abs(q1.W - q2.W) < 0.0001f) ? 0 : 1));
        Assert.That(transform.Scale, Is.EqualTo(originalScale).Within(0.0001f));
    }

    [Test]
    public void TestDecomposeTransform3DComponents()
    {
        // Create a matrix using TRS
        Vector3 originalPosition = new Vector3(1, 2, 3);
        Quaternion originalRotation = euler(30, 45, 60);
        Vector3 originalScale = new Vector3(2, 3, 4);

        Matrix4x4 matrix = matrix4trs(originalPosition, originalRotation, originalScale);

        // Decompose the matrix
        Vector3 scale;
        Quaternion rotation;
        Vector3 translation;
        decompose(matrix, out scale, out rotation, out translation);

        // Check if decomposed values match the original values
        Assert.That(translation, Is.EqualTo(originalPosition).Within(0.0001f));
        Assert.That(rotation, Is.EqualTo(originalRotation).Using<Quaternion>((q1, q2) =>
            (abs(q1.X - q2.X) < 0.0001f &&
             abs(q1.Y - q2.Y) < 0.0001f &&
             abs(q1.Z - q2.Z) < 0.0001f &&
             abs(q1.W - q2.W) < 0.0001f) ? 0 : 1));
        Assert.That(scale, Is.EqualTo(originalScale).Within(0.0001f));
    }

    [Test]
    public void TestDecomposeTransform3DEuler()
    {
        // Create a matrix using TRS
        Vector3 originalPosition = new Vector3(1, 2, 3);
        Vector3 originalEuler = new Vector3(30, 45, 60);
        Quaternion originalRotation = euler(originalEuler);
        Vector3 originalScale = new Vector3(2, 3, 4);

        Matrix4x4 matrix = matrix4trs(originalPosition, originalRotation, originalScale);

        // Decompose the matrix
        Vector3 scale;
        Vector3 eulerAngles;
        Vector3 translation;
        decompose(matrix, out scale, out eulerAngles, out translation);

        // Check if decomposed values match the original values
        Assert.That(translation, Is.EqualTo(originalPosition).Within(0.0001f));
        // Euler angles may differ slightly due to conversion, so use a larger tolerance
        Assert.That(eulerAngles, Is.EqualTo(originalEuler).Within(0.5f));
        Assert.That(scale, Is.EqualTo(originalScale).Within(0.0001f));
    }

    [Test]
    public void TestDecomposeTransform2D()
    {
        // Create a matrix using TRS for 2D
        Vector2 originalPosition = new Vector2(1, 2);
        float angleRadians = radians(45.0f);
        Rotation2D originalRotation = new Rotation2D(angleRadians);
        Vector2 originalScale = new Vector2(2, 3);

        Matrix4x4 matrix = matrix4trs(originalPosition, originalRotation, originalScale);

        // Decompose the matrix
        Transform2D transform = new Transform2D();
        decompose(matrix, out transform);

        // Check if decomposed values match the original values
        Assert.That(transform.Position, Is.EqualTo(originalPosition).Within(0.0001f));
        Assert.That(transform.Rotation.C, Is.EqualTo(originalRotation.C).Within(0.0001f));
        Assert.That(transform.Rotation.S, Is.EqualTo(originalRotation.S).Within(0.0001f));
        Assert.That(transform.Scale, Is.EqualTo(originalScale).Within(0.0001f));
    }

    [Test]
    public void TestDecomposeTransform2DComponents()
    {
        // Create a matrix using TRS for 2D
        Vector2 originalPosition = new Vector2(1, 2);
        float angleRadians = radians(45.0f);
        Rotation2D originalRotation = new Rotation2D(angleRadians);
        Vector2 originalScale = new Vector2(2, 3);

        Matrix4x4 matrix = matrix4trs(originalPosition, originalRotation, originalScale);

        // Decompose the matrix
        Vector2 scale;
        Rotation2D rotation;
        Vector2 translation;
        decompose(matrix, out scale, out rotation, out translation);

        // Check if decomposed values match the original values
        Assert.That(translation, Is.EqualTo(originalPosition).Within(0.0001f));
        Assert.That(rotation.C, Is.EqualTo(originalRotation.C).Within(0.0001f));
        Assert.That(rotation.S, Is.EqualTo(originalRotation.S).Within(0.0001f));
        Assert.That(scale, Is.EqualTo(originalScale).Within(0.0001f));
    }

    [Test]
    public void TestDecomposeTransform2DAngle()
    {
        // Create a matrix using TRS for 2D
        Vector2 originalPosition = new Vector2(1, 2);
        float originalAngle = 45.0f; // degrees
        float angleRadians = radians(originalAngle);
        Rotation2D originalRotation = new Rotation2D(angleRadians);
        Vector2 originalScale = new Vector2(2, 3);

        Matrix4x4 matrix = matrix4trs(originalPosition, originalRotation, originalScale);

        // Decompose the matrix
        Vector2 scale;
        float angle;
        Vector2 translation;
        decompose(matrix, out scale, out angle, out translation);

        // Check if decomposed values match the original values
        Assert.That(translation, Is.EqualTo(originalPosition).Within(0.0001f));
        Assert.That(angle, Is.EqualTo(originalAngle).Within(0.1f));
        Assert.That(scale, Is.EqualTo(originalScale).Within(0.0001f));
    }
}