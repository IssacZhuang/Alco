using Vocore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using System.Threading;
using System.Threading.Tasks;


using System.IO;

using System.Numerics;

namespace Vocore.Test
{
    delegate void TestDelegate();

    public struct TestStruct2
    {
        public void Update()
        {
        }
    }

    public class TestClass
    {
        public virtual void Update()
        {
        }
    }

    public class Playground
    {
        delegate void TestDelegate();
        TestDelegate update;

        //some temp code for testing
        [Test("Playground")]
        public unsafe void Test()
        {
            Vector3 v3 = new Vector3(1, 2, 3);
            Quaternion r3 = math.euler(math.radians(new Vector3(45, 0, 0)));
            Quaternion rt3 = math.euler(math.radians(new Vector3(43, 0, 0)));


            Vector2 v2 = new Vector2(1, 2);
            Rotation2D r2 = new Rotation2D(math.radians(45));
            Rotation2D rt2 = new Rotation2D(math.radians(43));

            int count = 10000000;
            UnitTest.Benchmark("rotate 3D", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    r3 = math.mul(r3, rt3);
                }
            });

            UnitTest.Benchmark("rotate 2D", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    r2 = math.mul(r2, rt2);
                }
            });

        }
    }

}

