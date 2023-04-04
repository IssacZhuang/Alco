using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public class ResBase
    {
        public string name;
        
        public virtual void Initialize()
        {

        }

        public virtual bool ConflictWith(ResBase other)
        {
            return this.name == other.name;
        }

        public virtual IEnumerable<string> CheckConfigError()
        {
            yield break;
        }
    }
}
