using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public class InstancedRenderManager : BaseInstancedRenderManager<InstancedRenderQueue>
    {
        private static InstancedRenderManager _instance;
        public static InstancedRenderManager Default
        {
            get
            {
                if (_instance == null) _instance = new InstancedRenderManager();
                return _instance;
            }
        }
    }
}
