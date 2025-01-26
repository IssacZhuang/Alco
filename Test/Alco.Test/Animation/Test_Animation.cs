using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Alco.Test;

public class TestAnimation
{
    [Test(Description = "animation curve event test")]
    public void TestEvent()
    {
        float[] t = new float[] { 0, 1, 3, 7 };
        float[] value = new float[] { 0, 5, 4, 8 };
        CurveHermite curve = new CurveHermite(t, value);
        List<CurveEvent> events = new List<CurveEvent>();
        events.Add(new CurveEvent(9, "event5"));
        events.Add(new CurveEvent(0, "event1"));
        events.Add(new CurveEvent(-1, "event0"));
        events.Add(new CurveEvent(2, "event3"));
        events.Add(new CurveEvent(7.7f, "event4"));
        events.Add(new CurveEvent(1, "event2"));
        events.Add(new CurveEvent(11, "event6"));


        CurveAnimation animation = new CurveAnimation(curve, events);

        int flagEvent0 = 0;
        int flagEvent1 = 0;
        int flagEvent2 = 0;
        int flagEvent3 = 0;
        int flagEvent4 = 0;
        int flagEvent5 = 0;
        int flagEvent6 = 0;

        int multiEventTriggered = 0;

        animation.BindEvent("event0", () => { flagEvent0++; });
        animation.BindEvent("event1", () => { flagEvent1++; });
        animation.BindEvent("event2", () => { flagEvent2++; });
        animation.BindEvent("event3", () => { flagEvent3++; TestContext.WriteLine("event trigged"); });
        animation.BindEvent("event3", () => { multiEventTriggered++; });
        animation.BindEvent("event3", () => { multiEventTriggered++; });
        animation.BindEvent("event4", () => { flagEvent4++; });
        animation.BindEvent("event5", () => { flagEvent5++; });
        animation.BindEvent("event6", () => { flagEvent6++; });

        for (int i = 0; i < 10; i++)
        {
            animation.Evaluate(i);
        }

        Assert.IsFalse(flagEvent0 == 1, "event0 failed");
        Assert.IsTrue(flagEvent1 == 1, "event1 failed");
        Assert.IsTrue(flagEvent2 == 1, "event2 failed");
        Assert.IsTrue(flagEvent3 == 1, "event3 failed");
        TestContext.WriteLine("multiEventTriggered: " + multiEventTriggered);
        Assert.IsTrue(multiEventTriggered == 2, "multi event failed");
        Assert.IsTrue(flagEvent4 == 1, "event4 failed");
        Assert.IsTrue(flagEvent5 == 1, "event5 failed");
        Assert.IsFalse(flagEvent6 == 1, "event6 failed");


    }
}


