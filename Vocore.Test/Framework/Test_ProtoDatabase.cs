using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore.Test.Core.Framework
{
    [DisabledTestTemporarily]
    internal class Test_ResDatabase
    {
        [Test("ResDatabase.Load() Duplicate refer", true)]
        public void Test_DuplicateAdd()
        {
            ResBase res = new ResBase();
            ResDatabase<ResBase>.Load(res);
            ResDatabase<ResBase>.Load(res); // error
        }

        [Test("ResDatabase.Load() Duplicate name", true)]
        public void Test_DuplicateAdd2()
        {
            ResBase res1 = new ResBase
            {
                name = "same"
            };
            ResBase res2 = new ResBase
            {
                name = "same"
            };
            ResDatabase<ResBase>.Load(res1);
            ResDatabase<ResBase>.Load(res2); // error
        }

        [Test("ResDatabase.Load() Load")]
        public void Test_Load()
        {
            ResDatabase<ResBase>.Clear();
            ResBase res1 = new ResBase
            {
                name = "p1"
            };
            ResBase res2 = new ResBase
            {
                name = "p2"
            };
            ResDatabase<ResBase>.Load(res1);
            ResDatabase<ResBase>.Load(res2);

            TestUtility.Assert(ResDatabase<ResBase>.Count != 2);
        }

        private class resFoo : ResBase { }
        private class resBar : ResBase { }


        [Test("ResDatabase.Load() Load different type")]
        public void Test_LoadTwoType()
        {

            ResDatabase<ResBase>.Clear();
            resFoo res1 = new resFoo
            {
                name = "p1"
            };
            resBar res2 = new resBar
            {
                name = "p2"
            };
            ResDatabase<ResBase>.Load(res1);
            ResDatabase<ResBase>.Load(res2);

            TestUtility.Assert(ResDatabase<ResBase>.Count != 2);
        }

        [Test("ResDatabase.Load() Load with different DB")]
        public void Test_LoadTwoDB()
        {

            ResDatabase<ResBase>.Clear();
            resFoo res1 = new resFoo
            {
                name = "p1"
            };
            resBar res2 = new resBar
            {
                name = "p2"
            };
            ResDatabase<resFoo>.Load(res1);
            ResDatabase<resBar>.Load(res2);

            TestUtility.Assert(ResDatabase<ResBase>.Count != 0);
            TestUtility.Assert(ResDatabase<resFoo>.Count != 1);
            TestUtility.Assert(ResDatabase<resBar>.Count != 1);
        }
    }
}
