using System;
using System.Text;
using System.Runtime.CompilerServices;

using UnityEngine;

public static class Log
{
    [ThreadStatic]
    private static StringBuilder _builder = new StringBuilder();
    private static Action<string> _logInfo;
    private static Action<string> _logError;
    private static Action<string> _logWarning;
    private static string ColorWhite = "#ffffff";
    private static string ColorRed = "#ff0000";
    private static string ColorGreen = "#00ff00";
    private static string ColorBlue = "#0000ff";
    private static string ColorYellow = "#ffff00";
    private static string ColorCyan = "#00ffff";
    private static string ColorMagenta = "#ff00ff";

    static Log()
    {
        LoadPresetConsole();
    }


    public static void SetLogCallback(Action<string> logInfo, Action<string> logError, Action<string> logWarning)
    {
        _logError = logError;
        _logWarning = logWarning;
        _logInfo = logInfo;
    }

    public static void SetLogInfoCallback(Action<string> logInfo)
    {
        _logInfo = logInfo;
    }

    public static void SetLogErrorCallback(Action<string> logError)
    {
        _logError = logError;
    }

    public static void SetLogWarningCallback(Action<string> logWarning)
    {
        _logWarning = logWarning;
    }

    public static void Info(params object[] messages)
    {
        _builder.Clear();
        for (int i = 0; i < messages.Length; i++)
        {
            _builder.Append(messages[i]?.ToString());
            _builder.Append(" ");
        }
        _logInfo?.Invoke(_builder.ToString());
    }

    public static void Warning(params object[] messages)
    {
        _builder.Clear();
        for (int i = 0; i < messages.Length; i++)
        {
            _builder.Append(messages[i]?.ToString());
            _builder.Append(" ");
        }
        _logWarning?.Invoke(_builder.ToString());
    }

    public static void Error(params object[] messages)
    {
        _builder.Clear();
        for (int i = 0; i < messages.Length; i++)
        {
            _builder.Append(messages[i]?.ToString());
            _builder.Append(" ");
        }
        _logError?.Invoke(_builder.ToString());
    }

    public static void LoadPresetConsole()
    {
        SetLogCallback(ConsolePrintInfo, ConsolePrintError, ConsolePrintWarning);
    }

    public static void LoadPresetUnity()
    {
        SetLogCallback(UnityPrintInfo, UnityPrintError, UnityPrintWarning);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnityPrintInfo(string str)
    {
        Debug.Log(str);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnityPrintWarning(string str)
    {
        Debug.LogWarning(str);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnityPrintError(string str)
    {
        Debug.LogError(str);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConsolePrint(string str, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(str);
        Console.ForegroundColor = ConsoleColor.White;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConsolePrintInfo(string str)
    {
        ConsolePrint(str, ConsoleColor.White);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConsolePrintWarning(string str)
    {
        ConsolePrint(str, ConsoleColor.Yellow);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConsolePrintError(string str)
    {
        ConsolePrint(str, ConsoleColor.Red);
    }
}