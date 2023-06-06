using Vocore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using System.Threading;

using UnityEngine;
using System.Threading.Tasks;

namespace Vocore.Test
{
    delegate void TestDelegate();

    public class Playground
    {
        //some temp code for testing
        [Test("Playground")]
        public unsafe void Test()
        {
            // float[] randomArray = new float[1000000];
            // Unity.Mathematics.Random random = new Unity.Mathematics.Random();
            // for (int i = 0; i < randomArray.Length; i++)
            // {
            //     randomArray[i] = random.NextInt();
            // }

            // List<float> list = new List<float>();

            // TestHelper.Benchmark("List Add", () =>
            // {
            //     for (int i = 0; i < randomArray.Length; i++)
            //     {
            //         list.Add(randomArray[i]);
            //     }
            //     list.Sort();
            // });

            // NativeList<float> nativeList = new NativeList<float>();
            // TestHelper.Benchmark("NativeList Add", () =>
            // {
            //     for (int i = 0; i < randomArray.Length; i++)
            //     {
            //         nativeList.Add(randomArray[i]);
            //     }
            //     nativeList.Sort();
            // });        
        }
    }

}

