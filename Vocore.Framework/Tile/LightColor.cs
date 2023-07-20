using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

namespace Vocore
{
    public struct LightColor
    {
        public static readonly LightColor Default = new LightColor
        {
            value = new int3(0, 0, 0),
        };

        public static readonly int MaxIntensity = 255;
        public int3 value;

        public LightColor(int r, int g, int b)
        {
            value = new int3(r, g, b);
        }

        public LightColor(Color color)
        {
            value = new int3((int)(color.r * MaxIntensity), (int)(color.g * MaxIntensity), (int)(color.b * MaxIntensity));
        }

        public Color ToColor()
        {
            return new Color(r / (float)MaxIntensity, g / (float)MaxIntensity, b / (float)MaxIntensity);
        }

        public void Clamp()
        {
            value = math.clamp(value, 0, MaxIntensity);
        }

        public void ClampHDR()
        {
            value = math.clamp(value, 0, MaxIntensity * 16);
        }

        public int r
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => value.x;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => this.value.x = value;
        }

        public int g
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => value.y;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => this.value.y = value;
        }

        public int b
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => value.z;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => this.value.z = value;
        }

        public static LightColor operator +(LightColor a, LightColor b)
        {
            return new LightColor
            {
                value = a.value + b.value,
            };
        }

        public static LightColor operator -(LightColor a, LightColor b)
        {
            return new LightColor
            {
                value = a.value - b.value,
            };
        }

        public static LightColor operator -(LightColor a, int b)
        {
            return new LightColor
            {
                value = a.value - b,
            };
        }

        public static LightColor operator *(LightColor a, LightColor b)
        {
            return new LightColor
            {
                value = a.value * b.value,
            };
        }

        public static LightColor operator *(LightColor a, int b)
        {
            return new LightColor
            {
                value = a.value * b,
            };
        }

        public static LightColor operator *(int a, LightColor b)
        {
            return new LightColor
            {
                value = a * b.value,
            };
        }

        public static LightColor operator /(LightColor a, LightColor b)
        {
            return new LightColor
            {
                value = a.value / b.value,
            };
        }

        public static LightColor operator /(LightColor a, int b)
        {
            return new LightColor
            {
                value = a.value / b,
            };
        }

        public static LightColor operator /(int a, LightColor b)
        {
            return new LightColor
            {
                value = a / b.value,
            };
        }
    }
}