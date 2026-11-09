using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>配色方案。可在【外观】页切换，切换后重启生效。</summary>
    public static class Pal
    {
        public static Color Title1 = Color.FromArgb(5, 12, 26);
        public static Color Title2 = Color.FromArgb(12, 34, 62);
        public static Color Accent = Color.FromArgb(0, 229, 255);
        public static Color AccentDeep = Color.FromArgb(0, 128, 255);
        public static Color PageBack = Color.FromArgb(4, 7, 16);
        public static Color White = Color.FromArgb(11, 18, 34);
        public static Color CardBorder = Color.FromArgb(95, 0, 229, 255);
        public static Color Panel1 = Color.FromArgb(7, 13, 27);
        public static Color Panel2 = Color.FromArgb(13, 23, 48);
        public static Color TextMain = Color.FromArgb(228, 246, 255);
        public static Color TextSub = Color.FromArgb(132, 174, 204);
        public static Color TextFaint = Color.FromArgb(78, 108, 140);
        public static Color WarnBack = Color.FromArgb(42, 30, 10);
        public static Color WarnBorder = Color.FromArgb(255, 180, 40);
        public static Color WarnText = Color.FromArgb(255, 214, 120);
        public static Color Danger = Color.FromArgb(255, 66, 96);
        public static Color OkGreen = Color.FromArgb(0, 255, 170);
        public static Color HoverBlue = Color.FromArgb(14, 34, 58);
        public static Color DownBlue = Color.FromArgb(20, 48, 82);

        public static readonly string[] ThemeNames = new string[] { L.T("赛博青（默认）"), L.T("霓虹紫"), L.T("猩红协议"), L.T("矩阵绿") };
        public static int ThemeIndex;

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public static void ApplyTheme(int idx)
        {
            if (idx < 0 || idx >= ThemeNames.Length) idx = 0;
            ThemeIndex = idx;
            // 四套都是赛博朋克暗色底，只换霓虹主色
            Title1 = C(5, 12, 26); Title2 = C(12, 34, 62);
            PageBack = C(4, 7, 16); White = C(11, 18, 34);
            Panel1 = C(7, 13, 27); Panel2 = C(13, 23, 48);
            TextMain = C(228, 246, 255); TextSub = C(132, 174, 204); TextFaint = C(78, 108, 140);
            HoverBlue = C(14, 34, 58); DownBlue = C(20, 48, 82);
            WarnBack = C(42, 30, 10); WarnBorder = C(255, 180, 40); WarnText = C(255, 214, 120);
            Danger = C(255, 66, 96); OkGreen = C(0, 255, 170);
            if (idx == 0)
            {
                Accent = C(0, 229, 255); AccentDeep = C(0, 128, 255); CardBorder = Color.FromArgb(95, 0, 229, 255);
            }
            else if (idx == 1)
            {
                Accent = C(190, 80, 255); AccentDeep = C(120, 50, 230); CardBorder = Color.FromArgb(95, 190, 80, 255);
            }
            else if (idx == 2)
            {
                Accent = C(255, 60, 120); AccentDeep = C(220, 30, 80); CardBorder = Color.FromArgb(95, 255, 60, 120);
            }
            else
            {
                Accent = C(60, 255, 160); AccentDeep = C(0, 180, 120); CardBorder = Color.FromArgb(95, 60, 255, 160);
            }
        }
    }

    public static class Fonts
    {
        public static readonly string Family = Pick();
        private static string Pick()
        {
            string[] names = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", L.T("微软雅黑"), "Segoe UI", "Tahoma" };
            try
            {
                using (InstalledFontCollection col = new InstalledFontCollection())
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        for (int j = 0; j < col.Families.Length; j++)
                        {
                            if (string.Equals(col.Families[j].Name, names[i], StringComparison.OrdinalIgnoreCase)) return col.Families[j].Name;
                        }
                    }
                }
            }
            catch { }
            return "Segoe UI";
        }
        public static Font F(float pt) { try { return new Font(Family, pt, FontStyle.Regular, GraphicsUnit.Point); } catch { return SystemFonts.DefaultFont; } }
        public static Font B(float pt) { try { return new Font(Family, pt, FontStyle.Bold, GraphicsUnit.Point); } catch { return SystemFonts.DefaultFont; } }
    }

    internal static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    public static class Draw
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (r.Width <= 1 || r.Height <= 1) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d < 2) { p.AddRectangle(r); return p; }
            try
            {
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure();
            }
            catch { p.Reset(); p.AddRectangle(r); }
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color c, float w)
        {
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(c, w))
                g.DrawPath(pen, p);
        }

        /// <summary>用几何图形画一颗爱心（不依赖字体，任何系统都能显示）。</summary>
        public static void Heart(Graphics g, int cx, int cy, int s, Color c)
        {
            try
            {
                int r = (int)(s * 0.52);
                int off = (int)(s * 0.46);
                int top = cy - (int)(s * 0.34);
                using (SolidBrush b = new SolidBrush(c))
                {
                    g.FillEllipse(b, cx - off - r, top - r, r * 2, r * 2);
                    g.FillEllipse(b, cx + off - r, top - r, r * 2, r * 2);
                    PointF[] pts = new PointF[3];
                    pts[0] = new PointF(cx - off - r + 2, top);
                    pts[1] = new PointF(cx + off + r - 2, top);
                    pts[2] = new PointF(cx, cy + s);
                    g.FillPolygon(b, pts);
                }
            }
            catch { }
        }

        /// <summary>单行文本（自动省略号）。</summary>
        public static void T1(Graphics g, string s, Font f, Color c, Rectangle r, ContentAlignment a)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            fl |= Align(a, false);
            TextRenderer.DrawText(g, s, f, r, c, fl);
        }

        /// <summary>多行文本（自动换行）。</summary>
        public static void TW(Graphics g, string s, Font f, Color c, Rectangle r, ContentAlignment a)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;
            fl |= Align(a, true);
            TextRenderer.DrawText(g, s, f, r, c, fl);
        }

        private static TextFormatFlags Align(ContentAlignment a, bool multi)
        {
            TextFormatFlags fl = TextFormatFlags.Left;
            if (a == ContentAlignment.MiddleCenter || a == ContentAlignment.TopCenter || a == ContentAlignment.BottomCenter) fl = TextFormatFlags.HorizontalCenter;
            else if (a == ContentAlignment.MiddleRight || a == ContentAlignment.TopRight || a == ContentAlignment.BottomRight) fl = TextFormatFlags.Right;
            if (!multi) fl |= TextFormatFlags.VerticalCenter;
            else
            {
                if (a == ContentAlignment.TopLeft || a == ContentAlignment.TopCenter || a == ContentAlignment.TopRight) fl |= TextFormatFlags.Top;
                else if (a == ContentAlignment.BottomLeft || a == ContentAlignment.BottomCenter || a == ContentAlignment.BottomRight) fl |= TextFormatFlags.Bottom;
                else fl |= TextFormatFlags.VerticalCenter;
            }
            return fl;
        }
    }

    /// <summary>渐变面板：把渐变画在 OnPaintBackground 里，这样子控件的透明背景也能正确显示。</summary>
    public class TransparentPanel : Panel
    {
        public TransparentPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
    }

    public class ZGradientPanel : Panel
    {
        public Color Grad1 = Pal.Panel1;
        public Color Grad2 = Pal.Panel2;
        public float GradAngle = 90f;
        /// <summary>面板背景图（可选），会按比例填满并蒙一层半透明暗色保证文字可读。</summary>
        public Image Background;
        public int BackgroundWash = 130;
        public bool Cyber;
        public bool SeeThrough;
        public float Phase;

        public ZGradientPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Pal.PageBack;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            try
            {
                Rectangle r = ClientRectangle;
                if (r.Width <= 0 || r.Height <= 0) return;
                if (SeeThrough)
                {
                    base.OnPaintBackground(e);
                    if (Background != null)
                    {
                        Graphics g2 = e.Graphics;
                        g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g2.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        float sc = Math.Max((float)r.Width / Background.Width, (float)r.Height / Background.Height);
                        int dw = (int)Math.Round(Background.Width * sc);
                        int dh = (int)Math.Round(Background.Height * sc);
                        g2.DrawImage(Background, new Rectangle(r.X + (r.Width - dw) / 2, r.Y + (r.Height - dh) / 2, dw, dh));
                        using (SolidBrush wb = new SolidBrush(Color.FromArgb(BackgroundWash, 4, 7, 16)))
                            g2.FillRectangle(wb, r);
                    }
                    using (LinearGradientBrush b = new LinearGradientBrush(r, Grad1, Grad2, GradAngle))
                        e.Graphics.FillRectangle(b, r);
                    return;
                }
                if (Cyber)
                {
                    CyberTheme.DrawBackdrop(e.Graphics, r, Phase, Pal.Accent);
                    if (Background != null)
                    {
                        Graphics g2 = e.Graphics;
                        g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g2.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        float sc = Math.Max((float)r.Width / Background.Width, (float)r.Height / Background.Height);
                        int dw = (int)Math.Round(Background.Width * sc);
                        int dh = (int)Math.Round(Background.Height * sc);
                        g2.DrawImage(Background, new Rectangle(r.X + (r.Width - dw) / 2, r.Y + (r.Height - dh) / 2, dw, dh));
                        using (SolidBrush wb = new SolidBrush(Color.FromArgb(BackgroundWash, 4, 7, 16)))
                            g2.FillRectangle(wb, r);
                    }
                    return;
                }
                if (Background != null)
                {
                    Graphics g2 = e.Graphics;
                    g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g2.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float sc = Math.Max((float)r.Width / Background.Width, (float)r.Height / Background.Height);
                    int dw = (int)Math.Round(Background.Width * sc);
                    int dh = (int)Math.Round(Background.Height * sc);
                    g2.DrawImage(Background, new Rectangle(r.X + (r.Width - dw) / 2, r.Y + (r.Height - dh) / 2, dw, dh));
                    using (SolidBrush wb = new SolidBrush(Color.FromArgb(BackgroundWash, 4, 7, 16)))
                        g2.FillRectangle(wb, r);
                }
                else
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(r, Grad1, Grad2, GradAngle))
                        e.Graphics.FillRectangle(b, r);
                }
            }
            catch { base.OnPaintBackground(e); }
        }
    }

    /// <summary>圆角卡片。</summary>
    public class ZCard : Panel
    {
        public int Radius = 10;
        public Color Fill = Pal.White;
        public Color Border = Pal.CardBorder;
        public int BorderWidth = 1;
        public bool Gradient = false;
        public bool SeeThrough;
        public Color Grad1 = Pal.Panel1;
        public Color Grad2 = Pal.Panel2;
        /// <summary>圆角外面那一圈的颜色（一般等于父容器的颜色）。</summary>
        public Color CornerColor = Pal.PageBack;

        public ZCard()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Pal.PageBack;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            try
            {
                Rectangle r = ClientRectangle;
                if (r.Width <= 0 || r.Height <= 0) return;
                if (SeeThrough) base.OnPaintBackground(e);
                else using (SolidBrush b = new SolidBrush(CornerColor)) e.Graphics.FillRectangle(b, r);
                Rectangle rr = new Rectangle(0, 0, r.Width - 1, r.Height - 1);
                using (GraphicsPath p = Draw.Round(rr, Radius))
                {
                    if (Gradient)
                    {
                        using (LinearGradientBrush gb = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(2, r.Width), Math.Max(2, r.Height)), Grad1, Grad2, 90f))
                            e.Graphics.FillPath(gb, p);
                    }
                    else
                    {
                        using (SolidBrush b = new SolidBrush(Fill)) e.Graphics.FillPath(b, p);
                    }
                    if (BorderWidth > 0)
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (Pen pen = new Pen(Border, BorderWidth)) e.Graphics.DrawPath(pen, p);
                    }
                }
            }
            catch { base.OnPaintBackground(e); }
        }
    }

    /// <summary>圆角按钮。</summary>
    public class ZButton : Control
    {
        public Color BackNormal = Pal.White;
        public Color BackHover = Pal.HoverBlue;
        public Color BackDown = Pal.DownBlue;
        public Color BorderColor = Pal.Accent;
        public Color TextColor = Pal.Accent;
        public Color CornerColor = Pal.White;
        // 默认开启：按钮贴在渐变/图片背景上时，圆角外的四角必须裁掉，
        // 否则会露出 CornerColor 的方块（深色背景上就是一圈突兀的"黑边"）。
        private bool _clipCorners = true;
        /// <summary>
        /// true = 用 Region 把圆角以外的四个角裁掉。
        /// 按钮贴在图片/渐变背景上时必须打开，否则那四个角会露出 CornerColor 的方块。
        /// </summary>
        public bool ClipCorners
        {
            get { return _clipCorners; }
            set { _clipCorners = value; ApplyRegion(); Invalidate(); }
        }
        public int Radius = 7;
        public int BorderWidth = 1;
        public string SubText = "";
        public bool Heart;
        public Color SubColor = Pal.TextSub;
        public ContentAlignment TextAlign = ContentAlignment.MiddleCenter;

        private bool _hover, _down;
        private Font _font, _sub;

        public ZButton()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Pal.White;
            Cursor = Cursors.Hand;
            TabStop = false;
            _font = Fonts.F(9f);
            _sub = Fonts.F(8f);
        }

        public void SetFont(Font f) { _font = f; Invalidate(); }
        public void SetSubFont(Font f) { _sub = f; Invalidate(); }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); ApplyRegion(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyRegion(); }

        private void ApplyRegion()
        {
            try
            {
                if (!_clipCorners)
                {
                    if (Region != null) { Region old = Region; Region = null; old.Dispose(); }
                    return;
                }
                int wd = Width, ht = Height;
                if (wd <= 2 || ht <= 2) return;
                using (GraphicsPath p = Draw.Round(new Rectangle(0, 0, wd - 1, ht - 1), Radius))
                {
                    Region old = Region;
                    Region = new Region(p);
                    if (old != null) old.Dispose();
                }
            }
            catch { }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                if (r.Width <= 1 || r.Height <= 1) return;

                if (!_clipCorners)
                {
                    Color corner = CornerColor;
                    using (SolidBrush cb = new SolidBrush(corner)) g.FillRectangle(cb, ClientRectangle);
                }

                Color bg;
                if (!Enabled) bg = Color.FromArgb(246, 248, 251);
                else if (_down) bg = BackDown;
                else if (_hover) bg = BackHover;
                else bg = BackNormal;

                Color bc = Enabled ? BorderColor : Color.FromArgb(214, 222, 232);
                Color tc = Enabled ? TextColor : Pal.TextFaint;

                Draw.FillRound(g, r, Radius, bg);
                if (BorderWidth > 0) Draw.StrokeRound(g, r, Radius, bc, BorderWidth);

                if (string.IsNullOrEmpty(SubText))
                {
                    if (Heart)
                    {
                        int tw = 0;
                        try { tw = TextRenderer.MeasureText(Text, _font).Width; } catch { }
                        int hs = 8;
                        int total = hs * 2 + 8 + tw;
                        int sx = r.X + (r.Width - total) / 2;
                        Draw.Heart(g, sx + hs, r.Y + r.Height / 2, hs, tc);
                        Draw.T1(g, Text, _font, tc, new Rectangle(sx + hs * 2 + 8, r.Y, tw + 12, r.Height), ContentAlignment.MiddleLeft);
                    }
                    else Draw.T1(g, Text, _font, tc, r, TextAlign);
                }
                else
                {
                    Rectangle rt = new Rectangle(r.X + 6, r.Y + 10, r.Width - 12, 26);
                    Rectangle rs = new Rectangle(r.X + 6, r.Y + 36, r.Width - 12, 18);
                    Draw.T1(g, Text, _font, tc, rt, ContentAlignment.MiddleCenter);
                    Draw.T1(g, SubText, _sub, SubColor, rs, ContentAlignment.MiddleCenter);
                }
            }
            catch { }
        }
    }

    /// <summary>启动器里的整合包图标：优先用 exe 旁边的图片，其次用内嵌的 logo.png。</summary>
    public static class AppLogo
    {
        private static Image _img;
        private static bool _tried;

        public static Image Get()
        {
            if (_tried) return _img;
            _tried = true;
            try
            {
                string dir = "";
                try { dir = Path.GetDirectoryName(Application.ExecutablePath); } catch { }
                if (!string.IsNullOrEmpty(dir))
                {
                    string[] names = new string[] { L.T("ZOGOS图标.png"), L.T("ZOGOS图标.jpg"), L.T("ZOGOS图标.jpeg"), "logo.png", L.T("图标.png") };
                    for (int i = 0; i < names.Length; i++)
                    {
                        string p = Path.Combine(dir, names[i]);
                        if (File.Exists(p))
                        {
                            _img = LoadNoLock(p);
                            if (_img != null) { Log.Info(L.T("界面图标使用外部图片：") + p); return _img; }
                        }
                    }
                }
                Assembly asm = Assembly.GetExecutingAssembly();
                string[] res = asm.GetManifestResourceNames();
                for (int i = 0; i < res.Length; i++)
                {
                    if (res[i].IndexOf("logo", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        using (Stream s = asm.GetManifestResourceStream(res[i]))
                        {
                            if (s != null)
                            {
                                _img = FromRes(s);
                                Log.Info(L.T("界面图标使用内嵌图片：") + res[i]);
                                return _img;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(L.T("读取界面图标失败：") + ex.Message); }
            return _img;
        }

        private static Image _top;
        private static bool _topTried;

        /// <summary>标题栏左上角的小图标。</summary>
        public static Image Top()
        {
            if (_topTried) return _top;
            _topTried = true;
            try
            {
                string dir = "";
                try { dir = Path.GetDirectoryName(Application.ExecutablePath); } catch { }
                if (!string.IsNullOrEmpty(dir))
                {
                    string[] names = new string[] { L.T("ZOGOS顶栏图标.png"), "topicon.png" };
                    for (int i = 0; i < names.Length; i++)
                    {
                        string p = Path.Combine(dir, names[i]);
                        if (File.Exists(p)) { _top = LoadNoLock(p); if (_top != null) return _top; }
                    }
                }
                Assembly asm = Assembly.GetExecutingAssembly();
                string[] res = asm.GetManifestResourceNames();
                for (int i = 0; i < res.Length; i++)
                {
                    if (res[i].IndexOf("topicon", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        using (Stream s = asm.GetManifestResourceStream(res[i]))
                            if (s != null) { _top = FromRes(s); return _top; }
                    }
                }
            }
            catch { }
            return _top;
        }

        /// <summary>按名字取内嵌图片（背景图等）。</summary>
        public static Image GetByName(string key)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string[] res = asm.GetManifestResourceNames();
                for (int i = 0; i < res.Length; i++)
                {
                    if (res[i].IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        using (Stream s = asm.GetManifestResourceStream(res[i]))
                            if (s != null) return FromRes(s);
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>从磁盘读一张图（不锁文件）。</summary>
        public static Image LoadFile(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) return LoadNoLock(path); } catch { }
            return null;
        }

        /// <summary>读成内存流再解码，避免图片文件被锁住。</summary>
        /// <summary>把资源流安全地转成 Image（必须把流留在内存里，Image 会持续读它）。</summary>
        private static Image FromRes(Stream s)
        {
            try
            {
                if (s == null) return null;
                byte[] data;
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[81920];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    data = ms.ToArray();
                }
                return Decouple(data);
            }
            catch { return null; }
        }

        /// <summary>
        /// 把图片字节解码成「完全不依赖流」的 Bitmap。
        /// Image.FromStream 出来的图会一直引用那个流，流一旦被 GC 回收，
        /// 之后再用这张图就是野指针 —— 轻则黑图，重则 0xC0000005。
        /// </summary>
        private static Image Decouple(byte[] data)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(data))
                using (Image tmp = Image.FromStream(ms))
                {
                    Bitmap bmp = new Bitmap(tmp.Width, tmp.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
                    }
                    return bmp;
                }
            }
            catch { return null; }
        }

        private static Image LoadNoLock(string path)
        {
            try
            {
                byte[] b = File.ReadAllBytes(path);
                return Decouple(b);
            }
            catch { return null; }
        }
    }

    /// <summary>整合包图标方块。</summary>
    public class ZIconBox : Control
    {
        public string Glyph = L.T("创");
        public Image Picture;
        public int Radius = 18;
        public Color C1 = Color.FromArgb(104, 88, 200);
        public Color C2 = Color.FromArgb(54, 148, 228);
        public Color CornerColor = Pal.White;

        public ZIconBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Pal.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                if (r.Width <= 1 || r.Height <= 1) return;
                using (SolidBrush cb = new SolidBrush(CornerColor)) g.FillRectangle(cb, ClientRectangle);
                Image img = Picture != null ? Picture : AppLogo.Get();
                using (GraphicsPath p = Draw.Round(r, Radius))
                {
                    if (img != null)
                    {
                        GraphicsState state = g.Save();
                        g.SetClip(p);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        float sc = Math.Max((float)r.Width / img.Width, (float)r.Height / img.Height);
                        int dw = (int)Math.Round(img.Width * sc);
                        int dh = (int)Math.Round(img.Height * sc);
                        g.DrawImage(img, new Rectangle(r.X + (r.Width - dw) / 2, r.Y + (r.Height - dh) / 2, dw, dh));
                        g.Restore(state);
                    }
                    else
                    {
                        using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(2, Width), Math.Max(2, Height)), C1, C2, 45f))
                            g.FillPath(b, p);
                        float size = Width * 0.42f;
                        if (size < 8f) size = 8f;
                        using (Font f = Fonts.B(size))
                            Draw.T1(g, Glyph, f, Color.White, ClientRectangle, ContentAlignment.MiddleCenter);
                    }
                    using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1)) g.DrawPath(pen, p);
                }
            }
            catch { }
        }
    }

    /// <summary>排序方案卡片（单选）。</summary>
    public class ZSchemeCard : Control
    {
        public SortScheme Scheme;
        public bool Selected;
        public event EventHandler Picked;

        private bool _hover;
        private Font _fName, _fMeta, _fBadge;

        public ZSchemeCard()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Pal.White;
            Cursor = Cursors.Hand;
            TabStop = false;
            Height = 66;
            _fName = Fonts.B(10f);
            _fMeta = Fonts.F(8.5f);
            _fBadge = Fonts.F(8f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e)
        {
            Selected = true;
            Invalidate();
            if (Picked != null) Picked(this, EventArgs.Empty);
            base.OnClick(e);
        }

        public void SetSelected(bool sel)
        {
            if (Selected == sel) return;
            Selected = sel;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                if (r.Width <= 4 || r.Height <= 4) return;

                using (SolidBrush cb = new SolidBrush(Pal.PageBack)) g.FillRectangle(cb, ClientRectangle);

                Color bg = Selected ? Color.FromArgb(22, 44, 70) : (_hover ? Color.FromArgb(16, 30, 50) : Pal.White);
                Draw.FillRound(g, r, 9, bg);
                Draw.StrokeRound(g, r, 9, Selected ? Pal.Accent : Pal.CardBorder, Selected ? 2f : 1f);

                int cy = Height / 2;
                Rectangle rr = new Rectangle(18, cy - 9, 18, 18);
                using (Pen pen = new Pen(Selected ? Pal.Accent : Pal.TextFaint, 2f))
                    g.DrawEllipse(pen, rr);
                if (Selected)
                {
                    using (SolidBrush b = new SolidBrush(Pal.Accent))
                        g.FillEllipse(b, rr.X + 5, rr.Y + 5, 8, 8);
                }

                int tx = 48;
                int badW = 0;
                bool rjw = Scheme != null && Scheme.HasRjw;
                bool miss = Scheme != null && Scheme.Missing > 0;
                string rjwTxt = (Scheme != null && Scheme.RjwCount > 0) ? (L.T("含 RJW ×") + Scheme.RjwCount) : L.T("含 RJW");
                string missTxt = Scheme == null ? "" : (L.T("本地缺失 ") + Scheme.Missing + L.T(" 个"));
                if (rjw) badW += BadgeWidth(g, rjwTxt) + 6;
                if (miss) badW += BadgeWidth(g, missTxt) + 6;

                int textW = Width - tx - 14 - badW;
                if (textW < 40) textW = 40;
                string nm = Scheme == null ? "" : Scheme.Name;
                string meta = Scheme == null ? "" : Scheme.Describe();
                Draw.T1(g, nm, _fName, Pal.TextMain, new Rectangle(tx, cy - 21, textW, 20), ContentAlignment.MiddleLeft);
                Draw.T1(g, meta, _fMeta, Pal.TextSub, new Rectangle(tx, cy + 1, textW, 18), ContentAlignment.MiddleLeft);

                int bx = Width - 14;
                if (miss)
                {
                    int w = BadgeWidth(g, missTxt);
                    bx -= w + 6;
                    DrawBadge(g, new Rectangle(bx, cy - 10, w, 20), missTxt, Color.FromArgb(60, 42, 12), Color.FromArgb(255, 200, 90));
                }
                if (rjw)
                {
                    int w = BadgeWidth(g, rjwTxt);
                    bx -= w + 6;
                    DrawBadge(g, new Rectangle(bx, cy - 10, w, 20), rjwTxt, Color.FromArgb(56, 16, 38), Color.FromArgb(255, 110, 180));
                }
            }
            catch { }
        }

        private int BadgeWidth(Graphics g, string s)
        {
            try { return TextRenderer.MeasureText(g, s, _fBadge, new Size(int.MaxValue, 40), TextFormatFlags.NoPadding).Width + 16; }
            catch { return 70; }
        }

        private void DrawBadge(Graphics g, Rectangle r, string s, Color back, Color fore)
        {
            Draw.FillRound(g, r, 9, back);
            Draw.T1(g, s, _fBadge, fore, r, ContentAlignment.MiddleCenter);
        }
    }

    /// <summary>PCL 风格标题栏：渐变底 + 导航标签 + 窗口按钮 + 拖动窗口。</summary>
    public class ZTabBar : Control
    {
        public string[] Tabs = new string[] { L.T("主页"), L.T("排序"), L.T("设置"), L.T("日志") };
        public int Selected;
        public event EventHandler TabChanged;
        public event EventHandler MinimizeClicked;
        public event EventHandler CloseClicked;
        public string Title = L.T("ASTRA // 创世神协议");
        public Image Logo;
        public float Phase;

        private int _hover = -1;
        private bool _down;
        private int _pressed = -1;
        private Rectangle[] _tabRects = new Rectangle[0];
        private Rectangle _minRect, _closeRect;
        private Font _f, _fb, _fIcon;

        public ZTabBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            TabStop = false;
            _f = Fonts.F(9.5f);
            _fb = Fonts.B(9.5f);
            _fIcon = Fonts.B(11f);
        }

        private void Compute()
        {
            int w = Width, h = Height;
            _closeRect = new Rectangle(w - 46, 0, 46, h);
            _minRect = new Rectangle(w - 92, 0, 46, h);
            int n = Tabs.Length;
            int tabW = 74, tabH = 30, gap = 6;
            int total = n * tabW + (n - 1) * gap;
            int x0 = w - 92 - 16 - total;
            int y0 = (h - tabH) / 2;
            _tabRects = new Rectangle[n];
            for (int i = 0; i < n; i++) _tabRects[i] = new Rectangle(x0 + i * (tabW + gap), y0, tabW, tabH);
        }

        private int Hit(int x, int y)
        {
            if (_closeRect.Contains(x, y)) return -2;
            if (_minRect.Contains(x, y)) return -3;
            for (int i = 0; i < _tabRects.Length; i++) if (_tabRects[i].Contains(x, y)) return i;
            return -1;
        }

        protected override void OnResize(EventArgs e) { Compute(); base.OnResize(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Compute();
            int h = Hit(e.X, e.Y);
            if (h != _hover) { _hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Compute();
            _pressed = Hit(e.X, e.Y);
            if (_pressed == -1)
            {
                Form f = FindForm();
                if (f != null)
                {
                    Native.ReleaseCapture();
                    Native.SendMessage(f.Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Compute();
            int up = Hit(e.X, e.Y);
            if (up == _pressed)
            {
                if (up == -2) { if (CloseClicked != null) CloseClicked(this, EventArgs.Empty); }
                else if (up == -3) { if (MinimizeClicked != null) MinimizeClicked(this, EventArgs.Empty); }
                else if (up >= 0 && up != Selected)
                {
                    Selected = up;
                    Invalidate();
                    if (TabChanged != null) TabChanged(this, EventArgs.Empty);
                }
            }
            _pressed = -1;
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Compute();
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle cr = ClientRectangle;
                if (cr.Width <= 1 || cr.Height <= 1) return;

                try { base.OnPaintBackground(e); } catch { }
                using (LinearGradientBrush bg = new LinearGradientBrush(cr, Color.FromArgb(165, 5, 12, 26), Color.FromArgb(165, 12, 34, 62), 0f))
                    g.FillRectangle(bg, cr);
                using (Pen line = new Pen(CyberTheme.Alpha(Pal.Accent, 90), 1f))
                    g.DrawLine(line, 0, cr.Height - 1, cr.Width, cr.Height - 1);

                Rectangle lr = new Rectangle(16, (Height - 26) / 2, 26, 26);
                Image lg = Logo != null ? Logo : AppLogo.Top();
                if (lg != null)
                {
                    GraphicsState gs = g.Save();
                    using (GraphicsPath lp = Draw.Round(lr, 7)) g.SetClip(lp);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(lg, lr);
                    g.Restore(gs);
                }
                else
                {
                    Draw.FillRound(g, lr, 7, CyberTheme.Alpha(Pal.Accent, 60));
                    Draw.T1(g, "Z", _fIcon, Color.White, lr, ContentAlignment.MiddleCenter);
                }
                Draw.T1(g, Title, _fb, Pal.TextMain, new Rectangle(50, 0, 372, Height), ContentAlignment.MiddleLeft);

                for (int i = 0; i < Tabs.Length; i++)
                {
                    if (i >= _tabRects.Length) break;
                    Rectangle r = _tabRects[i];
                    if (i == Selected)
                    {
                        using (GraphicsPath p = Draw.Round(r, 15))
                        {
                            using (LinearGradientBrush b = new LinearGradientBrush(r,
                                CyberTheme.Alpha(Pal.Accent, 70), CyberTheme.Alpha(Color.FromArgb(255, 60, 210), 45), 0f))
                                g.FillPath(b, p);
                            using (Pen pen = new Pen(CyberTheme.Alpha(Pal.Accent, 230), 1.4f))
                                g.DrawPath(pen, p);
                        }
                        Draw.T1(g, Tabs[i], _fb, Color.White, r, ContentAlignment.MiddleCenter);
                        int uw = (int)CyberTheme.Pulse(Phase, 2.2f, 18f, r.Width - 20);
                        using (Pen up = new Pen(Color.FromArgb(235, Pal.Accent), 2f))
                            g.DrawLine(up, r.X + (r.Width - uw) / 2, r.Bottom - 3, r.X + (r.Width + uw) / 2, r.Bottom - 3);
                    }
                    else
                    {
                        if (_hover == i)
                            Draw.FillRound(g, r, 15, CyberTheme.Alpha(Pal.Accent, 38));
                        Draw.T1(g, Tabs[i], _f, _hover == i ? Color.White : Pal.TextSub, r, ContentAlignment.MiddleCenter);
                    }
                }

                if (_hover == -3) Draw.FillRound(g, _minRect, 4, CyberTheme.Alpha(Pal.Accent, 50));
                if (_hover == -2) Draw.FillRound(g, _closeRect, 4, CyberTheme.Alpha(Pal.Danger, 180));
                using (Pen pen = new Pen(Pal.TextMain, 1.6f))
                {
                    int mx = _minRect.X + _minRect.Width / 2, my = _minRect.Y + _minRect.Height / 2;
                    g.DrawLine(pen, mx - 6, my, mx + 6, my);
                }
                using (Pen pen = new Pen(Pal.TextMain, 1.6f))
                {
                    int cx = _closeRect.X + _closeRect.Width / 2, cy = _closeRect.Y + _closeRect.Height / 2;
                    g.DrawLine(pen, cx - 6, cy - 6, cx + 6, cy + 6);
                    g.DrawLine(pen, cx + 6, cy - 6, cx - 6, cy + 6);
                }
            }
            catch { }
        }
    }
}

