using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public static class ExceptionCollection
    {
        public readonly static Exception SizeIsEmpty = new Exception("The size of array/list should not be 0");
        public readonly static Exception OutOfRange = new Exception("The index is out of range.");
        public readonly static Exception LengthNotEqual = new Exception("The length of two arrays/lists are not equal.");
        public readonly static Exception Full = new Exception("Trying to add an element to a full collection.");
        public readonly static Exception Empty = new Exception("Trying to remove an element from an empty collection.");
        public static Exception Null(string field)
        {
            return new Exception($"The field {field} is null.");
        }

        public static Exception CommandBufferNotEnoughSize(int sizeNative, int sizeManaged)
        {
            return new Exception($"The size of command buffer is not enough. Native: {sizeNative}, Managed: {sizeManaged}");
        }
    }
}
