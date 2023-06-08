using System;
using System.Collections.Generic;

using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Vocore.Test
{
    public class TestBVH
    {
        // [Test("Benchmark build BVH tree")]
        // public unsafe void TestBuildTree()
        // {
        //     NativeArrayList<ColliderBox> list = new NativeArrayList<ColliderBox>();
        //     int count = 100000;
        //     Random random = new Random(12345);
        //     //random collider
        //     for (int i = 0; i < count; i++)
        //     {

        //         float3 pos = random.NextFloat3(-100, 100);
        //         float3 size = random.NextFloat3(1, 10);
        //         quaternion rot = random.NextQuaternionRotation();
        //         list.Add(new ColliderBox
        //         {
        //             shape = new ShapeBox(pos, size, rot)
        //         });
        //     }

        //     StatelessBVH bvh = new StatelessBVH();
        //     bvh.Init();

        //     ColliderBox* ptr = list.Ptr;
        //     for (int i = 0; i < count; i++)
        //     {
        //         bvh.AddCollider(ref *(ptr + i));
        //     }
        //     bvh.BuildTree();
        //     bvh.Reset();
        //     TestHelper.Benchmark("Build BVH tree", () =>
        //     {
        //         for (int i = 0; i < count; i++)
        //         {
        //             bvh.AddCollider(ref *(ptr + i));
        //         }
        //         bvh.BuildTree();

        //     });

        // }
    }
}

