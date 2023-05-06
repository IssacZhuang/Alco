using System;
using System.Reflection;
using Vocore;

namespace Vocore.Test
{
    internal class Entry
    {
        static void Main(string[] _)
        {
            Assembly assembly = Assembly.GetAssembly(typeof(Entry));
            TestHelper.StartTest(assembly);
            //PointerTracker.Instance.DisplayResult();
            foreach (var item in PointerTracker.GetAllocatedStackTrace())
            {
                TestHelper.PrintRed(item);
            }

            Console.ReadLine();

        }
    }
}
