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

namespace Vocore.Test
{
    delegate void TestDelegate();

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

        }

        public void TestGeneric<T>(T data)
        {
            //Log.Info("TestGeneric: " + data);
        }
    }

}

