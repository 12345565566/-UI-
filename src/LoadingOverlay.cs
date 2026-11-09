using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 盖在窗口上的"正在加载"遮罩。
    /// 动画是【雷达波】：几圈同心圆从中心持续向外扩散并渐隐，
    /// 叠上静止的雷达刻度网格，整体是科幻蓝，呼应主题。
    /// </summary>
    public class LoadingOverlay : Control
    {
        private readonly Timer _timer;
        private float _phase;
        private string _text = L.T("正在加载中，请稍候…");
        private string _sub = "";
        private const int Rings = 4;

        public LoadingOverlay()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(4, 7, 16);
            Visible = false;
            TabStop = false;

            _timer = new Timer();
            _timer.Interval = 33;          // 30fps，雷达波扩散才顺滑
            _timer.Tick += delegate
            {
                _phase += 0.055f;
                if (_phase > 1000f) _phase -= 1000f;
                if (Visible && IsHandleCreated && !IsDisposed) Invalidate();
            };
        }

        public void ShowOverlay(string text, string sub)
        {
            if (!string.IsNullOrEmpty(text)) _text = text;
            _sub = sub == null ? "" : sub;
            try
            {
                if (Parent != null && Parent.ClientRectangle.Width > 0)
                    Bounds = Parent.ClientRectangle;
            }
            catch { }
            Visible = true;
            try { BringToFront(); } catch { }
            if (!_timer.Enabled) _timer.Start();
            Invalidate();
        }

        public void HideOverlay()
        {
            try { _timer.Stop(); } catch { }
            Visible = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { _timer.Stop(); _timer.Dispose(); } catch { } }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                Rectangle r = ClientRectangle;
                if (r.Width <= 2 || r.Height <= 2) return;

                using (SolidBrush dim = new SolidBrush(Color.FromArgb(208, 3, 6, 14)))
                    g.FillRectangle(dim, r);

                int pw = Math.Min(340, r.Width - 40);
                int ph = Math.Min(210, r.Height - 40);
                if (pw < 80 || ph < 80) return;
                Rectangle panel = new Rectangle(r.X + (r.Width - pw) / 2, r.Y + (r.Height - ph) / 2, pw, ph);

                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (GraphicsPath pp = Draw.Round(panel, 12))
                {
                    using (LinearGradientBrush pb = new LinearGradientBrush(
                        new Rectangle(panel.X, panel.Y, Math.Max(2, panel.Width), Math.Max(2, panel.Height)),
                        Color.FromArgb(248, 7, 13, 27), Color.FromArgb(248, 11, 22, 44), 90f))
                        g.FillPath(pb, pp);
                    using (Pen pen = new Pen(Color.FromArgb(140, Pal.Accent), 1f))
                        g.DrawPath(pen, pp);
                }

                int cx = panel.X + panel.Width / 2;
                int cy = panel.Y + panel.Height / 2 - 26;
                float maxR = 44f;

                // ── 静止的雷达刻度：十字线 + 外圈 ──
                using (Pen axis = new Pen(Color.FromArgb(42, Pal.Accent), 1f))
                {
                    g.DrawLine(axis, cx - maxR - 8, cy, cx + maxR + 8, cy);
                    g.DrawLine(axis, cx, cy - maxR - 8, cx, cy + maxR + 8);
                }
                using (Pen outer = new Pen(Color.FromArgb(70, Pal.Accent), 1f))
                    g.DrawEllipse(outer, cx - maxR, cy - maxR, maxR * 2, maxR * 2);
                using (Pen mid = new Pen(Color.FromArgb(40, Pal.AccentDeep), 1f))
                    g.DrawEllipse(mid, cx - maxR * 0.55f, cy - maxR * 0.55f, maxR * 1.1f, maxR * 1.1f);

                // ── 扩散的雷达波 ──
                for (int i = 0; i < Rings; i++)
                {
                    float p = (_phase * 0.9f + (float)i / Rings) % 1f;   // 0..1 生命周期
                    float rad = 5f + (maxR - 5f) * p;
                    int alpha = (int)(210 * Math.Pow(1.0 - p, 1.25));
                    if (alpha <= 2) continue;
                    float wd = 2.6f - 1.5f * p;                           // 越往外越细
                    if (wd < 0.6f) wd = 0.6f;
                    using (Pen pn = new Pen(Color.FromArgb(alpha, Pal.Accent), wd))
                        g.DrawEllipse(pn, cx - rad, cy - rad, rad * 2, rad * 2);
                }

                // ── 中心亮点（轻微呼吸）──
                float core = 4.2f + 1.1f * (float)Math.Sin(_phase * 2.2f);
                using (GraphicsPath cp = new GraphicsPath())
                {
                    cp.AddEllipse(cx - core * 3.2f, cy - core * 3.2f, core * 6.4f, core * 6.4f);
                    using (PathGradientBrush cb = new PathGradientBrush(cp))
                    {
                        cb.CenterColor = Color.FromArgb(190, Pal.Accent);
                        cb.SurroundColors = new Color[] { Color.FromArgb(0, Pal.Accent) };
                        g.FillPath(cb, cp);
                    }
                }
                using (SolidBrush dot = new SolidBrush(Color.FromArgb(255, 236, 252, 255)))
                    g.FillEllipse(dot, cx - core, cy - core, core * 2, core * 2);

                Rectangle rt = new Rectangle(panel.X + 12, panel.Y + panel.Height - 60, panel.Width - 24, 26);
                TextRenderer.DrawText(g, _text, Fonts.B(10f), rt, Color.FromArgb(238, 248, 255),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                if (!string.IsNullOrEmpty(_sub))
                {
                    Rectangle rs = new Rectangle(panel.X + 12, panel.Y + panel.Height - 34, panel.Width - 24, 22);
                    TextRenderer.DrawText(g, _sub, Fonts.F(8.5f), rs, Color.FromArgb(140, 190, 220),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
            catch { }
        }
    }
}
