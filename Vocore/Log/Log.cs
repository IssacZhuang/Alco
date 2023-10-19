using System;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Vocore{
    public static class Log
    {
        private static ThreadLocal<StringBuilder> _builder = new ThreadLocal<StringBuilder>(()=> new StringBuilder());
        private static StringBuilder Builder
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _builder.Value;
            }
        }
        public const string ColorWhite = "#ffffff";
        public const string ColorRed = "#ff0000";
        public const string ColorGreen = "#00ff00";
        public const string ColorBlue = "#0000ff";
        public const string ColorYellow = "#ffff00";
        public const string ColorCyan = "#00ffff";
        public const string ColorMagenta = "#ff00ff";

        public static void Info(params object[] messages)
        {
            Builder.Clear();
            for (int i = 0; i < messages.Length; i++)
            {
                Builder.Append(messages[i]?.ToString());
                Builder.Append(" ");
            }
            ConsolePrint(Builder.ToString(), ConsoleColor.White);
        }

        public static void Warning(params object[] messages)
        {
            Builder.Clear();
            for (int i = 0; i < messages.Length; i++)
            {
                Builder.Append(messages[i]?.ToString());
                Builder.Append(" ");
            }
            ConsolePrint(Builder.ToString(), ConsoleColor.Yellow);
        }

        public static void Error(params object[] messages)
        {
            Builder.Clear();
            for (int i = 0; i < messages.Length; i++)
            {
                Builder.Append(messages[i]?.ToString());
                Builder.Append(" ");
            }
            ConsolePrint(Builder.ToString(), ConsoleColor.Red);
        }

        public static void Success(params object[] messages)
        {
            Builder.Clear();
            for (int i = 0; i < messages.Length; i++)
            {
                Builder.Append(messages[i]?.ToString());
                Builder.Append(" ");
            }
            ConsolePrint(Builder.ToString(), ConsoleColor.Green);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ConsolePrint(string str, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(str);
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}

