namespace Vocore.Test;

using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics;
using Vocore.Unsafe;

public class Test_Native
{
    [Test(Description = "native chunk list add")]
    public void TestNativeChunkListAdd()
    {
        using NativeChunkList<int> list = new NativeChunkList<int>();
        int count = 100;
        for (int i = 0; i < count; i++)
        {
            list.Add(i);
        }

        bool result = true;

        for (int i = 0; i < count; i++)
        {
            if (list[i] != i)
            {
                result = false;
                break;
            }
        }

        Assert.IsTrue(result);

    }

    [Test(Description = "native chunk list remove")]
    public void TestNativeChunkListRemove()
    {
        using NativeChunkList<int> list = new NativeChunkList<int>();
        int count = 100;
        for (int i = 0; i < count; i++)
        {
            list.Add(i);
        }

        list.Remove(5);

        bool result = true;

        for (int i = 0; i < count-1; i++)
        {
            if (list[i] == 5)
            {
                result = false;
                break;
            }
        }

        Assert.IsTrue(result);
    }

    [Test(Description = "native chunkList vs List add element performance")]
    public void TestVsList()
    {
        int count = 10000000;

        List<int> list = new List<int>();

        using NativeChunkList<int> chunkList = new NativeChunkList<int>();

        UtilsTest.Benchmark(() =>
        {
            for (int i = 0; i < count; i++)
            {
                list.Add(i);
            }
        }, "List add element");

        //boxing happens
        // UtilsTest.Benchmark(() =>
        // {
        //     for (int i = 0; i < count; i++)
        //     {
        //         chunkList.Add(i);
        //     }
        // }, "ChunkList add element");

        Stopwatch sw = new Stopwatch();
        sw.Start();
        for (int i = 0; i < count; i++)
        {
            chunkList.Add(i);
        }

        sw.Stop();
        TestContext.WriteLine($"Native ChunkList add element: {sw.ElapsedMilliseconds} ms");

    }

    [Test(Description = "native chunkList vs List remove element performance")]
    public void TestVsListRemove()
    {
        int count = 100000;

        List<int> list = new List<int>();

        using NativeChunkList<int> chunkList = new NativeChunkList<int>();

        for (int i = 0; i < count; i++)
        {
            list.Add(i);
            chunkList.Add(i);
        }

        UtilsTest.Benchmark(() =>
        {
            for (int i = 0; i < count; i++)
            {
                list.Remove(i);
            }
        }, "List remove element");

        // UtilsTest.Benchmark(() =>
        // {
        //     for (int i = 0; i < count; i++)
        //     {
        //         chunkList.Remove(i);
        //     }
        // }, "ChunkList remove element");

        Stopwatch sw = new Stopwatch();
        sw.Start();
        for (int i = 0; i < count; i++)
        {
            chunkList.Remove(i);
        }

        sw.Stop();
        TestContext.WriteLine($"Native ChunkList remove element: {sw.ElapsedMilliseconds} ms");

    }
}