using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Zogos
{
    public static class CyberTheme
    {
        public static Color Alpha(Color c, int a)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B);
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static float Pulse(float phase, float speed, float min, float max)
        {
            float v = (float)((Math.Sin(phase * speed) + 1.0) * 0.5);
            return min + (max - min) * v;
        }

        public static void DrawBackdrop(Graphics g, Rectangle r, float phase, Color accent)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (LinearGradientBrush b = new LinearGradientBrush(r,
                Color.FromArgb(4, 7, 16), Color.FromArgb(9, 18, 42), 35f))
                g.FillRectangle(b, r);

            // 中心呼吸光
            int cx = r.Width / 2;
            int cy = r.Height / 2;
            int rad = Math.Max(r.Width, r.Height) / 2;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(cx - rad, cy - rad, rad * 2, rad * 2);
                using (PathGradientBrush pb = new PathGradientBrush(p))
                {
                    pb.CenterColor = Alpha(accent, (int)Pulse(phase, 0.9f, 10f, 30f));
                    pb.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                    g.FillPath(pb, p);
                }
            }

            // 第二、第三组 RGB 光斑，制造脉冲变化的次级界面
            try
            {
                int mx = (int)(r.Width * 0.72 + Math.Sin(phase * 0.8) * 38);
                int my = (int)(r.Height * 0.24 + Math.Cos(phase * 0.6) * 24);
                int mr = Math.Max(80, r.Width / 3);
                using (GraphicsPath p2 = new GraphicsPath())
                {
                    p2.AddEllipse(mx - mr, my - mr, mr * 2, mr * 2);
                    using (PathGradientBrush pb2 = new PathGradientBrush(p2))
                    {
                        pb2.CenterColor = Color.FromArgb((int)Pulse(phase, 1.3f, 8f, 28f), 255, 40, 210);
                        pb2.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                        g.FillPath(pb2, p2);
                    }
                }
                int gx = (int)(r.Width * 0.28 + Math.Cos(phase * 0.7) * 28);
                int gy = (int)(r.Height * 0.72 + Math.Sin(phase * 0.9) * 26);
                int gr = Math.Max(70, r.Width / 4);
                using (GraphicsPath p3 = new GraphicsPath())
                {
                    p3.AddEllipse(gx - gr, gy - gr, gr * 2, gr * 2);
                    using (PathGradientBrush pb3 = new PathGradientBrush(p3))
                    {
                        pb3.CenterColor = Color.FromArgb((int)Pulse(phase, 1.1f, 6f, 22f), 0, 255, 170);
                        pb3.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                        g.FillPath(pb3, p3);
                    }
                }
            }
            catch { }

            DrawGridOverlay(g, r, phase, accent);
        }

        public static void DrawGridOverlay(Graphics g, Rectangle r, float phase, Color accent)
        {
            int step = 42;
            using (Pen gp = new Pen(Color.FromArgb(18, accent), 1))
            {
                for (int x = (int)(phase * 12) % step; x < r.Width; x += step)
                    g.DrawLine(gp, x, 0, x, r.Height);
                for (int y = 0; y < r.Height; y += step)
                    g.DrawLine(gp, 0, y, r.Width, y);
            }
            int sy = (int)((phase * 46) % Math.Max(1, r.Height));
            using (LinearGradientBrush sb = new LinearGradientBrush(new Rectangle(0, sy - 26, r.Width, 52),
                Color.FromArgb(0, accent), Color.FromArgb(42, accent), 90f))
                g.FillRectangle(sb, 0, sy - 26, r.Width, 52);
        }

        public static void DrawHudCorners(Graphics g, Rectangle r, Color c, float phase, int len)
        {
            using (Pen p = new Pen(Alpha(c, (int)Pulse(phase, 1.4f, 90f, 210f)), 2f))
            {
                g.DrawLine(p, r.Left, r.Top, r.Left + len, r.Top);
                g.DrawLine(p, r.Left, r.Top, r.Left, r.Top + len);
                g.DrawLine(p, r.Right, r.Top, r.Right - len, r.Top);
                g.DrawLine(p, r.Right, r.Top, r.Right, r.Top + len);
                g.DrawLine(p, r.Left, r.Bottom, r.Left + len, r.Bottom);
                g.DrawLine(p, r.Left, r.Bottom, r.Left, r.Bottom - len);
                g.DrawLine(p, r.Right, r.Bottom, r.Right - len, r.Bottom);
                g.DrawLine(p, r.Right, r.Bottom, r.Right, r.Bottom - len);
            }
        }

        public static void DrawGlowEllipse(Graphics g, Rectangle r, Color c, float phase, int layers)
        {
            for (int i = layers; i >= 1; i--)
            {
                int a = (int)(10 + Pulse(phase, 1.7f, 10f, 40f)) * i / layers;
                using (Pen p = new Pen(Alpha(c, a), i * 3f))
                    g.DrawEllipse(p, r);
            }
        }
    }
}
