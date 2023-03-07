using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Collections;

namespace Vocore.Serialization
{
    public static class UtilsLog
    {
        public static string TAB_SPACE = "   ";
        public static string DumpToString(this object obj, string prefix = "", int recursion = 4)
        {
            if (recursion < 0)
            {
                return prefix + "...\n";
            }

            if (obj == null)
            {
                return "#null\n";
            }

            //iterate through all field of obj
            StringBuilder sb = new StringBuilder();
            Type type = obj.GetType();
            if (obj is IList list && list != null)
            {
                sb.AppendLine();
                for (int i = 0; i < list.Count; i++)
                {
                    sb.Append(prefix);
                    sb.Append("[");
                    sb.Append(i);
                    sb.Append("] ===> ");
                    sb.Append(DumpToString(list[i], prefix + TAB_SPACE, recursion - 1));
                }
                return sb.ToString();
            }

            if (obj is IDictionary dict && dict != null)
            {
                sb.AppendLine();
                foreach (DictionaryEntry item in dict)
                {
                    sb.Append(prefix);
                    sb.Append("[");
                    sb.Append(item.Key);
                    sb.Append("] ===> ");
                    sb.Append(DumpToString(item.Value, prefix + TAB_SPACE, recursion - 1));
                }
                return sb.ToString();
            }

            if (obj is string str)
            {
                sb.Append(str.Replace("\n", "\\n"));
                sb.AppendLine();
                return sb.ToString();
            }

            if (type.IsClass)
            {
                sb.AppendLine();
                foreach (FieldInfo field in obj.GetType().GetFields())
                {
                    Object subObj = field.GetValue(obj);
                    sb.Append(prefix);
                    sb.Append(field.Name);
                    sb.Append(": ");

                    sb.Append(DumpToString(subObj, prefix + TAB_SPACE, recursion - 1));

                }
                return sb.ToString();
            }

            sb.Append(obj);
            sb.AppendLine();
            return sb.ToString();
        }
    }
}


