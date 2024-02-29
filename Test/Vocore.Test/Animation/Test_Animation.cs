using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Vocore.Test;

public class Test_Animation
{
    [Test(Description = "animation curve event test")]
    public void Test_Event()
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

        bool flagEvent0 = false;
        bool flagEvent1 = false;
        bool flagEvent2 = false;
        bool flagEvent3 = false;
        bool flagEvent4 = false;
        bool flagEvent5 = false;
        bool flagEvent6 = false;

        animation.BindEvent("event0", () => { flagEvent0 = true; });
        animation.BindEvent("event1", () => { flagEvent1 = true; });
        animation.BindEvent("event2", () => { flagEvent2 = true; });
        animation.BindEvent("event3", () => { flagEvent3 = true; });
        animation.BindEvent("event4", () => { flagEvent4 = true; });
        animation.BindEvent("event5", () => { flagEvent5 = true; });
        animation.BindEvent("event6", () => { flagEvent6 = true; });

        for (int i = 0; i < 10; i++)
        {
            animation.Evaluate(i);
        }

        // UnitTest.AssertFalse(flagEvent0, "event0 failed");
        // UnitTest.AssertFalse(!flagEvent1, "event1 failed");
        // UnitTest.AssertFalse(!flagEvent2, "event2 failed");
        // UnitTest.AssertFalse(!flagEvent3, "event3 failed");
        // UnitTest.AssertFalse(!flagEvent4, "event4 failed");
        // UnitTest.AssertFalse(!flagEvent5, "event5 failed");
        // UnitTest.AssertFalse(flagEvent6, "event6 failed");

        Assert.IsFalse(flagEvent0, "event0 failed");
        Assert.IsTrue(flagEvent1, "event1 failed");
        Assert.IsTrue(flagEvent2, "event2 failed");
        Assert.IsTrue(flagEvent3, "event3 failed");
        Assert.IsTrue(flagEvent4, "event4 failed");
        Assert.IsTrue(flagEvent5, "event5 failed");
        Assert.IsFalse(flagEvent6, "event6 failed");


    }
}


