using System;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Diagnostics.CodeAnalysis;

namespace Alco{
    /// <summary>
    /// Static logging facade that forwards messages to a configurable logger.
    /// </summary>
    public static class Log
    {
        private readonly static ThreadLocal<SpanStringBuilder> _builder = new ThreadLocal<SpanStringBuilder>(()=> new SpanStringBuilder());
        private static SpanStringBuilder Builder
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

        /// <summary>
        /// The logger that receives all messages; defaults to a console logger.
        /// </summary>
        public static ILogger Logger { get; set; } = new ConsoleLogger();

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Info(ReadOnlySpan<char> message)
        {
            Logger.Info(message);
        }

        /// <summary>
        /// Logs the string representation of a value as an informational message.
        /// </summary>
        /// <param name="message">The value to log.</param>
        public static void Info<T> (T message)
        {
            if (message == null)
            {
                Logger.Info(Null);
                return;
            }
            Logger.Info(message.ToString()??string.Empty);
        }

        /// <summary>
        /// Logs an informational message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        public static void Info<T1, T2> (T1 message1, T2 message2)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Logger.Info(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs an informational message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        /// <param name="message3">The third value to log.</param>
        public static void Info<T1, T2, T3> (T1 message1, T2 message2, T3 message3)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            Logger.Info(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs an informational message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        /// <param name="message3">The third value to log.</param>
        /// <param name="message4">The fourth value to log.</param>
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
            Logger.Info(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs an informational message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        /// <param name="message3">The third value to log.</param>
        /// <param name="message4">The fourth value to log.</param>
        /// <param name="message5">The fifth value to log.</param>
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
            Logger.Info(Builder.AsReadOnlySpan());
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

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Warning(ReadOnlySpan<char> message)
        {
            Logger.Warning(message);
        }

        /// <summary>
        /// Logs the string representation of a value as a warning message.
        /// </summary>
        /// <param name="message">The value to log.</param>
        public static void Warning<T> (T message)
        {
            if (message == null)
            {
                Logger.Warning(Null);
                return;
            }
            Logger.Warning(message.ToString()??string.Empty);
        }

        /// <summary>
        /// Logs a warning message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        public static void Warning<T1, T2> (T1 message1, T2 message2)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Logger.Warning(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs a warning message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        /// <param name="message3">The third value to log.</param>
        public static void Warning<T1, T2, T3> (T1 message1, T2 message2, T3 message3)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            Logger.Warning(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs a warning message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        /// <param name="message3">The third value to log.</param>
        /// <param name="message4">The fourth value to log.</param>
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
            Logger.Warning(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs a warning message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        /// <param name="message3">The third value to log.</param>
        /// <param name="message4">The fourth value to log.</param>
        /// <param name="message5">The fifth value to log.</param>
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
            Logger.Warning(Builder.AsReadOnlySpan());
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

        /// <summary>
        /// Logs an error message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Error(ReadOnlySpan<char> message)
        {
            Logger.Error(message);
        }

        /// <summary>
        /// Logs the string representation of a value as an error message.
        /// </summary>
        /// <param name="message">The value to log.</param>
        public static void Error<T> (T message)
        {
            if (message == null)
            {
                Logger.Error(Null);
                return;
            }
            Logger.Error(message.ToString()??string.Empty);
        }

        /// <summary>
        /// Logs an error message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        public static void Error<T1, T2> (T1 message1, T2 message2)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Logger.Error(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs an error message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        /// <param name="message3">The third value to log.</param>
        public static void Error<T1, T2, T3> (T1 message1, T2 message2, T3 message3)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            Logger.Error(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs an error message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        /// <param name="message3">The third value to log.</param>
        /// <param name="message4">The fourth value to log.</param>
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
            Logger.Error(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs an error message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        /// <param name="message3">The third value to log.</param>
        /// <param name="message4">The fourth value to log.</param>
        /// <param name="message5">The fifth value to log.</param>
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
            Logger.Error(Builder.AsReadOnlySpan());
        }


        /// <summary>
        /// Logs a success message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Success(ReadOnlySpan<char> message)
        {
            Logger.Success(message);
        }

        /// <summary>
        /// Logs the string representation of a value as a success message.
        /// </summary>
        /// <param name="message1">The value to log.</param>
        public static void Success<T1>(T1 message1)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Logger.Success(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs a success message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        public static void Success<T1, T2>(T1 message1, T2 message2)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Logger.Success(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs a success message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        /// <param name="message3">The third value to log.</param>
        public static void Success<T1, T2, T3>(T1 message1, T2 message2, T3 message3)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            Logger.Success(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs a success message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        /// <param name="message3">The third value to log.</param>
        /// <param name="message4">The fourth value to log.</param>
        public static void Success<T1, T2, T3, T4>(T1 message1, T2 message2, T3 message3, T4 message4)
        {
            Builder.Clear();
            Builder.Append(message1?.ToString());
            Builder.Append(" ");
            Builder.Append(message2?.ToString());
            Builder.Append(" ");
            Builder.Append(message3?.ToString());
            Builder.Append(" ");
            Builder.Append(message4?.ToString());
            Logger.Success(Builder.AsReadOnlySpan());
        }

        /// <summary>
        /// Logs a success message composed of multiple values separated by spaces.
        /// </summary>
        /// <param name="message1">The first value to log.</param>
        /// <param name="message2">The second value to log.</param>
        /// <param name="message3">The third value to log.</param>
        /// <param name="message4">The fourth value to log.</param>
        /// <param name="message5">The fifth value to log.</param>
        public static void Success<T1, T2, T3, T4, T5>(T1 message1, T2 message2, T3 message3, T4 message4, T5 message5)
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
            Logger.Success(Builder.AsReadOnlySpan());
        }
    }
}

