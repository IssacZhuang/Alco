using System;
using System.Collections.Generic;

namespace Vocore.Test
{
    public class Test_Config
    {
        public static readonly string path = "Config/Files";

        [Test("Test config")]
        public void Test_LoadConfig()
        {
            ConfigManager.LoadFolder(path);
            ExampleConfig config = ConfigManager.GetConfig<ExampleConfig>("test1");
            UnitTest.PrintBlue(config.fieldInt);
        }
    }
}

