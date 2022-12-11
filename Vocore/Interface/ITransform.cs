using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace Vocore
{
    public interface ITransform
    {
        Matrix4x4 ToMatrix4x4();
    }
}
