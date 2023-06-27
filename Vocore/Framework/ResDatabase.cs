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

        public static void Load(T res)
        {
            if (_resList.AsParallel().Any(x => x == res || x.name == res.name))
            {
                throw ExceptionFramework.ResAlreadyExist(res.name);
            }

            _resList.Add(res);
        }

        public static T Get(string name)
        {
            return _resList.AsParallel().FirstOrDefault(x => x.name == name);
        }

        public static void Clear()
        {
            _resList.Clear();
        }
    }
}
