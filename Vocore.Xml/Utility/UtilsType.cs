using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Vocore.Xml
{
    public static class UtilsType
    {
        private static Dictionary<Type, bool> _isListCache = new Dictionary<Type, bool>();
        private static Dictionary<Type, bool> _isDictionaryCache = new Dictionary<Type, bool>();
        private static Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();

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
			_isListCache.Add(type, result);
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
            _isDictionaryCache.Add(type, result);
            return result;
        }

        /// <summary>
        /// Get the type from all loaded assemblies.
        /// </summary>
        public static Type GetTypeFromAllAssemblies(string typeName, bool throwOnError = false)
        {
            if(_typeCache.TryGetValue(typeName, out Type type))
            {
                return type;
            }
            
            type = Type.GetType(typeName);
            if (type != null)
            {
                _typeCache.Add(typeName, type);
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

            if (throwOnError)
            {
                throw new Exception("Type not found: " + typeName);
            }

            return null;
        }

    }
}


