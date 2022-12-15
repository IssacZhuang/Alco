using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Vocore.Test
{
    public static class TestUtility
    {
        private static int _counterFailed = 0;
        private static int _counterSuccess = 0;

        public static void ResetCounter()
        {
            _counterFailed = 0;
            _counterSuccess = 0;
        }

        public static void AddFailed()
        {
            _counterFailed++;
        }
        public static void AddSuccess()
        {
            _counterSuccess++;
        }

        public static void DisplayCounter()
        {
            if (_counterFailed > 0) PrintRed("Failed: " + _counterFailed);
            if (_counterSuccess > 0) PrintGreen("Success: " + _counterSuccess);
        }


        public static void Print(object obj)
        {
            Console.WriteLine(obj);
        }

        public static void PrintRed(object obj)
        {
            PrintColor(obj, ConsoleColor.Red);
        }

        public static void PrintGreen(object obj)
        {
            PrintColor(obj, ConsoleColor.Green);
        }
        public static void PrintGray(object obj)
        {
            PrintColor(obj, ConsoleColor.Gray);
        }

        public static void PrintColor(object obj ,ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(obj.ToString());
            Console.ForegroundColor = ConsoleColor.White;
        }

        public static void Assert(bool condition, string failed = "Failed", string success = "Success")
        {
            if (condition)
            {
                AddFailed();
                PrintRed(failed);
            }
            else
            {
                AddSuccess();
                PrintGreen(success);
            }
        }

        public static void StartTest(Assembly assembly)
        {
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
                        if (obj != null) TryInvokeTestForObj(obj, typeInfo);
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

        public static void TryInvokeTestForObj(object obj, TypeInfo typeInfo)
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
