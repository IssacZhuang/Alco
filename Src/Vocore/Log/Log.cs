using System;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Diagnostics.CodeAnalysis;

namespace Vocore{
    public static class Log
    {
        private readonly static ThreadLocal<StringBuilder> _builder = new ThreadLocal<StringBuilder>(()=> new StringBuilder());
        private static StringBuilder Builder
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#pragma warning disable CS8603      
                return _builder.Value;
#pragma warning restore CS8603
            }
        }
        public const string ColorWhite = "#ffffff";
        public const string ColorRed = "#ff0000";
        public const string ColorGreen = "#00ff00";
        public const string ColorBlue = "#0000ff";
        public const string ColorYellow = "#ffff00";
        public const string ColorCyan = "#00ffff";
        public const string ColorMagenta = "#ff00ff";
        public const string Null = "null";

        // public static void Info(params object[] messages)
        // {
        //     Builder.Clear();
        //     for (int i = 0; i < messages.Length; i++)
        //     {
        //         Builder.Append(messages[i]?.ToString());
        //         Builder.Append(" ");
        //     }
        //     ConsolePrint(Builder.ToString(), ConsoleColor.White);
        // }

        public static void Info<T> (T message)
        {
            if (message == null)
            {
                ConsolePrint(Null, ConsoleColor.White);
                return;
            }
            ConsolePrint(message.ToString()??string.Empty, ConsoleColor.White);
        }

        public static void Info<T1, T2> (T1 message1, T2 message2)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            ConsolePrint(Builder.ToString(), ConsoleColor.White);
        }

        public static void Info<T1, T2, T3> (T1 message1, T2 message2, T3 message3)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            ConsolePrint(Builder.ToString(), ConsoleColor.White);
        }

        public static void Info<T1, T2, T3, T4> (T1 message1, T2 message2, T3 message3, T4 message4)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            Builder.Append(" ");
            Builder.Append(message4?.ToString());
            ConsolePrint(Builder.ToString(), ConsoleColor.White);
        }

        public static void Info<T1, T2, T3, T4, T5> (T1 message1, T2 message2, T3 message3, T4 message4, T5 message5)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            Builder.Append(" ");
            Builder.Append(message4?.ToString());
            Builder.Append(" ");
            Builder.Append(message5?.ToString());
            ConsolePrint(Builder.ToString(), ConsoleColor.White);
        }

        // public static void Warning(params object[] messages)
        // {
        //     Builder.Clear();
        //     for (int i = 0; i < messages.Length; i++)
        //     {
        //         Builder.Append(messages[i]?.ToString());
        //         Builder.Append(" ");
        //     }
        //     ConsolePrint(Builder.ToString(), ConsoleColor.Yellow);
        // }

        public static void Warning<T> (T message)
        {
            if (message == null)
            {
                ConsolePrint(Null, ConsoleColor.Yellow);
                return;
            }
            ConsolePrint(message.ToString()??string.Empty, ConsoleColor.Yellow);
        }

        public static void Warning<T1, T2> (T1 message1, T2 message2)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            ConsolePrint(Builder.ToString(), ConsoleColor.Yellow);
        }

        public static void Warning<T1, T2, T3> (T1 message1, T2 message2, T3 message3)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            ConsolePrint(Builder.ToString(), ConsoleColor.Yellow);
        }

        public static void Warning<T1, T2, T3, T4> (T1 message1, T2 message2, T3 message3, T4 message4)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            Builder.Append(" ");
            Builder.Append(message4?.ToString());
            ConsolePrint(Builder.ToString(), ConsoleColor.Yellow);
        }

        public static void Warning<T1, T2, T3, T4, T5> (T1 message1, T2 message2, T3 message3, T4 message4, T5 message5)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            Builder.Append(" ");
            Builder.Append(message4?.ToString());
            Builder.Append(" ");
            Builder.Append(message5?.ToString());
            ConsolePrint(Builder.ToString(), ConsoleColor.Yellow);
        }

        // public static void Error(params object[] messages)
        // {
        //     Builder.Clear();
        //     for (int i = 0; i < messages.Length; i++)
        //     {
        //         Builder.Append(messages[i]?.ToString());
        //         Builder.Append(" ");
        //     }
        //     ConsolePrint(Builder.ToString(), ConsoleColor.Red);
        // }

        public static void Error<T> (T message)
        {
            if (message == null)
            {
                ConsolePrint(Null, ConsoleColor.Red);
                return;
            }
            ConsolePrint(message.ToString()??string.Empty, ConsoleColor.Red);
        }

        public static void Error<T1, T2> (T1 message1, T2 message2)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            ConsolePrint(Builder.ToString(), ConsoleColor.Red);
        }

        public static void Error<T1, T2, T3> (T1 message1, T2 message2, T3 message3)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            ConsolePrint(Builder.ToString(), ConsoleColor.Red);
        }

        public static void Error<T1, T2, T3, T4> (T1 message1, T2 message2, T3 message3, T4 message4)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            Builder.Append(" ");
            Builder.Append(message4?.ToString());
            ConsolePrint(Builder.ToString(), ConsoleColor.Red);
        }

        public static void Error<T1, T2, T3, T4, T5> (T1 message1, T2 message2, T3 message3, T4 message4, T5 message5)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            Builder.Append(" ");
            Builder.Append(message4?.ToString());
            Builder.Append(" ");
            Builder.Append(message5?.ToString());
            ConsolePrint(Builder.ToString(), ConsoleColor.Red);
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

