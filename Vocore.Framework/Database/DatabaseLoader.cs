// using System;
// using System.Reflection;
// using System.Collections.Generic;

// using UnityEngine;
// using UnityEngine.Diagnostics;
// using Vocore;
// using System.Xml;

// public static class DatabaseLoader
// {
//     private static readonly StaticGenericMothodHelper methodAddToDatabase = new StaticGenericMothodHelper(typeof(DatabaseLoader), "AddToDatabseGeneric");
//     private static readonly StaticGenericMothodHelper methodHotUpdateConfig = new StaticGenericMothodHelper(typeof(DatabaseLoader), "HotUpdateConfigGeneric");
//     private static readonly StaticGenericMothodHelper methodRegisterParser = new StaticGenericMothodHelper(typeof(DatabaseLoader), "RegisterParserGeneric");

//     private static int count = 0;

//     /// <summary>
//     /// 加载所有配置
//     /// </summary>
//     public static void Load()
//     {
//         TryRegisterConfigParser();

//         Dictionary<Type, List<BaseConfig>> content = new Dictionary<Type, List<BaseConfig>>();
//         ConfigLoader loader = new ConfigLoader();
//         count = 0;

//         loader.Load();
//         XmlParser.ClearErrors();

//         foreach (BaseConfig config in loader.Content)
//         {
//             Type type = config.GetType();
//             if (!content.TryGetValue(type, out List<BaseConfig> list))
//             {
//                 list = new List<BaseConfig>();
//                 content.Add(type, list);
//             }
//             list.Add(config);
//         }

//         foreach (var pair in content)
//         {
//             Clear(pair.Key);
//             foreach (BaseConfig config in pair.Value)
//             {
//                 try
//                 {
//                     AddToDatabse(config);
//                 }
//                 catch (Exception e)
//                 {
//                     Debug.LogError(e);
//                 }
//             }
//         }

//         CrossReferenceResolver.Clear();
//         CrossReferenceResolver.ResolveCrossReference(loader.Content);

//         foreach (string error in XmlParser.GetErrors())
//         {
//             Debug.LogError(error);
//         }

//         Debug.Log(TextColor.Green(string.Format("加载了 {0} 个配置", count)));
//     }

//     /// <summary>
//     /// 热更新所有配置
//     /// </summary>
//     public static void HotUpdate()
//     {
//         Dictionary<Type, List<BaseConfig>> content = new Dictionary<Type, List<BaseConfig>>();
//         ConfigLoader loader = new ConfigLoader();

//         loader.Load();

//         foreach (BaseConfig config in loader.Content)
//         {
//             Type type = config.GetType();
//             if (!content.TryGetValue(type, out List<BaseConfig> list))
//             {
//                 list = new List<BaseConfig>();
//                 content.Add(type, list);
//             }
//             list.Add(config);
//         }

//         CrossReferenceResolver.Clear();
//         CrossReferenceResolver.ResolveCrossReference(loader.Content);

//         foreach (var pair in content)
//         {
//             foreach (BaseConfig config in pair.Value)
//             {
//                 try
//                 {
//                     HotUpdateConfig(config);
//                 }
//                 catch (Exception e)
//                 {
//                     Debug.LogError(e);
//                 }
//             }
//         }
//     }

//     private static void AddToDatabse(BaseConfig config)
//     {
//         AddToDatabse(config.GetType(), config);
//     }

//     private static void AddToDatabse(Type type, BaseConfig config)
//     {
//         MethodInfo method = methodAddToDatabase.GetMethod(type);
//         method.Invoke(null, new object[] { config });
//     }

//     private static void AddToDatabseGeneric<T>(T config) where T : BaseConfig
//     {
//         try
//         {
//             Database<T>.Add(config);
//             count++;
//         }
//         catch (Exception e)
//         {
//             Debug.LogError(e.ToString());
//         }
//     }

//     private static void HotUpdateConfig(BaseConfig config)
//     {
//         HotUpdateConfig(config.GetType(), config);
//     }

//     private static void HotUpdateConfig(Type type, BaseConfig config)
//     {
//         MethodInfo method = methodHotUpdateConfig.GetMethod(type);
//         method.Invoke(null, new object[] { config });
//     }

//     private static void HotUpdateConfigGeneric<T>(T config) where T : BaseConfig
//     {
//         try
//         {
//             Database<T>.HotUpdate(config);
//             count++;
//         }
//         catch (Exception e)
//         {
//             Debug.LogError(e.ToString());
//         }
//     }

//     public static void Clear(Type type)
//     {
//         MethodInfo methodClearDatabase = typeof(DatabaseLoader).GetMethod("ClearDatabse", BindingFlags.NonPublic | BindingFlags.Static);
//         MethodInfo methodClearDatabaseTyped = methodClearDatabase.MakeGenericMethod(type);
//         methodClearDatabaseTyped.Invoke(null, null);
//     }

//     private static void ClearDatabse<T>() where T : BaseConfig
//     {
//         Database<T>.Clear();
//     }

//     private static void TryRegisterConfigParser()
//     {
//         if (!UtilsParse.HasParser<EventId>())
//         {
//             UtilsParse.RegisterParser<EventId>((string str) =>
//             {
//                 try
//                 {
//                     EventId eventId = EventManager.GetCachedEvent(str);
//                     return eventId;
//                 }
//                 catch (Exception e)
//                 {
//                     if (!Application.isEditor)
//                     {
//                         Debug.LogError(e);
//                     }
//                 }
//                 return default(EventId);
//             });
//         }

//         //register all sub class of BaseConfig
//         RegisterParserGeneric<BaseConfig>();
//         Type[] types = typeof(BaseConfig).Assembly.GetTypes();
//         foreach (Type type in types)
//         {
//             if (type.IsSubclassOf(typeof(BaseConfig)))
//             {
//                 RegisterParser(type);
//             }
//         }
//     }

//     private static void RegisterParser(Type type)
//     {
//         XmlParser.DisableParserOnRoot(type);
//         MethodInfo method = methodRegisterParser.GetMethod(type);
//         method.Invoke(null, new object[] { });
//     }

//     private static void RegisterParserGeneric<T>() where T : BaseConfig
//     {
//         if (!UtilsParse.HasParser<T>())
//         {
//             UtilsParse.RegisterParser<T>(ConfigParser<T>);
//         }
//     }

//     private static T ConfigParser<T>(string str) where T : BaseConfig
//     {
//         T config = Activator.CreateInstance<T>();
//         config.name = str;
//         return config;
//     }
// }
