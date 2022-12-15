using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore.Test
{
    internal class Test: Attribute
    {
        public string name { get; private set; }
        public bool expectError { get; private set; }

        public Test()
        {
            this.name = "Test";
            expectError = false;
        }

        public Test(string testName)
        {
            this.name = testName;
            expectError = false;
        }

        public Test(string testName, bool expectError)
        {
            this.name = testName;
            this.expectError = expectError;
        }
    }
}
