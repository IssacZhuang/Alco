using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;

namespace Vocore
{
    public static class PointerTracker
    {
        public struct PointerInfo
        {
            public IntPtr Pointer;
            public string StackTrace;

            public override string ToString()
            {
                return $"Pointer: {Pointer}, StackTrace: {StackTrace}";
            }
        }

        private static readonly ConcurrentDictionary<IntPtr, string> _allocated = new ConcurrentDictionary<IntPtr, string>();

        public static void AddAllocated(IntPtr ptr)
        {
            AddAllocated(ptr, Environment.StackTrace);
        }

        public static void AddAllocated(IntPtr ptr, string stackTrace)
        {
            _allocated.TryAdd(ptr, stackTrace);
        }

        public static void Remove(IntPtr ptr)
        {
            _allocated.TryRemove(ptr, out _);
        }

        public static IEnumerable<PointerInfo> GetAllocated()
        {
            foreach (var item in _allocated)
            {
                yield return new PointerInfo { Pointer = item.Key, StackTrace = item.Value };
            }
        }

        public static IEnumerable<string> GetAllocatedStackTrace()
        {
            foreach (var item in _allocated)
            {
                yield return item.ToString();
            }
        }
    }
}

