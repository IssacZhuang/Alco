using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

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

    public static class TestHelper
    {
        public const string TEXT_BENCHMARK = "Benchmark: ";
        public const string TEXT_TIME_COST = "Time cost: ";
        public const string TEXT_FAILED = "Failed";
        public const string TEXT_SUCCESS = "Success";
        public const string TEXT_TEST_FAILED = "Failed: ";
        public const string TEXT_TEST_SUCCESS = "Success: ";
        public const string TEXT_TEST_FINISHED = "Test finished";

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
            if (_counterFailed > 0) PrintRed(TEXT_TEST_FAILED + _counterFailed);
            if (_counterSuccess > 0) PrintGreen(TEXT_TEST_SUCCESS + _counterSuccess);
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

        public static void Assert(bool condition, string failed = TEXT_FAILED, string success = TEXT_SUCCESS)
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

        public static void Benchmark(Action action, string name = TEXT_BENCHMARK)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            action();
            stopwatch.Stop();
            PrintBlue(name + ": " + stopwatch.ElapsedMilliseconds + " ms");
        }

        public static void Benchmark(string name, Action action)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            action();
            stopwatch.Stop();
            PrintBlue(name + ": " + stopwatch.ElapsedMilliseconds + " ms");
        }

        public static void CheckGCAlloc(Action action, string name = TEXT_BENCHMARK)
        {
            GC.Collect();
            long start = GC.GetTotalMemory(true);
            action();
            long end = GC.GetTotalMemory(false);
            PrintBlue(name + ": " + (end - start) + " bytes");
        }

        public static void CheckGCAlloc(string name, Action action)
        {
            GC.Collect();
            long start = GC.GetTotalMemory(true);
            action();
            long end = GC.GetTotalMemory(false);
            PrintBlue(name + ": " + (end - start) + " bytes");
        }

        public static void PrintList<T>(IList<T> list)
        {
            for(int i = 0; i < list.Count; i++)
            {
                Print(i + ": " + list[i]);
            }
        }

        public static void PrintArray<T>(T[] array)
        {
            for (int i = 0; i < array.Length; i++)
            {
                Print(i + ": " + array[i]);
            }
        }

        public static void PrintEnumerable<T>(IEnumerable<T> enumerable)
        {
            int i = 0;
            foreach (T item in enumerable)
            {
                Print(i + ": " + item);
                i++;
            }
        }

        public static void StartTest(Assembly assembly)
        {
            TestHelper.ResetCounter();
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

            Console.WriteLine(TEXT_TEST_FINISHED);
            TestHelper.DisplayCounter();
        }

        public static void StartTest(Type type)
        {
            TestHelper.ResetCounter();

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


            Console.WriteLine(TEXT_TEST_FINISHED);
            TestHelper.DisplayCounter();
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
                    TestHelper.PrintGray("------" + testAttr.Name + " | started:");
                    method.Invoke(obj, null);
                    TestHelper.PrintGray("----Test finished.\n");
                }
                catch (Exception e)
                {
                    if (testAttr.ExpectError)
                    {
                        TestHelper.PrintGreen("An error is occurred as expected");
                        TestHelper.AddSuccess();
                        TestHelper.PrintGray("----Test finished.\n");
                    }
                    else
                    {
                        TestHelper.PrintRed(e.InnerException);
                        TestHelper.AddFailed();
                        TestHelper.PrintGray("----Test failed.\n");
                    }
                }
            }
        }
    }
}
