using System;
using System.Reflection;

namespace Vocore
{
    public class GenericDatabaseAcess
    {
        private readonly Type _type;

        private readonly MethodInfo _funcAdd;
        private readonly MethodInfo _funcGet;
        private readonly MethodInfo _funcHotUpdate;
        private static BindingFlags _flags = BindingFlags.Public | BindingFlags.Static;

        public GenericDatabaseAcess(Type type)
        {
            if (!type.IsSubclassOf(typeof(BaseConfig)))
            {
                throw new ArgumentException("Type must be a subclass of BaseConfig");
            }
            _type = type;
            Type typedDatabase = typeof(ConfigDB<>).MakeGenericType(type);
            _funcAdd = typedDatabase.GetMethod("Add", _flags);
            _funcGet = typedDatabase.GetMethod("Get", _flags);
            _funcHotUpdate = typedDatabase.GetMethod("HotUpdate", _flags);
        }

        public void Add(BaseConfig config)
        {
            _funcAdd.Invoke(null, new object[] { config });
        }

        public BaseConfig Get(string name)
        {
            return (BaseConfig)_funcGet.Invoke(null, new object[] { name });
        }

        public void HotUpdate(BaseConfig config)
        {
            _funcHotUpdate.Invoke(null, new object[] { config });
        }
    }
}