using System;
using System.Threading;
using System.Threading.Tasks;


namespace Vocore
{
    public static class FastParallel
    {
        public static int Parallelism = 32;

        public static void For(int fromInclusive, int toExclusive, Action<int> body)
        {
            int length = toExclusive - fromInclusive;
            int rangeSize = length / Parallelism;
            int remaining = length % Parallelism;
            
            long finished = 0;

            int start = 0;
            int end = 0;

            ThreadPool.SetMaxThreads(Parallelism, Parallelism);

            for (int i = 0; i < Parallelism; i++)
            {
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
                        Interlocked.Increment(ref finished);
                    }
                }, i, true);

                
            }

            while (Interlocked.Read(ref finished) < Parallelism)
            {
                Thread.Sleep(1);
            }
        }
    }
}