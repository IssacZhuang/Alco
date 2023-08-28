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
            int i = _parseHelper.StrToInt(str);
            TestHelper.AssertFalse(i != 123);
            TestHelper.AssertFalse(str != _parseHelper.IntToStr(i));
        }

        [Test("test UtilsParse.ToFloat")]
        public void Test_ToFloat()
        {
            string str = "123.456";
            float f = _parseHelper.StrToFloat(str);
            TestHelper.AssertFalse(f != 123.456f);
            TestHelper.AssertFalse(str != _parseHelper.FloatToStr(f));
        }

        [Test("test UtilsParse.ToBool")]
        public void Test_ToBool()
        {
            string str = "True";
            bool b = _parseHelper.StrToBool(str);
            TestHelper.AssertFalse(b != true);
            TestHelper.AssertFalse(str != _parseHelper.BoolToStr(b));
        }

        [Test("test UtilsParse.ToLong")]
        public void Test_ToLong()
        {
            string str = "1234567890";
            long l = _parseHelper.StrToLong(str);
            TestHelper.AssertFalse(l != 1234567890);
            TestHelper.AssertFalse(str != _parseHelper.LongToStr(l));
        }

        [Test("test UtilsParse.ToDouble")]
        public void Test_ToDouble()
        {
            string str = "123.456789";
            double d = _parseHelper.StrToDouble(str);
            TestHelper.AssertFalse(d != 123.4567890);
            TestHelper.AssertFalse(str != _parseHelper.DoubleToStr(d));
        }

        [Test("test UtilsParse.ToSByte")]
        public void Test_ToSByte()
        {
            string str = "123";
            sbyte sb = _parseHelper.StrToSByte(str);
            TestHelper.AssertFalse(sb != 123);
            TestHelper.AssertFalse(str != _parseHelper.SByteToStr(sb));
        }

        [Test("test UtilsParse.ToByte")]
        public void Test_ToByte()
        {
            string str = "123";
            byte b = _parseHelper.StrToByte(str);
            TestHelper.AssertFalse(b != 123);
            TestHelper.AssertFalse(str != _parseHelper.ByteToStr(b));
        }

        [Test("test UtilsParse.ToVector2")]
        public void Test_ToVector2()
        {
            string str = "(123.456,789.012)";
            float2 v = _parseHelper.StrToFloat2(str);
            TestHelper.AssertFalse(!v.Equals(new float2(123.456f, 789.012f)));
            TestHelper.AssertFalse(str != _parseHelper.Float2ToStr(v));
        }

        [Test("test UtilsParse.ToVector3")]
        public void Test_ToVector3()
        {
            string str = "(123.456,789.012,345.678)";
            float3 v = _parseHelper.StrToFloat3(str);
            TestHelper.AssertFalse(!v.Equals(new float3(123.456f, 789.012f, 345.678f)));
            TestHelper.AssertFalse(str != _parseHelper.Float3ToStr(v));
        }

        [Test("test UtilsParse.ToVector4Adaptive")]
        public void Test_ToVector4Adaptive()
        {
            string str = "(123.456,789.012,345.678,901.234)";
            float4 v = _parseHelper.StrToFloat4Adaptive(str);
            TestHelper.AssertFalse(!v.Equals(new float4(123.456f, 789.012f, 345.678f, 901.234f)));
            TestHelper.AssertFalse(str != _parseHelper.Float4ToStr(v));
        }

        [Test("test UtilsParse.ToQuaternion")]
        public void Test_ToQuaternion()
        {
            string str = "(123.456,789.012,345.678,901.234)";
            quaternion q = _parseHelper.StrToQuaternion(str);
            TestHelper.AssertFalse(!q.Equals(new quaternion(123.456f, 789.012f, 345.678f, 901.234f)));
            TestHelper.AssertFalse(str != _parseHelper.QuaternionToStr(q));
        }

        [Test("test UtilsParse.ToColor")]
        public void Test_ToColor()
        {
            string str = "(0,0,255,255)";
            Color c = _parseHelper.StrToColor(str);
            TestHelper.AssertFalse(c != Color.blue);

            str = "#FFFFFFFF";
            c = _parseHelper.StrToColor(str);
            TestHelper.AssertFalse(c != Color.white);
            TestHelper.AssertFalse(str != _parseHelper.ColorToHexStr(c));
        }

        [Test("test UtilsParse.ToRect")]
        public void Test_ToRect()
        {
            string str = "(123.456,789.012,345.678,901.234)";
            Rect r = _parseHelper.StrToRect(str);
            TestHelper.AssertFalse(r != new Rect(123.456f, 789.012f, 345.678f, 901.234f));
            TestHelper.AssertFalse(str != _parseHelper.RectToStr(r));
        }

        [Test("test UtilsParse.ToType")]
        public void Test_ToType()
        {
            string str = "System.Int32";
            Type t = _parseHelper.StrToType(str);
            TestHelper.AssertFalse(t != typeof(int));
            TestHelper.AssertFalse(str != _parseHelper.TypeToStr(t));
        }


    }
}


