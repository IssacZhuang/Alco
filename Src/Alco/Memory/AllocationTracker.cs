using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;

namespace Alco
{
    /// <summary>
    /// Tracks unmanaged allocations by address for leak detection.
    /// </summary>
    public static class AllocationTracker
    {
        /// <summary>
        /// Describes a tracked unmanaged allocation.
        /// </summary>
        public struct PointerInfo
        {
            public IntPtr pointer;
            public long size;
            public string stackTrace;

            /// <summary>
            /// Returns the address, size, and allocation stack trace.
            /// </summary>
            /// <returns>A formatted description of the allocation.</returns>
            public override string ToString()
            {
                return $"Address: 0x{pointer:X16}, Size: {size}, StackTrace:\n{stackTrace}";
            }
        }

        private static readonly ConcurrentDictionary<IntPtr, PointerInfo> _allocated = new ConcurrentDictionary<IntPtr, PointerInfo>();

        /// <summary>
        /// Records an allocation using the current stack trace.
        /// </summary>
        /// <param name="ptr">The allocated memory address.</param>
        /// <param name="size">The allocation size in bytes.</param>
        public static void AddAllocated(IntPtr ptr, long size)
        {
            AddAllocated(ptr, size, Environment.StackTrace);
        }

        /// <summary>
        /// Records an allocation with the specified stack trace.
        /// </summary>
        /// <param name="ptr">The allocated memory address.</param>
        /// <param name="size">The allocation size in bytes.</param>
        /// <param name="stackTrace">The stack trace captured at allocation.</param>
        public static void AddAllocated(IntPtr ptr, long size, string stackTrace)
        {
            _allocated.TryAdd(ptr, new PointerInfo { pointer = ptr, size = size, stackTrace = stackTrace });
        }

        /// <summary>
        /// Stops tracking the allocation at the specified address.
        /// </summary>
        /// <param name="ptr">The allocated memory address.</param>
        public static void Remove(IntPtr ptr)
        {
            _allocated.TryRemove(ptr, out _);
        }

        /// <summary>
        /// Returns the currently tracked allocations.
        /// </summary>
        /// <returns>The tracked allocation records.</returns>
        public static IEnumerable<PointerInfo> GetAllocated()
        {
            foreach (var item in _allocated)
            {
                yield return item.Value;
            }
        }

        /// <summary>
        /// Returns formatted descriptions of the currently tracked allocations.
        /// </summary>
        /// <returns>A formatted description for each tracked allocation.</returns>
        public static IEnumerable<string> GetAllocatedStackTrace()
        {
            foreach (var item in _allocated)
            {
                yield return item.Value.ToString();
            }
        }

        /// <summary>
        /// Forces a full garbage collection and reports any allocations still tracked.
        /// </summary>
        /// <param name="throw">True to throw on the first detected leak; false to log it.</param>
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

