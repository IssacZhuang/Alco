using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

using Vocore;


public unsafe static class CrossReferenceResolver
{
    private static readonly MethodInfo methodGeneric = typeof(CrossReferenceResolver).GetMethod("GetConfigGeneric", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly Dictionary<Type, MethodInfo> methodTyped = new Dictionary<Type, MethodInfo>();
    private static readonly List<CrossReferenceRecord> _records = new List<CrossReferenceRecord>();

    private struct CrossReferenceRecord
    {
        public BaseConfig config;
        public FieldInfo field;
        public string refConfigName;
    }

    public static void ResolveCrossReference(IList<BaseConfig> configs)
    {
        List<CrossReferenceRecord>[] records = new List<CrossReferenceRecord>[configs.Count];

        for (int i = 0; i < configs.Count; i++)
        {
            records[i] = new List<CrossReferenceRecord>();
        }

        Parallel.For(0, configs.Count, i =>
        {
            records[i].AddRange(GetRecord(configs[i]));
        });

        foreach (var list in records)
        {
            _records.AddRange(list);
        }

        Resolve();
    }

    private static IEnumerable<CrossReferenceRecord> GetRecord(BaseConfig config)
    {
        foreach (FieldInfo field in config.GetType().GetFields())
        {
            Type type = field.FieldType;
            if (type == typeof(BaseConfig) || type.IsSubclassOf(typeof(BaseConfig)))
            {
                BaseConfig virtualConfig = (BaseConfig)field.GetValue(config);

                if (virtualConfig != null)
                {
                    yield return new CrossReferenceRecord
                    {
                        config = config,
                        field = field,
                        refConfigName = virtualConfig.name
                    };
                }
            }
        }
    }

    public static void Clear()
    {
        _records.Clear();
    }

    private static BaseConfig GetConfigGeneric<T>(string name) where T : BaseConfig
    {
        return Database<T>.Get(name, false);
    }

    private static BaseConfig GetConfig(Type type, string name)
    {
        if (!methodTyped.TryGetValue(type, out MethodInfo method))
        {
            method = methodGeneric.MakeGenericMethod(type);
            methodTyped.Add(type, method);
        }

        

        return (BaseConfig)method.Invoke(null, new object[] { name });
    }

    private static void Resolve()
    {
        foreach (CrossReferenceRecord record in _records)
        {
            BaseConfig refConfig = GetConfig(record.field.FieldType, record.refConfigName);
            if (refConfig == null)
            {
                Log.Error(Text_Config.CrossRefConfigConfigNotFound(record.config.name, record.config.name, record.field.FieldType.Name, record.refConfigName));
                continue;
            }

            record.field.SetValue(record.config, refConfig);
        }
    }
}
