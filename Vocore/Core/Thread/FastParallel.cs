using System;
using System.Threading;
using System.Threading.Tasks;


namespace Vocore
{
    public static class FastParallel
    {
        public static int Parallelism = 8;

        public static void For(int fromInclusive, int toExclusive, Action<int> body)
        {
            int length = toExclusive - fromInclusive;
            int rangeSize = length / Parallelism;
            int remaining = length % Parallelism;
            ManualResetEvent[] resetEvents = new ManualResetEvent[Parallelism];

            int start = 0;
            int end = 0;

            for (int i = 0; i < Parallelism; i++)
            {
                resetEvents[i] = new ManualResetEvent(false);

                start = end;
                end += rangeSize;

                if (remaining > 0)
                {
                    end++;
                    remaining--;
                }

                ThreadPool.QueueUserWorkItem<int>(state =>
                {
                    try
                    {
                        for (int j = start; j < end; j++)
                        {
                            body(j);
                        }
                    }
                    finally
                    {
                        resetEvents[state].Set();
                    }
                }, i, true);

                
            }

            WaitHandle.WaitAll(resetEvents);
        }
    }
}