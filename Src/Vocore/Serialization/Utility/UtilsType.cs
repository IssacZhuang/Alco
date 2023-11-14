using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Vocore
{
    public class TypeHelper
    {
        public static readonly TypeHelper Default = new TypeHelper();
        private Dictionary<Type, bool> _isListCache = new Dictionary<Type, bool>();
        private object _lockListCache = new object();
        private Dictionary<Type, bool> _isDictionaryCache = new Dictionary<Type, bool>();
        private object _lockDictionaryCache = new object();
        private Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();
        private object _lockTypeCache = new object();

        private List<string> defaultNamespaces = new List<string>{
            "Vocore",
            "Vocore.Framework",
            "Vocore.Test",
            "System"
        };
        private readonly object _lockDefaultNamespaces = new object();

        /// <summary>
        /// Check if a type is a generic type of another type.
        /// </summary>
        public bool IsGenericTypeOf(Type type, Type genericType)
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
        public bool IsList(Type type)
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
        public bool IsDictionary(Type type)
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

        public object CreateKeyValuePair(Type keyType, Type valueType, object key, object value)
        {
            return Activator.CreateInstance(typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType), new object[] { key, value });
        }

        public object CreateDictionaty(Type keyType, Type valueType)
        {
            return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType));
        }

        /// <summary>
        /// Get the type from all loaded assemblies.
        /// </summary>
        public Type GetTypeFromAllAssemblies(string typeName)
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
        public bool AddDefaultNamespace(string nameSpace)
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
        public void ClearCache()
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

        private bool AddIsListCache(Type type, bool result)
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

        private bool AddIsDictionaryCache(Type type, bool result)
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

        private bool AddTypeCache(string typeName, Type type)
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


