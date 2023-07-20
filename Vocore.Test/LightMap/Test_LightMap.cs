using System;
using System.Collections.Generic;

using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;

using Unity.Mathematics;
using Vocore;

namespace Vocore.Test
{
    public class Test_LightMap
    {
        [Test("Test render light map to PNG")]
        public void Test_RenderLightMapToPNG()
        {
            TileLightMap lightMap = new TileLightMap(128, 128);
            lightMap.ResetFrame();
            lightMap.AddLight(TileLight.Create(new int2(64, 64), new LightColor(255, 200, 200)));
            for (int i = 0; i < 8; i++)
            {
                int x = 60;
                int y = 60 + i;
                lightMap.SetTransparency(new int2(x, y), 0);
            }

            for (int i = 0; i < 20; i++)
            {
                int x = 66;
                int y = 54 + i;
                lightMap.SetTransparency(new int2(x, y), 0.8f);
            }
            TestHelper.Benchmark("flood fiil", () =>
            {
                lightMap.FloorFillLight();
            });


            Bitmap bitmap = new Bitmap(lightMap.Width, lightMap.Height);
            for (int x = 0; x < lightMap.Width; x++)
            {
                for (int y = 0; y < lightMap.Height; y++)
                {
                    bitmap.SetPixel(x, y, Color.Black);

                    LightColor color = lightMap.GetColor(x, lightMap.Height - 1 - y);
                    color.Clamp();

                    bitmap.SetPixel(x, y, Color.FromArgb(color.r, color.g, color.b));
                }
            }

            bitmap.SetPixel(0, 0, Color.Blue);
            for (int i = 0; i < 20; i++)
            {
                int x = 66;
                int y = 54 + i;
                bitmap.SetPixel(x, y, Color.FromArgb(204,204,204,204));
            }

            string fileName = "lightmap.png";
            bitmap.Save(fileName, ImageFormat.Png);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = fileName;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
            Process.Start(psi);





        }
    }
}

