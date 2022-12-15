using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore.Test
{
    public static class TestUtility
    {
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
    }
}
