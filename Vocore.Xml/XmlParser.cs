using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace Vocore.Xml
{
    public class XmlParser
    {
        public static T ObjectFromXml<T>(XmlNode xml)
        {
            Type type = typeof(T);
            T obj = (T)Activator.CreateInstance(type);
            //iterate through the fields of the object and parse the xml
            foreach (FieldInfo field in type.GetFields())
            {
                //get the xml node that matches the field name
                XmlNode node = xml.SelectSingleNode(field.Name);
                Type fieldType = field.FieldType;
                
                
            }

            
           return obj;
        }
    }
}


