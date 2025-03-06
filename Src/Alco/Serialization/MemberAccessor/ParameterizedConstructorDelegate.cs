using System;

namespace Alco;

public delegate T ParameterizedConstructorDelegate<T, TArg0, TArg1, TArg2, TArg3>(TArg0? arg0, TArg1? arg1, TArg2? arg2, TArg3? arg3);