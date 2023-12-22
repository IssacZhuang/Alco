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
            Quaternion q = math.euler(math.radians(new Vector3(45, 45, 0)));
            Vector3 vector = new Vector3(1,2,3);

            Vector3 x = math.rotate(q, new Vector3(vector.X, 0, 0));
            Vector3 y = math.rotate(q, new Vector3(0, vector.Y, 0));
            Vector3 z = math.rotate(q, new Vector3(0, 0, vector.Z));

            Vector3 rotated = math.abs(x) + math.abs(y) + math.abs(z);
            Vector3 rotated2 = math.abs(math.rotate(vector, q));

            UnitTest.Print(rotated);
            UnitTest.Print(rotated2);
        }
    }

}

