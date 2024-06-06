// modify from https://github.com/ValveResourceFormat/ValvePak

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Vocore.IO;

internal sealed class CaseInsensitivePackageEntryComparer(StringComparison comparison) : IComparer<PackageEntry>
{
	public StringComparison Comparison { get; } = comparison;

	/// <remarks>
	/// Intentionally not comparing TypeName because this comparer is used on Entries which is split by extension already.
	/// </remarks>
	public int Compare(PackageEntry? x, PackageEntry? y)
	{
		if (x is null)
		{
			return y is null ? 0 : -1;
		}

		if (y is null)
		{
			return 1;
		}

		var comp = x.FileName.Length.CompareTo(y.FileName.Length);

		if (comp != 0)
		{
			return comp;
		}

		comp = x.DirectoryName.Length.CompareTo(y.DirectoryName.Length);

		if (comp != 0)
		{
			return comp;
		}

		comp = string.Compare(x.FileName, y.FileName, Comparison);

		if (comp != 0)
		{
			return comp;
		}

		return string.Compare(x.DirectoryName, y.DirectoryName, Comparison);
	}
}

