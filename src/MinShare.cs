using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 自动换行的只读多行文本区（对应原 _txtSpec，分享页的格式规范展示）。
    /// 分层窗口里不能用真 TextBox，故自绘：按宽度自动换行（中文按字符断行）、滚轮滚动、右侧细滚动条。
    /// 简化（原型阶段）：不支持文本选择（「复制格式规范」按钮可复制全文）。
    /// </summary>
    class ElSpecView : El
    {
        string _raw = "";
        readonly List<string> _lines = new List<string>();
        int _scroll; // 像素

        const int LINEH = 17;

        public string Text
        {
            get { return _raw; }
            set { _raw = value ?? ""; Reflow(); }
        }

        protected override void OnBoundsChanged() { Reflow(); }

        /// <summary>按当前宽度重排（换行）；段落以 \n 分隔，段内按字符贪心断行</summary>
        void Reflow()
        {
            _lines.Clear();
            int w = Bounds.Width - 4;
            if (w > 40 && _raw.Length > 0)
            {
                var f = T.F(11.5f);
                foreach (var para in _raw.Split('\n'))
                {
                    if (para.Length == 0) { _lines.Add(""); continue; }
                    int start = 0;
                    while (start < para.Length)
                    {
                        int lo = 1, hi = para.Length - start;
                        while (lo < hi)
                        {
                            int mid = (lo + hi + 1) / 2;
                            if (TextRenderer.MeasureText(para.Substring(start, mid), f).Width <= w) lo = mid; else hi = mid - 1;
                        }
                        _lines.Add(para.Substring(start, lo));
                        start += lo;
                    }
                }
            }
            _scroll = Math.Max(0, Math.Min(_scroll, MaxScroll));
            Redraw();
        }

        int ContentHeight { get { return _lines.Count * LINEH + 8; } }
        int MaxScroll { get { return Math.Max(0, ContentHeight - Bounds.Height); } }

        public override bool OnMouseWheel(int delta)
        {
            if (MaxScroll <= 0) return false;
            int next = Math.Max(0, Math.Min(MaxScroll, _scroll - Math.Sign(delta) * 44));
            if (next == _scroll) return false;
            _scroll = next;
            Redraw();
            return true;
        }

        public override void Paint(Graphics g)
        {
            g.SetClip(Bounds);
            var f = T.F(11.5f);
            using (var b = new SolidBrush(T.Text2))
            {
                int y = Bounds.Y + 4 - _scroll;
                foreach (var line in _lines)
                {
                    if (y + LINEH > Bounds.Y && y < Bounds.Bottom)
                        g.DrawString(line, f, b, Bounds.X + 2, y);
                    y += LINEH;
                }
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
}
