using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

class MakeIcon
{
    static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        GraphicsPath p = new GraphicsPath();
        int d = radius * 2;
        if (d < 2) d = 2;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static MemoryStream BmpEntry(Bitmap bmp)
    {
        int s = bmp.Width;
        MemoryStream ms = new MemoryStream();
        BinaryWriter w = new BinaryWriter(ms);
        w.Write((int)40);
        w.Write((int)s);
        w.Write((int)(s * 2));
        w.Write((short)1);
        w.Write((short)32);
        w.Write((int)0);
        w.Write((int)(s * s * 4));
        w.Write((int)0); w.Write((int)0); w.Write((int)0); w.Write((int)0);

        BitmapData d = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = d.Stride;
        byte[] buf = new byte[stride * s];
        Marshal.Copy(d.Scan0, buf, 0, buf.Length);
        bmp.UnlockBits(d);

        for (int y = s - 1; y >= 0; y--) w.Write(buf, y * stride, s * 4);

        int maskRow = ((s + 31) / 32) * 4;
        w.Write(new byte[maskRow * s]);
        w.Flush();
        return ms;
    }

    static void WriteIco(string path, int[] sizes, MemoryStream[] blobs)
    {
        using (FileStream fs = new FileStream(path, FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                byte b = (byte)(s >= 256 ? 0 : s);
                w.Write(b); w.Write(b);
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write((int)blobs[i].Length);
                w.Write((int)offset);
                offset += (int)blobs[i].Length;
            }
            for (int i = 0; i < sizes.Length; i++) w.Write(blobs[i].ToArray());
        }
    }

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("用法: MakeIcon <源图> <输出.ico> [圆角比例]"); return 2; }
        string srcPath = args[0];
        string dstPath = args[1];
        double round = args.Length > 2 ? double.Parse(args[2]) : 0.16;

        if (!File.Exists(srcPath)) { Console.WriteLine("找不到源图: " + srcPath); return 1; }

        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        MemoryStream[] blobs = new MemoryStream[sizes.Length];

        using (Image img = Image.FromFile(srcPath))
        {
            Console.WriteLine("源图: " + img.Width + "x" + img.Height + "  " + img.PixelFormat);
            int side = Math.Min(img.Width, img.Height);
            Rectangle crop = new Rectangle((img.Width - side) / 2, (img.Height - side) / 2, side, side);

            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    int r = (int)Math.Round(s * round);
                    if (r >= 3)
                    {
                        using (GraphicsPath p = RoundRect(new Rectangle(0, 0, s, s), r))
                        {
                            g.SetClip(p);
                            g.DrawImage(img, new Rectangle(0, 0, s, s), crop, GraphicsUnit.Pixel);
                        }
                    }
                    else
                    {
                        g.DrawImage(img, new Rectangle(0, 0, s, s), crop, GraphicsUnit.Pixel);
                    }
                }
                blobs[i] = BmpEntry(bmp);
                bmp.Dispose();
            }
        }

        WriteIco(dstPath, sizes, blobs);
        FileInfo fi = new FileInfo(dstPath);
        Console.WriteLine("已写出: " + dstPath + "  " + fi.Length + " 字节, 包含尺寸: " + string.Join(",", Array.ConvertAll(sizes, delegate(int x) { return x.ToString(); })));
        return 0;
    }
}
