using System;
using System.Collections.Generic;

namespace Alco.Test
{
    public class TestWeak
    {
        private class TestObject
        {

            public int Value;
            public TestObject()
            {
                Log.Info("Initialize TestObject");
            }
        }

        [Test(Description = "WeakReference")]
        public void TestWeakReference()
        {
            TestObject obj = new TestObject(){
                Value = 123
            };
            WeakReference reference = new WeakReference(obj);

            GC.Collect();
            GC.WaitForFullGCComplete();
            GC.WaitForPendingFinalizers();

            Assert.IsTrue(reference.IsAlive);
            //refer the object to keep it alive
            Log.Info(obj.Value);

            obj = null;
            GC.Collect();
            GC.WaitForFullGCComplete();
            GC.WaitForPendingFinalizers();

            Assert.IsFalse(reference.IsAlive);
        }
    }
}

