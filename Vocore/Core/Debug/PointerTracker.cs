using System;
using System.Collections.Generic;

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
                return $"Pointer: {Pointer} \nStackTrace: {StackTrace}\n\n";
            }
        }

        private static readonly HashSet<PointerInfo> _allocated = new HashSet<PointerInfo>();

        public static void AddAllocated(IntPtr ptr)
        {
            lock (_allocated)
            {
                string stackTrace = Environment.StackTrace;
                _allocated.Add(new PointerInfo { Pointer = ptr, StackTrace = stackTrace });
            }
        }

        public static void AddAllocated(IntPtr ptr, string stackTrace)
        {
            lock (_allocated)
            {
                _allocated.Add(new PointerInfo { Pointer = ptr, StackTrace = stackTrace });
            }
        }

        public static void Remove(IntPtr ptr)
        {
            _allocated.RemoveWhere(x => x.Pointer == ptr);
        }

        public static IEnumerable<PointerInfo> GetAllocated()
        {
            return _allocated;
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

