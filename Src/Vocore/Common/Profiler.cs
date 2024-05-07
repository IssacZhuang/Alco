using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Vocore;

public struct ProfilerBlock
{
    public string name;
    public long time; // cpu ticks
}

public class Profiler
{
    public static readonly double MilisecondMultiplier = 1000.0 / Stopwatch.Frequency;
    private readonly Stack<ProfilerBlock> _blocks = new Stack<ProfilerBlock>();
    private readonly Stopwatch _stopwatch = new Stopwatch();

    //[Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start(string name)
    {
        long time = 0;
        if (_blocks.Count > 0)
        {
            time = _stopwatch.ElapsedTicks;
        }
        else
        {
            _stopwatch.Restart();
        }
        _blocks.Push(new ProfilerBlock { name = name, time = time });
    }

    //[Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ProfilerBlock End()
    {
       if (_blocks.Count == 0)
        {
            throw new InvalidOperationException("No matching Start() for End()");
        }
        ProfilerBlock block = _blocks.Pop();
        block.time = _stopwatch.ElapsedTicks - block.time;

        if (_blocks.Count == 0)
        {
            _stopwatch.Stop();
        }

        return block;
    }
}