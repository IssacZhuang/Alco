using System;
using System.IO;
using System.Xml;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Reflection;

namespace Vocore
{
    public class ConfigLoader
    {
        public const string XPathRootNode = "Data";
        public const string FileSearchPattern = "*.xml";

        private List<BaseConfig> _content = new List<BaseConfig>();
        private XmlParser _xmlParser;
        private ParseHelper _parseHelper;
        public List<BaseConfig> Content => _content;

        public ConfigLoader(params string[] defaultNamespaces)
        {
            _parseHelper = new ParseHelper(defaultNamespaces);
            _xmlParser = new XmlParser(_parseHelper);
        }

        public void Load(string dicrectory)
        {
            _content.Clear();
            string[] files = Directory.GetFiles(dicrectory, FileSearchPattern, SearchOption.AllDirectories);
            List<BaseConfig>[] content = new List<BaseConfig>[files.Length];

            for (int i = 0; i < files.Length; i++)
            {
                content[i] = new List<BaseConfig>();
            }

            Parallel.For(0, files.Length, i =>
            {
                LoadFile(files[i], content[i]);
            });

            foreach (var list in content)
            {
                _content.AddRange(list);
            }
        }

        private void LoadFile(string path, List<BaseConfig> collector)
        {
            if (!File.Exists(path))
            {
                return;
            }

            string xmlContent = File.ReadAllText(path);
            LoadXML(xmlContent, collector);
        }

        private void LoadXML(string xmlText, List<BaseConfig> collector)
        {
            try
            {
                //To xml document
                XmlDocument xmlDocument = new XmlDocument();
                xmlDocument.LoadXml(xmlText);

                XmlNode rootNode = xmlDocument.SelectSingleNode(XPathRootNode);

                foreach (XmlNode node in rootNode.ChildNodes)
                {
                    try
                    {
                        object obj = _xmlParser.ParseToObject(node);

                        BaseConfig config = obj as BaseConfig;

                        if (config == null)
                        {
                            Log.Error(Text_Config.CannotParseToConfig(node.OuterXml));
                            continue;
                        }

                        collector.Add(config);
                    }
                    catch (Exception e)
                    {
                        Log.Error(e.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
            }
        }

        private void RegisterConfiParser()
        {
            Type[] types = typeof(BaseConfig).Assembly.GetTypes();
            foreach (Type type in types)
            {
                if (type.IsSubclassOf(typeof(BaseConfig)))
                {
                    MethodInfo method = typeof(ConfigLoader).GetMethod(nameof(RegisterParser), BindingFlags.NonPublic | BindingFlags.Instance);
                    MethodInfo methodGeneric = method.MakeGenericMethod(type);
                    methodGeneric.Invoke(this, null);
                }
            }
        }

        private void RegisterParser<T>() where T : BaseConfig
        {
            _parseHelper.RegisterStrParser<T>(ConfigParserGeneric<T>);
        }

        private static T ConfigParserGeneric<T>(string str) where T : BaseConfig
        {
            T config = Activator.CreateInstance<T>();
            config.name = str;
            return config;
        }

    }
}

