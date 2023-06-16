using System;
using System.Collections.Generic;

using Unity.Mathematics;
using Unity.Collections;

using UnityEngine;
using UnityEngine.Rendering;

using Vocore.Unsafe;

using UnityToolBox.UnitTest;

namespace Vocore.Test.Unity
{
    public class Test_ComputeBuffer
    {
        [UnitTest("Copy to ComputeBuffer")]
        public void TestTransfer()
        {
            int size = 512 * 64 * 2;
            int stride = UtilsMemory.SizeOf<int>();

            int iteration = 4;

            ComputeBuffer computeBuffer = new ComputeBuffer(size, stride);

            int[] array = new int[size];

            for (int i = 0; i < size; i++)
            {
                array[i] = i;
            }

            TestHelper.Benchmark("Set", () =>
            {
                for (int i = 0; i < iteration; i++)
                {
                    computeBuffer.SetData(array);
                }
            });

            TestHelper.Benchmark("Get", () =>
            {
                for (int i = 0; i < iteration; i++)
                {
                    computeBuffer.GetData(array);
                }
            });

            

        }
    }
}

