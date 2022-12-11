using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public class StructuredBuffer<T> where T : struct
    {
        private readonly T[] innerArray;

        public T this[int index] {
            get
            {
                return innerArray[index];
            }
            set
            {
                innerArray[index] = value;
            }
        }

        public T[] Raw => innerArray;

        public StructuredBuffer(int size)
        {
            innerArray = new T[size];
        }
    }
}
