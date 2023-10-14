using System;
using System.Text;
using System.Runtime.CompilerServices;

namespace Vocore{
    public static class Log
    {
        [ThreadStatic]
        private static StringBuilder _builder = new StringBuilder();
        public const string ColorWhite = "#ffffff";
        public const string ColorRed = "#ff0000";
        public const string ColorGreen = "#00ff00";
        public const string ColorBlue = "#0000ff";
        public const string ColorYellow = "#ffff00";
        public const string ColorCyan = "#00ffff";
        public const string ColorMagenta = "#ff00ff";

        public static void Info(params object[] messages)
        {
            _builder.Clear();
            for (int i = 0; i < messages.Length; i++)
            {
                _builder.Append(messages[i]?.ToString());
                _builder.Append(" ");
            }
            ConsolePrint(_builder.ToString(), ConsoleColor.White);
        }

        public static void Warning(params object[] messages)
        {
            _builder.Clear();
            for (int i = 0; i < messages.Length; i++)
            {
                _builder.Append(messages[i]?.ToString());
                _builder.Append(" ");
            }
            ConsolePrint(_builder.ToString(), ConsoleColor.Yellow);
        }

        public static void Error(params object[] messages)
        {
            _builder.Clear();
            for (int i = 0; i < messages.Length; i++)
            {
                _builder.Append(messages[i]?.ToString());
                _builder.Append(" ");
            }
            ConsolePrint(_builder.ToString(), ConsoleColor.Red);
        }

        public static void Success(params object[] messages)
        {
            _builder.Clear();
            for (int i = 0; i < messages.Length; i++)
            {
                _builder.Append(messages[i]?.ToString());
                _builder.Append(" ");
            }
            ConsolePrint(_builder.ToString(), ConsoleColor.Green);
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

