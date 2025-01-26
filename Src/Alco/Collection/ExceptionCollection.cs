using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alco
{
    public sealed class EmptySizeException : Exception
    {
        public EmptySizeException(string memberName) : base($"The size of array/list should not be 0 in '{memberName}'") { }
    }
}
