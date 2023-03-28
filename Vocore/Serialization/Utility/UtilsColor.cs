using System.Globalization;
using UnityEngine;

namespace Vocore
{
    public static class UtilsColor
    {
        /// <summary>
		/// Convert bytes to Color. For example: (r:255, g:255, b:255, a:255) is white.
		/// </summary>
        public static Color FromBytes(int r, int g, int b, int a = 255)
		{
			return new Color
			{
				r = (float)r / 255f,
				g = (float)g / 255f,
				b = (float)b / 255f,
				a = (float)a / 255f
			};
		}

		/// <summary>
		/// Convert hex string to Color. For example: #FFFFFF is white.
		/// </summary>
        public static Color ToColorHex(this string hex)
		{
			if (hex.StartsWith("#"))
			{
				hex = hex.Substring(1);
			}
			if (hex.Length != 6 && hex.Length != 8)
			{
				return Color.white;
			}
			int r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
			int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
			int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
			int a = 255;
			if (hex.Length == 8)
			{
				a = int.Parse(hex.Substring(6, 2), NumberStyles.HexNumber);
			}
			return FromBytes(r, g, b, a);
		}
    }
}


