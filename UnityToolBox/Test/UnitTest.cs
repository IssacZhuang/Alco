using System;
using System.Collections.Generic;

namespace UnityToolBox.UnitTest
{
    public class UnitTest : Attribute
    {
        public string Name { get; private set; }

        public UnitTest()
        {
            this.Name = "Test";
        }

        public UnitTest(string testName)
        {
            this.Name = testName;
        }
    }
}

