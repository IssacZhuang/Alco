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
            int count = 1000000;
            int[] array = new int[count];
            for (int i = 0; i < count; i++)
            {
                array[i] = i;
            }

            JobScheduler.Instance.ScheduleParallel(count, (i) =>
            {
                int temp = array[i];
            });
            Parallel.For(0, count, (i) =>
                {
                    int temp = array[i];
                });

            UnitTest.Benchmark(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    int temp = array[i];
                }
            }, "for");

            UnitTest.Benchmark(() =>
            {
                Parallel.For(0, count, (i) =>
                {
                    int temp = array[i];
                });
            }, "parallel for");

            UnitTest.Benchmark(() =>
            {
                JobScheduler.Instance.ScheduleParallel(count, (i) =>
                {
                    int temp = array[i];
                });
            }, "job scheduler");
        }

        public void TestGeneric<T>(T data)
        {
            //Log.Info("TestGeneric: " + data);
        }
    }

}

