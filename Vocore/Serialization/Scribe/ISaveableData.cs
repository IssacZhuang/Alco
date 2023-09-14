using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Vocore
{
    public interface ISaveableData
    {
        IEnumerable<ISaveableData> Childs { get; }
        ScribeMode Mode { get; }
        void AddChild(ISaveableData node);
        XmlNode ToXmlNode();
    }
}