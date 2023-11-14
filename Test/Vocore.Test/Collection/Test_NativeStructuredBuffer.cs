using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Vocore;

using System.Numerics;
using System.Diagnostics;

namespace Vocore.Test
{
    [DisabledTestTemporarily]
    internal class Test_NativeStructuredBuffer
    {
        [Test("NativeStructuredBuffer Get/Set")]
        public unsafe void ValueGetSet()
        {
            NativeBuffer<Vector3> bufferVec = new NativeBuffer<Vector3>(5);
            UnitTest.AssertFalse(bufferVec.DataPtr == null);
            UnitTest.AssertFalse((byte*)bufferVec.DataPtr == null);

            bufferVec[3] = Vector3.One;
            UnitTest.AssertFalse(bufferVec[3] != Vector3.One);
            bufferVec.Dispose();
        }

        [Test("NativeStructuredBuffer OutOfRange", true)]
        public void OutOfRange()
        {
            NativeBuffer<Vector3> bufferVec = new NativeBuffer<Vector3>(5);
            var _ = bufferVec[5];//
            bufferVec.Dispose();
        }

        [Test("NativeStructuredBuffer Benchmark Write")]
        public void BenchmarkWrite()
        {
            int count = 50000000;

            Vector3[] array = new Vector3[count];
            StructuredBuffer<Vector3> buffer = new StructuredBuffer<Vector3>(count);
            NativeBuffer<Vector3> nativeBuffer = new NativeBuffer<Vector3>(count);

            Stopwatch timer = new Stopwatch();
            timer.Start();
            for (int i = 0; i < count; i++)
            {
                array[i] = Vector3.One;
            }
            timer.Stop();
            UnitTest.PrintBlue(UnitTest.TEXT_TIME_COST + ": array |" + timer.ElapsedMilliseconds);

            timer.Restart();
            for (int i = 0; i < count; i++)
            {
                buffer[i] = Vector3.One;
            }
            timer.Stop();
            UnitTest.PrintBlue(UnitTest.TEXT_TIME_COST + ": StructuredBuffer |" + timer.ElapsedMilliseconds);

            timer.Restart();
            for (int i = 0; i < count; i++)
            {
                nativeBuffer[i] = Vector3.One;
            }
            timer.Stop();
            UnitTest.PrintBlue(UnitTest.TEXT_TIME_COST + ": NativeStructuredBuffer |" + timer.ElapsedMilliseconds);
            nativeBuffer.Dispose();
        }

        [Test("NativeStructuredBuffer Benchmark Modify")]
        public unsafe void BenchmarkModify()
        {
            int count = 50000000;

            Vector3[] array = new Vector3[count];
            StructuredBuffer<Vector3> buffer = new StructuredBuffer<Vector3>(count);
            NativeBuffer<Vector3> nativeBuffer = new NativeBuffer<Vector3>(count);

            Stopwatch timer = new Stopwatch();
            timer.Start();
            for (int i = 0; i < count; i++)
            {
                array[i].Y = 10;
            }
            timer.Stop();
            UnitTest.PrintBlue(UnitTest.TEXT_TIME_COST + ": array |" + timer.ElapsedMilliseconds);


            timer.Restart();
            for (int i = 0; i < count; i++)
            {
                buffer[i] = new Vector3(buffer[i].X, 10, buffer[i].Z);
            }
            timer.Stop();
            UnitTest.PrintBlue(UnitTest.TEXT_TIME_COST + ": StructuredBuffer |" + timer.ElapsedMilliseconds);

            timer.Restart();
            for (int i = 0; i < count; i++)
            {
                nativeBuffer.DataPtr[i].Y = 10;
            }
            timer.Stop();
            UnitTest.PrintBlue(UnitTest.TEXT_TIME_COST + ": NativeStructuredBuffer |" + timer.ElapsedMilliseconds);
        }
    }
}
