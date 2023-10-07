using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;



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

        private readonly Dictionary<Type, Func<string, object>> _strParser = new Dictionary<Type, Func<string, object>>();
        private readonly Dictionary<Type, Func<object, string>> _valueParser = new Dictionary<Type, Func<object, string>>();
        private readonly TypeHelper _typeHelper = new TypeHelper();

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
        public void RegisterStrParser<T>(Func<string, T> parser)
        {
            _strParser.Add(typeof(T), (str) => parser(str));
        }

        /// <summary>
        /// Register a parser for a type.
        /// </summary>
        public void RegisterStrParser(Type type, Func<string, object> parser)
        {
            _strParser.Add(type, parser);
        }

        /// <summary>
        /// Check if a parser is registered for a type.
        /// </summary>
        public bool HasStrParser<T>()
        {
            return _strParser.ContainsKey(typeof(T));
        }

        /// <summary>
        /// Check if a parser is registered for a type.
        /// </summary>
        public bool HasStrParser(Type type)
        {
            return _strParser.ContainsKey(type);
        }

        /// <summary>
        /// Parse a string to a type.
        /// </summary>
        public bool TryParseStr<T>(string str, out T result)
        {
            if (HasStrParser<T>())
            {
                result = (T)_strParser[typeof(T)](str);
                return true;
            }
            result = default(T);
            return false;
        }

        /// <summary>
        /// Parse a string to a type.
        /// </summary>
        public bool TryParseStr(string str, Type type, out object result)
        {
            if (HasStrParser(type))
            {
                result = _strParser[type](str);
                return result != null;
            }
            result = null;
            return false;
        }

        /// <summary>
        /// Register a parser for a type.
        /// </summary>
        public void RegisterValueParser<T>(Func<T, string> parser)
        {
            _valueParser.Add(typeof(T), (value) => parser((T)value));
        }

        /// <summary>
        /// Register a parser for a type.
        /// </summary>
        public void RegisterValueParser(Type type, Func<object, string> parser)
        {
            _valueParser.Add(type, parser);
        }

        /// <summary>
        /// Check if a parser is registered for a type.
        /// </summary>
        public bool HasValueParser<T>()
        {
            return _valueParser.ContainsKey(typeof(T));
        }

        /// <summary>
        /// Check if a parser is registered for a type.
        /// </summary>
        public bool HasValueParser(Type type)
        {
            return _valueParser.ContainsKey(type);
        }

        /// <summary>
        /// Parse a type to a string.
        /// </summary>
        public bool TryParseValue<T>(T value, out string result)
        {
            if (HasValueParser<T>())
            {
                result = _valueParser[typeof(T)](value);
                return true;
            }
            result = null;
            return false;
        }

        /// <summary>
        /// Parse a type to a string.
        /// </summary>
        public bool TryParseValue(object value, Type type, out string result)
        {
            if (HasValueParser(type))
            {
                result = _valueParser[type](value);
                return true;
            }
            result = null;
            return false;
        }


        public void RegisterAllParser()
        {
            RegisterStrParser<string>(ParseString);
            RegisterStrParser<int>(StrToInt);
            RegisterStrParser<float>(StrToFloat);
            RegisterStrParser<bool>(StrToBool);
            RegisterStrParser<long>(StrToLong);
            RegisterStrParser<double>(StrToDouble);
            RegisterStrParser<sbyte>(StrToSByte);
            RegisterStrParser<byte>(StrToByte);
            RegisterStrParser<float2>(StrToFloat2);
            RegisterStrParser<float3>(StrToFloat3);
            RegisterStrParser<float4>(StrToFloat4Adaptive);
            RegisterStrParser<quaternion>(StrToQuaternion);
            RegisterStrParser<Type>(StrToType);

            RegisterValueParser<int>(IntToStr);
            RegisterValueParser<float>(FloatToStr);
            RegisterValueParser<bool>(BoolToStr);
            RegisterValueParser<long>(LongToStr);
            RegisterValueParser<double>(DoubleToStr);
            RegisterValueParser<sbyte>(SByteToStr);
            RegisterValueParser<byte>(ByteToStr);
            RegisterValueParser<float2>(Float2ToStr);
            RegisterValueParser<float3>(Float3ToStr);
            RegisterValueParser<float4>(Float4ToStr);
            RegisterValueParser<quaternion>(QuaternionToStr);
            RegisterValueParser<Type>(TypeToStr);
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
        public int StrToInt(string str)
        {
            int result;
            if (!int.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
            {
                result = (int)float.Parse(str, CultureInfo.InvariantCulture);
            }
            return result;
        }

        /// <summary>
        /// Parse a int to string.
        /// </summary>
        public string IntToStr(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a string to float.
        /// </summary>
        public float StrToFloat(string str)
        {
            return float.Parse(str, CultureInfo.InvariantCulture);
        }


        /// <summary>
        /// Parse a float to string.
        /// </summary>
        public string FloatToStr(float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a string to bool.
        /// </summary>
        public bool StrToBool(string str)
        {
            return bool.Parse(str);
        }

        /// <summary>
        /// Parse a bool to string.
        /// </summary>
        public string BoolToStr(bool value)
        {
            return value.ToString();
        }

        /// <summary>
        /// Parse a string to long.
        /// </summary>
        public long StrToLong(string str)
        {
            return long.Parse(str, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a long to string.
        /// </summary>
        public string LongToStr(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a string to double.
        /// </summary>
        public double StrToDouble(string str)
        {
            return double.Parse(str, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a double to string.
        /// </summary>
        public string DoubleToStr(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a string to sbyte.
        /// </summary>
        public sbyte StrToSByte(string str)
        {
            return sbyte.Parse(str, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a string to byte.
        /// </summary>
        public string SByteToStr(sbyte value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }


        /// <summary>
        /// Parse a string to byte.
        /// </summary>
        public byte StrToByte(string str)
        {
            return byte.Parse(str, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a byte to string.
        /// </summary>
        public string ByteToStr(byte value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a string to Unity.Mathmatics.float2. For example: string "(1,1)" or "(1)" is float2(1,1)
        /// </summary>
        public float2 StrToFloat2(string Str)
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
        /// Parse a Unity.Mathmatics.float2 to string. For example: float2(1,1) is "(1,1)"
        /// </summary>
        public string Float2ToStr(float2 value)
        {
            return string.Concat(new string[]
            {
                "(",
                value.x.ToString(CultureInfo.InvariantCulture),
                ",",
                value.y.ToString(CultureInfo.InvariantCulture),
                ")"
            });
        }

        /// <summary>
        /// Parse a string to Unity.Mathmatics.float3. For example: string "(1,1,1)" is float3(1,1,1)
        /// </summary>
        public float3 StrToFloat3(string Str)
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
        /// Parse a Unity.Mathmatics.float3 to string. For example: float3(1,1,1) is "(1,1,1)"
        /// </summary>
        public string Float3ToStr(float3 value)
        {
            return string.Concat(new string[]
            {
                "(",
                value.X.ToString(CultureInfo.InvariantCulture),
                ",",
                value.y.ToString(CultureInfo.InvariantCulture),
                ",",
                value.Z.ToString(CultureInfo.InvariantCulture),
                ")"
            });
        }

        /// <summary>
        /// Parse a string to Unity.Mathmatics.quaternion. For example: string "(1,1,1,1)" is Quaternion(1,1,1,1)
        /// </summary>
        public quaternion StrToQuaternion(string Str)
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
        /// Parse a Unity.Mathmatics.quaternion to string. For example: Quaternion(1,1,1,1) is "(1,1,1,1)"
        /// </summary>
        public string QuaternionToStr(quaternion value)
        {
            return string.Concat(new string[]
            {
                "(",
                value.value.X.ToString(CultureInfo.InvariantCulture),
                ",",
                value.value.Y.ToString(CultureInfo.InvariantCulture),
                ",",
                value.value.Z.ToString(CultureInfo.InvariantCulture),
                ",",
                value.value.W.ToString(CultureInfo.InvariantCulture),
                ")"
            });
        }

        /// <summary>
        /// Parse a string to Unity.Mathmatics.float4. For example: string "(1,1,1,1)" is float4(1,1,1,1)  
        /// </summary>
        public float4 StrToFloat4Adaptive(string Str)
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
        /// Parse a Unity.Mathmatics.float4 to string. For example: float4(1,1,1,1) is "(1,1,1,1)"
        /// </summary>
        public string Float4ToStr(float4 value)
        {
            return string.Concat(new string[]
            {
                "(",
                value.X.ToString(CultureInfo.InvariantCulture),
                ",",
                value.Y.ToString(CultureInfo.InvariantCulture),
                ",",
                value.Z.ToString(CultureInfo.InvariantCulture),
                ",",
                value.W.ToString(CultureInfo.InvariantCulture),
                ")"
            });
        }


        /// <summary>
        /// Parse a string to Enum
        /// </summary>
        public object StrToEnum(string str, Type type)
        {
            return Enum.Parse(type, str);
        }

        /// <summary>
        /// Parse a Enum to string
        /// </summary>
        public string EnumToStr(object value)
        {
            return value.ToString();
        }


        /// <summary>
        /// Parse a string to Type
        /// </summary>
        public Type StrToType(string str)
        {
            return _typeHelper.GetTypeFromAllAssemblies(str);
        }

        /// <summary>
        /// Parse a Type to string
        /// </summary>
        public string TypeToStr(Type type)
        {
            return type.FullName;
        }

    }
}


