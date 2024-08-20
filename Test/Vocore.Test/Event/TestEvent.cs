using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TestFramework;
using Vocore;

namespace Vocore.Test
{
    public class EventTestObject : IEventReciever
    {
        public static EventId TestEvent = EventGenerator.Generate("TestEvent");
        public static EventId TestEvent2 = EventGenerator.Generate("TestEvent2");
        private EventTracker _tracker;
        public bool success1 = false;
        public bool success2 = false;

        public EventTestObject()
        {
            _tracker = new EventTracker();
            _tracker.Subscribe<int>(TestEvent, OnEvent);
            _tracker.Subscribe<float>(TestEvent2, (float value) =>
            {
                success2 = true;
            });
        }

        public void OnEvent(int data)
        {
            success1 = true;
        }

        public void ClearEvent()
        {
            throw new NotImplementedException();
        }

        public void InvokeEvent(EventId evt)
        {
            _tracker.Invoke(evt);
        }

        public void InvokeEvent<TData>(EventId evt, TData data)
        {
            _tracker.Invoke(evt, data);
        }
    }

    public class EventTestObject2 : IEventReciever
    {
        public static EventId TestEvent = EventGenerator.Generate("TestEvent");
        private EventTracker _tracker;
        public EventTestObject2()
        {
            _tracker = new EventTracker();
            _tracker.Subscribe<int>(TestEvent, OnEvent);
        }
        public void OnEvent(int data)
        {
        }

        public void InvokeEvent(EventId evt)
        {
            _tracker.Invoke(evt);
        }

        public void InvokeEvent<TData>(EventId evt, TData data)
        {
            _tracker.Invoke(evt, data);
        }
    }
    public class TestEvent
    {
        [Test(Description ="Event send and recieve")]
        public void TestSend()
        {
            EventTestObject obj = new EventTestObject();
            obj.InvokeEvent(EventTestObject.TestEvent, 1);
            obj.InvokeEvent(EventTestObject.TestEvent2, 2f);
            obj.InvokeEvent(EventTestObject.TestEvent, "3");
            obj.InvokeEvent(EventTestObject.TestEvent, new object());
            obj.InvokeEvent(EventGenerator.Generate("TmpEvent"), 1);

            Assert.IsTrue(obj.success1, "Event send and recieve failed");
            Assert.IsTrue(obj.success2, "Event send and recieve failed");
        }

        [Test(Description ="Bechmark event send and recieve")]
        public void TestSend_Benchmark()
        {
            EventTestObject2 obj = new EventTestObject2();
            EventId evt = EventGenerator.Generate("TestEvent");
            int count = 1000000;

            UtilsTest.Benchmark(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    obj.InvokeEvent(evt, i);
                }
            }, "Event Invoke benchmark: ");

        }
    }
}

