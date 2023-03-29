using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public static class KeyFrameParseHelper
    {
        private static readonly char _trimStart = '(';
        private static readonly char _trimEnd = ')';
        private static readonly char _splitInsideValue = ',';
        private static readonly char _splitTimeWithValue = '-';

        /// <summary>
        /// Convert a string 't - value' to CurvePoint<float>
        /// </summary>
        public static CurvePoint<float> ToKeyFrame(this string str){
            string[] split = str.Split(_splitTimeWithValue);
            if (split.Length != 2){
                throw ExceptionCurve.InvalidKeyFrameFormat(str, typeof(float));
            }
            float t = float.Parse(split[0]);
            float value = float.Parse(split[1]);
            return new CurvePoint<float>(t, value);
        }

        /// <summary>
        /// Convert a string 't - (x,y)' to CurvePoint<Vector2>
        /// </summary>
        public static CurvePoint<Vector2> ToKeyFrame2D(this string str){
            string[] split = str.Split(_splitTimeWithValue);
            if (split.Length != 2){
                throw ExceptionCurve.InvalidKeyFrameFormat(str, typeof(Vector2));
            }
            float t = float.Parse(split[0]);
            string[] splitValue = split[1].Trim(_trimStart, _trimEnd).Split(_splitInsideValue);
            if (splitValue.Length != 2){
                throw ExceptionCurve.InvalidKeyFrameFormat(str, typeof(Vector2));
            }
            float x = float.Parse(splitValue[0]);
            float y = float.Parse(splitValue[1]);
            return new CurvePoint<Vector2>(t, new Vector2(x, y));
        }

        /// <summary>
        /// Convert a string 't - (x,y,z)' to CurvePoint<Vector3>
        /// </summary>
        public static CurvePoint<Vector3> ToKeyFrame3D(this string str){
            string[] split = str.Split(_splitTimeWithValue);
            if (split.Length != 2){
                throw ExceptionCurve.InvalidKeyFrameFormat(str, typeof(Vector3));
            }
            float t = float.Parse(split[0]);
            string[] splitValue = split[1].Trim(_trimStart, _trimEnd).Split(_splitInsideValue);
            if (splitValue.Length != 3){
                throw ExceptionCurve.InvalidKeyFrameFormat(str, typeof(Vector3));
            }
            float x = float.Parse(splitValue[0]);
            float y = float.Parse(splitValue[1]);
            float z = float.Parse(splitValue[2]);
            return new CurvePoint<Vector3>(t, new Vector3(x, y, z));
        }

        /// <summary>
        /// Convert a string 't - (x,y,z,w)' to CurvePoint<Quaternion>
        /// </summary>
        public static CurvePoint<Quaternion> ToKeyFrame4D(this string str){
            string[] split = str.Split(_splitTimeWithValue);
            if (split.Length != 2){
                throw ExceptionCurve.InvalidKeyFrameFormat(str, typeof(Quaternion));
            }
            float t = float.Parse(split[0]);
            string[] splitValue = split[1].Trim(_trimStart, _trimEnd).Split(_splitInsideValue);
            if (splitValue.Length != 4){
                throw ExceptionCurve.InvalidKeyFrameFormat(str, typeof(Quaternion));
            }
            float x = float.Parse(splitValue[0]);
            float y = float.Parse(splitValue[1]);
            float z = float.Parse(splitValue[2]);
            float w = float.Parse(splitValue[3]);
            return new CurvePoint<Quaternion>(t, new Quaternion(x, y, z, w));
        }
    }
}

