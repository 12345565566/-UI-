using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text;

namespace Zogos
{
    /// <summary>极简 PDF 写入器：每页一张 JPEG，A4 满页铺开。</summary>
    public class PdfDoc
    {
        public const double PW = 595.276, PH = 841.89;
        private List<byte[]> _imgs = new List<byte[]>();
        private List<int> _w = new List<int>();
        private List<int> _h = new List<int>();

        public int PageCount { get { return _imgs.Count; } }

        public void AddPage(Bitmap bmp, int quality)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ImageCodecInfo codec = null;
                foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                    if (c.FormatID == ImageFormat.Jpeg.Guid) { codec = c; break; }
                EncoderParameters ps = new EncoderParameters(1);
                ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                bmp.Save(ms, codec, ps);
                _imgs.Add(ms.ToArray());
                _w.Add(bmp.Width);
                _h.Add(bmp.Height);
            }
        }

        private static string F(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        private static byte[] Ascii(string s) { return Encoding.ASCII.GetBytes(s); }

        private static byte[] Cat(params byte[][] parts)
        {
            int n = 0;
            for (int i = 0; i < parts.Length; i++) n += parts[i].Length;
            byte[] r = new byte[n];
            int p = 0;
            for (int i = 0; i < parts.Length; i++) { Buffer.BlockCopy(parts[i], 0, r, p, parts[i].Length); p += parts[i].Length; }
            return r;
        }

        public void Save(string path)
        {
            int n = _imgs.Count;
            List<byte[]> objs = new List<byte[]>();
            objs.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));

            StringBuilder kids = new StringBuilder();
            for (int i = 0; i < n; i++) kids.Append((3 + i * 3).ToString() + " 0 R ");
            objs.Add(Ascii("<< /Type /Pages /Kids [" + kids.ToString().Trim() + "] /Count " + n + " >>"));

            for (int i = 0; i < n; i++)
            {
                int imgObj = 4 + i * 3;
                int contObj = 5 + i * 3;
                objs.Add(Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + F(PW) + " " + F(PH) + "]"
                    + " /Resources << /XObject << /Im0 " + imgObj + " 0 R >> >> /Contents " + contObj + " 0 R >>"));

                byte[] img = _imgs[i];
                byte[] head = Ascii("<< /Type /XObject /Subtype /Image /Width " + _w[i] + " /Height " + _h[i]
                    + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + img.Length + " >>\nstream\n");
                objs.Add(Cat(head, img, Ascii("\nendstream")));

                string cs = "q " + F(PW) + " 0 0 " + F(PH) + " 0 0 cm /Im0 Do Q";
                objs.Add(Ascii("<< /Length " + cs.Length + " >>\nstream\n" + cs + "\nendstream"));
            }

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                byte[] bin = new byte[] { 37, 80, 68, 70, 45, 49, 46, 52, 10, 0xE2, 0xE3, 0xCF, 0xD3, 10 };
                fs.Write(bin, 0, bin.Length);
                long[] off = new long[objs.Count + 1];
                for (int i = 0; i < objs.Count; i++)
                {
                    off[i + 1] = fs.Position;
                    byte[] h = Ascii((i + 1).ToString() + " 0 obj\n");
                    fs.Write(h, 0, h.Length);
                    fs.Write(objs[i], 0, objs[i].Length);
                    byte[] t = Ascii("\nendobj\n");
                    fs.Write(t, 0, t.Length);
                }
                long xref = fs.Position;
                byte[] x1 = Ascii("xref\n0 " + (objs.Count + 1).ToString() + "\n0000000000 65535 f \n");
                fs.Write(x1, 0, x1.Length);
                for (int i = 1; i <= objs.Count; i++)
                {
                    byte[] row = Ascii(off[i].ToString("D10") + " 00000 n \n");
                    fs.Write(row, 0, row.Length);
                }
                byte[] x2 = Ascii("trailer\n<< /Size " + (objs.Count + 1).ToString() + " /Root 1 0 R >>\nstartxref\n" + xref.ToString() + "\n%%EOF\n");
                fs.Write(x2, 0, x2.Length);
            }
        }
    }

    /// <summary>画图工具集。</summary>
    public static class P
    {
        public static readonly Color Ink = Color.FromArgb(22, 33, 46);
        public static readonly Color Sub = Color.FromArgb(92, 107, 122);
        public static readonly Color Line = Color.FromArgb(220, 228, 238);
        public static readonly Color Bg = Color.FromArgb(242, 245, 250);
        public static readonly Color Card = Color.White;
        public static readonly Color Red = Color.FromArgb(217, 59, 53);
        public static readonly Color RedBg = Color.FromArgb(253, 236, 234);
        public static readonly Color Orange = Color.FromArgb(224, 123, 31);
        public static readonly Color OrangeBg = Color.FromArgb(253, 241, 227);
        public static readonly Color Yellow = Color.FromArgb(176, 132, 0);
        public static readonly Color YellowBg = Color.FromArgb(251, 246, 224);
        public static readonly Color Green = Color.FromArgb(31, 157, 99);
        public static readonly Color GreenBg = Color.FromArgb(232, 247, 240);
        public static readonly Color Blue = Color.FromArgb(37, 99, 214);
        public static readonly Color BlueBg = Color.FromArgb(232, 240, 254);
        public static readonly Color Dark = Color.FromArgb(30, 46, 74);

        public static string FontName = "Microsoft YaHei";

        public static Font F(float px, bool bold)
        {
            try { return new Font(FontName, px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
            catch { return new Font("Segoe UI", px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
        }
        public static Font Mono(float px)
        {
            try { return new Font("Consolas", px, FontStyle.Regular, GraphicsUnit.Pixel); }
            catch { return F(px, false); }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d < 2f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            using (GraphicsPath p = Round(r, rad))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, RectangleF r, float rad, Color c, float w)
        {
            using (GraphicsPath p = Round(r, rad))
            using (Pen pen = new Pen(c, w))
                g.DrawPath(pen, p);
        }

        public static void CardBox(Graphics g, RectangleF r, Color fill, Color border, float rad, float bw)
        {
            Fill(g, r, rad, fill);
            if (bw > 0) Stroke(g, r, rad, border, bw);
        }

        public static void Txt(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h, StringAlignment v)
        {
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = h;
                sf.LineAlignment = v;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                using (SolidBrush b = new SolidBrush(c))
                    g.DrawString(s, f, b, r, sf);
            }
        }

        public static void Wrap(Graphics g, string s, Font f, Color c, RectangleF r)
        {
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Near;
                using (SolidBrush b = new SolidBrush(c))
                    g.DrawString(s, f, b, r, sf);
            }
        }

        public static float BadgeWidth(Graphics g, string text, Font f, float padX)
        {
            return g.MeasureString(text, f).Width + padX * 2;
        }

        public static void Badge(Graphics g, float x, float y, string text, Font f, Color bg, Color fg, float padX, float h)
        {
            float w = BadgeWidth(g, text, f, padX);
            RectangleF r = new RectangleF(x, y, w, h);
            Fill(g, r, h / 2f, bg);
            Txt(g, text, f, fg, r, StringAlignment.Center, StringAlignment.Center);
        }

        public static void NumCircle(Graphics g, float x, float y, float d, string num, Color bg, Color fg, Font f)
        {
            using (SolidBrush b = new SolidBrush(bg))
                g.FillEllipse(b, x, y, d, d);
            Txt(g, num, f, fg, new RectangleF(x, y, d, d), StringAlignment.Center, StringAlignment.Center);
        }
    }
}
