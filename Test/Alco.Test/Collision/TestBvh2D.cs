using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Numerics;


using Random = Alco.FastRandom;
using System.Runtime;

namespace Alco.Test
{

    public class TestBvh2D
    {
        [Test(Description = "BVH collision 2D with Collector")]
        public unsafe void TestBvhCollision()
        {
            NativeArrayList<ColliderBox2D> boxs = new NativeArrayList<ColliderBox2D>(8);
            NativeArrayList<ColliderSphere2D> spheres = new NativeArrayList<ColliderSphere2D>(8);
            NativeArrayList<ColliderRef2D> colliders = new NativeArrayList<ColliderRef2D>();

            boxs.Add(new ColliderBox2D
            {
                Shape = new ShapeBox2D(new Vector2(20, 0), new Vector2(1f), Rotation2D.Identity)
            });

            boxs.Add(new ColliderBox2D
            {
                Shape = new ShapeBox2D(new Vector2(10, 0), new Vector2(1f), Rotation2D.Identity)
            });

            spheres.Add(new ColliderSphere2D
            {
                Shape = new ShapeSphere2D(new Vector2(-10, 0), 1f)
            });

            spheres.Add(new ColliderSphere2D
            {
                Shape = new ShapeSphere2D(Vector2.Zero, 0.8f)
            });

            boxs.Add(new ColliderBox2D
            {
                Shape = new ShapeBox2D(Vector2.Zero, new Vector2(1f), Rotation2D.Identity)
            });

            for (int i = 0; i < boxs.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(boxs.UnsafePointer + i));
            }

            for (int i = 0; i < spheres.Length; i++)
            {
                colliders.Add(ColliderRef2D.Create(spheres.UnsafePointer + i));
            }

            NativeBvh2D bvh = new NativeBvh2D();
            bvh.BuildTree(colliders.AsSpan());

            // Test Ray Cast (NativeBvh2D.CastRay / CastRayFirstHit don't use collector anymore)
            {
                Ray2D ray = Ray2D.CreateWithStartAndEnd(new Vector2(-1.2f, 0), new Vector2(120f, 0));

                RayCastResult2D result = bvh.CastRayClosestHit(ray);

                Assert.IsTrue(result.Hit);
                TestContext.WriteLine($"Ray hit at fraction: {result.HitInfo.Fraction}");
            }

            // Test Ray Cast with Collector
            {
                Ray2D ray = Ray2D.CreateWithStartAndEnd(new Vector2(-1.2f, 0), new Vector2(120f, 0));

                FirstHitCollector collector = new FirstHitCollector();
                bvh.CastRay(ray, ref collector);

                Assert.IsTrue(collector.HasHit);
            }

            // Test Ray Cast Multi Hit with NativeListCollector
            {
                Ray2D ray = Ray2D.CreateWithStartAndEnd(new Vector2(-12f, 0), new Vector2(25f, 0));
                NativeArrayList<ColliderCastResult2D> hitResults = new NativeArrayList<ColliderCastResult2D>(8);
                NativeListCollector multiCollector = new NativeListCollector(&hitResults);

                bvh.CastRay(ray, ref multiCollector);

                Assert.IsTrue(hitResults.Length > 1);
                TestContext.WriteLine($"Ray hit {hitResults.Length} objects");
                for (int i = 0; i < hitResults.Length; i++)
                {
                    hitResults[i].Collider.IntersectRay(ray, out var hit);
                    TestContext.WriteLine($"Hit {i} at fraction: {hit.Fraction}");
                }
                hitResults.Dispose();
            }

            // Test Collider Cast with Collector
            {
                ShapeBox2D boxShape = new ShapeBox2D(new Vector2(-1.2f, 0), new Vector2(1f), Rotation2D.Identity);

                FirstHitCollector colliderCollector = new FirstHitCollector();
                bvh.CastBox(boxShape, ref colliderCollector);

                Assert.IsTrue(colliderCollector.HasHit);
            }

            boxs.Dispose();
            spheres.Dispose();
            colliders.Dispose();
            bvh.Dispose();
        }
    }
}
