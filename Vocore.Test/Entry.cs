using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Vocore.Test
{
    internal class Entry
    {
        static void Main(string[] _)
        {
            PointerTracker.StaticInitialize();
            Assembly assembly = Assembly.GetAssembly(typeof(Entry));
            TestUtility.StartTest(assembly);
            //PointerTracker.Instance.DisplayResult();
            Console.ReadLine();
        }
    }
}
