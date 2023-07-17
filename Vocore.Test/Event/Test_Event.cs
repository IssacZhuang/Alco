using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Vocore;

namespace Vocore.Test
{
    public class EventTestObject : IEventReciever
    {
        public void OnEvent(int data)
        {
            TestHelper.PrintColor("Event data: " + data, ConsoleColor.Green);
            TestHelper.AddSuccess();
        }

        void IEventReciever.ClearEvent()
        {
            throw new NotImplementedException();
        }

        void IEventReciever.InvokeEvent(EventId evt)
        {
            throw new NotImplementedException();
        }

        void IEventReciever.InvokeEvent<TData>(EventId evt, TData data)
        {
            throw new NotImplementedException();
        }
    }

    public class EventTestObject2 : IEventReciever
    {
        private int _hash;
        public EventTestObject2()
        {
            _hash = RuntimeHelpers.GetHashCode(this);
        }

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public override int GetHashCode()
        // {
        //     return _hash;
        // }
        public void OnEvent(int data)
        {
        }

        void IEventReciever.ClearEvent()
        {
            throw new NotImplementedException();
        }

        void IEventReciever.InvokeEvent(EventId evt)
        {
            throw new NotImplementedException();
        }

        void IEventReciever.InvokeEvent<TData>(EventId evt, TData data)
        {
            throw new NotImplementedException();
        }
    }
    public class Test_Event
    {
        [Test("Event send and recieve")]
        public void Test_Send()
        {
            EventTestObject obj = new EventTestObject();
            EventId evt = EventGenerator.Generate("TestEvent");
            GlobalEventManger.RegisterEvent<int>(obj, evt, obj.OnEvent);
            GlobalEventManger.InvkeEvent<int>(obj, evt, 1);
            GlobalEventManger.UnregisterEvent<int>(obj, evt);
        }

        [Test("Bechmark event send and recieve")]
        public void Test_Send_Benchmark()
        {
            EventTestObject2 obj = new EventTestObject2();
            EventId evt = EventGenerator.Generate("TestEvent");
            GlobalEventManger.RegisterEvent<int>(obj, evt, obj.OnEvent);

            TestHelper.Benchmark("Bechmark event", () =>
            {
                for (int i = 0; i < 100000; i++)
                {
                    GlobalEventManger.InvkeEvent<int>(obj, evt, i);
                }
            });

            GlobalEventManger.UnregisterEvent<int>(obj, evt);
        }
    }
}

