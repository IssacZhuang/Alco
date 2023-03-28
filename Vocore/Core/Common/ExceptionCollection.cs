using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public static class ExceptionCollection
    {
        public readonly static Exception SizeIsEmpty = new Exception("The size of StructuredBuffer should not be 0");
        public readonly static Exception OutOfRange = new Exception("The index is out of range.");
    }
}
