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

            int size = 40000;
            //Vector3 vs vector
            Vector3 pos = new Vector3(4, 5, 6);
            Quaternion rot = Quaternion.CreateFromYawPitchRoll(1, 2, 3);
            Vector3 scale = new Vector3(7, 8, 9);
            UnitTest.Benchmark("matrix 4x4", () =>
            {
                for (int i = 0; i < size; i++)
                {
                    Matrix4x4 m = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
                }
            });

        }

        public void TestGeneric<T>(T data)
        {
            //Log.Info("TestGeneric: " + data);
        }
    }

}

