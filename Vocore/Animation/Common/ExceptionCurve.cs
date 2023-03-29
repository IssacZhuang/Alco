using System;
using System.Collections.Generic;

namespace Vocore
{
    public static class ExceptionCurve
    {
        public static Exception NullCurve = new Exception("Null curve");
        public static Exception NullOrEmptyPoints(string name){
            return new Exception("Null or empty points list: " + name);
        }
        public static Exception UnequalPointsArray(string name1, string name2)
        {
            return new Exception("Unequal points array: " + name1 + " and " + name2);
        }

        public static Exception InvalidKeyFrameFormat(string str, Type type)
        {
            return new Exception("Invalid KeyFrame format: " + str + " for type " + type);
        }
    }
}

