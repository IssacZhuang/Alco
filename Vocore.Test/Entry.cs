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
            TestUtility.ResetCounter();
            foreach (TypeInfo typeInfo in assembly.DefinedTypes)
            {
                //Console.WriteLine(typeInfo.FullName);
                object obj = null;
                try
                {
                    obj = Activator.CreateInstance(typeInfo);
                    try
                    {
                        if (obj != null) TryInvokeTest(obj, typeInfo);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                catch (Exception) { }
            }

            Console.WriteLine("Test Finished");
            TestUtility.DisplayCounter();
            Console.ReadLine();
        }

        public static void TryInvokeTest(object obj, TypeInfo typeInfo)
        {
            foreach (MethodInfo method in typeInfo.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                Test testAttr = method.GetCustomAttribute<Test>();
                if (testAttr == null) continue;
                try
                {
                    TestUtility.PrintGray("------" + testAttr.name + " | started:");
                    method.Invoke(obj, null);
                    TestUtility.PrintGray("----Test finished.\n");
                }
                catch (Exception e)
                {
                    if (testAttr.expectError)
                    {
                        TestUtility.PrintGreen("An error is occurred as expected");
                        TestUtility.AddSuccess();
                        TestUtility.PrintGray("----Test finished.\n");
                    }
                    else
                    {
                        TestUtility.PrintRed(e.InnerException);
                        TestUtility.AddFailed();
                        TestUtility.PrintGray("----Test failed.\n");
                    }
                }
            }
        }
    }
}
