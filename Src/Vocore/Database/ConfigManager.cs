using System;
using System.Collections.Generic;
using System.Reflection;

namespace Vocore
{

    public static class ConfigManager
    {
        private static readonly Dictionary<Type, GenericDatabaseAcess> _databaseAccess = new Dictionary<Type, GenericDatabaseAcess>();

        public static void LoadFolder(string folderPath)
        {
            ConfigLoader loader = new ConfigLoader();
            loader.Load(folderPath);
            AddConfigs(loader.Content);
        }

        public static void AddConfigs(IList<BaseConfig> configs)
        {
            foreach (var config in configs)
            {
                AddConfig(config);
            }
        }

        public static void AddConfig<T>(T config) where T : BaseConfig
        {
            ConfigDB<T>.Add(config);
        }

        public static T GetConfig<T>(string name, bool exceptionOnNotFound = true) where T : BaseConfig
        {
            return ConfigDB<T>.Get(name, exceptionOnNotFound);
        }

        public static void HotUpdate<T>(T config) where T : BaseConfig
        {
            ConfigDB<T>.HotUpdate(config);
        }


        private static void AddConfig(BaseConfig config)
        {
            GetDatabaseAccess(config.GetType()).Add(config);
        }

        public static void HotUpdate(BaseConfig config)
        {
            GetDatabaseAccess(config.GetType()).HotUpdate(config);
        }


        private static GenericDatabaseAcess GetDatabaseAccess(Type type)
        {
            if (!_databaseAccess.ContainsKey(type))
            {
                _databaseAccess.Add(type, new GenericDatabaseAcess(type));
            }
            return _databaseAccess[type];
        }
    }
}
