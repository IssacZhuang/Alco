using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Vocore;

using System.Diagnostics;

namespace Vocore.Test
{
    
    internal class Test_Parallel
    {
        [Test("Parallel For")]
        public void ParallelFor()
        {
            int count = 10000000;

            Stopwatch timer = new Stopwatch();
            float reuslt = 0;
            timer.Start();
            object objLock = new object();
            for (int i = 0; i < count; i++)
            {
                reuslt = i / 3 / 3 / 3 / 7 / 7 / 7;
            }
            timer.Stop();
            TestUtility.PrintBlue(TestUtility.TEXT_TIME_COST + ": for |" + timer.ElapsedMilliseconds);

            timer.Restart();
            Parallel.For(0, count, (i) =>
            {
                lock (objLock)
                {
                    reuslt = i / 3 / 3 / 3 / 7 / 7 / 7;
                }
            });
            timer.Stop();
            TestUtility.PrintBlue(TestUtility.TEXT_TIME_COST + ": Parallel.For |" + timer.ElapsedMilliseconds);
        }
    }

}

