using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public interface ISystem
    {
        void OnCreate();
        void OnTick();
        void OnUpdate();
        void OnDestroy();
    }
}
