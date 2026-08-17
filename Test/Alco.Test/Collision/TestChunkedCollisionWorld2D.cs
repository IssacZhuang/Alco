using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco.Test;

/// <summary>
/// Cross-validates <see cref="ChunkedCollisionWorld2D"/> against a brute-force reference over
/// randomized worlds with mixed collider sizes, in both linear-bucket mode and promoted-tree
/// mode, including mutation sequences between query rounds.
/// </summary>
public unsafe class TestChunkedCollisionWorld2D
{
    private const int ChunkSize = 4;
    private const float GridMin = -32f;
    private const int ChunkCount = 16;
    private const int TargetCount = 150;

    private sealed class Target
    {
        public readonly int Id;

        public Target(int id)
        {
            Id = id;
        }
    }

    private sealed class ReferenceCollider
    {
        public required Target Target;
        public ColliderType2D Type;
        public ShapeBox2D Box;
        public ShapeSphere2D Sphere;

        public bool CollidesWith(ColliderRef2D caster)
        {
            // the collider must live in this frame: returning a ColliderRef2D to a callee-local
            // shape dangles once that frame pops, and the comparison then reads dead stack
            if (Type == ColliderType2D.Box)
            {
                ColliderBox2D box = new ColliderBox2D { Shape = Box };
                return ColliderRef2D.Create(&box).CollidesWith(caster);
            }

            ColliderSphere2D sphere = new ColliderSphere2D { Shape = Sphere };
            return ColliderRef2D.Create(&sphere).CollidesWith(caster);
        }

        public bool IntersectRay(Ray2D ray, float maxT, out RaycastHit2D hit)
        {
            if (Type == ColliderType2D.Box)
            {
                ColliderBox2D box = new ColliderBox2D { Shape = Box };
                return box.IntersectRay(ray, out hit) && hit.Fraction <= maxT;
            }

            ColliderSphere2D sphere = new ColliderSphere2D { Shape = Sphere };
            return sphere.IntersectRay(ray, out hit) && hit.Fraction <= maxT;
        }

        public bool ContainsPoint(Vector2 point)
        {
            if (Type == ColliderType2D.Box)
            {
                ColliderBox2D box = new ColliderBox2D { Shape = Box };
                return box.IntersectPoint(point);
            }

            ColliderSphere2D sphere = new ColliderSphere2D { Shape = Sphere };
            return sphere.IntersectPoint(point);
        }
    }

    private sealed class DualRegistration : IDisposable
    {
        public ReferenceCollider? Collider;
        public IDisposable? Linear;
        public IDisposable? Tree;

        public void Dispose()
        {
            Linear?.Dispose();
            Linear = null;
            Tree?.Dispose();
            Tree = null;
        }
    }

    private struct ListCollector : ICollisionCastCollector, IRayCastCollector2D
    {
        public List<int> HitIds;

        public bool OnHit(object target)
        {
            HitIds.Add(((Target)target).Id);
            return true;
        }

        public bool OnHit(object target, RaycastHit2D hit)
        {
            HitIds.Add(((Target)target).Id);
            return true;
        }
    }

    private struct CountingCollector : ICollisionCastCollector
    {
        public int Count;

        public bool OnHit(object target)
        {
            Count++;
            return Count < 2; // stops after the second hit to exercise the early-out contract
        }
    }

    [Test(Description = "chunked world fuzz against brute force (linear + tree modes, with mutations)")]
    public void TestFuzzAgainstBruteForce()
    {
        FastRandom random = new FastRandom(20260817);

        ChunkedCollisionWorld2D linearWorld = new ChunkedCollisionWorld2D(GridMin, GridMin, ChunkCount, ChunkCount, ChunkSize)
        {
            TreeBuildThreshold = int.MaxValue,
        };
        ChunkedCollisionWorld2D treeWorld = new ChunkedCollisionWorld2D(GridMin, GridMin, ChunkCount, ChunkCount, ChunkSize)
        {
            TreeBuildThreshold = 6,
        };

        List<ReferenceCollider> reference = new List<ReferenceCollider>();
        List<DualRegistration> registrations = new List<DualRegistration>();
        int nextId = 0;
        for (int round = 0; round < 3; round++)
        {
            if (round > 0)
            {
                Mutate(random, reference, registrations);
            }

            while (registrations.Count < TargetCount)
            {
                Target target = new Target(nextId++);
                ReferenceCollider collider = CreateRandomCollider(random, target);
                DualRegistration registration = new DualRegistration
                {
                    Collider = collider,
                    Linear = AddTo(collider, linearWorld),
                    Tree = AddTo(collider, treeWorld),
                };
                reference.Add(collider);
                registrations.Add(registration);
            }

            treeWorld.RebuildDirtyTrees();
            VerifyQueries(random, linearWorld, reference, $"linear round {round}");
            VerifyQueries(random, treeWorld, reference, $"tree round {round}");
        }

        Assert.That(treeWorld.TreeCount, Is.GreaterThan(0), "tree world should have promoted buckets");
        Assert.That(linearWorld.TreeCount, Is.EqualTo(0), "linear world must never promote");

        linearWorld.Dispose();
        treeWorld.Dispose();
    }

    [Test(Description = "chunked world empty and edge-case queries")]
    public void TestEdgeCases()
    {
        ChunkedCollisionWorld2D world = new ChunkedCollisionWorld2D(GridMin, GridMin, ChunkCount, ChunkCount, ChunkSize);
        ListCollector collector = new ListCollector { HitIds = new List<int>() };

        world.CastPoint(ref collector, Vector2.Zero);
        world.CastBox(ref collector, new ShapeBox2D(Vector2.Zero, new Vector2(4, 4), Rotation2D.Identity));
        world.CastSphere(ref collector, new ShapeSphere2D(Vector2.Zero, 2f));
        world.CastRay(ref collector, new Ray2D(new Vector2(-40, -40), new Vector2(80, 80)));
        Assert.That(collector.HitIds.Count, Is.EqualTo(0));
        Assert.That(world.TryCastRayClosestHit<Target>(new Ray2D(Vector2.Zero, new Vector2(10, 0)), out _, out _), Is.False);

        // single collider centered exactly on chunk corners (the grid center)
        Target borderTarget = new Target(1);
        ShapeBox2D borderShape = new ShapeBox2D(Vector2.Zero, new Vector2(2, 2), Rotation2D.Identity);
        IDisposable borderRegistration = world.Add(borderTarget, borderShape);
        collector.HitIds.Clear();
        world.CastPoint(ref collector, new Vector2(0.5f, 0.5f));
        Assert.That(collector.HitIds, Is.EqualTo(new List<int> { 1 }));
        Assert.That(world.TryCastRayClosestHit<Target>(new Ray2D(new Vector2(-10, 0), new Vector2(20, 0)), out Target? hit, out RaycastHit2D hitInfo), Is.True);
        Assert.That(hit, Is.EqualTo(borderTarget));
        Assert.That(hitInfo.Fraction, Is.EqualTo(0.45f).Within(1e-3f));

        // maxT: the border box sits at fraction 4.5/20 = 0.225; a limit below misses, above hits
        Assert.That(world.TryCastRayClosestHit<Target>(new Ray2D(new Vector2(-10, 0), new Vector2(20, 0)), out _, out _, 0.1f), Is.False);
        Assert.That(world.TryCastRayClosestHit<Target>(new Ray2D(new Vector2(-10, 0), new Vector2(20, 0)), out _, out _, 0.5f), Is.True);

        // huge collider routed to the big bucket, extending past the grid border (clamped);
        // its AABB spans (-70,-70)-(10,10)
        Target hugeTarget = new Target(2);
        IDisposable hugeRegistration = world.Add(hugeTarget, new ShapeBox2D(new Vector2(-30, -30), new Vector2(80, 80), Rotation2D.Identity));
        collector.HitIds.Clear();
        world.CastPoint(ref collector, new Vector2(5, 5));
        Assert.That(collector.HitIds, Is.EqualTo(new List<int> { 2 }));
        collector.HitIds.Clear();
        world.CastPoint(ref collector, new Vector2(-0.5f, -0.5f));
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, collector.HitIds);

        // removal: double dispose is a no-op, removed targets are never reported again
        hugeRegistration.Dispose();
        hugeRegistration.Dispose();
        collector.HitIds.Clear();
        world.CastPoint(ref collector, new Vector2(5, 5));
        Assert.That(collector.HitIds.Count, Is.EqualTo(0));

        // slot reuse after removal
        Target reused = new Target(3);
        IDisposable reusedRegistration = world.Add(reused, new ShapeSphere2D(new Vector2(20, 20), 1f));
        collector.HitIds.Clear();
        world.CastPoint(ref collector, new Vector2(20.5f, 20.5f));
        Assert.That(collector.HitIds, Is.EqualTo(new List<int> { 3 }));

        // degenerate rays: a zero-displacement ray is a miss even inside a collider (the engine
        // RayBox primitive yields a negative fraction in that case), and must not crash
        collector.HitIds.Clear();
        world.CastRay(ref collector, new Ray2D(new Vector2(0.5f, 0.5f), Vector2.Zero));
        Assert.That(collector.HitIds.Count, Is.EqualTo(0));
        collector.HitIds.Clear();
        world.CastRay(ref collector, new Ray2D(new Vector2(-100, -100), new Vector2(5, 5)));
        Assert.That(collector.HitIds.Count, Is.EqualTo(0));

        // early-out collector: three overlapping targets, the collector stops after the second hit
        world.Add(new Target(4), new ShapeSphere2D(new Vector2(31, 31), 2f));
        world.Add(new Target(5), new ShapeSphere2D(new Vector2(31, 31), 2f));
        world.Add(new Target(6), new ShapeSphere2D(new Vector2(31, 31), 2f));
        CountingCollector counting = new CountingCollector();
        world.CastPoint(ref counting, new Vector2(31, 31));
        Assert.That(counting.Count, Is.EqualTo(2));

        // clear resets everything
        world.Clear();
        Assert.That(world.Count, Is.EqualTo(0));
        collector.HitIds.Clear();
        world.CastPoint(ref collector, new Vector2(0.5f, 0.5f));
        Assert.That(collector.HitIds.Count, Is.EqualTo(0));

        borderRegistration.Dispose();
        reusedRegistration.Dispose();
        world.Dispose();
    }

    [Test(Description = "chunked world collect-by-bounds region enumeration")]
    public void TestCollectTargets()
    {
        ChunkedCollisionWorld2D world = new ChunkedCollisionWorld2D(GridMin, GridMin, ChunkCount, ChunkCount, ChunkSize);
        Target small = new Target(1);
        Target huge = new Target(2);
        world.Add(small, new ShapeSphere2D(new Vector2(10, 10), 1f));
        world.Add(huge, new ShapeBox2D(new Vector2(-30, -30), new Vector2(80, 80), Rotation2D.Identity));

        List<object> targets = new List<object>();
        world.CollectTargets(new BoundingBox2D(new Vector2(9, 9), new Vector2(11, 11)), targets);
        CollectionAssert.AreEquivalent(new object[] { small, huge }, targets);

        // region beyond every AABB (clamped into the grid but overlapping nothing)
        targets.Clear();
        world.CollectTargets(new BoundingBox2D(new Vector2(30, 30), new Vector2(32, 32)), targets);
        Assert.That(targets.Count, Is.EqualTo(0));

        // broadphase-only query: the huge collider's AABB covers (0,0)-(8,8) even though the
        // small sphere at (10,10) does not
        targets.Clear();
        world.CollectTargets(new BoundingBox2D(new Vector2(0, 0), new Vector2(8, 8)), targets);
        CollectionAssert.AreEquivalent(new object[] { huge }, targets);

        world.Dispose();
    }

    private static void VerifyQueries(FastRandom random, ChunkedCollisionWorld2D world, List<ReferenceCollider> reference, string context)
    {
        ListCollector collector = new ListCollector { HitIds = new List<int>() };
        for (int i = 0; i < 200; i++)
        {
            Vector2 position = random.NextVector2(GridMin + 2, -GridMin - 2);

            collector.HitIds.Clear();
            world.CastPoint(ref collector, position);
            AssertIds(ReferencePoints(reference, position), collector.HitIds, $"{context} point at {position}");

            ShapeBox2D boxShape = new ShapeBox2D(position, random.NextVector2(1, 20), random.NextRotation2D());
            collector.HitIds.Clear();
            world.CastBox(ref collector, boxShape);
            ColliderBox2D boxCaster = new ColliderBox2D { Shape = boxShape };
            AssertIds(ReferenceCaster(reference, ColliderRef2D.Create(&boxCaster)), collector.HitIds, $"{context} box at {position}");

            ShapeSphere2D sphereShape = new ShapeSphere2D(position, random.NextFloat(0.5f, 15f));
            collector.HitIds.Clear();
            world.CastSphere(ref collector, sphereShape);
            ColliderSphere2D sphereCaster = new ColliderSphere2D { Shape = sphereShape };
            HashSet<int> expectedSphere = ReferenceCaster(reference, ColliderRef2D.Create(&sphereCaster));
            AssertIds(expectedSphere, collector.HitIds, $"{context} sphere at {position} r={sphereShape.Radius}");

            Ray2D ray = new Ray2D(position, random.NextVector2(-40, 40));
            float maxT = random.NextFloat(0, 1) < 0.5f ? 1f : 1.5f;
            collector.HitIds.Clear();
            world.CastRay(ref collector, ray, maxT);
            AssertIds(ReferenceRay(reference, ray, maxT), collector.HitIds, $"{context} ray at {position}");

            bool referenceHit = ReferenceClosest(reference, ray, maxT, out int referenceId, out float referenceFraction);
            bool worldHit = world.TryCastRayClosestHit<Target>(ray, out Target? worldTarget, out RaycastHit2D worldHitInfo, maxT);
            Assert.That(worldHit, Is.EqualTo(referenceHit), $"{context} closest-hit flag at {position}");
            if (!referenceHit)
            {
                continue;
            }

            Assert.That(worldHitInfo.Fraction, Is.EqualTo(referenceFraction).Within(1e-4f), $"{context} closest-hit fraction at {position}");
            if (worldTarget!.Id != referenceId)
            {
                Assert.That(MathF.Abs(worldHitInfo.Fraction - referenceFraction), Is.LessThan(1e-4f),
                    $"{context} closest-hit target mismatch (ids {worldTarget.Id} vs {referenceId}) is only allowed for exact fraction ties");
            }
        }
    }

    private static void AssertIds(HashSet<int> expected, List<int> actual, string context)
    {
        // unordered comparison: chunk discovery order differs from reference insertion order, and
        // multi-chunk leaves may be reported more than once per query by contract
        HashSet<int> actualSet = new HashSet<int>(actual);
        Assert.That(actualSet, Is.EquivalentTo(expected), context);
    }

    private static HashSet<int> ReferencePoints(List<ReferenceCollider> reference, Vector2 point)
    {
        HashSet<int> ids = new HashSet<int>();
        foreach (ReferenceCollider collider in reference)
        {
            if (collider.ContainsPoint(point))
            {
                ids.Add(collider.Target.Id);
            }
        }

        return ids;
    }

    private static HashSet<int> ReferenceCaster(List<ReferenceCollider> reference, ColliderRef2D caster)
    {
        HashSet<int> ids = new HashSet<int>();
        foreach (ReferenceCollider collider in reference)
        {
            if (collider.CollidesWith(caster))
            {
                ids.Add(collider.Target.Id);
            }
        }

        return ids;
    }

    private static HashSet<int> ReferenceRay(List<ReferenceCollider> reference, Ray2D ray, float maxT)
    {
        HashSet<int> ids = new HashSet<int>();
        foreach (ReferenceCollider collider in reference)
        {
            if (collider.IntersectRay(ray, maxT, out _))
            {
                ids.Add(collider.Target.Id);
            }
        }

        return ids;
    }

    private static bool ReferenceClosest(List<ReferenceCollider> reference, Ray2D ray, float maxT, out int id, out float fraction)
    {
        id = -1;
        fraction = float.MaxValue;
        foreach (ReferenceCollider collider in reference)
        {
            if (!collider.IntersectRay(ray, maxT, out RaycastHit2D hit))
            {
                continue;
            }

            if (hit.Fraction < fraction)
            {
                fraction = hit.Fraction;
                id = collider.Target.Id;
            }
        }

        return id >= 0;
    }

    private static ReferenceCollider CreateRandomCollider(FastRandom random, Target target)
    {
        float kind = random.NextFloat(0, 1);
        // 60% small (single chunk), 30% medium (multi chunk), 10% huge (big bucket)
        float sizeKind = random.NextFloat(0, 1);

        Vector2 position;
        float size;
        if (sizeKind < 0.6f)
        {
            position = random.NextVector2(GridMin + 2, -GridMin - 2);
            size = random.NextFloat(0.3f, 2.5f);
        }
        else if (sizeKind < 0.9f)
        {
            position = random.NextVector2(GridMin + 6, -GridMin - 6);
            size = random.NextFloat(4f, 15f);
        }
        else
        {
            position = random.NextVector2(GridMin + 20, -GridMin - 20);
            size = random.NextFloat(20f, 60f);
        }

        ReferenceCollider collider = new ReferenceCollider { Target = target };
        if (kind < 0.5f)
        {
            collider.Type = ColliderType2D.Box;
            collider.Box = new ShapeBox2D(position, new Vector2(size, size * random.NextFloat(0.5f, 1.5f)), random.NextRotation2D());
        }
        else
        {
            collider.Type = ColliderType2D.Sphere;
            collider.Sphere = new ShapeSphere2D(position, size * 0.5f);
        }

        return collider;
    }

    private static IDisposable AddTo(ReferenceCollider collider, ChunkedCollisionWorld2D world)
    {
        if (collider.Type == ColliderType2D.Box)
        {
            return world.Add(collider.Target, collider.Box);
        }

        return world.Add(collider.Target, collider.Sphere);
    }

    private static void Mutate(FastRandom random, List<ReferenceCollider> reference, List<DualRegistration> registrations)
    {
        for (int i = 0; i < 40 && registrations.Count > 80; i++)
        {
            int index = (int)random.NextFloat(0, registrations.Count);
            DualRegistration registration = registrations[index];
            registrations.RemoveAt(index);
            reference.Remove(registration.Collider!);
            registration.Dispose();
        }
    }
}
