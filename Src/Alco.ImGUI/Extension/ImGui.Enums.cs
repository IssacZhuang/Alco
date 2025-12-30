using System.Collections.Concurrent;
using System.Runtime.CompilerServices;


namespace Alco.ImGUI;

public static unsafe partial class ImGui
{
    private struct EnumInfo
    {
        public string[] Names;
        public int[] Values;

        public EnumInfo(string[] names, int[] values)
        {
            Names = names;
            Values = values;
        }
    }
    private static readonly ConcurrentDictionary<Type, EnumInfo> _cache = new();

    private static EnumInfo GetEnumInfos<TEnum>() where TEnum : struct,Enum
    {
        return _cache.GetOrAdd(typeof(TEnum), static (Type type) =>
        {
            var names = Enum.GetNames(type);
            var values = Enum.GetValues<TEnum>();
            int[] intValues = new int[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                intValues[i] = ConvertEnumValue(values[i]);
            }
            return new EnumInfo(names, intValues);
        });
    }

    public static bool Combo<TEnum>(string label, ref TEnum currentItem) where TEnum : struct, Enum
    {
        var infos = GetEnumInfos<TEnum>();

        int value = ConvertEnumValue(currentItem);
        //the enum values are always sorted
        int index = Array.BinarySearch(infos.Values, value);
        if (index < 0)
        {
            index = 0;
        }
        bool result = Combo(label, ref index, infos.Names, infos.Names.Length);
        currentItem = ConvertEnumValue<TEnum>(infos.Values[index]);
        return result;
    }

    public static bool Combo<TEnum>(ReadOnlySpan<char> label, ref TEnum currentItem) where TEnum : struct, Enum
    {
        var infos = GetEnumInfos<TEnum>();
        int value = ConvertEnumValue(currentItem);
        //the enum values are always sorted
        int index = Array.BinarySearch(infos.Values, value);
        if (index < 0)
        {
            index = 0;
        }
        bool result = Combo(label, ref index, infos.Names, infos.Names.Length);
        currentItem = ConvertEnumValue<TEnum>(infos.Values[index]);
        return result;
    }

    private static int ConvertEnumValue<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        Type underlyingType = Enum.GetUnderlyingType(typeof(TEnum));
        if (underlyingType == typeof(int))
        {
            return Unsafe.As<TEnum, int>(ref value);
        }
        else if (underlyingType == typeof(byte))
        {
            return (int)Unsafe.As<TEnum, byte>(ref value);
        }
        else if (underlyingType == typeof(short))
        {
            return (int)Unsafe.As<TEnum, short>(ref value);
        }

        // unsupported enum type
        return 0;
    }

    private static TEnum ConvertEnumValue<TEnum>(int value) where TEnum : struct, Enum
    {
        Type underlyingType = Enum.GetUnderlyingType(typeof(TEnum));
        if (underlyingType == typeof(int))
        {
            return Unsafe.As<int, TEnum>(ref value);
        }
        else if (underlyingType == typeof(byte))
        {
            return Unsafe.As<int, TEnum>(ref value);
        }
        else if (underlyingType == typeof(short))
        {
            return Unsafe.As<int, TEnum>(ref value);
        }

        // unsupported enum type
        return default;
    }

    /// <summary>
    /// Displays checkboxes for each flag value in a flags enumeration.
    /// </summary>
    /// <typeparam name="TEnum">The enum type marked with [Flags] attribute.</typeparam>
    /// <param name="label">The label for this group of checkboxes.</param>
    /// <param name="flags">Reference to the current flags value.</param>
    /// <returns>True if any flag was toggled; otherwise false.</returns>
    public static bool CheckBox<TEnum>(string label, ref TEnum flags) where TEnum : struct, Enum
    {
        var infos = GetEnumInfos<TEnum>();
        int currentValue = ConvertEnumValue(flags);
        bool changed = false;

        if (!string.IsNullOrEmpty(label))
        {
            Text(label);
        }

        for (int i = 0; i < infos.Names.Length; i++)
        {
            int flagValue = infos.Values[i];
            // Skip zero value (typically "None")
            if (flagValue == 0)
            {
                continue;
            }

            bool isSet = (currentValue & flagValue) != 0;
            bool newValue = isSet;

            if (Checkbox(infos.Names[i], ref newValue))
            {
                if (newValue)
                {
                    currentValue |= flagValue;
                }
                else
                {
                    currentValue &= ~flagValue;
                }
                changed = true;
            }
        }

        if (changed)
        {
            flags = ConvertEnumValue<TEnum>(currentValue);
        }

        return changed;
    }

    /// <summary>
    /// Displays checkboxes for each flag value in a flags enumeration.
    /// </summary>
    /// <typeparam name="TEnum">The enum type marked with [Flags] attribute.</typeparam>
    /// <param name="label">The label for this group of checkboxes.</param>
    /// <param name="flags">Reference to the current flags value.</param>
    /// <returns>True if any flag was toggled; otherwise false.</returns>
    public static bool CheckBoxFlags<TEnum>(ReadOnlySpan<char> label, ref TEnum flags) where TEnum : struct, Enum
    {
        var infos = GetEnumInfos<TEnum>();
        int currentValue = ConvertEnumValue(flags);
        bool changed = false;

        if (label.Length > 0)
        {
            Text(label);
        }

        for (int i = 0; i < infos.Names.Length; i++)
        {
            int flagValue = infos.Values[i];
            // Skip zero value (typically "None")
            if (flagValue == 0)
            {
                continue;
            }

            bool isSet = (currentValue & flagValue) != 0;
            bool newValue = isSet;

            if (Checkbox(infos.Names[i], ref newValue))
            {
                if (newValue)
                {
                    currentValue |= flagValue;
                }
                else
                {
                    currentValue &= ~flagValue;
                }
                changed = true;
            }
        }

        if (changed)
        {
            flags = ConvertEnumValue<TEnum>(currentValue);
        }

        return changed;
    }

}