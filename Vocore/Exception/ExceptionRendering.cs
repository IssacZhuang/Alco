using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public static class ExceptionRendering
    {
        public readonly static Exception MaterialNotInstanced = new Exception("The GPU instancing of material is not enabled");
    }
}
