using System.Collections.Concurrent;
using System.Runtime.CompilerServices;


namespace Alco.ImGUI;

public static unsafe partial class ImGui
{
    private static readonly ConcurrentDictionary<Type, string[]> _cache = new();

    private static string[] GetEnumNames(Type enumType)
    {
        return _cache.GetOrAdd(enumType, static (Type type) =>
        {
            if (type.IsEnum)
            {
                return Enum.GetNames(type);
            }

            return Array.Empty<string>();
        });
    }

    public static bool Combo<TEnum>(string label, ref TEnum currentItem) where TEnum : Enum
    {
        Type underlyingType = Enum.GetUnderlyingType(typeof(TEnum));
        if (underlyingType == typeof(int))
        {
            var names = GetEnumNames(typeof(TEnum));
            int value = Unsafe.As<TEnum, int>(ref currentItem);
            bool result = Combo(label, ref value, names, names.Length);
            currentItem = Unsafe.As<int, TEnum>(ref value);
            return result;
        }
        else if (underlyingType == typeof(byte))
        {
            var names = GetEnumNames(typeof(TEnum));
            int value = (int)Unsafe.As<TEnum, byte>(ref currentItem);
            bool result = Combo(label, ref value, names, names.Length);
            currentItem = Unsafe.As<int, TEnum>(ref value);
            return result;
        }
        else if (underlyingType == typeof(short))
        {
            var names = GetEnumNames(typeof(TEnum));
            int value = (int)Unsafe.As<TEnum, short>(ref currentItem);
            bool result = Combo(label, ref value, names, names.Length);
            currentItem = Unsafe.As<int, TEnum>(ref value);
            return result;
        }

        // unsupported enum type
        return false;
    }

    public static bool Combo<TEnum>(ReadOnlySpan<char> label, ref TEnum currentItem, TEnum[] items) where TEnum : Enum
    {
        Type underlyingType = Enum.GetUnderlyingType(typeof(TEnum));
        if (underlyingType == typeof(int))
        {
            var names = GetEnumNames(typeof(TEnum));
            int value = Unsafe.As<TEnum, int>(ref currentItem);
            bool result = Combo(label, ref value, names, names.Length);
            currentItem = Unsafe.As<int, TEnum>(ref value);
            return result;
        }
        else if (underlyingType == typeof(byte))
        {
            var names = GetEnumNames(typeof(TEnum));
            int value = (int)Unsafe.As<TEnum, byte>(ref currentItem);
            bool result = Combo(label, ref value, names, names.Length);
            currentItem = Unsafe.As<int, TEnum>(ref value);
            return result;
        }
        else if (underlyingType == typeof(short))
        {
            var names = GetEnumNames(typeof(TEnum));
            int value = (int)Unsafe.As<TEnum, short>(ref currentItem);
            bool result = Combo(label, ref value, names, names.Length);
            currentItem = Unsafe.As<int, TEnum>(ref value);
            return result;
        }

        return false;
    }
}