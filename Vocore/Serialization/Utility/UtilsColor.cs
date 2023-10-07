using System.Globalization;


namespace Vocore
{
    public static class UtilsColor
    {
		public static readonly float4 White  = new float4(1f, 1f, 1f, 1f);

        /// <summary>
		/// Convert bytes to Color. For example: (r:255, g:255, b:255, a:255) is white.
		/// </summary>
        public static float4 FromBytes(int r, int g, int b, int a = 255)
		{
			return new float4
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
        public static float4 ToColorHex(this string hex)
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

        /// <summary>
        /// Convert Color to hex string. For example: (r:255, g:255, b:255, a:255) is #FFFFFFFF.
        /// </summary>
        public static string ToHexStr(this float4 color)
		{
            int4 color32 = default;
            color32.x = (int)math.round(color.X * 255f);
            color32.y = (int)math.round(color.Y * 255f);
            color32.z = (int)math.round(color.Z * 255f);
            color32.w = (int)math.round(color.W * 255f);
            return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", color32.x, color32.y, color32.z, color32.w);
		}
    }
}


