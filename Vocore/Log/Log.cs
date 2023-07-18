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

    public static void Info(string message)
    {
        _logInfo?.Invoke(message);
    }

    public static void Warning(string message)
    {
        _logWarning?.Invoke(message);
    }

    public static void Error(string message)
    {
        _logError?.Invoke(message);
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
    private static void UnityPrintInfo(object obj)
    {
        Debug.Log(obj);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnityPrintWarning(object obj)
    {
        Debug.LogWarning(obj);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UnityPrintError(object obj)
    {
        Debug.LogError(obj);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConsolePrint(object obj, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(obj);
        Console.ForegroundColor = ConsoleColor.White;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConsolePrintInfo(object obj)
    {
        ConsolePrint(obj, ConsoleColor.White);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConsolePrintWarning(object obj)
    {
        ConsolePrint(obj, ConsoleColor.Yellow);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConsolePrintError(object obj)
    {
        ConsolePrint(obj, ConsoleColor.Red);
    }
}