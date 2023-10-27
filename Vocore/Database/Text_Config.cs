using System;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public class Text_Config
    {
        public static readonly string NullConfig = "Trying to add null config to database.";
        public static readonly string EmptyConfigName = "Config name is empty.";
        public static string DuplicatedConfigName(string name)
        {
            return string.Format("Duplicated config name: {0}", name);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string FieldIsEmtpyInXml(string xmlText)
        {
            return string.Format("Name is empty, xml text:\n{0}", xmlText);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string NameOnlyAllowNumberCharacterUnderline(string name)
        {
            return string.Format("Name only allow number, character and underline: {0}", name);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string CannotParseToConfig(string xmlText)
        {
            return string.Format("Failed to convert xml text to config:\n{0}", xmlText);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ConfigNotFound(Type type, string name)
        {
            return string.Format("Config not found, type: {0}, name: {1}", type, name);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ConfigNotCorrect(string name, string errorContent, string xmlText)
        {
            return string.Format("Some error in config, name: {0}, error content: \n{1} \n XML text {2}", name, errorContent);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string CrossRefConfigConfigNotFound(string configName, string fieldName, string fieldType, string refConfigName)
        {
            return string.Format("Cross reference config not found, config name: {0}, field name: {1}, field type: {2}, ref config name: {3}", configName, fieldName, fieldType, refConfigName);
        }

    }
}