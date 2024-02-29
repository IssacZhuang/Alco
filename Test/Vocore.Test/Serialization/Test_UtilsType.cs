using System;
using System.Collections.Generic;
using Vocore;

namespace Vocore.Test
{
    public class Test_UtilsType
    {
        TypeHelper _typeHelper = TypeHelper.Default;

        [Test(Description = "UtilsType.IsList")]
        public void Test_IsList()
        {
            Type type = typeof(List<int>);
           Assert.IsFalse(!_typeHelper.IsList(type), "Test_IsList failed. " + _typeHelper.IsList(type));
        }

        [Test(Description = "UtilsType.IsDictionary")]
        public void Test_IsDictionary()
        {
            Type type = typeof(Dictionary<int, string>);
            Assert.IsFalse(!_typeHelper.IsDictionary(type), "Test_IsDictionary failed. " + _typeHelper.IsDictionary(type));
        }

    }

}

