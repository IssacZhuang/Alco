using System;
using System.Collections.Generic;

namespace Vocore.Test
{
    public class EventTestObject : IEventReciever
    {
        public void OnEvent(int data)
        {
            TestHelper.PrintColor("Event data: " + data, ConsoleColor.Green);
            TestHelper.AddSuccess();
        }
    }

    public class EventTestObject2 : IEventReciever
    {
        public void OnEvent(int data)
        {

        }
    }
    public class Test_Event
    {
        [Test("Event send and recieve")]
        public void Test_Send()
        {
            EventTestObject obj = new EventTestObject();
            Event evt = EventGenerator.Generate("TestEvent");
            obj.RegisterEvent<EventTestObject, int>(evt, obj.OnEvent);
            obj.SendEvent<EventTestObject, int>(evt, 1);
            obj.UnregisterEvent<EventTestObject, int>(evt);
        }

        [Test("Bechmark event send and recieve")]
        public void Test_Send_Benchmark()
        {
            EventTestObject2 obj = new EventTestObject2();
            Event evt = EventGenerator.Generate("TestEvent");
            obj.RegisterEvent<EventTestObject2, int>(evt, obj.OnEvent);

            TestHelper.Benchmark("Bechmark event", () =>
            {
                for (int i = 0; i < 100000; i++)
                {
                    obj.SendEvent<EventTestObject2, int>(evt, i);
                }
            });

            obj.UnregisterEvent<EventTestObject2, int>(evt);
        }
    }
}

