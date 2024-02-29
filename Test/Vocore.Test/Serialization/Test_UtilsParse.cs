using System;
using System.Numerics;
using System.Collections.Generic;
using Vocore;


namespace Vocore.Test
{
    internal class Test_UtilsParse
    {
        ParseHelper _parseHelper = ParseHelper.Default;

        [Test(Description = "UtilsParse.ToInt")]
        public void Test_ToInt()
        {
            string str = "123";
            int i = _parseHelper.StrToInt(str);
            Assert.IsFalse(i != 123);
            Assert.IsFalse(str != _parseHelper.IntToStr(i));
        }

        [Test(Description = "UtilsParse.ToFloat")]
        public void Test_ToFloat()
        {
            string str = "123.456";
            float f = _parseHelper.StrToFloat(str);
            Assert.IsFalse(f != 123.456f);
            Assert.IsFalse(str != _parseHelper.FloatToStr(f));
        }

        [Test(Description = "UtilsParse.ToBool")]
        public void Test_ToBool()
        {
            string str = "True";
            bool b = _parseHelper.StrToBool(str);
            Assert.IsFalse(b != true);
            Assert.IsFalse(str != _parseHelper.BoolToStr(b));
        }

        [Test(Description = "UtilsParse.ToLong")]
        public void Test_ToLong()
        {
            string str = "1234567890";
            long l = _parseHelper.StrToLong(str);
            Assert.IsFalse(l != 1234567890);
            Assert.IsFalse(str != _parseHelper.LongToStr(l));
        }

        [Test(Description = "UtilsParse.ToDouble")]
        public void Test_ToDouble()
        {
            string str = "123.456789";
            double d = _parseHelper.StrToDouble(str);
            Assert.IsFalse(d != 123.4567890);
            Assert.IsFalse(str != _parseHelper.DoubleToStr(d));
        }

        [Test(Description = "UtilsParse.ToSByte")]
        public void Test_ToSByte()
        {
            string str = "123";
            sbyte sb = _parseHelper.StrToSByte(str);
            Assert.IsFalse(sb != 123);
            Assert.IsFalse(str != _parseHelper.SByteToStr(sb));
        }

        [Test(Description = "UtilsParse.ToByte")]
        public void Test_ToByte()
        {
            string str = "123";
            byte b = _parseHelper.StrToByte(str);
            Assert.IsFalse(b != 123);
            Assert.IsFalse(str != _parseHelper.ByteToStr(b));
        }

        [Test(Description = "UtilsParse.ToVector2")]
        public void Test_ToVector2()
        {
            string str = "(123.456,789.012)";
            Vector2 v = _parseHelper.StrToFloat2(str);
            Assert.IsFalse(!v.Equals(new Vector2(123.456f, 789.012f)));
            Assert.IsFalse(str != _parseHelper.Float2ToStr(v));
        }

        [Test(Description = "UtilsParse.ToVector3")]
        public void Test_ToVector3()
        {
            string str = "(123.456,789.012,345.678)";
            Vector3 v = _parseHelper.StrToVector3(str);
            Assert.IsFalse(!v.Equals(new Vector3(123.456f, 789.012f, 345.678f)));
            Assert.IsFalse(str != _parseHelper.Vector3ToStr(v));
        }

        [Test(Description = "UtilsParse.ToVector4Adaptive")]
        public void Test_ToVector4Adaptive()
        {
            string str = "(123.456,789.012,345.678,901.234)";
            Vector4 v = _parseHelper.StrToFloat4Adaptive(str);
            Assert.IsFalse(!v.Equals(new Vector4(123.456f, 789.012f, 345.678f, 901.234f)));
            Assert.IsFalse(str != _parseHelper.Float4ToStr(v));
        }

        [Test(Description = "UtilsParse.ToQuaternion")]
        public void Test_ToQuaternion()
        {
            string str = "(123.456,789.012,345.678,901.234)";
            Quaternion q = _parseHelper.StrToQuaternion(str);
            Assert.IsFalse(!q.Equals(new Quaternion(123.456f, 789.012f, 345.678f, 901.234f)));
            Assert.IsFalse(str != _parseHelper.QuaternionToStr(q));
        }

        [Test(Description = "UtilsParse.ToType")]
        public void Test_ToType()
        {
            string str = "System.Int32";
            Type t = _parseHelper.StrToType(str);
            Assert.IsFalse(t != typeof(int));
            Assert.IsFalse(str != _parseHelper.TypeToStr(t));
        }


    }
}


