using System;

namespace Vocore
{
    interface IOutput<T>
    {
        bool TryConnect(IInput<T> input);
    }
}