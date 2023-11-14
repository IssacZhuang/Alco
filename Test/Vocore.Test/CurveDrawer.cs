using System;
using System.Collections.Generic;
// using System.Drawing;
// using System.Drawing.Imaging;

using Vocore;
using System.Diagnostics;

namespace Vocore.Test
{
    public class CurveDrawer
    {
        public static void Draw(ICurve curve, string name = "curve", int bitmapHeight = 512, float step = 0.01f, float penWidth = 3, int borderMargin = 10)
        {
            Draw(curve, curve.Points[0].t, curve.Points[curve.PointsCount - 1].t, name, bitmapHeight, step, penWidth, borderMargin);
        }


        public static void Draw(ICurve curve, float startX, float endX, string name = "curve", int bitmapHeight = 512, float step = 0.01f, float penWidth = 3, int borderMargin = 10)
        {
            //get max y
            // float maxY = 0;
            // float minY = 0;
            // for (float x = startX; x <= endX; x += step)
            // {
            //     float y = curve.Evaluate(x);
            //     if (y > maxY)
            //         maxY = y;
            //     if (y < minY)
            //         minY = y;
            // }

            // float scale = (float)bitmapHeight / (maxY - minY);

            // int width = (int)((endX - startX) * scale) + borderMargin * 2;
            // int height = bitmapHeight + borderMargin * 2;
            // Bitmap bmp = new Bitmap(width, height);
            // Graphics g = Graphics.FromImage(bmp);
            // g.Clear(Color.White);
            // Pen penLine = new Pen(Color.Gray, penWidth);
            // //draw the curve
            // float x0 = startX;
            // float y0 = curve.Evaluate(x0);

            // for (float x = startX; x <= endX; x += step)
            // {

            //     float y = curve.Evaluate(x);
            //     g.DrawLine(penLine, borderMargin + (x0 - startX) * scale, height - borderMargin - y0 * scale, borderMargin + (x - startX) * scale, height - borderMargin - y * scale);
            //     x0 = x;
            //     y0 = y;
            // }

            // Pen penPoint = new Pen(Color.Red, penWidth * 1.5f);

            // //draw points
            // foreach (var point in curve.Points)
            // {
            //     g.DrawEllipse(penPoint, borderMargin + (point.t - startX) * scale - penWidth, height - borderMargin - point.value * scale - penWidth, penWidth * 2, penWidth * 2);
            // }
            // string fileName = name + ".png";
            // bmp.Save(fileName, ImageFormat.Png);
            // ProcessStartInfo psi = new ProcessStartInfo();
            // psi.FileName = fileName;
            // psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
            // Process.Start(psi);

        }
    }
}

