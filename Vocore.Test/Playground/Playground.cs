using Vocore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using System.Threading;

using UnityEngine;

using Unity.Mathematics;

namespace Vocore.Test
{
    public class Playground
    {
        //some temp code for testing
        [Test("Playground")]
        public unsafe void Test()
        {
            TestHelper.PrintBlue(quaternion.Euler(new float3(90, 90, 0)));

        }
    }

}

