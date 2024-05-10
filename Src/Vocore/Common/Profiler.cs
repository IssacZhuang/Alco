using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Vocore;

public struct ProfilerBlock
{
    /// <summary>
    /// The name of the block
    /// </summary>
    public string name;
    /// <summary>
    /// The time in CPU ticks
    /// <br>Use <see cref="Profiler.MilisecondMultiplier"/> to convert to miliseconds</br>
    /// </summary>
    public long time;

    /// <summary>
    /// The time in miliseconds. It will be rounded to 3 decimal places
    /// </summary>
    /// <value></value>
    public double Miliseconds
    {
        get
        {
            return Math.Round(time * Profiler.MilisecondMultiplier, 3);
        }
    }
}


/// <summary>
/// The tool to profile the code
/// </summary>
public class Profiler
{
    public static readonly double MilisecondMultiplier = 1000.0 / Stopwatch.Frequency;
    private readonly Stack<ProfilerBlock> _blocks = new Stack<ProfilerBlock>();
    private readonly Stopwatch _stopwatch = new Stopwatch();

    /// <summary>
    /// Start a new block
    /// </summary>
    /// <param name="name">The name of the block</param>
    //[Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start(string name = "ProfilerBlock")
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


    /// <summary>
    /// End the block
    /// </summary>
    /// <returns>The block</returns>
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