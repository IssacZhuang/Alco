using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alco
{
    /// <summary>
    /// Thrown when an array or list member has a size of zero.
    /// </summary>
    public sealed class EmptySizeException : Exception
    {
        /// <summary>
        /// Initializes the exception for the named member.
        /// </summary>
        /// <param name="memberName">The name of the empty member.</param>
        public EmptySizeException(string memberName) : base($"The size of array/list should not be 0 in '{memberName}'") { }
    }
}
