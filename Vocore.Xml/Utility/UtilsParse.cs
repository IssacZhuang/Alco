using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

using UnityEngine;

namespace Vocore.Xml
{
    public static class UtilsParse
    {
		private static readonly char[] _trimColorStart = new char[]
		{
			'(',
			'R',
			'G',
			'B',
			'A'
		};

		private static readonly char[] _trimColorEnd = new char[]
		{
			')'
		};

		private static Dictionary<Type, Func<string, object>> _parser = new Dictionary<Type, Func<string, object>>();

		static UtilsParse(){

		}



        /// <summary>
        /// Parse a string.
        /// </summary>
        public static string ParseString(this string str)
		{
			return str.Replace("\\n", "\n");
		}

        /// <summary>
        /// Parse a string to int.
        /// </summary>
        public static int ToInt(this string str)
		{
			int result;
			if (!int.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out result))
			{
				result = (int)float.Parse(str, CultureInfo.InvariantCulture);
			}
			return result;
		}

		/// <summary>
		/// Parse a string to float.
		/// </summary>
		public static float ToFloat(this string str)
		{
			return float.Parse(str, CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Parse a string to bool.
		/// </summary>
		public static bool ToBool(this string str)
		{
			return bool.Parse(str);
		}

		/// <summary>
		/// Parse a string to long.
		/// </summary>
		public static long ToLong(this string str)
		{
			return long.Parse(str, CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Parse a string to double.
		/// </summary>
		public static double ToDouble(this string str)
		{
			return double.Parse(str, CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Parse a string to sbyte.
		/// </summary>
		public static sbyte ToSByte(this string str)
		{
			return sbyte.Parse(str, CultureInfo.InvariantCulture);
		}

		

		/// <summary>
		/// Parse a string to UnityEngine.Vector2
		/// </summary>
        public static Vector2 ToVector2(this string Str)
		{
			Str = Str.TrimStart(new char[]
			{
				'('
			});
			Str = Str.TrimEnd(new char[]
			{
				')'
			});
			string[] array = Str.Split(new char[]
			{
				','
			});
			CultureInfo invariantCulture = CultureInfo.InvariantCulture;
			float x;
			float y;
			if (array.Length == 1)
			{
				y = (x = Convert.ToSingle(array[0], invariantCulture));
			}
			else
			{
				if (array.Length != 2)
				{
					throw new InvalidOperationException();
				}
				x = Convert.ToSingle(array[0], invariantCulture);
				y = Convert.ToSingle(array[1], invariantCulture);
			}
			return new Vector2(x, y);
		}

		/// <summary>
		/// Parse a string to UnityEngine.Vector3
		/// </summary>
		public static Vector3 ToVector3(this string Str)
		{
			Str = Str.TrimStart(new char[]
			{
				'('
			});
			Str = Str.TrimEnd(new char[]
			{
				')'
			});
			string[] array = Str.Split(new char[]
			{
				','
			});
			CultureInfo invariantCulture = CultureInfo.InvariantCulture;
			float x = Convert.ToSingle(array[0], invariantCulture);
			float y = Convert.ToSingle(array[1], invariantCulture);
			float z = Convert.ToSingle(array[2], invariantCulture);
			return new Vector3(x, y, z);
		}

		/// <summary>
		/// Parse a string to UnityEngine.Vector4
		/// </summary>
		public static Vector4 ToVectorAdaptive(this string Str)
		{
			Str = Str.TrimStart(new char[]
			{
				'('
			});
			Str = Str.TrimEnd(new char[]
			{
				')'
			});
			string[] array = Str.Split(new char[]
			{
				','
			});
			CultureInfo invariantCulture = CultureInfo.InvariantCulture;
			float x = 0f;
			float y = 0f;
			float z = 0f;
			float w = 0f;
			if (array.Length >= 1)
			{
				x = Convert.ToSingle(array[0], invariantCulture);
			}
			if (array.Length >= 2)
			{
				y = Convert.ToSingle(array[1], invariantCulture);
			}
			if (array.Length >= 3)
			{
				z = Convert.ToSingle(array[2], invariantCulture);
			}
			if (array.Length >= 4)
			{
				w = Convert.ToSingle(array[3], invariantCulture);
			}
			if (array.Length >= 5)
			{
				// to go error
			}
			return new Vector4(x, y, z, w);
		}

		/// <summary>
		/// Parse a string to UnityEngine.Rect
		/// </summary>
		public static Rect ToRect(this string str)
		{
			str = str.TrimStart(new char[]
			{
				'('
			});
			str = str.TrimEnd(new char[]
			{
				')'
			});
			string[] array = str.Split(new char[]
			{
				','
			});
			CultureInfo invariantCulture = CultureInfo.InvariantCulture;
			float x = Convert.ToSingle(array[0], invariantCulture);
			float y = Convert.ToSingle(array[1], invariantCulture);
			float width = Convert.ToSingle(array[2], invariantCulture);
			float height = Convert.ToSingle(array[3], invariantCulture);
			return new Rect(x, y, width, height);
		}

		/// <summary>
		/// Parse a string to UnityEngine.Color
		/// </summary>
		public static Color ToColor(this string str)
		{
			if(str.StartsWith("#"))
			{
				return str.ToColorHex();
			}

			str = str.TrimStart(_trimColorStart);
			str = str.TrimEnd(_trimColorEnd);
			string[] array = str.Split(new char[]
			{
				','
			});
			float num = array[0].ToFloat();
			float num2 = array[1].ToFloat();
			float num3 = array[2].ToFloat();
			bool flag = num > 1f || num3 > 1f || num2 > 1f;
			float num4 = (float)(flag ? 255 : 1);
			if (array.Length == 4)
			{
				num4 = array[3].ToFloat();
			}
			Color result;
			if (!flag)
			{
				result.r = num;
				result.g = num2;
				result.b = num3;
				result.a = num4;
			}
			else
			{
				result = UtilsColor.FromBytes(Mathf.RoundToInt(num), Mathf.RoundToInt(num2), Mathf.RoundToInt(num3), Mathf.RoundToInt(num4));
			}
			return result;
		}

    }
}


