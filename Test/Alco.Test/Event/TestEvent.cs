using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TestFramework;
using Alco;

namespace Alco.Test
{
    /// <summary>
    /// Test event receiver that records invocations of subscribed events.
    /// </summary>
    public class EventTestObject : IEventReciever
    {
        /// <summary>
        /// Event identifier used for integer event subscriptions.
        /// </summary>
        public static EventId TestEvent = EventGenerator.Generate("TestEvent");
        /// <summary>
        /// Event identifier used for float event subscriptions.
        /// </summary>
        public static EventId TestEvent2 = EventGenerator.Generate("TestEvent2");
        private EventTracker _tracker;
        public bool success1 = false;
        public bool success2 = false;

        /// <summary>
        /// Subscribes handlers for both test events.
        /// </summary>
        public EventTestObject()
        {
            _tracker = new EventTracker();
            _tracker.Subscribe<int>(TestEvent, OnEvent);
            _tracker.Subscribe<float>(TestEvent2, (float value) =>
            {
                success2 = true;
            });
        }

        /// <summary>
        /// Records that the integer test event was received.
        /// </summary>
        public void OnEvent(int data)
        {
            success1 = true;
        }

        /// <summary>
        /// Not implemented for this test receiver.
        /// </summary>
        public void ClearEvent()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Invokes the given event on the tracker.
        /// </summary>
        public void InvokeEvent(EventId evt)
        {
            _tracker.Invoke(evt);
        }

        /// <summary>
        /// Invokes the given event with data on the tracker.
        /// </summary>
        public void InvokeEvent<TData>(EventId evt, TData data)
        {
            _tracker.Invoke(evt, data);
        }
    }

    /// <summary>
    /// Test event receiver that subscribes only to the integer test event.
    /// </summary>
    public class EventTestObject2 : IEventReciever
    {
        /// <summary>
        /// Event identifier used for the integer event subscription.
        /// </summary>
        public static EventId TestEvent = EventGenerator.Generate("TestEvent");
        private EventTracker _tracker;
        /// <summary>
        /// Subscribes a handler for the integer test event.
        /// </summary>
        public EventTestObject2()
        {
            _tracker = new EventTracker();
            _tracker.Subscribe<int>(TestEvent, OnEvent);
        }
        /// <summary>
        /// Handles the integer test event without recording state.
        /// </summary>
        public void OnEvent(int data)
        {
        }

        /// <summary>
        /// Invokes the given event on the tracker.
        /// </summary>
        public void InvokeEvent(EventId evt)
        {
            _tracker.Invoke(evt);
        }

        /// <summary>
        /// Invokes the given event with data on the tracker.
        /// </summary>
        public void InvokeEvent<TData>(EventId evt, TData data)
        {
            _tracker.Invoke(evt, data);
        }
    }
    /// <summary>
    /// Tests event dispatch to subscribed receivers.
    /// </summary>
    public class TestEvent
    {
        /// <summary>
        /// Tests sending integer, float, and mismatched payload events.
        /// </summary>
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

    }
}

