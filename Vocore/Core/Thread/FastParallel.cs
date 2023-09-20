using System;
using System.Threading;
using System.Threading.Tasks;


namespace Vocore
{
    public static class FastParallel
    {
        private struct Partition
        {
            public int start;
            public int end;
        }

        public static readonly int ThreadCount = Environment.ProcessorCount * 4;
        private static readonly Partition[] _partition = new Partition[ThreadCount];
        public static void For<T>(int fromInclusive, int toExclusive, T job) where T : unmanaged, IJobBatch
        {
            int count = toExclusive - fromInclusive;

            if (count < ThreadCount)
            {
                Parallel.For(0, count, job.Execute);
                return;
            }

            int step = count / ThreadCount;
            int remainder = count % ThreadCount;

            _partition[0].start = fromInclusive;
            _partition[0].end = fromInclusive + step + remainder;
            for (int i = 1; i < ThreadCount; i++)
            {
                _partition[i].start = _partition[i - 1].end;
                _partition[i].end = _partition[i].start + step;
            }


            Parallel.For(0, ThreadCount, (i) =>
            {
                int start = _partition[i].start;
                int end = _partition[i].end;
                for (int j = start; j < end; j++)
                {
                    job.Execute(j);
                }
            });
        }
    }
}