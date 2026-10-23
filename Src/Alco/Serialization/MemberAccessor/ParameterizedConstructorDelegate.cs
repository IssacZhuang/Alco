using System;

namespace Alco;

/// <summary>
/// Represents a factory that creates a <typeparamref name="T"/> from four arguments.
/// </summary>
public delegate T ParameterizedConstructorDelegate<T, TArg0, TArg1, TArg2, TArg3>(TArg0? arg0, TArg1? arg1, TArg2? arg2, TArg3? arg3);