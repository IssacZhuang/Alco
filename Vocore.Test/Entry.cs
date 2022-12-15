using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Vocore.Test
{
    internal class Entry
    {
        static void Main(string[] args)
        {
            Assembly assembly = Assembly.GetAssembly(typeof(Entry));
            TestUtility.StartTest(assembly);
        }
    }
}
