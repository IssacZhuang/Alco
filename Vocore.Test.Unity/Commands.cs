using System;
using System.Reflection;
using System.Collections.Generic;

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
        }
    }
}

