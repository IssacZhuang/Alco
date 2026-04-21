using System;

namespace Alco.LLM;

/// <summary>
/// Marks a class as a game tool whose methods can be invoked by LLM agents.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class GameToolAttribute : Attribute
{
}
