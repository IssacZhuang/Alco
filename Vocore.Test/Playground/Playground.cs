using Vocore;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Vocore.Test
{
    public class Playground
    {
        //some temp code for testing
        //[Test("Playground")]
        public void Test()
        {
            //compare query speed of list and hashset
            LinkedList<int> list = new LinkedList<int>();
            HashSet<int> hashset = new HashSet<int>();

            int count = 10000000;
            for (int i = 0; i < count; i++)
            {
                list.AddLast(i);
                hashset.Add(i);
            }

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            foreach (var i in list)
            {
                var tmp = i*2;
            }

            stopwatch.Stop();
            TestUtility.PrintBlue("list: " + stopwatch.ElapsedMilliseconds);

            stopwatch.Restart();
            foreach (var i in hashset)
            {
                var tmp = i*2;
            }

            stopwatch.Stop();
            TestUtility.PrintBlue("hashset: " + stopwatch.ElapsedMilliseconds);

        }
    }

}

