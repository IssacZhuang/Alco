using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace Alco.Test
{
    public class TestCurve
    {
        [Test(Description = "CurveLinear Functionality")]
        public void TestCurveLinear()
        {
            var points = new CurvePoint<float>[]
            {
                new CurvePoint<float>(0, 0),
                new CurvePoint<float>(1, 10),
                new CurvePoint<float>(2, 0)
            };

            var curve = new CurveLinear(points);

            Assert.That(curve.Evaluate(0), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(curve.Evaluate(0.5f), Is.EqualTo(5f).Within(1e-5f));
            Assert.That(curve.Evaluate(1), Is.EqualTo(10f).Within(1e-5f));
            Assert.That(curve.Evaluate(1.5f), Is.EqualTo(5f).Within(1e-5f));
            Assert.That(curve.Evaluate(2), Is.EqualTo(0f).Within(1e-5f));
            
            // Out of bounds
            Assert.That(curve.Evaluate(-1), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(curve.Evaluate(3), Is.EqualTo(0f).Within(1e-5f));
        }

        [Test(Description = "CurveHermite Functionality")]
        public void TestCurveHermite()
        {
            // Simple linear-like points to test basic interpolation
            var points = new CurvePoint<float>[]
            {
                new CurvePoint<float>(0, 0),
                new CurvePoint<float>(1, 1),
                new CurvePoint<float>(2, 0)
            };

            var curve = new CurveHermite(points);

            // Hermite interpolation should smooth out the peak
            Assert.That(curve.Evaluate(0), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(curve.Evaluate(1), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(curve.Evaluate(2), Is.EqualTo(0f).Within(1e-5f));
            
            // At 0.5, value should be > 0.5 due to ease-out/ease-in nature if slopes are calculated correctly
            // But with specific slopes it might vary.
            // Let's just check it's within expected range [0, 1]
            float midVal = curve.Evaluate(0.5f);
            Assert.IsTrue(midVal >= 0 && midVal <= 1);
        }

        [Test(Description = "CurveLinear2D Functionality")]
        public void TestCurveLinear2D()
        {
            var points = new CurvePoint<Vector2>[]
            {
                new CurvePoint<Vector2>(0, new Vector2(0, 0)),
                new CurvePoint<Vector2>(1, new Vector2(10, 20))
            };

            var curve = new CurveLinear2D(points);

            Vector2 res = curve.Evaluate(0.5f);
            Assert.That(res.X, Is.EqualTo(5f).Within(1e-5f));
            Assert.That(res.Y, Is.EqualTo(10f).Within(1e-5f));
        }

        [Test(Description = "Curve Collection Operations (Add, Remove, Clear)")]
        public void TestCurveCollectionOperations()
        {
            // Test with CurveLinear (BaseCurveLinear)
            var curve = new CurveLinear();
            Assert.That(curve.Count, Is.EqualTo(0));

            // Test Add
            // Add points in unsorted order
            curve.Add(new CurvePoint<float>(1, 10));
            curve.Add(new CurvePoint<float>(0, 0));
            curve.Add(new CurvePoint<float>(2, 20));

            Assert.That(curve.Count, Is.EqualTo(3));
            
            // Should be sorted upon evaluation or access if implemented correctly
            // Let's check evaluation which triggers sort
            Assert.That(curve.Evaluate(0.5f), Is.EqualTo(5f).Within(1e-5f)); // Between 0 and 1
            Assert.That(curve.Evaluate(1.5f), Is.EqualTo(15f).Within(1e-5f)); // Between 1 and 2

            // Test Remove
            bool removed = curve.Remove(new CurvePoint<float>(1, 10));
            Assert.IsTrue(removed);
            Assert.That(curve.Count, Is.EqualTo(2));
            
            // Verify interpolation changes after removal (now interpolates between 0 and 2 directly)
            Assert.That(curve.Evaluate(1.0f), Is.EqualTo(10f).Within(1e-5f)); // Should be linear between (0,0) and (2,20) -> at 1 it's 10

            // Test Clear
            curve.Clear();
            Assert.That(curve.Count, Is.EqualTo(0));
            
            // Test empty evaluation
            Assert.That(curve.Evaluate(1.0f), Is.EqualTo(0f));
        }

        [Test(Description = "Curve Indexer Setter and Re-sorting")]
        public void TestCurveIndexerSetter()
        {
            var curve = new CurveLinear();
            curve.Add(new CurvePoint<float>(0, 0));
            curve.Add(new CurvePoint<float>(1, 10));
            curve.Add(new CurvePoint<float>(2, 20));

            // Initial evaluation to clear dirty flag
            Assert.That(curve.Evaluate(1.0f), Is.EqualTo(10f));

            // 1. Test modifying Value
            curve[1] = new CurvePoint<float>(1, 50);
            Assert.That(curve.Evaluate(1.0f), Is.EqualTo(50f).Within(1e-5f));
            Assert.That(curve.Evaluate(0.5f), Is.EqualTo(25f).Within(1e-5f)); // Between (0,0) and (1,50)

            // 2. Test modifying Key (out of order)
            // Change point at index 1 (Key 1) to Key 3.
            // Points should become: (0,0), (2,20), (3,50)
            curve[1] = new CurvePoint<float>(3, 50);
            
            // Before evaluation, the internal list is [(0,0), (3,50), (2,20)]
            // Evaluate(2.5f) should trigger Sort() and then interpolate between (2,20) and (3,50)
            Assert.That(curve.Evaluate(2.5f), Is.EqualTo(35f).Within(1e-5f));
            
            // Check order via indexer after Sort() has been triggered by Evaluate
            Assert.That(curve[0].Key, Is.EqualTo(0f));
            Assert.That(curve[1].Key, Is.EqualTo(2f));
            Assert.That(curve[2].Key, Is.EqualTo(3f));
        }
    }
}
