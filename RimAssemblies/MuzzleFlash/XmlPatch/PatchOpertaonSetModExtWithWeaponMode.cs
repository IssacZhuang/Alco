using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Xml;
using Verse;

using MuzzleFlash;

namespace SafePatcher
{

    public class PatchOpertaonSetModExtWithWeaponMode : PatchOperationReloable
    {
        [Description("Muzzle flash props only")]
        public XmlContainer value;

        private const string AttrClass = "Class";
        private const string ValueWeaponType = "type";

        protected override bool ApplyWorker(XmlDocument xml)
        {
            XmlNode node = this.value.node;
            bool result = false;
            XmlNodeList targetNodes = xml.SelectNodes(this.xpath);

            if (targetNodes == null || targetNodes.Count == 0)
            {
                Log.Warning("[SafePatcher]The xPath: \"" + this.xpath + "\" not found");
                return true;
            }

            if (this.value.node.ChildNodes == null)
            {
                Log.Error("[SafePatcher] The patch has no content");
                return true;
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
                    AddOrReplaceNode(nodeTarget.OwnerDocument, nodeExtensionParent, nodePatch);
                }
                result = true;
            }
            return result;
        }

        private void AddOrReplaceNode(XmlDocument importDest, XmlNode nodeExtensionParent, XmlNode nodePatch)
        {
            XmlAttribute attrPatch = nodePatch.Attributes[AttrClass];
            XmlNode typePatch = nodePatch[ValueWeaponType];
            foreach (XmlNode existExtension in nodeExtensionParent.ChildNodes)
            {
                XmlAttribute attrExist = existExtension.Attributes[AttrClass];
                XmlNode typeExist = existExtension[ValueWeaponType];
                if (attrExist.Value == attrPatch?.Value && typeExist?.InnerText == typePatch?.InnerText)
                {
                    Log.Warning("[SafePatcher] Duplicated extension with same muzzle flash type found, removing: " + attrExist.Value);
                    nodeExtensionParent.RemoveChild(existExtension);
                }

            }
            nodeExtensionParent.AppendChild(importDest.ImportNode(nodePatch, true));
        }

        public override void ApplyToObject(Def def)
        {
            XmlNode nodeValue = this.value.node;
            if (nodeValue == null)
            {
                Log.Message("[SafePatcher] Failed to create mod extension: empty node");
                return;
            }

            foreach (XmlNode nodePatch in nodeValue.ChildNodes)
            {

                XmlAttribute attrClass = nodePatch.Attributes["Class"];
                if (attrClass == null)
                {
                    Log.Message("[SafePatcher] Failed to create mod extension: no class attribute");
                    return;
                }

                Type type = typeof(MuzzleFlashProps);
                var func = DirectXmlToObject.GetObjectFromXmlMethod(type);
                MuzzleFlashProps obj = func(nodePatch, false) as MuzzleFlashProps;
                if (obj == null)
                {
                    Log.Message("[SafePatcher] Failed to create muzzle flash props: " + type);
                    return;
                }

                PatchUtility.ResolveDefs(obj, nodePatch);

                if (def.modExtensions == null)
                {
                    def.modExtensions = new List<DefModExtension>();
                }


                //find the mod extension by	type
                DefModExtension modExtension = def.modExtensions?.FirstOrDefault(x => x is MuzzleFlashProps props && props.type == obj.type);
                //copy all the fields in the mod extension
                if (modExtension != null)
                {
                    foreach (var field in type.GetFields())
                    {
                        field.SetValue(modExtension, field.GetValue(obj));
                    }
                }
                else
                {
                    def.modExtensions.Add(obj);
                }
            }
        }
    }
}
