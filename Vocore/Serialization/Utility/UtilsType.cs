using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Vocore
{
    public static class UtilsType
    {

        private static Dictionary<Type, bool> _isListCache = new Dictionary<Type, bool>();
        private static object _lockListCache = new object();
        private static Dictionary<Type, bool> _isDictionaryCache = new Dictionary<Type, bool>();
        private static object _lockDictionaryCache = new object();
        private static Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();
        private static object _lockTypeCache = new object();

        private static List<string> defaultNamespaces = new List<string>{
            "Vocore",
            "System"
        };
        private static object _lockDefaultNamespaces = new object();

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
        public static bool IsDictionary(this Type type)
        {
            bool result;
            if (_isDictionaryCache.TryGetValue(type, out result))
            {
                return result;
            }
            result = IsGenericTypeOf(type, typeof(Dictionary<,>));
            AddIsDictionaryCache(type, result);
            return result;
        }

        public static object CreateKeyValuePair(Type keyType, Type valueType, object key, object value)
        {
            return Activator.CreateInstance(typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType), new object[]{key,value});
        }

        public static object CreateDictionaty(Type keyType, Type valueType)
        {
            return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType));
        }

        /// <summary>
        /// Get the type from all loaded assemblies.
        /// </summary>
        public static Type GetTypeFromAllAssemblies(string typeName)
        {
            if (_typeCache.TryGetValue(typeName, out Type type))
            {
                return type;
            }

            AppDomain appDomain = AppDomain.CurrentDomain;
            var types = appDomain.GetAssemblies().SelectMany<Assembly, Type>((Assembly asm) => asm.GetTypes()).AsParallel().Where(t => t.FullName == typeName || (t.Name == typeName && defaultNamespaces.Contains(t.Namespace)));

            if (types.Count() > 1)
            {
                string error = "Duplicated types found for " + typeName + " : ";

                foreach (var item in types)
                {
                    error += item.FullName + ", ";
                }
                throw new Exception(error);
            }

            type = types.FirstOrDefault();
            if (type != null)
            {
                AddTypeCache(typeName, type);
                return type;
            }

            return null;
        }


        /// <summary>
        /// Add a default namespace to search for types. This is used when the type name is not fully qualified.
        /// </summary>
        public static bool AddDefaultNamespace(string nameSpace)
        {
            lock (_lockDefaultNamespaces)
            {
                if (!defaultNamespaces.Contains(nameSpace))
                {
                    defaultNamespaces.Add(nameSpace);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Clear the cache including the default namespaces, the type cache, the list cache and the dictionary cache.
        /// </summary>
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
            lock (_lockDefaultNamespaces)
            {
                defaultNamespaces = new List<string>{
                    "Vocore",
                    "System"
                };
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


