using Vocore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using System.Threading;
using System.Threading.Tasks;


using System.IO;

using Vocore.Lua;
using Mond;
using System.Numerics;
using Unity.Collections;

namespace Vocore.Test
{
    delegate void TestDelegate();

    public struct TestJob : IJobBatch
    {
        public void Execute(int i)
        {
        }
    }

    public class Playground
    {
        //some temp code for testing
        [Test("Playground")]
        public unsafe void Test()
        {
            int count = 10000000;
            int[] array = new int[count];
            for (int i = 0; i < count; i++)
            {
                array[i] = i;
            }

            UnitTest.Benchmark(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    int temp = array[i];
                }
            }, "for");

            // Parallel.For(0, count, (i) =>
            //     {
            //         int temp = array[i];
            //     });
            ParallelScheduler.Instance.For(count, (i) =>
            {
                int temp = array[i];
            });
            UnitTest.Benchmark(() =>
            {
                ParallelScheduler.Instance.For(count, (i) =>
                {
                    int temp = array[i];
                });
            }, "parallel scheduler");
            Parallel.For(0, count, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, (i) =>
            {
                int temp = array[i];
            });
            UnitTest.Benchmark(() =>
            {
                Parallel.For(0, count, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, (i) =>
                {
                    int temp = array[i];
                });
            }, "parallel for");


        }

        public void TestGeneric<T>(T data)
        {
            //Log.Info("TestGeneric: " + data);
        }
    }

}

