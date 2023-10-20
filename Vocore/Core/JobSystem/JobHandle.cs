using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Vocore{
    public readonly struct JobHandle
    {
        internal static ThreadSafePool<ManualResetEvent> ManualResetEventPool = new ThreadSafePool<ManualResetEvent>(Environment.ProcessorCount * 2, () => new ManualResetEvent(false));
        internal readonly ManualResetEvent _event;
        internal readonly bool _poolOnComplete;

        public JobHandle(ManualResetEvent @event, bool @poolOnComplete)
        {
            _event = @event;
            _poolOnComplete = @poolOnComplete;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Notify()
        {
            _event.Set();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Complete()
        {

            if (_poolOnComplete) throw new Exception("PoolOnComplete was set on JobHandle, therefore it returns automatically to the pool and you can not wait for its completion anymore since its handle might have been already reused.");
            _event.WaitOne();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return()
        {

            if (_poolOnComplete) throw new Exception("PoolOnComplete was set on JobHandle, therefore it returns automatically to the pool. Do not call Return on such a handle !");
            if (_event != null) ManualResetEventPool.Return(_event);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Complete(JobHandle[] handles)
        {

            for (var index = 0; index < handles.Length; index++)
            {
                ref var handle = ref handles[index];
                handle.Complete();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Complete(IList<JobHandle> handles)
        {

            for (var index = 0; index < handles.Count; index++)
            {
                var handle = handles[index];
                handle.Complete();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(JobHandle[] handles)
        {

            for (var index = 0; index < handles.Length; index++)
            {
                ref var handle = ref handles[index];
                handle.Return();
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(IList<JobHandle> handles)
        {

            for (var index = 0; index < handles.Count; index++)
            {
                var handle = handles[index];
                handle.Return();
            }
        }
    }
}


