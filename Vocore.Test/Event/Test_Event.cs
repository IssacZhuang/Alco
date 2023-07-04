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
            ObjectEventExtension.RegisterEvent<int>(obj, evt, obj.OnEvent);
            ObjectEventExtension.SendEvent<int>(obj, evt, 1);
            ObjectEventExtension.UnregisterEvent<int>(obj, evt);
        }

        [Test("Bechmark event send and recieve")]
        public void Test_Send_Benchmark()
        {
            EventTestObject2 obj = new EventTestObject2();
            Event evt = EventGenerator.Generate("TestEvent");
            ObjectEventExtension.RegisterEvent<int>(obj, evt, obj.OnEvent);

            TestHelper.Benchmark("Bechmark event", () =>
            {
                for (int i = 0; i < 100000; i++)
                {
                    ObjectEventExtension.SendEvent<int>(obj, evt, i);
                }
            });

            ObjectEventExtension.UnregisterEvent<int>(obj, evt);
        }
    }
}

