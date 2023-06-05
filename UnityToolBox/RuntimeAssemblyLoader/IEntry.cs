using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnityToolBox
{
    public interface IEntry
    {
        int ExecuteOder { get; }
        void Entry();
    }
}
