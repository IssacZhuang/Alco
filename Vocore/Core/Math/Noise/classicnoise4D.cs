//
// GLSL textureless classic 4D noise "cnoise",
// with an RSL-style periodic variant "pnoise".
// Author:  Stefan Gustavson (stefan.gustavson@liu.se)
// Version: 2011-08-22
//
// Many thanks to Ian McEwan of Ashima Arts for the
// ideas for permutation and gradient selection.
//
// Copyright (c) 2011 Stefan Gustavson. All rights reserved.
// Distributed under the MIT license. See LICENSE file.
// https://github.com/stegu/webgl-noise
//

using static Vocore.math;

namespace Vocore
{
    public static partial class noise
    {
        /// <summary>
        /// Classic Perlin noise
        /// </summary>
        /// <param name="P">Point on a 4D grid of gradient vectors.</param>
        /// <returns>Noise value.</returns>
        public static float cnoise(float4 P)
        {
            float4 Pi0 = floor(P); // Integer part for indexing
            float4 Pi1 = Pi0 + 1.0f; // Integer part + 1
            Pi0 = mod289(Pi0);
            Pi1 = mod289(Pi1);
            float4 Pf0 = frac(P); // Fractional part for interpolation
            float4 Pf1 = Pf0 - 1.0f; // Fractional part - 1.0
            float4 ix = float4(Pi0.X, Pi1.X, Pi0.X, Pi1.X);
            float4 iy = float4(Pi0.yy, Pi1.yy);
            float4 iz0 = float4(Pi0.zzzz);
            float4 iz1 = float4(Pi1.zzzz);
            float4 iw0 = float4(Pi0.wwww);
            float4 iw1 = float4(Pi1.wwww);

            float4 ixy = permute(permute(ix) + iy);
            float4 ixy0 = permute(ixy + iz0);
            float4 ixy1 = permute(ixy + iz1);
            float4 ixy00 = permute(ixy0 + iw0);
            float4 ixy01 = permute(ixy0 + iw1);
            float4 ixy10 = permute(ixy1 + iw0);
            float4 ixy11 = permute(ixy1 + iw1);

            float4 gx00 = ixy00 * (1.0f / 7.0f);
            float4 gy00 = floor(gx00) * (1.0f / 7.0f);
            float4 gz00 = floor(gy00) * (1.0f / 6.0f);
            gx00 = frac(gx00) - 0.5f;
            gy00 = frac(gy00) - 0.5f;
            gz00 = frac(gz00) - 0.5f;
            float4 gw00 = float4(0.75f) - abs(gx00) - abs(gy00) - abs(gz00);
            float4 sw00 = step(gw00, float4(0.0f));
            gx00 -= sw00 * (step(0.0f, gx00) - 0.5f);
            gy00 -= sw00 * (step(0.0f, gy00) - 0.5f);

            float4 gx01 = ixy01 * (1.0f / 7.0f);
            float4 gy01 = floor(gx01) * (1.0f / 7.0f);
            float4 gz01 = floor(gy01) * (1.0f / 6.0f);
            gx01 = frac(gx01) - 0.5f;
            gy01 = frac(gy01) - 0.5f;
            gz01 = frac(gz01) - 0.5f;
            float4 gw01 = float4(0.75f) - abs(gx01) - abs(gy01) - abs(gz01);
            float4 sw01 = step(gw01, float4(0.0f));
            gx01 -= sw01 * (step(0.0f, gx01) - 0.5f);
            gy01 -= sw01 * (step(0.0f, gy01) - 0.5f);

            float4 gx10 = ixy10 * (1.0f / 7.0f);
            float4 gy10 = floor(gx10) * (1.0f / 7.0f);
            float4 gz10 = floor(gy10) * (1.0f / 6.0f);
            gx10 = frac(gx10) - 0.5f;
            gy10 = frac(gy10) - 0.5f;
            gz10 = frac(gz10) - 0.5f;
            float4 gw10 = float4(0.75f) - abs(gx10) - abs(gy10) - abs(gz10);
            float4 sw10 = step(gw10, float4(0.0f));
            gx10 -= sw10 * (step(0.0f, gx10) - 0.5f);
            gy10 -= sw10 * (step(0.0f, gy10) - 0.5f);

            float4 gx11 = ixy11 * (1.0f / 7.0f);
            float4 gy11 = floor(gx11) * (1.0f / 7.0f);
            float4 gz11 = floor(gy11) * (1.0f / 6.0f);
            gx11 = frac(gx11) - 0.5f;
            gy11 = frac(gy11) - 0.5f;
            gz11 = frac(gz11) - 0.5f;
            float4 gw11 = float4(0.75f) - abs(gx11) - abs(gy11) - abs(gz11);
            float4 sw11 = step(gw11, float4(0.0f));
            gx11 -= sw11 * (step(0.0f, gx11) - 0.5f);
            gy11 -= sw11 * (step(0.0f, gy11) - 0.5f);

            float4 g0000 = float4(gx00.X, gy00.X, gz00.X, gw00.X);
            float4 g1000 = float4(gx00.Y, gy00.Y, gz00.Y, gw00.Y);
            float4 g0100 = float4(gx00.Z, gy00.Z, gz00.Z, gw00.Z);
            float4 g1100 = float4(gx00.W, gy00.W, gz00.W, gw00.W);
            float4 g0010 = float4(gx10.X, gy10.X, gz10.X, gw10.X);
            float4 g1010 = float4(gx10.Y, gy10.Y, gz10.Y, gw10.Y);
            float4 g0110 = float4(gx10.Z, gy10.Z, gz10.Z, gw10.Z);
            float4 g1110 = float4(gx10.W, gy10.W, gz10.W, gw10.W);
            float4 g0001 = float4(gx01.X, gy01.X, gz01.X, gw01.X);
            float4 g1001 = float4(gx01.Y, gy01.Y, gz01.Y, gw01.Y);
            float4 g0101 = float4(gx01.Z, gy01.Z, gz01.Z, gw01.Z);
            float4 g1101 = float4(gx01.W, gy01.W, gz01.W, gw01.W);
            float4 g0011 = float4(gx11.X, gy11.X, gz11.X, gw11.X);
            float4 g1011 = float4(gx11.Y, gy11.Y, gz11.Y, gw11.Y);
            float4 g0111 = float4(gx11.Z, gy11.Z, gz11.Z, gw11.Z);
            float4 g1111 = float4(gx11.W, gy11.W, gz11.W, gw11.W);

            float4 norm00 = taylorInvSqrt(float4(dot(g0000, g0000), dot(g0100, g0100), dot(g1000, g1000), dot(g1100, g1100)));
            g0000 *= norm00.X;
            g0100 *= norm00.Y;
            g1000 *= norm00.Z;
            g1100 *= norm00.W;

            float4 norm01 = taylorInvSqrt(float4(dot(g0001, g0001), dot(g0101, g0101), dot(g1001, g1001), dot(g1101, g1101)));
            g0001 *= norm01.X;
            g0101 *= norm01.Y;
            g1001 *= norm01.Z;
            g1101 *= norm01.W;

            float4 norm10 = taylorInvSqrt(float4(dot(g0010, g0010), dot(g0110, g0110), dot(g1010, g1010), dot(g1110, g1110)));
            g0010 *= norm10.X;
            g0110 *= norm10.Y;
            g1010 *= norm10.Z;
            g1110 *= norm10.W;

            float4 norm11 = taylorInvSqrt(float4(dot(g0011, g0011), dot(g0111, g0111), dot(g1011, g1011), dot(g1111, g1111)));
            g0011 *= norm11.X;
            g0111 *= norm11.Y;
            g1011 *= norm11.Z;
            g1111 *= norm11.W;

            float n0000 = dot(g0000, Pf0);
            float n1000 = dot(g1000, float4(Pf1.X, Pf0.yzw));
            float n0100 = dot(g0100, float4(Pf0.X, Pf1.Y, Pf0.zw));
            float n1100 = dot(g1100, float4(Pf1.xy, Pf0.zw));
            float n0010 = dot(g0010, float4(Pf0.xy, Pf1.Z, Pf0.W));
            float n1010 = dot(g1010, float4(Pf1.X, Pf0.Y, Pf1.Z, Pf0.W));
            float n0110 = dot(g0110, float4(Pf0.X, Pf1.yz, Pf0.W));
            float n1110 = dot(g1110, float4(Pf1.xyz, Pf0.W));
            float n0001 = dot(g0001, float4(Pf0.xyz, Pf1.W));
            float n1001 = dot(g1001, float4(Pf1.X, Pf0.yz, Pf1.W));
            float n0101 = dot(g0101, float4(Pf0.X, Pf1.Y, Pf0.Z, Pf1.W));
            float n1101 = dot(g1101, float4(Pf1.xy, Pf0.Z, Pf1.W));
            float n0011 = dot(g0011, float4(Pf0.xy, Pf1.zw));
            float n1011 = dot(g1011, float4(Pf1.X, Pf0.Y, Pf1.zw));
            float n0111 = dot(g0111, float4(Pf0.X, Pf1.yzw));
            float n1111 = dot(g1111, Pf1);

            float4 fade_xyzw = fade(Pf0);
            float4 n_0w = lerp(float4(n0000, n1000, n0100, n1100), float4(n0001, n1001, n0101, n1101), fade_xyzw.W);
            float4 n_1w = lerp(float4(n0010, n1010, n0110, n1110), float4(n0011, n1011, n0111, n1111), fade_xyzw.W);
            float4 n_zw = lerp(n_0w, n_1w, fade_xyzw.Z);
            float2 n_yzw = lerp(n_zw.xy, n_zw.zw, fade_xyzw.Y);
            float n_xyzw = lerp(n_yzw.x, n_yzw.y, fade_xyzw.X);
            return 2.2f * n_xyzw;
        }

        /// <summary>
        /// Classic Perlin noise, periodic variant
        /// </summary>
        /// <param name="P">Point on a 4D grid of gradient vectors.</param>
        /// <param name="rep">Period of repetition.</param>
        /// <returns>Noise value.</returns>
        public static float pnoise(float4 P, float4 rep)
        {
            float4 Pi0 = fmod(floor(P), rep); // Integer part math.modulo rep
            float4 Pi1 = fmod(Pi0 + 1.0f, rep); // Integer part + 1 math.mod rep
            Pi0 = mod289(Pi0);
            Pi1 = mod289(Pi1);
            float4 Pf0 = frac(P); // Fractional part for interpolation
            float4 Pf1 = Pf0 - 1.0f; // Fractional part - 1.0
            float4 ix = float4(Pi0.X, Pi1.X, Pi0.X, Pi1.X);
            float4 iy = float4(Pi0.yy, Pi1.yy);
            float4 iz0 = float4(Pi0.zzzz);
            float4 iz1 = float4(Pi1.zzzz);
            float4 iw0 = float4(Pi0.wwww);
            float4 iw1 = float4(Pi1.wwww);

            float4 ixy = permute(permute(ix) + iy);
            float4 ixy0 = permute(ixy + iz0);
            float4 ixy1 = permute(ixy + iz1);
            float4 ixy00 = permute(ixy0 + iw0);
            float4 ixy01 = permute(ixy0 + iw1);
            float4 ixy10 = permute(ixy1 + iw0);
            float4 ixy11 = permute(ixy1 + iw1);

            float4 gx00 = ixy00 * (1.0f / 7.0f);
            float4 gy00 = floor(gx00) * (1.0f / 7.0f);
            float4 gz00 = floor(gy00) * (1.0f / 6.0f);
            gx00 = frac(gx00) - 0.5f;
            gy00 = frac(gy00) - 0.5f;
            gz00 = frac(gz00) - 0.5f;
            float4 gw00 = float4(0.75f) - abs(gx00) - abs(gy00) - abs(gz00);
            float4 sw00 = step(gw00, float4(0.0f));
            gx00 -= sw00 * (step(0.0f, gx00) - 0.5f);
            gy00 -= sw00 * (step(0.0f, gy00) - 0.5f);

            float4 gx01 = ixy01 * (1.0f / 7.0f);
            float4 gy01 = floor(gx01) * (1.0f / 7.0f);
            float4 gz01 = floor(gy01) * (1.0f / 6.0f);
            gx01 = frac(gx01) - 0.5f;
            gy01 = frac(gy01) - 0.5f;
            gz01 = frac(gz01) - 0.5f;
            float4 gw01 = float4(0.75f) - abs(gx01) - abs(gy01) - abs(gz01);
            float4 sw01 = step(gw01, float4(0.0f));
            gx01 -= sw01 * (step(0.0f, gx01) - 0.5f);
            gy01 -= sw01 * (step(0.0f, gy01) - 0.5f);

            float4 gx10 = ixy10 * (1.0f / 7.0f);
            float4 gy10 = floor(gx10) * (1.0f / 7.0f);
            float4 gz10 = floor(gy10) * (1.0f / 6.0f);
            gx10 = frac(gx10) - 0.5f;
            gy10 = frac(gy10) - 0.5f;
            gz10 = frac(gz10) - 0.5f;
            float4 gw10 = float4(0.75f) - abs(gx10) - abs(gy10) - abs(gz10);
            float4 sw10 = step(gw10, float4(0.0f));
            gx10 -= sw10 * (step(0.0f, gx10) - 0.5f);
            gy10 -= sw10 * (step(0.0f, gy10) - 0.5f);

            float4 gx11 = ixy11 * (1.0f / 7.0f);
            float4 gy11 = floor(gx11) * (1.0f / 7.0f);
            float4 gz11 = floor(gy11) * (1.0f / 6.0f);
            gx11 = frac(gx11) - 0.5f;
            gy11 = frac(gy11) - 0.5f;
            gz11 = frac(gz11) - 0.5f;
            float4 gw11 = float4(0.75f) - abs(gx11) - abs(gy11) - abs(gz11);
            float4 sw11 = step(gw11, float4(0.0f));
            gx11 -= sw11 * (step(0.0f, gx11) - 0.5f);
            gy11 -= sw11 * (step(0.0f, gy11) - 0.5f);

            float4 g0000 = float4(gx00.X, gy00.X, gz00.X, gw00.X);
            float4 g1000 = float4(gx00.Y, gy00.Y, gz00.Y, gw00.Y);
            float4 g0100 = float4(gx00.Z, gy00.Z, gz00.Z, gw00.Z);
            float4 g1100 = float4(gx00.W, gy00.W, gz00.W, gw00.W);
            float4 g0010 = float4(gx10.X, gy10.X, gz10.X, gw10.X);
            float4 g1010 = float4(gx10.Y, gy10.Y, gz10.Y, gw10.Y);
            float4 g0110 = float4(gx10.Z, gy10.Z, gz10.Z, gw10.Z);
            float4 g1110 = float4(gx10.W, gy10.W, gz10.W, gw10.W);
            float4 g0001 = float4(gx01.X, gy01.X, gz01.X, gw01.X);
            float4 g1001 = float4(gx01.Y, gy01.Y, gz01.Y, gw01.Y);
            float4 g0101 = float4(gx01.Z, gy01.Z, gz01.Z, gw01.Z);
            float4 g1101 = float4(gx01.W, gy01.W, gz01.W, gw01.W);
            float4 g0011 = float4(gx11.X, gy11.X, gz11.X, gw11.X);
            float4 g1011 = float4(gx11.Y, gy11.Y, gz11.Y, gw11.Y);
            float4 g0111 = float4(gx11.Z, gy11.Z, gz11.Z, gw11.Z);
            float4 g1111 = float4(gx11.W, gy11.W, gz11.W, gw11.W);

            float4 norm00 = taylorInvSqrt(float4(dot(g0000, g0000), dot(g0100, g0100), dot(g1000, g1000), dot(g1100, g1100)));
            g0000 *= norm00.X;
            g0100 *= norm00.Y;
            g1000 *= norm00.Z;
            g1100 *= norm00.W;

            float4 norm01 = taylorInvSqrt(float4(dot(g0001, g0001), dot(g0101, g0101), dot(g1001, g1001), dot(g1101, g1101)));
            g0001 *= norm01.X;
            g0101 *= norm01.Y;
            g1001 *= norm01.Z;
            g1101 *= norm01.W;

            float4 norm10 = taylorInvSqrt(float4(dot(g0010, g0010), dot(g0110, g0110), dot(g1010, g1010), dot(g1110, g1110)));
            g0010 *= norm10.X;
            g0110 *= norm10.Y;
            g1010 *= norm10.Z;
            g1110 *= norm10.W;

            float4 norm11 = taylorInvSqrt(float4(dot(g0011, g0011), dot(g0111, g0111), dot(g1011, g1011), dot(g1111, g1111)));
            g0011 *= norm11.X;
            g0111 *= norm11.Y;
            g1011 *= norm11.Z;
            g1111 *= norm11.W;

            float n0000 = dot(g0000, Pf0);
            float n1000 = dot(g1000, float4(Pf1.X, Pf0.yzw));
            float n0100 = dot(g0100, float4(Pf0.X, Pf1.Y, Pf0.zw));
            float n1100 = dot(g1100, float4(Pf1.xy, Pf0.zw));
            float n0010 = dot(g0010, float4(Pf0.xy, Pf1.Z, Pf0.W));
            float n1010 = dot(g1010, float4(Pf1.X, Pf0.Y, Pf1.Z, Pf0.W));
            float n0110 = dot(g0110, float4(Pf0.X, Pf1.yz, Pf0.W));
            float n1110 = dot(g1110, float4(Pf1.xyz, Pf0.W));
            float n0001 = dot(g0001, float4(Pf0.xyz, Pf1.W));
            float n1001 = dot(g1001, float4(Pf1.X, Pf0.yz, Pf1.W));
            float n0101 = dot(g0101, float4(Pf0.X, Pf1.Y, Pf0.Z, Pf1.W));
            float n1101 = dot(g1101, float4(Pf1.xy, Pf0.Z, Pf1.W));
            float n0011 = dot(g0011, float4(Pf0.xy, Pf1.zw));
            float n1011 = dot(g1011, float4(Pf1.X, Pf0.Y, Pf1.zw));
            float n0111 = dot(g0111, float4(Pf0.X, Pf1.yzw));
            float n1111 = dot(g1111, Pf1);

            float4 fade_xyzw = fade(Pf0);
            float4 n_0w = lerp(float4(n0000, n1000, n0100, n1100), float4(n0001, n1001, n0101, n1101), fade_xyzw.W);
            float4 n_1w = lerp(float4(n0010, n1010, n0110, n1110), float4(n0011, n1011, n0111, n1111), fade_xyzw.W);
            float4 n_zw = lerp(n_0w, n_1w, fade_xyzw.Z);
            float2 n_yzw = lerp(n_zw.xy, n_zw.zw, fade_xyzw.Y);
            float n_xyzw = lerp(n_yzw.x, n_yzw.y, fade_xyzw.X);
            return 2.2f * n_xyzw;
        }
    }
}
