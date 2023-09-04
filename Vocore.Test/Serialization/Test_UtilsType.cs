using System;
using System.Collections.Generic;
using Vocore;

namespace Vocore.Test
{
    public class Test_UtilsType
    {
        TypeHelper _typeHelper = TypeHelper.Default;

        [Test("Test_IsList")]
        public void Test_IsList()
        {
            Type type = typeof(List<int>);
            UnitTest.AssertFalse(!_typeHelper.IsList(type), "Test_IsList failed. " + _typeHelper.IsList(type));
        }

        [Test("Test_IsDictionary")]
        public void Test_IsDictionary()
        {
            Type type = typeof(Dictionary<int, string>);
            UnitTest.AssertFalse(!_typeHelper.IsDictionary(type), "Test_IsDictionary failed. " + _typeHelper.IsDictionary(type));
        }

    }

}

