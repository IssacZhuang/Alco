using System;
using System.Collections.Generic;

using UnityToolBox.UnitTest;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Vocore.Test.Unity
{
    public class TestBase
    {
        [UnitTest("Benchmark build BVH tree")]
        public unsafe void TestBuildTree()
        {
            NativeArrayList<ColliderBox> list = new NativeArrayList<ColliderBox>();
            int count = 100000;
            Random random = new Random(12345);
            //random collider
            for (int i = 0; i < count; i++)
            {

                float3 pos = random.NextFloat3(-100, 100);
                float3 size = random.NextFloat3(1, 10);
                quaternion rot = random.NextQuaternionRotation();
                list.Add(new ColliderBox
                {
                    shape = new ShapeBox(pos, size, rot)
                });
            }

            NativeArrayList<ColliderRef> colliders = new NativeArrayList<ColliderRef>(64, false);
            StatelessBVH bvh = new StatelessBVH();


            ColliderBox* ptr = list.Ptr;
            for (int i = 0; i < count; i++)
            {
                colliders.Add(ColliderRef.Create(ref *(ptr + i)));
            }

            bvh.BuildTree(colliders);
            colliders.Clear();

            TestHelper.Benchmark("Build BVH tree", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    colliders.Add(ColliderRef.Create(ref *(ptr + i)));
                }
                bvh.BuildTree(colliders);

            });

        }
    }
}

