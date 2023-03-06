using System;
using System.Collections.Generic;
using Vocore.Serialization;

using UnityEngine;

namespace Vocore.Test
{
    internal class Test_UtilsParse
    {
        [Test("test UtilsParse.ToInt")]
        public void Test_ToInt(){
            string str = "123";
            int i = UtilsParse.ToInt(str);
            TestUtility.Assert(i != 123);
        }

        [Test("test UtilsParse.ToFloat")]
        public void Test_ToFloat(){
            string str = "123.456";
            float f = UtilsParse.ToFloat(str);
            TestUtility.Assert(f != 123.456f);
        }

        [Test("test UtilsParse.ToBool")]
        public void Test_ToBool(){
            string str = "true";
            bool b = UtilsParse.ToBool(str);
            TestUtility.Assert(b != true);
        }

        [Test("test UtilsParse.ToLong")]
        public void Test_ToLong(){
            string str = "1234567890";
            long l = UtilsParse.ToLong(str);
            TestUtility.Assert(l != 1234567890);
        }

        [Test("test UtilsParse.ToDouble")]
        public void Test_ToDouble(){
            string str = "123.4567890";
            double d = UtilsParse.ToDouble(str);
            TestUtility.Assert(d != 123.4567890);
        }

        [Test("test UtilsParse.ToSByte")]
        public void Test_ToSByte(){
            string str = "123";
            sbyte sb = UtilsParse.ToSByte(str);
            TestUtility.Assert(sb != 123);
        }

        [Test("test UtilsParse.ToByte")]
        public void Test_ToByte(){
            string str = "123";
            byte b = UtilsParse.ToByte(str);
            TestUtility.Assert(b != 123);
        }

        [Test("test UtilsParse.ToVector2")]
        public void Test_ToVector2(){
            string str = "(123.456,789.012)";
            Vector2 v = UtilsParse.ToVector2(str);
            TestUtility.Assert(v != new Vector2(123.456f, 789.012f));
        }

        [Test("test UtilsParse.ToVector3")]
        public void Test_ToVector3(){
            string str = "(123.456,789.012,345.678)";
            Vector3 v = UtilsParse.ToVector3(str);
            TestUtility.Assert(v != new Vector3(123.456f, 789.012f, 345.678f));
        }

        [Test("test UtilsParse.ToVector4Adaptive")]
        public void Test_ToVector4Adaptive(){
            string str = "(123.456,789.012,345.678,901.234)";
            Vector4 v = UtilsParse.ToVector4Adaptive(str);
            TestUtility.Assert(v != new Vector4(123.456f, 789.012f, 345.678f, 901.234f));
        }

        [Test("test UtilsParse.ToQuaternion")]
        public void Test_ToQuaternion(){
            string str = "(123.456,789.012,345.678,901.234)";
            Quaternion q = UtilsParse.ToQuaternion(str);
            TestUtility.Assert(q != new Quaternion(123.456f, 789.012f, 345.678f, 901.234f));
        }

        [Test("test UtilsParse.ToColor")]
        public void Test_ToColor(){
            string str = "(0,0,255,255)";
            Color c = UtilsParse.ToColor(str);
            TestUtility.Assert(c != Color.blue);

            str = "#FFFFFF";
            c = UtilsParse.ToColor(str);
            TestUtility.Assert(c != Color.white);
        }

        [Test("test UtilsParse.ToRect")]
        public void Test_ToRect(){
            string str = "(123.456,789.012,345.678,901.234)";
            Rect r = UtilsParse.ToRect(str);
            TestUtility.Assert(r != new Rect(123.456f, 789.012f, 345.678f, 901.234f));
        }


    }
}


