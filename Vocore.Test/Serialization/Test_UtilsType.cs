using System;
using System.Collections.Generic;
using Vocore;

namespace Vocore.Test
{
    public class Test_UtilsType
    {
        [Test("Test_IsList")]
        public void Test_IsList()
        {
            Type type = typeof(List<int>);
            TestHelper.Assert(!type.IsList(), "Test_IsList failed. " + type.IsList());
        }

        [Test("Test_IsDictionary")]
        public void Test_IsDictionary()
        {
            Type type = typeof(Dictionary<int, string>);
            TestHelper.Assert(!type.IsDictionary(), "Test_IsDictionary failed. " + type.IsDictionary());
        }

    }

}

