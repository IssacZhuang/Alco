using System.Diagnostics;
using NUnit.Framework;

namespace TestFramework;

public static class UtilsTest
{
    private readonly static long SizeK = 1024;
    private readonly static long SizeM = 1024 * 1024;
    private readonly static long SizeG = 1024 * 1024 * 1024;




    //size in bytes, to B, KB, MB, GB
    public static string FormatSize(long size)
    {
        if (size < SizeK)
        {
            return size + " B";
        }
        if (size < SizeM)
        {
            return size / SizeK + " KB" + (size % SizeK > 0 ? " " + size % SizeK + " B" : "");
        }
        if (size < SizeG)
        {
            return size / SizeM + " MB" + (size % SizeM > 0 ? " " + size % SizeK + " KB" : "");
        }
        return size / SizeG + " GB" + (size % SizeG > 0 ? " " + size % SizeK + " MB" : "");
    }
}
