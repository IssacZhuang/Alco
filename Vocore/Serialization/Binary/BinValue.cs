using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vocore
{
    public class BinValue
    {
        public enum ValueType : byte
        {
            Null = 0x00,
            Binary = 0x01,
            Array = 0x02,
            Object = 0x03,
        };

        private readonly ValueType _type;
        private readonly byte[] _binary;

        /// Properties
        public ValueType Type { get { return _type; } }

        public byte[] BinaryValue
        {
            get
            {
                switch (_type)
                {
                    case ValueType.Binary:
                        return _binary;
                }

                throw new Exception(string.Format("Original type is {0}. Cannot convert from {0} to binary", _type));
            }
        }

        public bool IsNull
        {
            get { return _type == ValueType.Null; }
        }

        public virtual BinValue this[string key]
        {
            get { return null; }
            set { }
        }
        public virtual BinValue this[int index]
        {
            get { return null; }
            set { }
        }
        public virtual void Clear() { }
        public virtual void Add(string key, BinValue value) { }
        public virtual void Add(BinValue value) { }
        public virtual bool Contains(BinValue v) { return false; }
        public virtual bool ContainsKey(string key) { return false; }

        public static implicit operator BinValue(byte[] v)
        {
            return new BinValue(v);
        }

        public static implicit operator byte[](BinValue v)
        {
            return v.BinaryValue;
        }

        protected BinValue(ValueType valueType)
        {
            _type = valueType;
        }

        public BinValue()
        {
            _type = ValueType.Null;
        }


        public BinValue(byte[] v)
        {
            _type = ValueType.Binary;
            _binary = v;
        }
    }

}