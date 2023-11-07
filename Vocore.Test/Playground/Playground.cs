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
            int count = 10000000;
            TestStruct2[] testStructs = new TestStruct2[count];
            TestClass[] testClasses = new TestClass[count];
            TestDelegate[] testDelegates = new TestDelegate[count];
            
            for (int i = 0; i < count; i++)
            {
                update += () => { };
                testStructs[i] = new TestStruct2();
                testClasses[i] = new TestClass();
                testDelegates[i] = () => { };
            }
            
            UnitTest.Benchmark("Event", () =>
            {
                update?.Invoke();
            });

            UnitTest.Benchmark("Delegate", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    testDelegates[i]();
                }
            });

            UnitTest.Benchmark("Struct", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    testStructs[i].Update();
                }
            });

            UnitTest.Benchmark("Class", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    testClasses[i].Update();
                }
            });



        }
    }

}

