using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace Vocore
{
    public static class XmlParser
    {
        public const int recursionLimit = 20;

        private static List<string> _errors = new List<string>();
        private static object _lock = new object();

        /// <summary>
        /// Parse a XML node to an object
        /// </summary>
        public static object ParseToObject(this XmlNode xml)
        {
            if (xml == null)
            {
                AddError("Xml node is null");
                return null;
            }
            Type typeNode = null;
            try
            {
                typeNode = UtilsType.GetTypeFromAllAssemblies(xml.Name);
            }
            catch (Exception e)
            {
                AddError("Error when getting type from name: " + xml.Name + "\n" + e);
                return null;
            }
            Type typeAttrClass = null;
            XmlAttribute attr = xml.Attributes[ConstField.Class];

            if (attr != null)
            {
                try
                {
                    typeAttrClass = UtilsType.GetTypeFromAllAssemblies(attr.Value);
                }
                catch (Exception e)
                {
                    AddError("Error when getting type from name: " + attr.Value + "\n" + e);
                    return null;
                }
            }


            Type typeFinal = null;
            if (typeAttrClass == null)
            {
                typeFinal = typeNode;
            }
            else if (typeAttrClass.IsSubclassOf(typeNode))
            {
                typeFinal = typeAttrClass;
            }
            else
            {
                typeFinal = typeNode;
                AddError("Type " + typeAttrClass.Name + " is not a subclass of " + typeNode.Name);
            }

            if (typeFinal == null)
            {
                AddError("Type not found: " + (attr != null ? attr.Value : xml.Name) + "\nRaw xml text: \n" + xml.GetXmlTextFormated());
                return null;
            }

            return ObjectFromXml(typeFinal, xml);
        }

        /// <summary>
        /// Parse a XML node to an object with a specific type
        /// </summary>
        public static object ObjectFromXml(Type type, XmlNode xml, int depth = 0)
        {
            if (depth > recursionLimit)
            {
                AddError("Recursion limit reached, type: " + type.Name + "\nRaw xml text: \n" + xml.GetXmlTextFormated());
                return null;
            }

            try
            {
                if (UtilsParse.TryParse(xml.InnerText, type, out Object objParsed))
                {
                    return objParsed;
                }
            }
            catch (Exception e)
            {
                AddError("Parse failed for node: '" + xml.Name + "' in type: '" + type.Name + "', value: " + xml.InnerText + ", error: \n" + e + "\nRaw xml text: \n" + xml.GetXmlTextFormated());
            }


            if (type.IsList())
            {
                Type listType = type.GetGenericArguments()[0];
                IList list = (IList)Activator.CreateInstance(type);
                foreach (XmlNode child in xml.ChildNodes)
                {
                    object childObj = ObjectFromXml(listType, child, depth + 1);
                    list.Add(childObj);
                }
                return list;
            }

            if (type.IsDictionary())
            {
                Type[] genericTypes = type.GetGenericArguments();
                Type keyType = genericTypes[0];
                Type valueType = genericTypes[1];
                IDictionary dict = (IDictionary)UtilsType.CreateDictionaty(keyType, valueType);
                foreach (XmlNode child in xml.ChildNodes)
                {
                    try
                    {
                        if (UtilsParse.TryParse(child.Name, keyType, out Object key))
                        {
                            object value = ObjectFromXml(valueType, child, depth + 1);
                            dict.Add(key, value);
                        }
                        else
                        {
                            AddError("Parse failed for node: '" + xml.Name + "' in type: '" + type.Name + "', value: " + xml.InnerText + "\nRaw xml text: \n" + xml.GetXmlTextFormated());
                        }
                    }
                    catch (Exception e)
                    {
                        AddError("Parse failed for node: '" + xml.Name + "' in type: '" + type.Name + "', value: " + xml.InnerText + ", error: \n" + e + "\nRaw xml text: \n" + xml.GetXmlTextFormated());
                    }
                }
                return dict;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                Type[] genericTypes = type.GetGenericArguments();
                Type keyType = genericTypes[0];
                Type valueType = genericTypes[1];
                if (UtilsParse.TryParse(xml.Name, keyType, out Object key))
                {
                    object value = ObjectFromXml(valueType, xml, depth + 1);
                    return UtilsType.CreateKeyValuePair(keyType, valueType, key, value);
                }
                AddError("Parse failed for node: '" + xml.Name + "' in type: '" + type.Name + "', value: " + xml.InnerText + "\nRaw xml text: \n" + xml.GetXmlTextFormated());
            }

            if (type.IsEnum)
            {
                Object objEnum = Enum.Parse(type, xml.InnerText);
                if (objEnum == null)
                {
                    AddError("Enum parse failed for node: '" + xml.Name + "' in type: '" + type.Name + "', value: " + xml.InnerText + "\nRaw xml text: \n" + xml.GetXmlTextFormated());
                }
                return objEnum;
            }

            //is class or is struct
            if (type.IsClass || type.IsValueType)
            {
                Dictionary<string, XmlNode> nodeUnused = new Dictionary<string, XmlNode>();
                foreach (XmlNode node in xml.ChildNodes)
                {
                    if (node.NodeType != XmlNodeType.Element)
                    {
                        continue;
                    }

                    if (nodeUnused.ContainsKey(node.Name))
                    {
                        AddError("Duplicated node found for field '" + node.Name + "' in type '" + type.Name + "', only the first node will be parsed.\nRaw xml text: \n" + node.GetXmlTextFormated());
                        continue;
                    }
                    nodeUnused.Add(node.Name, node);
                }

                object result = Activator.CreateInstance(type);
                foreach (FieldInfo field in type.GetFields())
                {
                    XmlNodeList nodes = xml.SelectNodes(field.Name);
                    if (nodes == null || nodes.Count == 0)
                    {
                        continue;
                    }

                    XmlNode node = nodes[0];

                    nodeUnused.Remove(field.Name);

                    Type fieldType = field.FieldType;
                    object subObj = ObjectFromXml(fieldType, node, depth + 1);
                    field.SetValue(result, subObj);
                }

                foreach (XmlNode node in nodeUnused.Values)
                {
                    AddError("Unused node found: '" + node.Name + "' in type: " + type.Name + "\nRaw xml text: \n" + xml.GetXmlTextFormated());
                }
                return result;
            }

            return null;
        }


        /// <summary>
        /// Parse a XML node to an object with a specific type
        /// </summary>
        public static T ObjectFromXml<T>(XmlNode xml)
        {
            return (T)ObjectFromXml(typeof(T), xml, 0);
        }


        /// <summary>
        /// Get XML text with line breaks
        /// </summary>
        public static string GetXmlTextFormated(this XmlNode xml)
        {
            if (xml == null)
            {
                return "";
            }
            return xml.OuterXml.Replace("><", ">\n<");
        }

        /// <summary>
        /// Get all errors during the parsing
        /// </summary>
        public static IEnumerable<string> GetErrors()
        {
            foreach (string error in _errors)
            {
                yield return error;
            }
        }

        /// <summary>
        /// Clear cached errors
        /// </summary>
        public static void ClearErrors()
        {
            lock (_lock)
            {
                _errors.Clear();
            }
        }

        private static void AddError(string error)
        {
            lock (_lock)
            {
                _errors.Add(error);
            }
        }


    }
}


