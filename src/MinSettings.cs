using System;
using System.Drawing;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 自绘单行输入框（分层窗口里不能用真 TextBox）。
    /// 支持：点击定位光标、输入、退格、Delete、左右/Home/End 键、闪烁光标、NumericOnly 数字模式（右侧 ▲▼ 微调，夹取 Min..Max）。
    /// 简化（原型阶段）：不支持文本选择、不支持水平滚动（路径不长于框宽时无影响）、不支持粘贴。
    /// 焦点由 MainForm 统一管理（SetFocusInput），Tab 键在输入框间循环。
    /// </summary>
    class ElInput : El
    {
        public string Text = "";
        public bool NumericOnly;
        public bool Password; // true=显示为 ●（API KEY 用），Text 仍是明文
        public int Min = int.MinValue, Max = int.MaxValue;
        public event EventHandler TextChanged;

        public bool Focused { get; private set; }
        int _caret;          // 0..Text.Length
        bool _caretVis = true;
        readonly Timer _blink;
        bool _hover;

        const int PADX = 8;

        public ElInput()
        {
            _blink = new Timer { Interval = 530 };
            _blink.Tick += delegate { _caretVis = !_caretVis; Redraw(); };
        }

        public void SetFocus(bool f)
        {
            if (Focused == f) return;
            Focused = f;
            _caretVis = true;
            if (f) { _caret = Text.Length; _blink.Start(); }
            else _blink.Stop();
            Redraw();
        }

        void Changed()
        {
            if (TextChanged != null) TextChanged(this, EventArgs.Empty);
            Redraw();
        }

        // —— 键盘（由 MainForm 转发）——
        public void OnChar(char c)
        {
            if (c == '\b') // 退格
            {
                if (_caret > 0) { Text = Text.Remove(_caret - 1, 1); _caret--; Changed(); }
                return;
            }
            if (c < ' ' || c == '') return;
            if (NumericOnly && (c < '0' || c > '9')) return;
            Text = Text.Insert(_caret, c.ToString());
            _caret++;
            ClampNumeric();
            Changed();
        }

        /// <summary>方向/编辑键；返回 true 表示已消费</summary>
        public bool OnKey(Keys k)
        {
            switch (k)
            {
                case Keys.Left: if (_caret > 0) _caret--; break;
                case Keys.Right: if (_caret < Text.Length) _caret++; break;
                case Keys.Home: _caret = 0; break;
                case Keys.End: _caret = Text.Length; break;
                case Keys.Delete:
                    if (_caret < Text.Length) { Text = Text.Remove(_caret, 1); Changed(); return true; }
                    return true;
                default: return false;
            }
            _caretVis = true;
            Redraw();
            return true;
        }

        void ClampNumeric()
        {
            if (!NumericOnly) return;
            int v;
            if (int.TryParse(Text, out v))
            {
                if (v > Max) Text = Max.ToString();
                if (v < Min && Text.Length > 0 && v != 0) { /* 允许输入中间态（如先输 0） */ }
            }
        }

        /// <summary>粘贴入口：单行框会把换行压成空格，逐字符走和键盘输入一样的过滤</summary>
        public void InsertText(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            s = s.Replace("\r", "").Replace("\n", " ");
            foreach (char c in s)
            {
                if (c < ' ' || c == '') continue;
                if (NumericOnly && (c < '0' || c > '9')) continue;
                Text = Text.Insert(_caret, c.ToString());
                _caret++;
            }
            ClampNumeric();
            Changed();
        }

        void Bump(int d)
        {
            int v;
            if (!int.TryParse(Text, out v)) v = Min > 0 ? Min : 0;
            v += d;
            if (v < Min) v = Min;
            if (v > Max) v = Max;
            Text = v.ToString();
            _caret = Text.Length;
            Changed();
        }

        // —— 数字微调区（NumericOnly 时右侧 20px：上半 +1，下半 -1）——
        Rectangle UpZone { get { return new Rectangle(Bounds.Right - 20, Bounds.Y + 1, 19, Bounds.Height / 2 - 1); } }
        Rectangle DownZone { get { return new Rectangle(Bounds.Right - 20, Bounds.Y + Bounds.Height / 2, 19, Bounds.Height - Bounds.Height / 2 - 1); } }

        // —— 鼠标 ——
        public override void OnMouseMove(Point p) { if (!_hover) { _hover = true; Redraw(); } }
        public override void OnMouseLeave() { _hover = false; Redraw(); }
        public override void OnMouseDown(Point p)
        {
            if (NumericOnly && UpZone.Contains(p)) { Bump(1); return; }
            if (NumericOnly && DownZone.Contains(p)) { Bump(-1); return; }
            _caret = CaretAt(p.X);
            _caretVis = true;
            Redraw();
        }
        public override Cursor CursorOf(Point p)
        {
            if (NumericOnly && (UpZone.Contains(p) || DownZone.Contains(p))) return Cursors.Hand;
            return Cursors.IBeam;
        }

        /// <summary>显示用文本（Password 模式下同长度的 ●）</summary>
        string ShowText { get { return Password && Text.Length > 0 ? new string('●', Text.Length) : Text; } }

        int CaretAt(int x)
        {
            var f = T.F(12);
            int rel = x - (Bounds.X + PADX);
            string s = ShowText;
            for (int i = 0; i <= s.Length; i++)
            {
                int w = TextRenderer.MeasureText(s.Substring(0, i), f,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
                int next = i < s.Length ? TextRenderer.MeasureText(s.Substring(0, i + 1), f,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width : int.MaxValue;
                if (rel < (w + next) / 2) return i;
            }
            return s.Length;
        }

        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r, 6, T.Card);
            T.DrawRound(g, r, 6, Focused ? T.Accent : _hover ? T.Text3 : T.Line, Focused ? 1.6f : 1f);

            var f = T.F(12);
            int textW = Bounds.Width - PADX * 2 - (NumericOnly ? 20 : 0);
            g.SetClip(new Rectangle(Bounds.X + 2, Bounds.Y + 2, Bounds.Width - 4, Bounds.Height - 4));
            // 注意：必须用 DrawString——TextRenderer.DrawText 不吃 Graphics 的世界变换，
            // 在主窗口 UiScale 放大渲染下文字会画到错误位置（量宽也改用同一套 GDI+ 度量）
            var sf = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using (var b = new SolidBrush(T.Text))
                g.DrawString(ShowText, f, b, new RectangleF(Bounds.X + PADX, Bounds.Y, textW, Bounds.Height), sf);

            if (Focused && _caretVis)
            {
                int cx = Bounds.X + PADX + (int)(g.MeasureString(ShowText.Substring(0, _caret), f, int.MaxValue, sf).Width + 0.5f);
                using (var pen = new Pen(T.Accent, 1.4f))
                    g.DrawLine(pen, cx, Bounds.Y + 5, cx, Bounds.Bottom - 5);
            }
            g.ResetClip();

            if (NumericOnly)
            {
                using (var pen = new Pen(T.Line, 1f))
                    g.DrawLine(pen, Bounds.Right - 20, Bounds.Y + 3, Bounds.Right - 20, Bounds.Bottom - 3);
                using (var b = new SolidBrush(T.Text3))
                {
                    int cxm = Bounds.Right - 10;
                    var up = new PointF[] { new PointF(cxm - 4, UpZone.Y + UpZone.Height / 2 + 2), new PointF(cxm + 4, UpZone.Y + UpZone.Height / 2 + 2), new PointF(cxm, UpZone.Y + UpZone.Height / 2 - 3) };
                    var dn = new PointF[] { new PointF(cxm - 4, DownZone.Y + DownZone.Height / 2 - 2), new PointF(cxm + 4, DownZone.Y + DownZone.Height / 2 - 2), new PointF(cxm, DownZone.Y + DownZone.Height / 2 + 3) };
                    g.FillPolygon(b, up);
                    g.FillPolygon(b, dn);
                }
            }
        }
    }

    /// <summary>
    /// 自绘复选框（分层窗口里不能用真 CheckBox；替代原版「深色底 + 系统方块」的妥协样式）。
    /// 16px 方框 + 勾选线，标签在右；点击整行切换。
    /// </summary>
    class ElCheck : El
    {
        public string Text = "";
        public bool Checked;
        public event EventHandler Changed;
        bool _hover, _down;

        public ElCheck() { }
        public ElCheck(string text, bool val) { Text = text; Checked = val; }

        public override void OnMouseMove(Point p) { if (!_hover) { _hover = true; Redraw(); } }
        public override void OnMouseLeave() { _hover = false; _down = false; Redraw(); }
        public override void OnMouseDown(Point p) { _down = true; Redraw(); }
        public override void OnMouseUp(Point p)
        {
            bool was = _down; _down = false; Redraw();
            if (was && Bounds.Contains(p))
            {
                Checked = !Checked;
                if (Changed != null) Changed(this, EventArgs.Empty);
            }
        }
        public override Cursor CursorOf(Point p) { return Cursors.Hand; }

        public override void Paint(Graphics g)
        {
            int cy = Bounds.Y + Bounds.Height / 2;
            var box = new RectangleF(Bounds.X + 0.5f, cy - 8 + 0.5f, 15, 15);
            T.FillRound(g, box, 4, _hover ? T.Line2 : T.Card);
            T.DrawRound(g, box, 4, _hover ? T.Text3 : T.Line, 1f);
            if (Checked) // 只画青色对勾，不填充（减弱后的选中态）
                using (var pen = new Pen(T.Accent, 2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                {
                    g.DrawLine(pen, Bounds.X + 3.5f, cy + 0.5f, Bounds.X + 7, cy + 4);
                    g.DrawLine(pen, Bounds.X + 7, cy + 4, Bounds.X + 12.5f, cy - 4.5f);
                }
            using (var b = new SolidBrush(_down ? T.Text3 : T.Text))
                g.DrawString(Text, T.F(12), b, new RectangleF(Bounds.X + 24, Bounds.Y, Bounds.Width - 24, Bounds.Height),
                    new StringFormat { LineAlignment = StringAlignment.Center });
        }
    }
}

namespace Zogos
{
    /// <summary>
    /// 自绘多行输入框（分层窗口里不能用真 TextBox；创意工坊同步的 ID/链接粘贴区用）。
    /// 支持：点击定位光标、输入、退格/Delete、四方向/Home/End、Enter 换行、Ctrl+V 粘贴（由宿主转发）、滚轮滚动。
    /// 简化：不支持文本选择、不支持水平滚动（长行裁切显示）。
    /// </summary>
    class ElInputMulti : El
    {
        public string Text = "";
        public string Hint = ""; // 空文本时显示的灰字提示
        public event EventHandler TextChanged;

        public bool Focused { get; private set; }
        int _caret;             // 0..Text.Length（字符下标）
        int _scrollY;           // 垂直滚动（像素）
        bool _caretVis = true;
        readonly Timer _blink;
        bool _hover;

        const int PADX = 8, PADY = 6;
        int LineH { get { return 18; } }

        public ElInputMulti()
        {
            _blink = new Timer { Interval = 530 };
            _blink.Tick += delegate { _caretVis = !_caretVis; Redraw(); };
        }

        public void SetFocus(bool f)
        {
            if (Focused == f) return;
            Focused = f;
            _caretVis = true;
            if (f) { _caret = Text.Length; _blink.Start(); EnsureCaretVisible(); }
            else _blink.Stop();
            Redraw();
        }

        void Changed()
        {
            if (TextChanged != null) TextChanged(this, EventArgs.Empty);
            Redraw();
        }

        string[] LinesOf { get { return (Text ?? "").Replace("\r", "").Split('\n'); } }

        /// <summary>字符下标 → (行, 列)</summary>
        void CaretRC(out int row, out int col)
        {
            row = 0; col = 0;
            int pos = 0;
            string[] ls = LinesOf;
            for (int i = 0; i < ls.Length; i++)
            {
                int len = ls[i].Length;
                if (_caret <= pos + len) { row = i; col = _caret - pos; return; }
                pos += len + 1; // \n
            }
            row = ls.Length - 1; col = ls[ls.Length - 1].Length;
        }

        /// <summary>(行, 列) → 字符下标</summary>
        int CaretAtRC(int row, int col)
        {
            string[] ls = LinesOf;
            if (row < 0) row = 0;
            if (row >= ls.Length) row = ls.Length - 1;
            if (col > ls[row].Length) col = ls[row].Length;
            int pos = 0;
            for (int i = 0; i < row; i++) pos += ls[i].Length + 1;
            return pos + col;
        }

        // —— 键盘（由宿主转发）——
        public void OnChar(char c)
        {
            if (c == '\b')
            {
                if (_caret > 0) { Text = Text.Remove(_caret - 1, 1); _caret--; Changed(); }
                return;
            }
            if (c == '\r') c = '\n';
            if (c < ' ' && c != '\n') return;
            Text = Text.Insert(_caret, c.ToString());
            _caret++;
            EnsureCaretVisible();
            Changed();
        }

        public bool OnKey(Keys k)
        {
            int row, col;
            CaretRC(out row, out col);
            string[] ls = LinesOf;
            switch (k)
            {
                case Keys.Left:
                    if (_caret > 0) _caret--;
                    break;
                case Keys.Right:
                    if (_caret < Text.Length) _caret++;
                    break;
                case Keys.Up:
                    if (row > 0) _caret = CaretAtRC(row - 1, col);
                    break;
                case Keys.Down:
                    if (row < ls.Length - 1) _caret = CaretAtRC(row + 1, col);
                    break;
                case Keys.Home:
                    _caret = CaretAtRC(row, 0);
                    break;
                case Keys.End:
                    _caret = CaretAtRC(row, int.MaxValue);
                    break;
                case Keys.Delete:
                    if (_caret < Text.Length) { Text = Text.Remove(_caret, 1); Changed(); }
                    return true;
                default:
                    return false;
            }
            _caretVis = true;
            EnsureCaretVisible();
            Redraw();
            return true;
        }

        /// <summary>粘贴入口（换行保留）</summary>
        public void InsertText(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            s = s.Replace("\r\n", "\n").Replace("\r", "\n");
            Text = Text.Insert(_caret, s);
            _caret += s.Length;
            EnsureCaretVisible();
            Changed();
        }

        void EnsureCaretVisible()
        {
            int row, col;
            CaretRC(out row, out col);
            int y = row * LineH;
            int vis = Bounds.Height - PADY * 2;
            if (y - _scrollY < 0) _scrollY = y;
            else if (y - _scrollY > vis - LineH) _scrollY = y - vis + LineH;
            if (_scrollY < 0) _scrollY = 0;
        }

        public override void OnMouseMove(Point p) { if (!_hover) { _hover = true; Redraw(); } }
        public override void OnMouseLeave() { _hover = false; Redraw(); }
        public override void OnMouseDown(Point p)
        {
            var f = T.F(12);
            int row = (p.Y - Bounds.Y - PADY + _scrollY) / LineH;
            string[] ls = LinesOf;
            if (row < 0) row = 0;
            if (row >= ls.Length) row = ls.Length - 1;
            int rel = p.X - Bounds.X - PADX;
            int col = 0;
            string ln = ls[row];
            for (int i = 0; i <= ln.Length; i++)
            {
                int w = TextRenderer.MeasureText(ln.Substring(0, i), f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
                int next = i < ln.Length ? TextRenderer.MeasureText(ln.Substring(0, i + 1), f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width : int.MaxValue;
                if (rel < (w + next) / 2) { col = i; break; }
                col = i;
            }
            _caret = CaretAtRC(row, col);
            _caretVis = true;
            Redraw();
        }
        public override Cursor CursorOf(Point p) { return Cursors.IBeam; }

        public override bool OnMouseWheel(int delta)
        {
            int total = LinesOf.Length * LineH;
            int vis = Bounds.Height - PADY * 2;
            int max = Math.Max(0, total - vis);
            if (max == 0) return false;
            _scrollY = Math.Max(0, Math.Min(max, _scrollY - Math.Sign(delta) * LineH * 3));
            Redraw();
            return true;
        }

        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r, 6, T.Card);
            T.DrawRound(g, r, 6, Focused ? T.Accent : _hover ? T.Text3 : T.Line, Focused ? 1.6f : 1f);

            var f = T.F(12);
            string[] ls = LinesOf;
            bool empty = string.IsNullOrEmpty(Text);
            g.SetClip(new Rectangle(Bounds.X + 2, Bounds.Y + 2, Bounds.Width - 4, Bounds.Height - 4));
            using (var b = new SolidBrush(T.Text))
            using (var bh = new SolidBrush(T.Text3))
            {
                if (empty && !string.IsNullOrEmpty(Hint))
                    g.DrawString(Hint, f, bh, Bounds.X + PADX, Bounds.Y + PADY);
                else
                {
                    int y = Bounds.Y + PADY - _scrollY;
                    for (int i = 0; i < ls.Length; i++, y += LineH)
                    {
                        if (y + LineH < Bounds.Y) continue;
                        if (y > Bounds.Bottom) break;
                        g.DrawString(ls[i], f, b, Bounds.X + PADX, y);
                    }
                }
            }
            if (Focused && _caretVis)
            {
                int row, col;
                CaretRC(out row, out col);
                string[] l2 = LinesOf;
                int cx = Bounds.X + PADX + TextRenderer.MeasureText(l2[row].Substring(0, col), f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
                int cy = Bounds.Y + PADY + row * LineH - _scrollY;
                if (cy >= Bounds.Y && cy + LineH <= Bounds.Bottom + 2)
                    using (var pen = new Pen(T.Accent, 1.4f))
                        g.DrawLine(pen, cx, cy + 2, cx, cy + LineH - 3);
            }
            g.ResetClip();
        }
    }
}
