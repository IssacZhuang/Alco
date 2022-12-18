using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore.Test.Core.Framework
{
    internal class Test_ProtoDatabase
    {
        [Test("ProtoDatabase.Load() Duplicate refer", true)]
        public void Test_DuplicateAdd()
        {
            ProtoBase proto = new ProtoBase();
            ProtoDatabase<ProtoBase>.Load(proto);
            ProtoDatabase<ProtoBase>.Load(proto); // error
        }

        [Test("ProtoDatabase.Load() Duplicate name", true)]
        public void Test_DuplicateAdd2()
        {
            ProtoBase proto1 = new ProtoBase
            {
                nameID = "same"
            };
            ProtoBase proto2 = new ProtoBase
            {
                nameID = "same"
            };
            ProtoDatabase<ProtoBase>.Load(proto1);
            ProtoDatabase<ProtoBase>.Load(proto2); // error
        }

        [Test("ProtoDatabase.Load() Load")]
        public void Test_Load()
        {
            ProtoDatabase<ProtoBase>.Clear();
            ProtoBase proto1 = new ProtoBase
            {
                nameID = "p1"
            };
            ProtoBase proto2 = new ProtoBase
            {
                nameID = "p2"
            };
            ProtoDatabase<ProtoBase>.Load(proto1);
            ProtoDatabase<ProtoBase>.Load(proto2);

            TestUtility.Assert(ProtoDatabase<ProtoBase>.Count != 2);
        }

        private class ProtoFoo : ProtoBase { }
        private class ProtoBar : ProtoBase { }


        [Test("ProtoDatabase.Load() Load different type")]
        public void Test_LoadTwoType()
        {

            ProtoDatabase<ProtoBase>.Clear();
            ProtoFoo proto1 = new ProtoFoo
            {
                nameID = "p1"
            };
            ProtoBar proto2 = new ProtoBar
            {
                nameID = "p2"
            };
            ProtoDatabase<ProtoBase>.Load(proto1);
            ProtoDatabase<ProtoBase>.Load(proto2);

            TestUtility.Assert(ProtoDatabase<ProtoBase>.Count != 2);
        }

        [Test("ProtoDatabase.Load() Load with different DB")]
        public void Test_LoadTwoDB()
        {

            ProtoDatabase<ProtoBase>.Clear();
            ProtoFoo proto1 = new ProtoFoo
            {
                nameID = "p1"
            };
            ProtoBar proto2 = new ProtoBar
            {
                nameID = "p2"
            };
            ProtoDatabase<ProtoFoo>.Load(proto1);
            ProtoDatabase<ProtoBar>.Load(proto2);

            TestUtility.Assert(ProtoDatabase<ProtoBase>.Count != 0);
            TestUtility.Assert(ProtoDatabase<ProtoFoo>.Count != 1);
            TestUtility.Assert(ProtoDatabase<ProtoBar>.Count != 1);
        }
    }
}
