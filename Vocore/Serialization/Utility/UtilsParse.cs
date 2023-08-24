using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

using UnityEngine;
using Unity.Mathematics;

namespace Vocore
{
    public class ParseHelper
    {
        public static readonly ParseHelper Default = new ParseHelper();

        private static readonly char[] _trimColorStart = new char[]
        {
            '(',
            'R',
            'G',
            'B',
            'A'
        };

        private static readonly char[] _trimColorEnd = new char[]
        {
            ')'
        };

        private static readonly char _trimStart = '(';
        private static readonly char _trimEnd = ')';
        private static readonly char _split = ',';

        private Dictionary<Type, Func<string, object>> _parser = new Dictionary<Type, Func<string, object>>();
        private TypeHelper _typeHelper = new TypeHelper();

        public TypeHelper TypeHelper
        {
            get { return _typeHelper; }
        }

        public ParseHelper()
        {
            RegisterAllParser();
        }

        public ParseHelper(params string[] namespaces)
        {
            RegisterAllParser();
            foreach (var ns in namespaces)
            {
                _typeHelper.AddDefaultNamespace(ns);
            }
        }


        /// <summary>
        /// Register a parser for a type.
        /// </summary>
        public void RegisterParser<T>(Func<string, T> parser)
        {
            _parser.Add(typeof(T), (str) => parser(str));
        }

        /// <summary>
        /// Register a parser for a type.
        /// </summary>
        public void RegisterParser(Type type, Func<string, object> parser)
        {
            _parser.Add(type, parser);
        }

        /// <summary>
        /// Check if a parser is registered for a type.
        /// </summary>
        public bool HasParser<T>()
        {
            return _parser.ContainsKey(typeof(T));
        }

        /// <summary>
        /// Check if a parser is registered for a type.
        /// </summary>
        public bool HasParser(Type type)
        {
            return _parser.ContainsKey(type);
        }

        /// <summary>
        /// Parse a string to a type.
        /// </summary>
        public bool TryParse<T>(string str, out T result)
        {
            if (HasParser<T>())
            {
                result = (T)_parser[typeof(T)](str);
                return true;
            }
            result = default(T);
            return false;
        }

        /// <summary>
        /// Parse a string to a type.
        /// </summary>
        public bool TryParse(string str, Type type, out object result)
        {
            if (HasParser(type))
            {
                result = _parser[type](str);
                return result != null;
            }
            result = null;
            return false;
        }


        public void RegisterAllParser()
        {
            RegisterParser<string>(ParseString);
            RegisterParser<int>(ToInt);
            RegisterParser<float>(ToFloat);
            RegisterParser<bool>(ToBool);
            RegisterParser<long>(ToLong);
            RegisterParser<double>(ToDouble);
            RegisterParser<sbyte>(ToSByte);
            RegisterParser<byte>(ToByte);
            RegisterParser<float2>(ToFloat2);
            RegisterParser<float3>(ToFloat3);
            RegisterParser<float4>(ToFloat4Adaptive);
            RegisterParser<Vector2>(ToVector2);
            RegisterParser<Vector3>(ToVector3);
            RegisterParser<Vector4>(ToVector4Adaptive);
            RegisterParser<quaternion>(ToQuaternion);
            RegisterParser<Color>(ToColor);
            RegisterParser<Rect>(ToRect);
            RegisterParser<Type>(ToType);
        }

        /// <summary>
        /// Parse a string.
        /// </summary>
        public string ParseString(string str)
        {
            return str.Replace("\\n", "\n");
        }

        /// <summary>
        /// Parse a string to int.
        /// </summary>
        public int ToInt(string str)
        {
            int result;
            if (!int.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
            {
                result = (int)float.Parse(str, CultureInfo.InvariantCulture);
            }
            return result;
        }

        /// <summary>
        /// Parse a string to float.
        /// </summary>
        public float ToFloat(string str)
        {
            return float.Parse(str, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a string to bool.
        /// </summary>
        public bool ToBool(string str)
        {
            return bool.Parse(str);
        }

        /// <summary>
        /// Parse a string to long.
        /// </summary>
        public long ToLong(string str)
        {
            return long.Parse(str, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a string to double.
        /// </summary>
        public double ToDouble(string str)
        {
            return double.Parse(str, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a string to sbyte.
        /// </summary>
        public sbyte ToSByte(string str)
        {
            return sbyte.Parse(str, CultureInfo.InvariantCulture);
        }


        /// <summary>
        /// Parse a string to byte.
        /// </summary>
        public byte ToByte(string str)
        {
            return byte.Parse(str, CultureInfo.InvariantCulture);
        }


        /// <summary>
        /// Parse a string to Unity.Mathmatics.float2. For example: string "(1,1)" or "(1)" is float2(1,1)
        /// </summary>
        public float2 ToFloat2(string Str)
        {
            Str = Str.TrimStart(_trimStart);
            Str = Str.TrimEnd(_trimEnd);
            string[] array = Str.Split(_split);
            CultureInfo invariantCulture = CultureInfo.InvariantCulture;
            float x;
            float y;
            if (array.Length == 1)
            {
                y = (x = Convert.ToSingle(array[0], invariantCulture));
            }
            else
            {
                if (array.Length != 2)
                {
                    throw new InvalidOperationException();
                }
                x = Convert.ToSingle(array[0], invariantCulture);
                y = Convert.ToSingle(array[1], invariantCulture);
            }
            return new float2(x, y);
        }

        /// <summary>
        /// Parse a string to Unity.Mathmatics.float3. For example: string "(1,1,1)" is float3(1,1,1)
        /// </summary>
        public float3 ToFloat3(string Str)
        {
            Str = Str.TrimStart(_trimStart);
            Str = Str.TrimEnd(_trimEnd);
            string[] array = Str.Split(_split);
            CultureInfo invariantCulture = CultureInfo.InvariantCulture;
            float x = Convert.ToSingle(array[0], invariantCulture);
            float y = Convert.ToSingle(array[1], invariantCulture);
            float z = Convert.ToSingle(array[2], invariantCulture);
            return new float3(x, y, z);
        }

        /// <summary>
        /// Parse a string to Unity.Mathmatics.quaternion. For example: string "(1,1,1,1)" is Quaternion(1,1,1,1)
        /// </summary>
        public quaternion ToQuaternion(string Str)
        {
            Str = Str.TrimStart(_trimStart);
            Str = Str.TrimEnd(_trimEnd);
            string[] array = Str.Split(_split);
            CultureInfo invariantCulture = CultureInfo.InvariantCulture;
            float x = Convert.ToSingle(array[0], invariantCulture);
            float y = Convert.ToSingle(array[1], invariantCulture);
            float z = Convert.ToSingle(array[2], invariantCulture);
            float w = Convert.ToSingle(array[3], invariantCulture);
            return new quaternion(x, y, z, w);
        }

        /// <summary>
        /// Parse a string to Unity.Mathmatics.float4. For example: string "(1,1,1,1)" is float4(1,1,1,1)  
        /// </summary>
        public float4 ToFloat4Adaptive(string Str)
        {
            Str = Str.TrimStart(_trimStart);
            Str = Str.TrimEnd(_trimEnd);
            string[] array = Str.Split(_split);
            CultureInfo invariantCulture = CultureInfo.InvariantCulture;
            float x = 0f;
            float y = 0f;
            float z = 0f;
            float w = 0f;
            if (array.Length >= 1)
            {
                x = Convert.ToSingle(array[0], invariantCulture);
            }
            if (array.Length >= 2)
            {
                y = Convert.ToSingle(array[1], invariantCulture);
            }
            if (array.Length >= 3)
            {
                z = Convert.ToSingle(array[2], invariantCulture);
            }
            if (array.Length >= 4)
            {
                w = Convert.ToSingle(array[3], invariantCulture);
            }
            return new float4(x, y, z, w);
        }

        /// <summary>
        /// Parse a string to UnityEngine.Vector2. For example: string "(1,1)" or "(1)" is Vector2(1,1)
        /// </summary>
        public Vector2 ToVector2(string Str)
        {
            Str = Str.TrimStart(_trimStart);
            Str = Str.TrimEnd(_trimEnd);
            string[] array = Str.Split(_split);
            CultureInfo invariantCulture = CultureInfo.InvariantCulture;
            float x;
            float y;
            if (array.Length == 1)
            {
                y = (x = Convert.ToSingle(array[0], invariantCulture));
            }
            else
            {
                if (array.Length != 2)
                {
                    throw new InvalidOperationException();
                }
                x = Convert.ToSingle(array[0], invariantCulture);
                y = Convert.ToSingle(array[1], invariantCulture);
            }
            return new Vector2(x, y);
        }

        /// <summary>
        /// Parse a string to UnityEngine.Vector3. For example: string "(1,1,1)" is Vector3(1,1,1)
        /// </summary>
        public Vector3 ToVector3(string Str)
        {
            Str = Str.TrimStart(_trimStart);
            Str = Str.TrimEnd(_trimEnd);
            string[] array = Str.Split(_split);
            CultureInfo invariantCulture = CultureInfo.InvariantCulture;
            float x = Convert.ToSingle(array[0], invariantCulture);
            float y = Convert.ToSingle(array[1], invariantCulture);
            float z = Convert.ToSingle(array[2], invariantCulture);
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Parse a string to UnityEngine.Vector4. For example: string "(1,1,1,1)" is Vector4(1,1,1,1)  
        /// </summary>
        public Vector4 ToVector4Adaptive(string Str)
        {
            Str = Str.TrimStart(_trimStart);
            Str = Str.TrimEnd(_trimEnd);
            string[] array = Str.Split(_split);
            CultureInfo invariantCulture = CultureInfo.InvariantCulture;
            float x = 0f;
            float y = 0f;
            float z = 0f;
            float w = 0f;
            if (array.Length >= 1)
            {
                x = Convert.ToSingle(array[0], invariantCulture);
            }
            if (array.Length >= 2)
            {
                y = Convert.ToSingle(array[1], invariantCulture);
            }
            if (array.Length >= 3)
            {
                z = Convert.ToSingle(array[2], invariantCulture);
            }
            if (array.Length >= 4)
            {
                w = Convert.ToSingle(array[3], invariantCulture);
            }
            return new Vector4(x, y, z, w);
        }

        /// <summary>
        /// Parse a string to UnityEngine.Rect. For example: string "(1,1,1,1)" is Rect(1,1,1,1)
        /// </summary>
        public Rect ToRect(string str)
        {
            str = str.TrimStart(_trimStart);
            str = str.TrimEnd(_trimEnd);
            string[] array = str.Split(_split);
            CultureInfo invariantCulture = CultureInfo.InvariantCulture;
            float x = Convert.ToSingle(array[0], invariantCulture);
            float y = Convert.ToSingle(array[1], invariantCulture);
            float width = Convert.ToSingle(array[2], invariantCulture);
            float height = Convert.ToSingle(array[3], invariantCulture);
            return new Rect(x, y, width, height);
        }

        /// <summary>
        /// Parse a string to UnityEngine.Color
        /// </summary>
        public Color ToColor(string str)
        {
            if (str.StartsWith("#"))
            {
                return str.ToColorHex();
            }

            str = str.TrimStart(_trimColorStart);
            str = str.TrimEnd(_trimColorEnd);
            string[] array = str.Split(_split);
            float num = ToFloat(array[0]);
            float num2 = ToFloat(array[1]);
            float num3 = ToFloat(array[2]);
            bool flag = num > 1f || num3 > 1f || num2 > 1f;
            float num4 = (float)(flag ? 255 : 1);
            if (array.Length == 4)
            {
                num4 = ToFloat(array[3]);
            }
            Color result;
            if (!flag)
            {
                result.r = num;
                result.g = num2;
                result.b = num3;
                result.a = num4;
            }
            else
            {
                result = UtilsColor.FromBytes(Mathf.RoundToInt(num), Mathf.RoundToInt(num2), Mathf.RoundToInt(num3), Mathf.RoundToInt(num4));
            }
            return result;
        }

        /// <summary>
        /// Parse a string to Enum
        /// </summary>
        public object ToEnum(string str, Type type)
        {
            return Enum.Parse(type, str);
        }


        /// <summary>
        /// Parse a string to Type
        /// </summary>
        public Type ToType(string str)
        {
            return _typeHelper.GetTypeFromAllAssemblies(str);
        }

    }
}


