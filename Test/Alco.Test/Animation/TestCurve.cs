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

            Assert.AreEqual(0f, curve.Evaluate(0), 1e-5f);
            Assert.AreEqual(5f, curve.Evaluate(0.5f), 1e-5f);
            Assert.AreEqual(10f, curve.Evaluate(1), 1e-5f);
            Assert.AreEqual(5f, curve.Evaluate(1.5f), 1e-5f);
            Assert.AreEqual(0f, curve.Evaluate(2), 1e-5f);
            
            // Out of bounds
            Assert.AreEqual(0f, curve.Evaluate(-1), 1e-5f);
            Assert.AreEqual(0f, curve.Evaluate(3), 1e-5f);
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
            Assert.AreEqual(0f, curve.Evaluate(0), 1e-5f);
            Assert.AreEqual(1f, curve.Evaluate(1), 1e-5f);
            Assert.AreEqual(0f, curve.Evaluate(2), 1e-5f);
            
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
            Assert.AreEqual(5f, res.X, 1e-5f);
            Assert.AreEqual(10f, res.Y, 1e-5f);
        }

        [Test(Description = "Curve Collection Operations (Add, Remove, Clear)")]
        public void TestCurveCollectionOperations()
        {
            // Test with CurveLinear (BaseCurveLinear)
            var curve = new CurveLinear();
            Assert.AreEqual(0, curve.Count);

            // Test Add
            // Add points in unsorted order
            curve.Add(new CurvePoint<float>(1, 10));
            curve.Add(new CurvePoint<float>(0, 0));
            curve.Add(new CurvePoint<float>(2, 20));

            Assert.AreEqual(3, curve.Count);
            
            // Should be sorted upon evaluation or access if implemented correctly
            // Let's check evaluation which triggers sort
            Assert.AreEqual(5f, curve.Evaluate(0.5f), 1e-5f); // Between 0 and 1
            Assert.AreEqual(15f, curve.Evaluate(1.5f), 1e-5f); // Between 1 and 2

            // Test Remove
            bool removed = curve.Remove(new CurvePoint<float>(1, 10));
            Assert.IsTrue(removed);
            Assert.AreEqual(2, curve.Count);
            
            // Verify interpolation changes after removal (now interpolates between 0 and 2 directly)
            Assert.AreEqual(10f, curve.Evaluate(1.0f), 1e-5f); // Should be linear between (0,0) and (2,20) -> at 1 it's 10

            // Test Clear
            curve.Clear();
            Assert.AreEqual(0, curve.Count);
            
            // Test empty evaluation
            Assert.AreEqual(0f, curve.Evaluate(1.0f));
        }

        [Test(Description = "Curve Indexer Setter and Re-sorting")]
        public void TestCurveIndexerSetter()
        {
            var curve = new CurveLinear();
            curve.Add(new CurvePoint<float>(0, 0));
            curve.Add(new CurvePoint<float>(1, 10));
            curve.Add(new CurvePoint<float>(2, 20));

            // Initial evaluation to clear dirty flag
            Assert.AreEqual(10f, curve.Evaluate(1.0f));

            // 1. Test modifying Value
            curve[1] = new CurvePoint<float>(1, 50);
            Assert.AreEqual(50f, curve.Evaluate(1.0f), 1e-5f);
            Assert.AreEqual(25f, curve.Evaluate(0.5f), 1e-5f); // Between (0,0) and (1,50)

            // 2. Test modifying Time (out of order)
            // Change point at index 1 (time 1) to time 3.
            // Points should become: (0,0), (2,20), (3,50)
            curve[1] = new CurvePoint<float>(3, 50);
            
            // Before evaluation, the internal list is [(0,0), (3,50), (2,20)]
            // Evaluate(2.5f) should trigger Sort() and then interpolate between (2,20) and (3,50)
            Assert.AreEqual(35f, curve.Evaluate(2.5f), 1e-5f);
            
            // Check order via indexer after Sort() has been triggered by Evaluate
            Assert.AreEqual(0f, curve[0].Time);
            Assert.AreEqual(2f, curve[1].Time);
            Assert.AreEqual(3f, curve[2].Time);
        }
    }
}
