using System;
using System.Collections.Generic;

namespace Vocore
{
    public class StaticHashObject
    {
        private int _hashCode;

        public StaticHashObject()
        {
            _hashCode = base.GetHashCode();
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }
    }
}

