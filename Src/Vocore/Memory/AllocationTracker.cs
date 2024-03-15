using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;

namespace Vocore
{
    public static class AllocationTracker
    {
        public struct PointerInfo
        {
            public IntPtr pointer;
            public int size;
            public string stackTrace;

            public override string ToString()
            {
                return $"Address: {pointer:X16}, Size: {size}, StackTrace:\n{stackTrace}";
            }
        }

        private static readonly ConcurrentDictionary<IntPtr, PointerInfo> _allocated = new ConcurrentDictionary<IntPtr, PointerInfo>();

        public static void AddAllocated(IntPtr ptr, int size)
        {
            AddAllocated(ptr, size, Environment.StackTrace);
        }

        public static void AddAllocated(IntPtr ptr, int size, string stackTrace)
        {
            _allocated.TryAdd(ptr, new PointerInfo { pointer = ptr, size = size, stackTrace = stackTrace });
        }

        public static void Remove(IntPtr ptr)
        {
            _allocated.TryRemove(ptr, out _);
        }

        public static IEnumerable<PointerInfo> GetAllocated()
        {
            foreach (var item in _allocated)
            {
                yield return item.Value;
            }
        }

        public static IEnumerable<string> GetAllocatedStackTrace()
        {
            foreach (var item in _allocated)
            {
                yield return item.Value.ToString();
            }
        }

        public static void CheckAllocated(bool @throw = false)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            foreach (var item in GetAllocatedStackTrace())
            {
                if (@throw)
                {
                    throw new Exception($"Memory leak detected:\n {item}");
                }
                else
                {
                    Log.Error($"Memory leak detected:\n {item}");
                }
            }
        }
    }
}

