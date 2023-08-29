using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vocore
{
    public class BinaryValue
    {
        public enum ValueType : byte
        {
            Null = 0x00,
            Binary = 0x01,
            Array = 0x02,
            Table = 0x03,
        };

        private readonly ValueType _type;
        private readonly byte[] _binary;

        /// Properties
        public ValueType Type { get { return _type; } }

        public static BinaryValue NullValue
        {
            get { return new BinaryValue(ValueType.Null); }
        }

        public byte[] Bytes
        {
            get
            {
                if (_type == ValueType.Binary)
                {
                    return _binary;
                }
                throw new Exception(string.Format("Original type is {0}. Cannot convert from {0} to binary", _type));
            }
        }

        public bool IsNull
        {
            get { return _type == ValueType.Null; }
        }

        public static implicit operator BinaryValue(byte[] v)
        {
            return new BinaryValue(v);
        }

        public static implicit operator byte[](BinaryValue v)
        {
            return v.Bytes;
        }

        protected BinaryValue(ValueType valueType)
        {
            _type = valueType;
        }

        public BinaryValue()
        {
            _type = ValueType.Null;
        }


        public BinaryValue(byte[] v)
        {
            _type = ValueType.Binary;
            _binary = v;
        }
    }

}