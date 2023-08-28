using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace Vocore
{
    public class XmlParser
    {
        public const int recursionLimit = 20;
        private object _lock = new object();

        private ParseHelper _parseHelper;
        private TypeHelper _typeHelper;

        public XmlParser()
        {
            _parseHelper = new ParseHelper();
           _typeHelper = _parseHelper.TypeHelper;
        }

        public XmlParser(ParseHelper parseHelper)
        {
            if(parseHelper == null)
                throw new ArgumentNullException(nameof(parseHelper));
            _parseHelper = parseHelper;
            _typeHelper = parseHelper.TypeHelper;
        }

        public XmlParser(params string[] defaultNamespaces)
        {
            _parseHelper = new ParseHelper(defaultNamespaces);
            _typeHelper = _parseHelper.TypeHelper;
        }


        /// <summary>
        /// Parse a XML node to an object
        /// </summary>
        public object ParseToObject(XmlNode xml)
        {
            if (xml == null)
            {
                AddError("Xml node is null");
                return null;
            }
            Type typeNode = null;
            try
            {
                typeNode = _typeHelper.GetTypeFromAllAssemblies(xml.Name);
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
                    typeAttrClass = _typeHelper.GetTypeFromAllAssemblies(attr.Value);
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
                AddError("Type not found: " + (attr != null ? attr.Value : xml.Name) + "\nRaw xml text: \n" + GetXmlTextFormated(xml));
                return null;
            }

            return ObjectFromXml(typeFinal, xml);
        }

        /// <summary>
        /// Parse a XML node to an object with a specific type
        /// </summary>
        public object ObjectFromXml(Type type, XmlNode xml, int depth = 0)
        {
            if (depth > recursionLimit)
            {
                AddError("Recursion limit reached, type: " + type.Name + "\nRaw xml text: \n" + GetXmlTextFormated(xml));
                return null;
            }

            try
            {
                if (_parseHelper.TryParseStr(xml.InnerText, type, out Object objParsed))
                {
                    return objParsed;
                }
            }
            catch (Exception e)
            {
                AddError("Parse failed for node: '" + xml.Name + "' in type: '" + type.Name + "', value: " + xml.InnerText + ", error: \n" + e + "\nRaw xml text: \n" + GetXmlTextFormated(xml));
            }


            if (_typeHelper.IsList(type))
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

            if (_typeHelper.IsDictionary(type))
            {
                Type[] genericTypes = type.GetGenericArguments();
                Type keyType = genericTypes[0];
                Type valueType = genericTypes[1];
                IDictionary dict = (IDictionary)_typeHelper.CreateDictionaty(keyType, valueType);
                foreach (XmlNode child in xml.ChildNodes)
                {
                    try
                    {
                        if (_parseHelper.TryParseStr(child.Name, keyType, out Object key))
                        {
                            object value = ObjectFromXml(valueType, child, depth + 1);
                            dict.Add(key, value);
                        }
                        else
                        {
                            AddError("Parse failed for node: '" + xml.Name + "' in type: '" + type.Name + "', value: " + xml.InnerText + "\nRaw xml text: \n" + GetXmlTextFormated(xml));
                        }
                    }
                    catch (Exception e)
                    {
                        AddError("Parse failed for node: '" + xml.Name + "' in type: '" + type.Name + "', value: " + xml.InnerText + ", error: \n" + e + "\nRaw xml text: \n" + GetXmlTextFormated(xml));
                    }
                }
                return dict;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                Type[] genericTypes = type.GetGenericArguments();
                Type keyType = genericTypes[0];
                Type valueType = genericTypes[1];
                if (_parseHelper.TryParseStr(xml.Name, keyType, out Object key))
                {
                    object value = ObjectFromXml(valueType, xml, depth + 1);
                    return _typeHelper.CreateKeyValuePair(keyType, valueType, key, value);
                }
                AddError("Parse failed for node: '" + xml.Name + "' in type: '" + type.Name + "', value: " + xml.InnerText + "\nRaw xml text: \n" + GetXmlTextFormated(xml));
            }

            if (type.IsEnum)
            {
                Object objEnum = Enum.Parse(type, xml.InnerText);
                if (objEnum == null)
                {
                    AddError("Enum parse failed for node: '" + xml.Name + "' in type: '" + type.Name + "', value: " + xml.InnerText + "\nRaw xml text: \n" + GetXmlTextFormated(xml));
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
                        AddError("Duplicated node found for field '" + node.Name + "' in type '" + type.Name + "', only the first node will be parsed.\nRaw xml text: \n" + GetXmlTextFormated(node));
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
                    AddError("Unused node found: '" + node.Name + "' in type: " + type.Name + "\nRaw xml text: \n" + GetXmlTextFormated(xml));
                }
                return result;
            }

            return null;
        }


        /// <summary>
        /// Parse a XML node to an object with a specific type
        /// </summary>
        public T ObjectFromXml<T>(XmlNode xml)
        {
            return (T)ObjectFromXml(typeof(T), xml, 0);
        }


        /// <summary>
        /// Get XML text with line breaks
        /// </summary>
        public string GetXmlTextFormated(XmlNode xml)
        {
            if (xml == null)
            {
                return "";
            }
            return xml.OuterXml.Replace("><", ">\n<");
        }

        private void AddError(string error)
        {
            Log.Error(error);
        }


    }
}


