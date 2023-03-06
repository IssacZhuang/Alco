using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Vocore.Test
{
    public class Test : Attribute
    {
        public string Name { get; private set; }
        public bool ExpectError { get; private set; }

        public Test()
        {
            this.Name = "Test";
            ExpectError = false;
        }

        public Test(string testName)
        {
            this.Name = testName;
            ExpectError = false;
        }

        public Test(string testName, bool expectError)
        {
            this.Name = testName;
            this.ExpectError = expectError;
        }
    }

    public class DisabledTestTemporarily : Attribute
    {
        public DisabledTestTemporarily()
        {
        }
    }

    public static class TestUtility
    {
        public static string TEXT_BENCHMARK = "Benchmark: ";
        public static string TEXT_TIME_COST = "Time cost: ";
        public static string TAB_SPACE = "   ";

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

        public static string DumpToString(this object obj, string prefix = "", int recursion = 2)
        {
            if (recursion < 0)
            {
                return "...";
            }

            if (obj == null)
            {
                return "null";
            }

            //iterate through all field of obj
            StringBuilder sb = new StringBuilder();
            foreach (FieldInfo field in obj.GetType().GetFields())
            {
                sb.Append(prefix + field.Name + ": " + field.GetValue(obj) + "\n");
                // if (field.GetType()!=typeof(string)&&field.GetType().IsClass)
                // {
                //     sb.Append(DumpToString(field.GetValue(obj), prefix + TAB_SPACE, recursion - 1) + "\n");
                // }
            }
            return sb.ToString();
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
        public static void PrintBlue(object obj)
        {
            PrintColor(obj, ConsoleColor.Blue);
        }

        public static void PrintColor(object obj, ConsoleColor color)
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
                object obj;
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
        }

        public static void StartTest(Type type)
        {
            TestUtility.ResetCounter();

            object obj;
            try
            {
                obj = Activator.CreateInstance(type);
                try
                {
                    if (obj != null) TryInvokeTestForObj(obj, type.GetTypeInfo());
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
            catch (Exception) { }


            Console.WriteLine("Test Finished");
            TestUtility.DisplayCounter();
            Console.ReadLine();
        }

        public static void TryInvokeTestForObj(object obj, TypeInfo typeInfo)
        {
            if (typeInfo.GetCustomAttribute<DisabledTestTemporarily>() != null)
            {
                return;
            }

            foreach (MethodInfo method in typeInfo.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                Test testAttr = method.GetCustomAttribute<Test>();
                if (testAttr == null) continue;
                try
                {
                    TestUtility.PrintGray("------" + testAttr.Name + " | started:");
                    method.Invoke(obj, null);
                    TestUtility.PrintGray("----Test finished.\n");
                }
                catch (Exception e)
                {
                    if (testAttr.ExpectError)
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
