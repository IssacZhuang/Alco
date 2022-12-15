using System;
using System.Collections.Generic;
using System.Linq;
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
    }
}
