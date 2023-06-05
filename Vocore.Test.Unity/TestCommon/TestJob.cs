using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

using UnityToolBox.UnitTest;
using UnityToolBox;

namespace Vocore.Test.Unity
{
    public class TestJob
    {
        [UnitTest("TestJob")]
        public void Test()
        {
            TestHelper.Print("TestJob !!??");
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                Terminal.Log(arg);
            }
        }
    }
}

