using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public static class ExceptionFramework
    {
        public static Exception ProtoAlreadyExist(string protoName)
        {
            return new Exception("Duplicated protoName: " + protoName);
        }
    }
}
