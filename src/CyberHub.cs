using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>赛博朋克启动枢纽：外环脉冲、中央启动键、更多功能、版本信息和排序入口。</summary>
    public class CyberHub : Control
    {
        public float Phase;
        public Color CornerColor = Color.Transparent;
        public string PackName = L.T("未识别整合包");
        public string VersionText = L.T("RimWorld 未知");
        public string ModCountText = L.T("0 个模组");
        public string SchemeText = L.T("未选择排序");
        public string LaunchSubText = L.T("准备就绪");
        public string MoreText = L.T("打开更多功能");
        public event EventHandler StartClicked;
        public event EventHandler MoreClicked;
        public event EventHandler SortClicked;

        private int _hoverZone;
        private int _downZone;
        private Rectangle _moreRect;
        private Rectangle _startRect;
        private Rectangle _sortRect;

        public CyberHub()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            Cursor = Cursors.Default;
            TabStop = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int z = Hit(e.X, e.Y);
            if (z != _hoverZone)
            {
                _hoverZone = z;
                Cursor = z == 0 ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverZone = 0;
            _downZone = 0;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) _downZone = Hit(e.X, e.Y);
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int z = Hit(e.X, e.Y);
            int down = _downZone;
            _downZone = 0;
            Invalidate();
            if (z == down && z != 0)
            {
                if (z == 1 && MoreClicked != null) MoreClicked(this, EventArgs.Empty);
                else if (z == 2 && StartClicked != null) StartClicked(this, EventArgs.Empty);
                else if (z == 3 && SortClicked != null) SortClicked(this, EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        private int Hit(int x, int y)
        {
            if (_moreRect.Contains(x, y)) return 1;
            if (_startRect.Contains(x, y)) return 2;
            if (_sortRect.Contains(x, y)) return 3;
            return 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                Rectangle r = ClientRectangle;
                if (r.Width < 80 || r.Height < 120) return;
                if (CornerColor.A > 0) using (SolidBrush b = new SolidBrush(CornerColor)) g.FillRectangle(b, r);

                int w = r.Width, h = r.Height;
                int cx = w / 2;
                int cy = (int)(h * 0.47);
                int outerR = Math.Min((int)(w * 0.43), (int)(h * 0.29));
                if (outerR < 70) outerR = Math.Min(w / 3, Math.Max(50, h / 4));
                Rectangle ring = new Rectangle(cx - outerR, cy - outerR, outerR * 2, outerR * 2);

                // 顶部更多功能胶囊
                int moreW = Math.Min(190, w - 40);
                _moreRect = new Rectangle(cx - moreW / 2, 16, moreW, 34);
                bool moreHover = _hoverZone == 1;
                using (GraphicsPath mp = Draw.Round(_moreRect, 17))
                {
                    using (LinearGradientBrush mb = new LinearGradientBrush(_moreRect,
                        Color.FromArgb(moreHover ? 55 : 25, Pal.Accent),
                        Color.FromArgb(moreHover ? 30 : 12, Color.FromArgb(255, 60, 200)), 0f))
                        g.FillPath(mb, mp);
                    using (Pen pen = new Pen(CyberTheme.Alpha(Pal.Accent, moreHover ? 235 : 130), moreHover ? 2f : 1.2f))
                        g.DrawPath(pen, mp);
                }
                TextRenderer.DrawText(g, MoreText, Fonts.B(9.5f), _moreRect,
                    moreHover ? Color.White : Pal.TextMain,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                // 外环光晕 + 旋转刻度
                CyberTheme.DrawGlowEllipse(g, ring, Pal.Accent, Phase, 7);
                using (LinearGradientBrush rb = new LinearGradientBrush(ring,
                    Pal.Accent, Color.FromArgb(255, 50, 210), 35f))
                using (Pen rp = new Pen(rb, 2.4f))
                    g.DrawEllipse(rp, ring);
                for (int i = 0; i < 36; i++)
                {
                    float a = (float)((i * 10 + Phase * 24) * Math.PI / 180.0);
                    int x1 = cx + (int)((outerR + 9) * Math.Cos(a));
                    int y1 = cy + (int)((outerR + 9) * Math.Sin(a));
                    int x2 = cx + (int)((outerR + (i % 3 == 0 ? 18 : 13)) * Math.Cos(a));
                    int y2 = cy + (int)((outerR + (i % 3 == 0 ? 18 : 13)) * Math.Sin(a));
                    int alpha = i % 3 == 0 ? 120 : 45;
                    using (Pen tp = new Pen(CyberTheme.Alpha(Pal.Accent, alpha), 1f))
                        g.DrawLine(tp, x1, y1, x2, y2);
                }
                using (Pen ap = new Pen(CyberTheme.Alpha(Pal.Accent, 200), 2f))
                {
                    g.DrawArc(ap, ring, Phase * 42f, 74f);
                    g.DrawArc(ap, ring, Phase * 42f + 180f, 52f);
                }

                // 交叉准星线
                using (Pen cp = new Pen(CyberTheme.Alpha(Pal.Accent, 36), 1f))
                {
                    g.DrawLine(cp, cx - outerR - 24, cy, cx - outerR + 8, cy);
                    g.DrawLine(cp, cx + outerR - 8, cy, cx + outerR + 24, cy);
                    g.DrawLine(cp, cx, cy - outerR - 18, cx, cy - outerR + 8);
                    g.DrawLine(cp, cx, cy + outerR - 8, cx, cy + outerR + 18);
                }

                // 中央启动键
                int startR = Math.Max(42, (int)(outerR * 0.38));
                _startRect = new Rectangle(cx - startR, cy - startR, startR * 2, startR * 2);
                bool startHover = _hoverZone == 2;
                int glowA = (int)CyberTheme.Pulse(Phase, 2.1f, 35f, 105f);
                for (int i = 5; i >= 1; i--)
                {
                    using (Pen gp = new Pen(CyberTheme.Alpha(Pal.Accent, Math.Max(10, glowA / i)), i * 2.3f))
                        g.DrawEllipse(gp, _startRect);
                }
                using (GraphicsPath sp = new GraphicsPath())
                {
                    sp.AddEllipse(_startRect);
                    using (PathGradientBrush pb = new PathGradientBrush(sp))
                    {
                        pb.CenterColor = Color.FromArgb(230, 8, 18, 36);
                        pb.SurroundColors = new Color[] { Color.FromArgb(240, 12, 30, 60) };
                        g.FillPath(pb, sp);
                    }
                    using (Pen pen = new Pen(CyberTheme.Alpha(startHover ? Color.White : Pal.Accent, startHover ? 255 : 210), startHover ? 3f : 2f))
                        g.DrawPath(pen, sp);
                }
                TextRenderer.DrawText(g, L.T("启 动"), Fonts.B(18f), new Rectangle(_startRect.X, _startRect.Y + startR / 2 - 20, _startRect.Width, 38),
                    Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, LaunchSubText, Fonts.F(8.5f), new Rectangle(_startRect.X, _startRect.Y + startR + 2, _startRect.Width, 20),
                    Pal.TextSub, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                // 底部信息
                int infoY = cy + outerR + 26;
                if (infoY > h - 106) infoY = h - 106;
                TextRenderer.DrawText(g, PackName, Fonts.B(10.5f), new Rectangle(14, infoY, w - 28, 22),
                    Pal.TextMain, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, VersionText + "   ·   " + ModCountText, Fonts.F(8.5f), new Rectangle(14, infoY + 24, w - 28, 20),
                    Pal.TextSub, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, SchemeText, Fonts.F(8.2f), new Rectangle(14, infoY + 44, w - 28, 20),
                    Pal.TextFaint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                // 排序选择小胶囊
                int sw = Math.Min(150, w - 50);
                _sortRect = new Rectangle(cx - sw / 2, Math.Min(h - 34, infoY + 70), sw, 26);
                bool sortHover = _hoverZone == 3;
                using (GraphicsPath sp2 = Draw.Round(_sortRect, 13))
                {
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(sortHover ? 55 : 22, Pal.Accent)))
                        g.FillPath(sb, sp2);
                    using (Pen pen = new Pen(CyberTheme.Alpha(Pal.Accent, sortHover ? 220 : 100), 1f))
                        g.DrawPath(pen, sp2);
                }
                TextRenderer.DrawText(g, L.T("排序选择  ›"), Fonts.F(8.5f), _sortRect,
                    sortHover ? Color.White : Pal.TextSub,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            catch { }
        }
    }
}
