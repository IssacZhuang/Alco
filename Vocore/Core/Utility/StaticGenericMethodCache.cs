using System;
using System.Collections.Generic;
using System.Reflection;

namespace Vocore
{
    public class StaticGenericMethodCache
    {
        private MethodInfo _method;
        private Dictionary<Type, MethodInfo> _cache = new Dictionary<Type, MethodInfo>();

        public StaticGenericMethodCache(MethodInfo method)
        {
            _method = method;
        }

        public StaticGenericMethodCache(Type type, string methodName, BindingFlags bindingFlags = BindingFlags.Static)
        {
            _method = type.GetMethod(methodName, bindingFlags);
        }

        public MethodInfo GetMethod(Type type)
        {
            MethodInfo method;
            if (_cache.TryGetValue(type, out method))
            {
                return method;
            }
            method = _method.MakeGenericMethod(type);
            _cache.Add(type, method);
            return method;
        }
    }
}