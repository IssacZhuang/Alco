using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Xml;
using Verse;

namespace MuzzleFlash
{
    public class PatchOperationSetModExtension: PatchOperationPathed
	{
		public XmlContainer value;

		private const string AttrClass= "Class";

		protected override bool ApplyWorker(XmlDocument xml)
		{
			XmlNode node = this.value.node;
			bool result = false;
			XmlNodeList targetNodes = xml.SelectNodes(this.xpath);

			if(targetNodes == null || targetNodes.Count == 0)
            {
				Log.Warning("The xPath: \"" + this.xpath + "\" not found");
				return false;
            }

			if(this.value.node.ChildNodes == null)
            {
				Log.Error("The patch for xPath: \"" + this.xpath + "\" has no content");
				return false;
			}

			foreach (XmlNode nodeTarget in targetNodes)
			{
				XmlNode nodeExtensionParent = nodeTarget["modExtensions"];
				if (nodeExtensionParent == null)
				{
					nodeExtensionParent = nodeTarget.OwnerDocument.CreateElement("modExtensions");
					nodeTarget.AppendChild(nodeExtensionParent);
				}

				foreach (XmlNode nodePatch in node.ChildNodes)
				{
					AddOrReplaceNode(nodeExtensionParent, nodePatch);
				}
				result = true;
			}
			return result;
		}

		private void AddOrReplaceNode(XmlNode nodeExtensionParent, XmlNode nodePatch)
        {
			XmlAttribute attrPatch = nodePatch.Attributes[AttrClass];
			foreach (XmlNode existExtension in nodeExtensionParent.ChildNodes)
            {
				XmlAttribute attrExist = existExtension.Attributes[AttrClass];
				if (attrExist == null) continue;
				if (attrExist.Value == attrPatch?.Value)
                {
					Log.Message("Duplicated extesion found, removing: " + attrExist.Value);
					nodeExtensionParent.RemoveChild(existExtension);

				}

			}
			nodeExtensionParent.AppendChild(this.value.node.OwnerDocument.ImportNode(nodePatch, true));
		}
	}
}
