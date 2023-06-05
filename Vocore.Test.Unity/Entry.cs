using System;
using System.Collections.Generic;

using UnityToolBox;

namespace Vocore.Test.Unity
{
    public class Entry : IEntry
    {
        public int ExecuteOder => 0;

        void IEntry.Entry()
        {
            Terminal.Shell.RegisterCommands();
            string[] envArgs = Environment.GetCommandLineArgs();
            foreach (var arg in envArgs)
            {
                if (arg.ToLower().Equals("test"))
                {
                    Terminal.Open();
                    Terminal.Shell.RunCommand("test");
                }
            }
        }
    }
}

