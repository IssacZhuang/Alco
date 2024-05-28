using System;
using System.Collections.Generic;

namespace Ron.NET;

public abstract class RonGenMethods
{
    protected internal Dictionary<Type, (Func<object, IElement> serialize, Func<string, object> deserialize)> Methods;
}