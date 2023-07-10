using System;
using System.Collections.Generic;
using Vocore;

using UnityEngine;
using Unity.Mathematics;

namespace Vocore.Test
{
    internal class Test_UtilsParse
    {
        ParseHelper _parseHelper = ParseHelper.Default;

        [Test("test UtilsParse.ToInt")]
        public void Test_ToInt()
        {
            string str = "123";
            int i = _parseHelper.ToInt(str);
            TestHelper.Assert(i != 123);
        }

        [Test("test UtilsParse.ToFloat")]
        public void Test_ToFloat()
        {
            string str = "123.456";
            float f = _parseHelper.ToFloat(str);
            TestHelper.Assert(f != 123.456f);
        }

        [Test("test UtilsParse.ToBool")]
        public void Test_ToBool()
        {
            string str = "true";
            bool b = _parseHelper.ToBool(str);
            TestHelper.Assert(b != true);
        }

        [Test("test UtilsParse.ToLong")]
        public void Test_ToLong()
        {
            string str = "1234567890";
            long l = _parseHelper.ToLong(str);
            TestHelper.Assert(l != 1234567890);
        }

        [Test("test UtilsParse.ToDouble")]
        public void Test_ToDouble()
        {
            string str = "123.4567890";
            double d = _parseHelper.ToDouble(str);
            TestHelper.Assert(d != 123.4567890);
        }

        [Test("test UtilsParse.ToSByte")]
        public void Test_ToSByte()
        {
            string str = "123";
            sbyte sb = _parseHelper.ToSByte(str);
            TestHelper.Assert(sb != 123);
        }

        [Test("test UtilsParse.ToByte")]
        public void Test_ToByte()
        {
            string str = "123";
            byte b = _parseHelper.ToByte(str);
            TestHelper.Assert(b != 123);
        }

        [Test("test UtilsParse.ToVector2")]
        public void Test_ToVector2()
        {
            string str = "(123.456,789.012)";
            float2 v = _parseHelper.ToFloat2(str);
            TestHelper.Assert(!v.Equals(new float2(123.456f, 789.012f)));
        }

        [Test("test UtilsParse.ToVector3")]
        public void Test_ToVector3()
        {
            string str = "(123.456,789.012,345.678)";
            float3 v = _parseHelper.ToFloat3(str);
            TestHelper.Assert(!v.Equals(new float3(123.456f, 789.012f, 345.678f)));
        }

        [Test("test UtilsParse.ToVector4Adaptive")]
        public void Test_ToVector4Adaptive()
        {
            string str = "(123.456,789.012,345.678,901.234)";
            float4 v = _parseHelper.ToFloat4Adaptive(str);
            TestHelper.Assert(!v.Equals(new float4(123.456f, 789.012f, 345.678f, 901.234f)));
        }

        [Test("test UtilsParse.ToQuaternion")]
        public void Test_ToQuaternion()
        {
            string str = "(123.456,789.012,345.678,901.234)";
            quaternion q = _parseHelper.ToQuaternion(str);
            TestHelper.Assert(!q.Equals(new quaternion(123.456f, 789.012f, 345.678f, 901.234f)));
        }

        [Test("test UtilsParse.ToColor")]
        public void Test_ToColor()
        {
            string str = "(0,0,255,255)";
            Color c = _parseHelper.ToColor(str);
            TestHelper.Assert(c != Color.blue);

            str = "#FFFFFF";
            c = _parseHelper.ToColor(str);
            TestHelper.Assert(c != Color.white);
        }

        [Test("test UtilsParse.ToRect")]
        public void Test_ToRect()
        {
            string str = "(123.456,789.012,345.678,901.234)";
            Rect r = _parseHelper.ToRect(str);
            TestHelper.Assert(r != new Rect(123.456f, 789.012f, 345.678f, 901.234f));
        }


    }
}


