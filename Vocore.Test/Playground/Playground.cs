using Vocore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using System.Threading;

using UnityEngine;
using System.Threading.Tasks;

using Unity.Mathematics;
using System.IO;

using Vocore.Lua;
using Mond;

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
            // string filename = "test.zip";
            // string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
            // Log.Info("Path: " + path);
            // using (ResourcePack pack = new ResourcePack(path))
            // {
            //     pack.TrySetFile("test.bin", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            //     pack.TrySetTextFile("test.txt", "Hello World!");
            // }

            UnitTest.Benchmark("parallel for", () =>
            {
                Parallel.For(0, 10000000, (i) =>
                {

                });
            });


            UnitTest.Benchmark("fast parallel for", () =>
            {
                FastParallel.For(0, 10000000, (i)=>{

                });
            });

        }

        public void TestGeneric<T>(T data)
        {
            //Log.Info("TestGeneric: " + data);
        }
    }

}

