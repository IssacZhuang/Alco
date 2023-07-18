using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Vocore
{
    public class BaseConfig
    {
        public const string RegexPattern = @"^[a-zA-Z0-9_]+$";
        private string _xmlText = "";
        public string name;

        public string XmlText
        {
            get
            {
                return _xmlText;
            }
            set
            {
                _xmlText = value;
            }
        }

        public virtual IEnumerable<string> CheckErrors()
        {
            if (string.IsNullOrEmpty(name))
            {
                yield return Text_Config.FieldIsEmtpyInXml(XmlText);
            }

            if (!Regex.IsMatch(name, RegexPattern))
            {
                yield return Text_Config.NameOnlyAllowNumberCharacterUnderline(this.name);
            }
        }

        public void ClearCache()
        {
            _xmlText = "";
        }
    }
}


