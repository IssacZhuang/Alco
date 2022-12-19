using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public class ProtoBase
    {
        public string nameID;
        public string title;
        public string desc;
        
        public virtual void Initialize()
        {

        }

        public virtual bool CanGroupWith()
        {
            return false;
        }

        public virtual IEnumerable<string> CheckConfigError()
        {
            yield break;
        }
    }
}
