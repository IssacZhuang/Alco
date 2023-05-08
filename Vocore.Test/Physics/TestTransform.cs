using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore.Test
{
    public class TestTransform
    {
        [Test("test transform")]
        public void TestTransformPoint()
        {
            Vector3 transformFrom = new Vector3(1, 1, 1);
            Quaternion rotationFrom = Quaternion.identity;
            Vector3 point = new Vector3(3, 3, 3);

            Vector3 pointTransformed = UtilsMath.Transform(transformFrom, rotationFrom, point);
            TestHelper.PrintBlue("pointTransformed: " + pointTransformed);
            TestHelper.Assert(pointTransformed != new Vector3(2, 2, 2));
        }
    }
}

