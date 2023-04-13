using Vocore;
using System;
using System.Collections.Generic;
using System.Diagnostics;

using System.Threading;

using UnityEngine;

namespace Vocore.Test
{
    public class Playground
    {
        //some temp code for testing
        [Test("Playground")]
        public unsafe void Test()
        {
            NativeBuffer<Vector3> test = default;
            TestUtility.PrintBlue(test.Stride);
        }
    }

}

