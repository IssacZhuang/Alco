using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Numerics;
using Alco;
using Random = Alco.FastRandom;
using System.Runtime;

namespace Alco.Test
{
    public class TestBvh2DCollector
    {
        private struct CountCollector : ICollisionCollector<ColliderCastResult2D>
        {
            public int Count;
            public bool AddHit(ColliderCastResult2D result)
            {
                Count++;
                return true;
            }
        }

        [Test(Description = "BVH external collector 2D")]
        public unsafe void TestExternalCollector()
        {
            NativeArrayList<ColliderBox2D> boxs = new NativeArrayList<ColliderBox2D>(8);
            NativeArrayList<ColliderRef2D> colliders = new NativeArrayList<ColliderRef2D>();

            // Create 3 overlapping boxes at (0,0) to ensure multiple hits
            boxs.Add(new ColliderBox2D { Shape = new ShapeBox2D(Vector2.Zero, new Vector2(1f), Rotation2D.Identity) });
            boxs.Add(new ColliderBox2D { Shape = new ShapeBox2D(new Vector2(0.1f, 0), new Vector2(1f), Rotation2D.Identity) });
            boxs.Add(new ColliderBox2D { Shape = new ShapeBox2D(new Vector2(-0.1f, 0), new Vector2(1f), Rotation2D.Identity) });
            
            // Add a far away box
            boxs.Add(new ColliderBox2D { Shape = new ShapeBox2D(new Vector2(100, 100), new Vector2(1f), Rotation2D.Identity) });

            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(boxs.UnsafePointer + i));
            }

            NativeBvh2D bvh = new NativeBvh2D();
            bvh.BuildTree(colliders.AsSpan());

            ColliderBox2D castBox = new ColliderBox2D
            {
                Shape = new ShapeBox2D(Vector2.Zero, new Vector2(1f), Rotation2D.Identity)
            };
            ColliderRef2D castRef = ColliderRef2D.Create(&castBox);

            // Test 1: NativeListCollector (Collect All)
            var list = new NativeArrayList<ColliderCastResult2D>(4);
            var listCollector = new NativeListCollector(&list);
            bvh.CastCollider(castRef, ref listCollector);
            Assert.AreEqual(3, list.Length); // Should hit the 3 overlapping boxes
            list.Dispose();

            // Test 2: FirstHitCollector (Early Exit)
            var firstHitCollector = new FirstHitCollector();
            bvh.CastCollider(castRef, ref firstHitCollector);
            Assert.IsTrue(firstHitCollector.HasHit);

            // Test 3: Custom CountCollector
            var countCollector = new CountCollector();
            bvh.CastCollider(castRef, ref countCollector);
            Assert.AreEqual(3, countCollector.Count);

            // Cleanup
            boxs.Dispose();
            colliders.Dispose();
            bvh.Dispose();
        }
    }
}
