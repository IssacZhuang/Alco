using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml;

namespace Vocore
{
    public class XmlReference : FileReference
    {
        public XmlReference(string path) : base(path)
        {
        }

        public XmlDocument LoadXml()
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(base.GetString());
            return doc;
        }

        public Task LoadXmlAsync(Action<XmlDocument> success, Action<Exception> fail = null)
        {
            return base.LoadStringAsync((str) =>
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(str);
                success(doc);
            }, fail);
        }
    }
}

