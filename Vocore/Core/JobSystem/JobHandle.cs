using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Vocore{
    internal class InternalJobHandle
    {
        internal static readonly ThreadSafePool<InternalJobHandle> Pool = new ThreadSafePool<InternalJobHandle>(10000, ()=>{return new InternalJobHandle(false);});
        private bool _compelted;
        public InternalJobHandle(bool initalSate)
        {
            _compelted = initalSate;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetState(bool state)
        {
            Volatile.Write(ref _compelted, state);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WaitComplete()
        {
            while (!Volatile.Read(ref _compelted))
            {
            }
        }
    }
    public struct JobHandle
    {
        private InternalJobHandle _internalJobHandle;

        public JobHandle(bool initalSate)
        {
            _internalJobHandle = InternalJobHandle.Pool.Get();
            _internalJobHandle.SetState(initalSate);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Notify()
        {
            _internalJobHandle.SetState(true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Complete()
        {
            _internalJobHandle.WaitComplete();
            InternalJobHandle.Pool.Return(_internalJobHandle);
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
    }
}


