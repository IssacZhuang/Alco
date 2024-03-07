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
        public readonly static Exception LengthNotEqual = new Exception("The length of two arrays/lists are not equal.");
    }
}
