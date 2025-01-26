namespace Alco.Test;

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Alco.Unsafe;

public class TestNative
{
    [Test(Description = "native chunk list add")]
    public unsafe void TestMiniHeapAlloc()
    {
        using MiniHeap<int> heap = new MiniHeap<int>();
        using NativeArrayList<IntPtr> verify = new NativeArrayList<IntPtr>();
        int count = 100;
        for (int i = 0; i < count; i++)
        {
            verify.Add(new nint(heap.Alloc(i)));
        }

        bool result = true;

        for (int i = 0; i < count; i++)
        {
            int* ptr = (int*)verify[i];
            if (*ptr != i)
            {
                result = false;
                break;
            }
        }

        //reallocation
        heap.Reset();
        //verify.Clear();
        for (int i = 0; i < count; i++)
        {
            heap.Alloc(i);
        }

        for (int i = 0; i < count; i++)
        {
            int* ptr = (int*)verify[i];
            if (*ptr != i)
            {
                result = false;
                break;
            }
        }

        Assert.IsTrue(result);

    }

}