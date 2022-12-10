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
        Vector3 Postition { get; set; }
        Quaternion Rotation { get; set; }
        Vector3 Scale { get; set; }

        Matrix4x4 ToMatrix4x4();
    }
}
