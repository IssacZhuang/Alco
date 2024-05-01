namespace Vocore.Test;

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vocore.Unsafe;

public class Test_Native
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


    [Test(Description = "mini heap alloc vs List add element performance")]
    public unsafe void TestMiniHeapVsList()
    {
        int count = 10000000;

        List<int> list = new List<int>();

        using MiniHeap<int> chunkList = new MiniHeap<int>();

        UtilsTest.Benchmark(() =>
        {
            for (int i = 0; i < count; i++)
            {
                list.Add(i);
            }
        }, "List add element");


        Stopwatch sw = new Stopwatch();
        sw.Start();
        for (int i = 0; i < count; i++)
        {
            chunkList.Alloc(i);
        }

        sw.Stop();
        TestContext.WriteLine($"Mini heap alloc element: {sw.ElapsedMilliseconds} ms");

    }
}