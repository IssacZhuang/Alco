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

            int size = 100000000;
            //float3 vs vector
            float3 a = new float3(1, 2, 3);
            float3 b = new float3(4, 5, 6);
            float3 result = float3.zero;

            Vector3 va = new Vector3(1, 2, 3);
            Vector3 vb = new Vector3(4, 5, 6);
            Vector3 vresult = Vector3.Zero;

            UnitTest.Benchmark("float3", () =>
            {
                for (int i = 0; i < size; i++)
                {
                    result = a * b;
                }
            });


            UnitTest.Benchmark("simd", () =>
            {
                for (int i = 0; i < size; i++)
                {
                    vresult = Vector3.Multiply(va, vb);
                }
            });

        }

        public void TestGeneric<T>(T data)
        {
            //Log.Info("TestGeneric: " + data);
        }
    }

}

