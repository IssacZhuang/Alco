using NUnit.Framework;
using System;
using System.Collections.Generic;
using TestFramework;

namespace Vocore.Test
{
    public class TestChunkList
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
    }
}
