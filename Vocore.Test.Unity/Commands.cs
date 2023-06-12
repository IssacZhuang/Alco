using System;
using System.Reflection;
using System.Collections.Generic;

using Vocore.Unsafe;
using UnityToolBox;
using UnityToolBox.UnitTest;

namespace Vocore.Test.Unity
{
    public class Commands
    {
        [RegisterCommand(Help = "Do unit test", MaxArgCount = 0)]
        static void CommandTest(CommandArg[] args)
        {
            Assembly assembly = Assembly.GetAssembly(typeof(Commands));
            TestHelper.StartTest(assembly);
            foreach (var leak in PointerTracker.GetAllocated())
            {
                Terminal.Log(TerminalLogType.Error, leak.ToString() + "\n");
            }
            GC.Collect();
        }
    }
}

