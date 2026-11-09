using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 苏苏舞台（对应旧版 SusuWindow 的相框 + 气泡，极简风重制）：
    /// 左侧立绘框（contain 缩放）+ 右侧气泡（说话人标签 / 打字机文本 / 进度 / 操作提示）。
    /// 状态机在外部（MainForm），这里只负责画；点气泡 = BubbleClicked（推进对话）。
    /// </summary>
    class ElSusu : El
    {
        public Image Portrait;        // 当前立绘（susu01~15，null 显示「苏」占位）
        public string Who = "";       // 说话人；非空显示标签胶囊
        public string Text = "";      // 已打出来的部分（打字机增量）
        public bool Typing;           // 打字中（底部提示语不同）
        public string Progress = "";  // "2/7"
        public int PortraitW = 300;   // 立绘区宽度
        public event EventHandler BubbleClicked;

        // —— 立绘缩放缓存：bicubic 缩放只做一次，换图或尺寸变化才重算（打字机 26ms 一帧，不能每帧都缩）——
        Image _scaledSrc; Bitmap _scaled; int _scaledW, _scaledH;
        Image GetScaledPortrait(int w, int h)
        {
            if (Portrait == null) return null;
            if (_scaled != null && _scaledSrc == Portrait && _scaledW == w && _scaledH == h) return _scaled;
            if (_scaled != null) { _scaled.Dispose(); _scaled = null; }
            float sc = Math.Min((float)w / Portrait.Width, (float)h / Portrait.Height);
            int dw = Math.Max(1, (int)(Portrait.Width * sc)), dh = Math.Max(1, (int)(Portrait.Height * sc));
            var bmp = new Bitmap(dw, dh);
            using (var g2 = Graphics.FromImage(bmp))
            {
                g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g2.DrawImage(Portrait, 0, 0, dw, dh);
            }
            _scaledSrc = Portrait; _scaled = bmp; _scaledW = w; _scaledH = h;
            return _scaled;
        }

        RectangleF BubbleRect()
        {
            return new RectangleF(Bounds.X + PortraitW + 12, Bounds.Y,
                Math.Max(80, Bounds.Width - PortraitW - 12), Bounds.Height);
        }

        public override bool HitTest(Point p)
        {
            return Visible && BubbleRect().Contains(p); // 只有气泡可点（立绘不抢点击）
        }

        public override void OnMouseUp(Point p)
        {
            if (BubbleRect().Contains(p) && BubbleClicked != null) BubbleClicked(this, EventArgs.Empty);
        }
        public override Cursor CursorOf(Point p) { return Cursors.Hand; }

        public override void Paint(Graphics g)
        {
            // —— 立绘框 ——
            var pr = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, PortraitW - 1f, Bounds.Height - 1f);
            T.FillRound(g, pr, 12, T.Card);
            GraphicsState st = g.Save();
            using (GraphicsPath cp = T.Round(pr, 12))
                g.SetClip(cp, CombineMode.Intersect);
            Image scaled = Portrait == null ? null : GetScaledPortrait((int)pr.Width, (int)pr.Height);
            if (scaled != null)
            {
                // 已缓存的缩放结果直接贴（最近邻即可，源图就是按目标尺寸缩好的）
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(scaled, pr.X + (pr.Width - scaled.Width) / 2, pr.Y + (pr.Height - scaled.Height) / 2);
            }
            else if (Portrait != null) { }
            else
            {
                using (var b = new SolidBrush(T.AccentSoft))
                    g.FillRectangle(b, pr);
                using (var b = new SolidBrush(T.Accent))
                    g.DrawString(L.T("苏"), T.B(44), b, pr,
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }
            g.Restore(st);
            T.DrawRound(g, pr, 12, T.Line, 1f);

            // —— 气泡 ——
            var br0 = BubbleRect();
            var br = new RectangleF(br0.X + 0.5f, br0.Y + 0.5f, br0.Width - 1f, br0.Height - 1f);
            T.FillRound(g, br, 12, T.Card);
            T.DrawRound(g, br, 12, T.Line, 1f);
            // 小尾巴（指向立绘）
            PointF[] tail = {
                new PointF(br.X + 1, br.Y + 34), new PointF(br.X + 1, br.Y + 52), new PointF(br.X - 8, br.Y + 43)
            };
            using (var b = new SolidBrush(T.Card)) g.FillPolygon(b, tail);
            using (var pen = new Pen(T.Line, 1f))
            {
                g.DrawLine(pen, tail[0], tail[2]);
                g.DrawLine(pen, tail[2], tail[1]);
            }

            float pad = 16;
            float ty = br.Y + 12;
            if (!string.IsNullOrEmpty(Who)) // 说话人标签胶囊
            {
                float tw = TextRenderer.MeasureText(Who, T.B(11)).Width + 18;
                var tag = new RectangleF(br.X + pad, ty, tw, 22);
                T.FillRound(g, tag, 11, T.AccentSoft);
                using (var b = new SolidBrush(T.Accent))
                    g.DrawString(Who, T.B(11), b, tag,
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                ty += 30;
            }
            // 正文（打字机增量文本，自动换行）
            float th = br.Bottom - 30 - ty;
            using (var b = new SolidBrush(T.Text))
                g.DrawString(Text ?? "", T.F(12.5f), b, new RectangleF(br.X + pad, ty, br.Width - pad * 2, Math.Max(20, th)),
                    new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near });
            // 打字光标
            if (Typing)
                using (var b = new SolidBrush(T.Accent))
                    g.DrawString("▍", T.F(12.5f), b, br.X + pad, ty);

            // 进度 + 操作提示
            if (!string.IsNullOrEmpty(Progress))
                using (var b = new SolidBrush(T.Text3))
                    g.DrawString(Progress, T.F(11), b, new RectangleF(br.Right - 70, br.Bottom - 26, 56, 18),
                        new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });
            using (var b = new SolidBrush(T.Text3))
                g.DrawString(Typing ? L.T("点气泡可直接显示全文") : L.T("点气泡 / 下一句 继续"),
                    T.F(10.5f), b, new RectangleF(br.X + pad, br.Bottom - 26, br.Width - pad * 2 - 70, 18),
                    new StringFormat { LineAlignment = StringAlignment.Center });
        }
    }
}
