using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections.Generic;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using UnityToolBox.UnitTest;
using Vocore;

namespace Vocore.Test.Unity
{
    public class TestAlloc
    {
        [UnitTest("TestAlloc")]
        public unsafe void Test()
        {
            int count = 1000000;

            TestHelper.Benchmark("Marshal.AllocHGlobal", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr ptr = Marshal.AllocHGlobal(8);
                    Marshal.FreeHGlobal(ptr);
                }
            });

            TestHelper.Benchmark("UnsafeUtility.Malloc", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    void* ptr = UnsafeUtility.Malloc(8, 4, Allocator.Temp);
                    UnsafeUtility.Free(ptr, Allocator.Temp);
                }
            });
        }
    }
}

