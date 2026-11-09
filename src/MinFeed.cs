using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 整合包卡片墙（对应原 PackFeedForm 的 FlowLayoutPanel + PackCard）：
    /// 卡片 224×272、间距 12，按宽度自动排几列；滚轮滚动 + 右侧细滚动条；点卡片 → CardClicked。
    /// 封面图由外部异步下载后 SetCover(url, img) 填进来；没有封面时用名字哈希渐变 + 首字占位。
    /// </summary>
    class ElPackGrid : El
    {
        public readonly List<PackItem> Items = new List<PackItem>();
        public string EmptyHint = "";
        public Image SharedCover; // 所有卡片共用的默认预览图
        public bool Compact;      // true=紧凑列表（无封面，一行两个，方便快速浏览）
        public event EventHandler CardClicked;
        readonly Dictionary<string, Image> _covers = new Dictionary<string, Image>();
        int _scroll, _hoverIdx = -1;

        const int GAP = 12, PAD = 2; // 卡片宽高按可用宽度自适应（见 Cols/CardW/CardH）

        public void SetItems(List<PackItem> items)
        {
            Items.Clear();
            if (items != null) Items.AddRange(items);
            _scroll = 0;
            _hoverIdx = -1;
            Redraw();
        }

        public void SetCover(string url, Image img)
        {
            if (string.IsNullOrEmpty(url) || img == null) return;
            _covers[url] = img;
            Redraw();
        }

        /// <summary>按 URL 取已缓存的封面（详情页用），没有返回 null</summary>
        public Image GetCover(string url)
        {
            Image img;
            return !string.IsNullOrEmpty(url) && _covers.TryGetValue(url, out img) ? img : null;
        }

        /// <summary>列数：卡片模式优先 4 列（约 218 宽），紧凑模式一行 2 个；太窄自动减</summary>
        int Cols
        {
            get
            {
                int avail = Bounds.Width - PAD * 2;
                if (Compact) return Math.Max(1, Math.Min(2, (avail + GAP) / (430 + GAP)));
                return Math.Max(1, Math.Min(4, (avail + GAP) / (218 + GAP)));
            }
        }
        int CardW { get { int c = Cols; return (Bounds.Width - PAD * 2 - (c - 1) * GAP) / c; } }
        int CoverH { get { return (CardW - 24) * 3 / 4; } } // 封面维持 4:3
        int CardH { get { return Compact ? 56 : 12 + CoverH + 8 + 40 + 26 + 10; } }
        int ContentHeight { get { return Items.Count == 0 ? 0 : PAD * 2 + ((Items.Count + Cols - 1) / Cols) * (CardH + GAP) - GAP; } }
        int MaxScroll { get { return Math.Max(0, ContentHeight - Bounds.Height); } }

        Rectangle CardRect(int i)
        {
            int c = Cols, cw = CardW, ch = CardH;
            int col = i % c, row = i / c;
            return new Rectangle(Bounds.X + PAD + col * (cw + GAP), Bounds.Y + PAD - _scroll + row * (ch + GAP), cw, ch);
        }

        int CardAt(Point p)
        {
            for (int i = 0; i < Items.Count; i++)
                if (CardRect(i).Contains(p)) return i;
            return -1;
        }

        public override void OnMouseMove(Point p)
        {
            int i = CardAt(p);
            if (i != _hoverIdx) { _hoverIdx = i; Redraw(); }
        }
        public override void OnMouseLeave() { if (_hoverIdx != -1) { _hoverIdx = -1; Redraw(); } }
        public override void OnMouseUp(Point p)
        {
            int i = CardAt(p);
            if (i >= 0 && CardClicked != null) CardClicked(Items[i], EventArgs.Empty);
        }
        public override Cursor CursorOf(Point p) { return CardAt(p) >= 0 ? Cursors.Hand : Cursors.Default; }

        public override bool OnMouseWheel(int delta)
        {
            if (MaxScroll <= 0) return false;
            int next = Math.Max(0, Math.Min(MaxScroll, _scroll - Math.Sign(delta) * 60));
            if (next == _scroll) return false;
            _scroll = next;
            _hoverIdx = -1;
            Redraw();
            return true;
        }

        // —— 占位图（照搬原 PackCard：名字哈希取色 + 压暗渐变 + 大号首字）——
        static readonly Color[] PlaceholderPal = {
            Color.FromArgb(0, 229, 255), Color.FromArgb(190, 80, 255), Color.FromArgb(255, 80, 210),
            Color.FromArgb(120, 255, 170), Color.FromArgb(255, 190, 60), Color.FromArgb(80, 180, 255)
        };
        internal static Color PlaceholderColor(string name)
        {
            int hs = 0;
            if (name != null) for (int i = 0; i < name.Length; i++) hs = hs * 31 + name[i];
            int i2 = hs % PlaceholderPal.Length;
            if (i2 < 0) i2 += PlaceholderPal.Length;
            return PlaceholderPal[i2];
        }
        internal static Color BlendDark(Color c, int a)
        {
            return Color.FromArgb(Math.Min(255, a),
                Math.Min(255, (c.R + 12) / 3),
                Math.Min(255, (c.G + 18) / 3),
                Math.Min(255, (c.B + 30) / 3));
        }

        /// <summary>把 img 以 center-crop 方式画进 r（带圆角裁剪）</summary>
        internal static void DrawCover(Graphics g, Rectangle r, Image img, string placeholderName, float radius)
        {
            GraphicsState st = g.Save();
            using (GraphicsPath cp = T.Round(new RectangleF(r.X, r.Y, r.Width, r.Height), radius))
                g.SetClip(cp, CombineMode.Intersect);
            if (img != null)
            {
                float sc = Math.Max((float)r.Width / img.Width, (float)r.Height / img.Height);
                int dw = Math.Max(1, (int)(img.Width * sc));
                int dh = Math.Max(1, (int)(img.Height * sc));
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(img, new Rectangle(r.X + (r.Width - dw) / 2, r.Y + (r.Height - dh) / 2, dw, dh));
            }
            else
            {
                Color baseC = PlaceholderColor(placeholderName);
                using (LinearGradientBrush ph = new LinearGradientBrush(r, BlendDark(baseC, 44), BlendDark(baseC, 90), 60f))
                    g.FillRectangle(ph, r);
                string ini = string.IsNullOrEmpty(placeholderName) ? "?" : placeholderName.Substring(0, Math.Min(2, placeholderName.Length));
                using (var b = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                    g.DrawString(ini, T.B(30), b, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }
            g.Restore(st);
        }

        public override void Paint(Graphics g)
        {
            if (Items.Count == 0)
            {
                var r0 = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
                T.FillRound(g, r0, 8, T.LogBg);
                T.DrawRound(g, r0, 8, T.Line2, 1f);
                using (var b = new SolidBrush(T.Text3))
                    g.DrawString(EmptyHint, T.F(11.5f), b, r0,
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                return;
            }

            g.SetClip(new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height));
            for (int i = 0; i < Items.Count; i++)
            {
                Rectangle cr = CardRect(i);
                if (cr.Bottom < Bounds.Y || cr.Y > Bounds.Bottom) continue;
                PackItem it = Items[i];
                bool hov = i == _hoverIdx;

                var rf = new RectangleF(cr.X + 0.5f, cr.Y + 0.5f, cr.Width - 1f, cr.Height - 1f);
                T.FillRound(g, rf, 10, hov ? T.Line2 : T.Card);
                T.DrawRound(g, rf, 10, hov ? T.Accent : T.Line, hov ? 1.4f : 1f);

                if (Compact) // 紧凑行：无封面，名称 + 关键参数一行流
                {
                    int right = cr.Right - 12;
                    if (it.Dead)
                    {
                        var br = new RectangleF(right - 66, cr.Y + 8, 66, 20);
                        T.FillRound(g, br, 6, Color.FromArgb(235, 190, 52, 74));
                        using (var b = new SolidBrush(Color.White))
                            g.DrawString(L.T("维护中"), T.B(10.5f), b, br,
                                new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                        right -= 66 + 10;
                    }
                    using (var b = new SolidBrush(T.Text))
                        g.DrawString(it.Name ?? "", T.B(12), b, new RectangleF(cr.X + 12, cr.Y + 5, Math.Max(40, right - cr.X - 12), 22),
                            new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
                    string sub = (string.IsNullOrEmpty(it.GameVersion) ? L.T("版本未知") : L.T("本体 ") + it.GameVersion)
                        + (string.IsNullOrEmpty(it.ModCount) ? "" : L.T("  ·  MOD ") + it.ModCount)
                        + (string.IsNullOrEmpty(it.UnzippedSize) ? "" : L.T("  ·  解压 ") + it.UnzippedSize);
                    using (var b = new SolidBrush(T.Text3))
                        g.DrawString(sub, T.F(11), b, new RectangleF(cr.X + 12, cr.Y + 29, cr.Width - 24, 20),
                            new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
                    continue;
                }

                // 封面（4:3，随卡片宽度自适应）
                Rectangle cover = new Rectangle(cr.X + 12, cr.Y + 12, cr.Width - 24, CoverH);
                Image img = null;
                if (it.Cover != null) _covers.TryGetValue(it.Cover, out img);
                if (img == null) img = SharedCover;
                DrawCover(g, cover, img, it.Name, 8);

                // 维护中徽章：两个网盘都没有效链接
                if (it.Dead)
                {
                    var br = new RectangleF(cr.Right - 82, cr.Y + 20, 66, 22);
                    T.FillRound(g, br, 6, Color.FromArgb(235, 190, 52, 74));
                    using (var b = new SolidBrush(Color.White))
                        g.DrawString(L.T("维护中"), T.B(11), b, br,
                            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                }

                // 名称（最多两行，超出省略）
                var rn = new RectangleF(cr.X + 14, cr.Y + 12 + CoverH + 8, cr.Width - 28, 40);
                using (var b = new SolidBrush(T.Text))
                    g.DrawString(it.Name ?? "", T.B(12), b, rn,
                        new StringFormat { Trimming = StringTrimming.EllipsisCharacter });

                // 版本
                string ver = string.IsNullOrEmpty(it.GameVersion) ? L.T("版本未知") : (L.T("本体 ") + it.GameVersion);
                using (var b = new SolidBrush(T.Accent))
                    g.DrawString(ver, T.F(11), b, new RectangleF(cr.X + 14, cr.Bottom - 34, cr.Width - 28, 20),
                        new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
            }
            g.ResetClip();

            if (MaxScroll > 0)
            {
                float th = Math.Max(24f, Bounds.Height * Bounds.Height / (float)ContentHeight);
                float ty = Bounds.Y + (Bounds.Height - th) * (_scroll / (float)MaxScroll);
                T.FillRound(g, new RectangleF(Bounds.Right - 5, ty, 3, th), 1.5f, T.Line);
            }
        }
    }

    /// <summary>多行段落文本（详情页简介用，自动换行）</summary>
    class ElPara : El
    {
        public string Text = "";
        public Func<Font> FontOf = () => T.F(12);
        public Func<Color> ColorOf = () => T.Text2;
        public override void Paint(Graphics g)
        {
            using (var b = new SolidBrush(ColorOf()))
                g.DrawString(Text ?? "", FontOf(), b, new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height));
        }
    }

    /// <summary>封面图盒子（详情页用）：center-crop + 圆角；没图时占位渐变</summary>
    class ElCover : El
    {
        public Image Img;
        public string PlaceholderName = "";
        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r, 8, T.LogBg);
            ElPackGrid.DrawCover(g, Bounds, Img, PlaceholderName, 8);
            T.DrawRound(g, r, 8, T.Line2, 1f);
        }
    }
}
