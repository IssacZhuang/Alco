using NUnit.Framework;
using System;
using System.Collections.Generic;
using TestFramework;

namespace Vocore.Test
{
    public class Test_ChunkList
    {
        [Test(Description = "chunkList add")]
        public void TestAdd()
        {
            ChunkList<int> list = new ChunkList<int>();
            for (int i = 0; i < list.ChunkSize + 10; i++)
            {
                list.Add(i);
            }

            bool result = true;
            int index = 0;
            foreach (var item in list)
            {

                if (item != index)
                {
                    result = false;
                    //break;
                }
                index++;
            }

            Assert.IsFalse(!result);
        }

        [Test(Description = "chunkList remove")]
        public void TestRemove()
        {
            ChunkList<int> list = new ChunkList<int>();
            for (int i = 0; i < list.ChunkSize + 10; i++)
            {
                list.Add(i);
            }

            list.Remove(5);

            bool result = true;
            int index = 0;
            foreach (var item in list)
            {
                if (item == 5)
                {
                    result = false;
                    //break;
                }
                index++;
            }

            Assert.IsFalse(!result);
        }

        [Test(Description = "chunkList vs List add element performance")]
        public void TestVsList()
        {
            int count = 10000000;

            List<int> list = new List<int>();

            ChunkList<int> chunkList = new ChunkList<int>();

            UtilsTest.Benchmark(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    list.Add(i);
                }
            }, "List add element");

            UtilsTest.Benchmark(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    chunkList.Add(i);
                }
            }, "ChunkList add element");

        }

        [Test(Description = "chunkList vs List remove element performance")]
        public void TestVsListRemove()
        {
            int count = 100000;

            List<int> list = new List<int>();

            ChunkList<int> chunkList = new ChunkList<int>();

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

            UtilsTest.Benchmark(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    chunkList.Remove(i);
                }
            }, "ChunkList remove element");

        }

    }
}
