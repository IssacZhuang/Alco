using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

using Vocore.Animation;
using System.Diagnostics;

namespace Vocore.Test
{
    public class CurveDrawer
    {
        public static void Draw(ICurve curve, float startX, float endX, float scale = 1, float step = 0.01f, float penWidth = 3)
        {
            //get max y
            float maxY = 0;
            for (float x = startX; x <= endX; x += step)
            {
                float y = curve.Evaluate(x);
                if (y > maxY)
                    maxY = y;
            }
            
            int width = (int)((endX - startX) * scale) + (int)penWidth*2;
            int height = (int)(maxY * scale)+(int)penWidth*2;
            Bitmap bmp = new Bitmap(width, height);
            Graphics g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            Pen pen = new Pen(Color.Black, penWidth);
            //draw the curve
            float x0 = startX;
            float y0 = curve.Evaluate(x0);
            TestUtility.PrintBlue("x0: " + x0 + " y0: " + y0);
            for (float x = startX; x <= endX; x += step)
            {
                float y = curve.Evaluate(x);
                g.DrawLine(pen, penWidth + x0 * scale, height - penWidth - y0 * scale,penWidth + x * scale, height - penWidth - y * scale);
                x0 = x;
                y0 = y;
            }
            bmp.Save("curve.png", ImageFormat.Png);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "curve.png";
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
            Process.Start(psi);

        }
    }
}

