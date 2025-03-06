using System.Collections;
using System.Collections.Generic;

using Avalonia.Data.Converters;

namespace Alco.Editor.Converters;

public static class ListConverters
{
    public static readonly FuncValueConverter<IList, string> ToCount =
        new FuncValueConverter<IList, string>(v => v == null ? " (0)" : $" ({v.Count})");

    public static readonly FuncValueConverter<IList, bool> IsNullOrEmpty =
        new FuncValueConverter<IList, bool>(v => v == null || v.Count == 0);

    public static readonly FuncValueConverter<IList, bool> IsNotNullOrEmpty =
        new FuncValueConverter<IList, bool>(v => v != null && v.Count > 0);

    public static readonly FuncValueConverter<IList, bool> IsOnlyTop100Shows =
        new FuncValueConverter<IList, bool>(v => v != null && v.Count > 100);
}

