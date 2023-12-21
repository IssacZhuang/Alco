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
            UnitTest.AssertTrue(math.mul(q, math.inverse(q)).Equals(Quaternion.Identity));
            UnitTest.PrintBlue(q);
            UnitTest.PrintBlue(math.mul(q, math.inverse(q)));
        }
    }

}

