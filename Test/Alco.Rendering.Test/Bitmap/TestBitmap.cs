using System;
using System.Numerics;
using NUnit.Framework;

namespace Alco.Rendering.Test
{
    [TestFixture]
    public class TestBitmap
    {
        [Test]
        public void TestConstructorWithWidthAndHeight()
        {
            // Arrange & Act
            var bitmap = new Bitmap<int>(100, 200);

            // Assert
            Assert.That(bitmap.Width, Is.EqualTo(100));
            Assert.That(bitmap.Height, Is.EqualTo(200));
        }

        [Test]
        public void TestConstructorWithUIntWidthAndHeight()
        {
            // Arrange & Act
            var bitmap = new Bitmap<int>(100u, 200u);

            // Assert
            Assert.That(bitmap.Width, Is.EqualTo(100));
            Assert.That(bitmap.Height, Is.EqualTo(200));
        }

        [Test]
        public void TestConstructorWithDefaultValue()
        {
            // Arrange & Act
            var defaultValue = 42;
            var bitmap = new Bitmap<int>(5, 5, defaultValue);

            // Assert
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Assert.That(bitmap[x, y], Is.EqualTo(defaultValue));
                }
            }
        }

        [Test]
        public void TestIndexerSetAndGet()
        {
            // Arrange
            var bitmap = new Bitmap<int>(10, 10);

            // Act
            bitmap[3, 4] = 123;

            // Assert
            Assert.That(bitmap[3, 4], Is.EqualTo(123));
        }

        [Test]
        public void TestClearWithSpecifiedValue()
        {
            // Arrange
            var bitmap = new Bitmap<int>(5, 5, 10);

            // Act
            bitmap.Fill(25);

            // Assert
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Assert.That(bitmap[x, y], Is.EqualTo(25));
                }
            }
        }

        [Test]
        public void TestClearWithoutSpecifiedValue()
        {
            // Arrange
            var bitmap = new Bitmap<int>(5, 5, 10);

            // Act
            bitmap.Clear();

            // Assert
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Assert.That(bitmap[x, y], Is.EqualTo(0));
                }
            }
        }

        [Test]
        public void TestDispose()
        {
            // Arrange
            var bitmap = new Bitmap<int>(10, 10);

            // Act & Assert (no exception should be thrown)
            Assert.DoesNotThrow(() => bitmap.Dispose());
        }

        [Test]
        public void TestBitmapWithVector4()
        {
            // Arrange
            var bitmap = new Bitmap<Vector4>(3, 3);
            var testValue = new Vector4(1.0f, 2.0f, 3.0f, 4.0f);

            // Act
            bitmap[1, 1] = testValue;

            // Assert
            Assert.That(bitmap[1, 1], Is.EqualTo(testValue));
        }
    }
}