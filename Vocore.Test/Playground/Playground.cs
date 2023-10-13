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

            Vector3 v = new Vector3(0, 0, 3);
            Quaternion q = math.EulerXYZ(math.radians(new Vector3(0, 90f, 0)));
            Vector3 v2 = Vector3.Transform(v, q);
            Log.Info("v2: " + v2);

        }

        public void TestGeneric<T>(T data)
        {
            //Log.Info("TestGeneric: " + data);
        }
    }

}

