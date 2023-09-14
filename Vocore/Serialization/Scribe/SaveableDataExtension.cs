using System;

namespace Vocore{
    public static class SaveableDataExtension
    {
        public static void RefDeep<T>(this ISaveableData data, string fieldName, T value, T defaultValue = default) where T : ISaveable
        {
            
        }

        public static void RefValue<T>(this ISaveableData data, string fieldName, T value, T defaultValue = default) where T : unmanaged
        {

        }

        public static void RefString(this ISaveableData data, string fieldName, string value, string defaultValue = "")
        {

        }
    }
}