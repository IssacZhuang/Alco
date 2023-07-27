using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace Vocore
{
    public static class ConfigDB<T> where T : BaseConfig
    {
        private static Dictionary<string, T> _configs = new Dictionary<string, T>();
        private static readonly FieldInfo[] _fields = typeof(T).GetFields();

        public static void Add(T config)
        {
            if (config == null)
            {
                throw new Exception(Text_Config.NullConfig);
            }
            if (string.IsNullOrEmpty(config.name))
            {
                throw new Exception(Text_Config.EmptyConfigName);
            }
            if (_configs.ContainsKey(config.name))
            {
                throw new Exception(Text_Config.DuplicatedConfigName(config.name));
            }

            IEnumerable<string> errors = config.CheckErrors();

            if (errors.Any())
            {
                throw new Exception(Text_Config.ConfigNotCorrect(config.name, string.Join("\n", errors), config.XmlText));
            }

            _configs.Add(config.name, config);
        }

        public static T Get(string name, bool exceptionOnNotFound = true)
        {
            if (_configs.TryGetValue(name, out T config))
            {
                return config;
            }

            if (exceptionOnNotFound)
            {
                throw new Exception(Text_Config.ConfigNotFound(typeof(T), name));
            }

            return null;
        }

        public static void HotUpdate(T config)
        {
            T target = Get(config.name, false);
            if (target == null)
            {
                Add(config);
                return;
            }

            Copy(config, target);
        }

        public static void ClearCacheForAllConfigs()
        {
            foreach (var pair in _configs)
            {
                pair.Value.ClearCache();
            }
        }

        public static void Clear()
        {
            _configs.Clear();
        }

        private static void Copy(T source, T target)
        {
            foreach (var field in _fields)
            {
                field.SetValue(target, field.GetValue(source));
            }
        }
    }
}

