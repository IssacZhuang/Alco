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
		protected override bool ApplyWorker(XmlDocument xml)
		{
			XmlNode node = this.value.node;
			bool result = false;
			XmlNodeList targetNodes = xml.SelectNodes(this.xpath);

			if(targetNodes == null || targetNodes.Count == 0)
            {
				Log.Warning("xPath: \"" + this.xpath + "\" not found");
				return false;
            }

			if(this.value.node.ChildNodes == null)
            {
				Log.Error("the patch for xPath: \"" + this.xpath + "\" has no content");
				return false;
			}

			foreach (object objTarget in targetNodes)
			{
				XmlNode nodeTarget = objTarget as XmlNode;
				XmlNode nodeExtension = nodeTarget["modExtensions"];
				if (nodeExtension == null)
				{
					nodeExtension = nodeTarget.OwnerDocument.CreateElement("modExtensions");
					nodeTarget.AppendChild(nodeExtension);
				}
				foreach (object objPatch in node.ChildNodes)
				{
					XmlNode nodePatch = (XmlNode)objPatch;
					Log.Message("node name: "+nodePatch.Name);
					Log.Message("node class: " + nodePatch.Attributes["Class"]);
					nodeExtension.AppendChild(nodeTarget.OwnerDocument.ImportNode(nodePatch, true));
				}
				result = true;
			}
			return result;
		}



		private XmlContainer value;
	}
}
