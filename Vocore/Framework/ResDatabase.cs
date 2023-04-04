using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vocore
{
    public static class ResDatabase<T> where T : ResBase
    {
        private readonly static List<T> _resList = new List<T>();

        public static int Count => _resList.Count;
        public static IEnumerable<T> AllRes => _resList;

        public static void Load(T proto)
        {
            if (_resList.AsParallel().Any(x => x == proto || x.name == proto.name))
            {
                throw ExceptionFramework.ResAlreadyExist(proto.name);
            }

            _resList.Add(proto);
        }

        public static void Clear()
        {
            _resList.Clear();
        }
    }
}
