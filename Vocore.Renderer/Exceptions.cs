using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore.Renderer
{
    public static class Exceptions
    {
        public readonly static Exception Exception_MaterialNotInstanced = new Exception("The GPU instancing of material is not enabled");
    }
}
