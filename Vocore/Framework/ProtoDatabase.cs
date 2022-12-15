using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore.Framework
{
    public static class ProtoDatabase<T> where T : ProtoBase
    {
        private readonly static List<T> _protoList = new List<T>();

        public static int Count => _protoList.Count;
        public static List<T> AllProtos => _protoList;

        public static void Load(T proto)
        {
            if(_protoList.AsParallel().Any(x=>x.nameID == proto.nameID))
            {
                throw ExceptionFramework.ProtoAlreadyExist(proto.nameID);
            }

            _protoList.Add(proto);
        }

        public static void Clear()
        {
            _protoList.Clear();
        }
    }
}
