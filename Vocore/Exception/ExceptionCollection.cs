using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public static class ExceptionCollection
    {
        public readonly static Exception StructureSizeIsEmpty = new Exception("The size of StructuredBuffer should not be 0");
    }
}
