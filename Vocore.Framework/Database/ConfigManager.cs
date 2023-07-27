using System;
using System.Reflection;

namespace Vocore
{

    public static class ConfigManager
    {
        private static StaticGenericMethodCache _methodCachde_AddConfig = new StaticGenericMethodCache(typeof(ConfigManager), nameof(AddConfig));
        private static StaticGenericMethodCache _methodCachde_UpdateConfig = new StaticGenericMethodCache(typeof(ConfigManager), nameof(UpdateConfig));

        public static void LoadFolder(string folderPath)
        {
            ConfigLoader loader = new ConfigLoader();
            loader.Load(folderPath);
            foreach (var config in loader.Content)
            {
                AddConfigByType(config);
            }
        }

        private static void AddConfigByType(BaseConfig config)
        {
            _methodCachde_AddConfig.GetMethod(config.GetType()).Invoke(null, new object[] { config });
        }

        private static void UpdateConfigByType(BaseConfig config)
        {
            _methodCachde_UpdateConfig.GetMethod(config.GetType()).Invoke(null, new object[] { config });
        }

        public static void UpdateConfig<T>(T config) where T : BaseConfig
        {
            ConfigDB<T>.HotUpdate(config);
        }

        public static void AddConfig<T>(T config) where T : BaseConfig
        {

            ConfigDB<T>.Add(config);
        }

        public static T GetConfig<T>(string name, bool exceptionOnNotFound = true) where T : BaseConfig
        {

            return ConfigDB<T>.Get(name, exceptionOnNotFound);
        }
    }
}
