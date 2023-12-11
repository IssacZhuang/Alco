using System;

namespace Vocore
{
    interface IInput<T>
    {
        void OnInput(T input);
    }
}