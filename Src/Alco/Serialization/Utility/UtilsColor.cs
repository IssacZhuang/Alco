using System.Numerics;
using System.Globalization;


namespace Alco
{
    public static class UtilsColor
    {
        public static readonly Vector4 White = new Vector4(1f, 1f, 1f, 1f);

        /// <summary>
		/// Convert bytes to Color. For example: (r:255, g:255, b:255, a:255) is white.
		/// </summary>
        public static Vector4 FromBytes(int r, int g, int b, int a = 255)
		{
            return new Vector4
			{
                X = (float)r / 255f,
                Y = (float)g / 255f,
                Z = (float)b / 255f,
                W = (float)a / 255f
			};
		}

        /// <summary>
        /// Convert hex string to Color. For example: #FFFFFF is white.
        /// </summary>
        public static Vector4 ToColorHex(this string hex)
		{
			if (hex.StartsWith("#"))
			{
				hex = hex.Substring(1);
			}
			if (hex.Length != 6 && hex.Length != 8)
			{
				return White;
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


