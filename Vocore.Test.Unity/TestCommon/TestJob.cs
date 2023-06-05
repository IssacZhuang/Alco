using System;
using System.Collections.Generic;

using UnityToolBox.UnitTest;

namespace Vocore.Test.Unity
{
    public class TestJob
    {
        [UnitTest("TestJob")]
        public static void Test()
        {
            TestHelper.Print("TestJob");
        }
    }
}

