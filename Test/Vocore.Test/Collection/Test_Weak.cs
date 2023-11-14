using System;
using System.Collections.Generic;

namespace Vocore.Test
{
    public class Test_Weak
    {
        private class TestObject
        {

            public int Value;
            public TestObject()
            {
                Log.Info("Initialize TestObject");
            }
        }

        [Test("Test WeakReference")]
        public void Test_WeakReference()
        {
            TestObject obj = new TestObject(){
                Value = 123
            };
            WeakReference reference = new WeakReference(obj);

            GC.Collect();
            GC.WaitForFullGCComplete();
            GC.WaitForPendingFinalizers();

            UnitTest.AssertTrue(reference.IsAlive);
            //refer the object to keep it alive
            Log.Info(obj.Value);

            obj = null;
            GC.Collect();
            GC.WaitForFullGCComplete();
            GC.WaitForPendingFinalizers();

            UnitTest.AssertFalse(reference.IsAlive);
        }

        [Test("Test WeakCache")]
        public void Test_WeakCache()
        {
            WeakCache<TestObject> cache = new WeakCache<TestObject>();
            TestObject obj = new TestObject(){
                Value = 123
            };
            cache.Set("test", obj);

            GC.Collect();
            GC.WaitForFullGCComplete();
            GC.WaitForPendingFinalizers();

            UnitTest.AssertTrue(cache.Get("test") != null);
            //refer the object to keep it alive
            Log.Info(obj.Value);

            obj = null;
            GC.Collect();
            GC.WaitForFullGCComplete();
            GC.WaitForPendingFinalizers();

            UnitTest.AssertTrue(cache.Get("test") == null);
        }
    }
}

