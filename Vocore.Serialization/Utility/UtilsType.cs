using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Vocore.Serialization
{
    public static class UtilsType
    {
        private static Dictionary<Type, bool> _isListCache = new Dictionary<Type, bool>();
        private static object _lockListCache = new object();
        private static Dictionary<Type, bool> _isDictionaryCache = new Dictionary<Type, bool>();
        private static object _lockDictionaryCache = new object();
        private static Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();
        private static object _lockTypeCache = new object();

        /// <summary>
        /// Check if a type is a generic type of another type.
        /// </summary>
        public static bool IsGenericTypeOf(this Type type, Type genericType)
        {
            if (type == null || genericType == null)
            {
                return false;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == genericType)
            {
                return true;
            }

            return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericType);
        }


        /// <summary>
        /// Check if a type is a list.
        /// </summary>
        public static bool IsList(this Type type)
		{
			bool result;
			if (_isListCache.TryGetValue(type, out result))
			{
				return result;
			}
		    result = IsGenericTypeOf(type, typeof(List<>));
			AddIsListCache(type, result);
			return result;
		}


        /// <summary>
        /// Check if a type is a dictionary.
        /// </summary>
        public static bool IsDictionary(this Type type){
            bool result;
            if (_isDictionaryCache.TryGetValue(type, out result))
            {
                return result;
            }
            result = IsGenericTypeOf(type, typeof(Dictionary<,>));
            AddIsDictionaryCache(type, result);
            return result;
        }

        /// <summary>
        /// Get the type from all loaded assemblies.
        /// </summary>
        public static Type GetTypeFromAllAssemblies(string typeName)
        {
            if(_typeCache.TryGetValue(typeName, out Type type))
            {
                return type;
            }
            
            type = Type.GetType(typeName);
            if (type != null)
            {
                AddTypeCache(typeName, type);
                return type;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null)
                {
                    _typeCache.Add(typeName, type);
                    return type;
                }
            }

            return null;
        }

        public static Type GetTypeFromAllAssemblies(string typeName, string defaultNamespace)
        {
            Type type = GetTypeFromAllAssemblies(typeName);
            if (type != null)
            {
                return type;
            }

            if (defaultNamespace != null)
            {
                type = GetTypeFromAllAssemblies(defaultNamespace + "." + typeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }


        public static void ClearCache()
        {
            lock (_lockListCache)
            {
                _isListCache.Clear();
            }
            lock (_lockDictionaryCache)
            {
                _isDictionaryCache.Clear();
            }
            lock (_lockTypeCache)
            {
                _typeCache.Clear();
            }
        }

        private static bool AddIsListCache(Type type, bool result)
        {
            lock (_lockListCache)
            {
                if (!_isListCache.ContainsKey(type))
                {
                    _isListCache.Add(type, result);
                    return true;
                }
            }
            return false;
        }

        private static bool AddIsDictionaryCache(Type type, bool result)
        {
            lock (_lockDictionaryCache)
            {
                if (!_isDictionaryCache.ContainsKey(type))
                {
                    _isDictionaryCache.Add(type, result);
                    return true;
                }
            }
            return false;
        }

        private static bool AddTypeCache(string typeName, Type type)
        {
            lock (_lockTypeCache)
            {
                if (!_typeCache.ContainsKey(typeName))
                {
                    _typeCache.Add(typeName, type);
                    return true;
                }
            }
            return false;
        }

    }
}


