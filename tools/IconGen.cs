using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace IconGen
{
    class Program
    {
        static void Main()
        {
            using (Bitmap bmp = new Bitmap(128, 128))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // Body
                FillRoundedRectangle(g, Brushes.LightGray, 20, 45, 88, 70, 10);
                DrawRoundedRectangle(g, new Pen(Color.DarkGray, 2), 20, 45, 88, 70, 10);
                
                // Head
                FillRoundedRectangle(g, Brushes.WhiteSmoke, 30, 10, 68, 40, 8);
                DrawRoundedRectangle(g, new Pen(Color.DarkGray, 2), 30, 10, 68, 40, 8);
                
                // Eyes
                g.FillEllipse(Brushes.DeepSkyBlue, 45, 20, 12, 12);
                g.FillEllipse(Brushes.DeepSkyBlue, 71, 20, 12, 12);
                
                // Mouth
                g.DrawLine(new Pen(Color.Gray, 3), 50, 38, 78, 38);
                
                bmp.Save("icon.png", ImageFormat.Png);
                
                // Save as ICO
                using (MemoryStream ms = new MemoryStream()) {
                    bmp.Save(ms, ImageFormat.Png);
                    byte[] pngBytes = ms.ToArray();
                    using (FileStream fs = new FileStream("icon.ico", FileMode.Create)) {
                        fs.Write(new byte[] { 0, 0, 1, 0, 1, 0 }, 0, 6);
                        fs.WriteByte(128); fs.WriteByte(128); fs.WriteByte(0); fs.WriteByte(0);
                        fs.Write(BitConverter.GetBytes((short)1), 0, 2);
                        fs.Write(BitConverter.GetBytes((short)32), 0, 2);
                        fs.Write(BitConverter.GetBytes(pngBytes.Length), 0, 4);
                        fs.Write(BitConverter.GetBytes(22), 0, 4);
                        fs.Write(pngBytes, 0, pngBytes.Length);
                    }
                }
            }
        }

        static void FillRoundedRectangle(Graphics g, Brush brush, int x, int y, int width, int height, int radius)
        {
            using (var path = GetRoundedRectanglePath(x, y, width, height, radius))
                g.FillPath(brush, path);
        }

        static void DrawRoundedRectangle(Graphics g, Pen pen, int x, int y, int width, int height, int radius)
        {
            using (var path = GetRoundedRectanglePath(x, y, width, height, radius))
                g.DrawPath(pen, path);
        }

        static System.Drawing.Drawing2D.GraphicsPath GetRoundedRectanglePath(int x, int y, int width, int height, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
