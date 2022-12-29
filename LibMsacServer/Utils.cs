using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace LibMsacServer
{
    static class Utils
    {
        /// <summary>
        /// Searches for an exact match of target within src, returning the starting index if found.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        public static bool TryFindMatch(this byte[] src, byte[] target, int offset, int length, out int index)
        {
            for (index = offset; index < src.Length - target.Length && index < offset + length; index++)
            {
                if (RegionEquals(src, index, target, 0, target.Length))
                    return true;
            }
            return false;
        }

        public static T DeserializeAs<T>(this XmlNode node)
        {
            XmlSerializer serial = new XmlSerializer(typeof(T));
            T result;
            using (StringReader sr = new StringReader(node.OuterXml))
                result = (T)serial.Deserialize(sr);
            return result;
        }

        public static bool RegionEquals(this byte[] a, int aIndex, byte[] b, int bIndex, int length)
        {
            for (int i = 0; i < length; i++)
            {
                if (a[aIndex + i] != b[bIndex + i])
                    return false;
            }
            return true;
        }

        public static void AddAttribute(this XmlNode attributes, string key, string value)
        {
            XmlNode node = attributes.OwnerDocument.CreateNode(XmlNodeType.Attribute, key, null);
            node.Value = value;
            attributes.Attributes.SetNamedItem(node);
        }

        public static bool TryGetString(this XmlAttributeCollection attributes, string key, out string value)
        {
            foreach (var i in attributes)
            {
                if (((XmlAttribute)i).Name == key)
                {
                    value = ((XmlAttribute)i).Value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public static bool TryGetInt(this XmlAttributeCollection attributes, string key, out int value)
        {
            value = 0;
            return attributes.TryGetString(key, out string raw) && int.TryParse(raw, out value);
        }

        public static bool TryGetBool(this XmlAttributeCollection attributes, string key, out bool value)
        {
            value = false;
            return attributes.TryGetString(key, out string raw) && bool.TryParse(raw, out value);
        }
    }
}
