using System;
using System.Collections.Generic;
using Vocore;

namespace Vocore.Test
{
    [DisabledTestTemporarily]
    public class Test_StaticHashObject
    {
        [Test("object vs static hased object: add")]
        public void Test_Add()
        {
            int count = 1000000;
            var list = new object[count];
            var list2 = new object[count];
            for (int i = 0; i < count; i++)
            {
                list[i] = new object();
                list2[i] = new StaticHashObject();
            }
            var sw = new System.Diagnostics.Stopwatch();
            HashSet<object> set = new HashSet<object>(count);

            sw.Start();
            for (int i = 0; i < count; i++)
            {
                set.Add(list2[i]);
            }
            sw.Stop();
            TestUtility.PrintBlue("static hash object: " + sw.ElapsedMilliseconds);
            set.Clear();

            sw.Reset();
            sw.Start();
            for (int i = 0; i < count; i++)
            {
                set.Add(list[i]);
            }
            sw.Stop();
            TestUtility.PrintBlue("object: " + sw.ElapsedMilliseconds);
            set.Clear();

            //second time to get static hash
            sw.Reset();
            sw.Start();
            for (int i = 0; i < count; i++)
            {
                set.Add(list[i]);
            }
            sw.Stop();

            TestUtility.PrintBlue("static hash object second time: {0}" + sw.ElapsedMilliseconds);
            set.Clear();
        }

        [Test("object vs static hased object: query")]
        public void Test_Query(){
            int count = 1000000;
            var list = new object[count];
            var list2 = new object[count];
            for (int i = 0; i < count; i++)
            {
                list[i] = new object();
                list2[i] = new StaticHashObject();
            }
            var sw = new System.Diagnostics.Stopwatch();
            Dictionary<object, bool> dict1 = new Dictionary<object, bool>(count);
            Dictionary<object, bool> dict2 = new Dictionary<object, bool>(count);

            for (int i = 0; i < count; i++)
            {
                dict1.Add(list[i], true);
            }

            for (int i = 0; i < count; i++)
            {
                dict2.Add(list2[i], true);
            }

            sw.Start();
            for (int i = 0; i < count; i++)
            {
                dict2.ContainsKey(list2[i]);
            }
            sw.Stop();
            TestUtility.PrintBlue("static hash object: " + sw.ElapsedMilliseconds);


            sw.Reset();
            sw.Start();
            for (int i = 0; i < count; i++)
            {
                dict1.ContainsKey(list[i]);
            }
            sw.Stop();
            TestUtility.PrintBlue("object: " + sw.ElapsedMilliseconds);
        }
    }
}


