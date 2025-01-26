using System;
using System.Collections.Generic;
using Alco;

namespace Alco.Test
{
    public class TestUtilsType
    {
        TypeHelper _typeHelper = TypeHelper.Default;

        [Test(Description = "UtilsType.IsList")]
        public void TestIsList()
        {
            Type type = typeof(List<int>);
            Assert.IsFalse(!_typeHelper.IsList(type), "TestIsList failed. " + _typeHelper.IsList(type));
        }

        [Test(Description = "UtilsType.IsDictionary")]
        public void TestIsDictionary()
        {
            Type type = typeof(Dictionary<int, string>);
            Assert.IsFalse(!_typeHelper.IsDictionary(type), "TestIsDictionary failed. " + _typeHelper.IsDictionary(type));
        }

    }

}

