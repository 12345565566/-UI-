using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Zogos
{
    public enum PickMode { Folder = 0, OpenFile = 1, SaveFile = 2 }

    /// <summary>选择器内部共用配色与绘制工具。刻意不跟随「界面不透明度」：弹窗要始终清晰可读。</summary>
    static class Pk
    {
        public static bool Dk { get { return T.Dark; } }
        public static Color Bg { get { return Dk ? Color.FromArgb(30, 30, 33) : Color.FromArgb(252, 252, 250); } }
        public static Color Side { get { return Dk ? Color.FromArgb(23, 23, 27) : Color.FromArgb(246, 246, 243); } }
        public static Color List { get { return Dk ? Color.FromArgb(30, 30, 33) : Color.FromArgb(255, 255, 255); } }
        public static Color Head { get { return Dk ? Color.FromArgb(35, 35, 40) : Color.FromArgb(250, 250, 247); } }
        public static Color Well { get { return Dk ? Color.FromArgb(24, 24, 28) : Color.FromArgb(255, 255, 255); } }
        public static Color Hover { get { return Dk ? Color.FromArgb(44, 44, 51) : Color.FromArgb(241, 245, 246); } }
        public static Color Sel { get { return Dk ? Color.FromArgb(33, 64, 70) : Color.FromArgb(228, 241, 242); } }
        public static Color Line { get { return Dk ? Color.FromArgb(58, 58, 64) : Color.FromArgb(230, 229, 223); } }
        public static Color Text { get { return Dk ? Color.FromArgb(242, 242, 238) : Color.FromArgb(28, 27, 23); } }
        public static Color Text2 { get { return Dk ? Color.FromArgb(168, 168, 163) : Color.FromArgb(74, 73, 68); } }
        public static Color Text3 { get { return Dk ? Color.FromArgb(118, 118, 114) : Color.FromArgb(122, 121, 114); } }
        public static Color Accent { get { return Dk ? Color.FromArgb(58, 180, 192) : Color.FromArgb(14, 124, 134); } }
        public static Color AccentDim { get { return Dk ? Color.FromArgb(22, 52, 58) : Color.FromArgb(226, 240, 241); } }
        public static Color Card2 { get { return Dk ? Color.FromArgb(42, 42, 48) : Color.FromArgb(245, 245, 242); } }
        public static Color Danger { get { return Dk ? Color.FromArgb(226, 130, 120) : Color.FromArgb(176, 58, 42); } }

        public static string Ell(Graphics g, string s, Font f, float maxW)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (g.MeasureString(s, f).Width <= maxW) return s;
            string t = s;
            while (t.Length > 1 && g.MeasureString(t + "…", f).Width > maxW) t = t.Substring(0, t.Length - 1);
            return t + "…";
        }

        public static string Size(long b)
        {
            if (b < 1024) return b + " B";
            double k = b / 1024.0;
            if (k < 1024) return k < 10 ? k.ToString("0.0") + " KB" : ((int)k) + " KB";
            double m = k / 1024.0;
            if (m < 1024) return m < 10 ? m.ToString("0.0") + " MB" : ((int)m) + " MB";
            double gb = m / 1024.0;
            return gb < 10 ? gb.ToString("0.00") + " GB" : gb.ToString("0.0") + " GB";
        }
    }

    class PickEntry
    {
        public string Name, Full, Note;
        public bool IsDir;
        public long Size;
        public DateTime Modified;
        public int Kind;   // 0 文件 1 文件夹 2 磁盘
    }

    /// <summary>自绘的类型图标（18×18）。全部代码画，不依赖系统图标，也不碰 shell。</summary>
    static class PickIcons
    {
        static Bitmap _folder, _drive, _video, _image, _zip, _csv, _file, _up;
        static readonly Dictionary<string, Bitmap> _cache = new Dictionary<string, Bitmap>();

        public static Bitmap Get(PickEntry e)
        {
            if (e == null) return null;
            if (e.Kind == 1) return _folder != null ? _folder : (_folder = Folder());
            if (e.Kind == 2) return _drive != null ? _drive : (_drive = Drive());
            string ext = "";
            try { ext = Path.GetExtension(e.Name).ToLowerInvariant(); } catch { }
            if (ext == ".mp4" || ext == ".webm" || ext == ".mkv" || ext == ".avi" || ext == ".mov")
                return _video != null ? _video : (_video = Video());
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif")
                return _image != null ? _image : (_image = ImageIco());
            if (ext == ".zip" || ext == ".7z" || ext == ".rar")
                return _zip != null ? _zip : (_zip = Zip());
            if (ext == ".csv" || ext == ".txt" || ext == ".ini" || ext == ".log")
                return _csv != null ? _csv : (_csv = Csv());
            return _file != null ? _file : (_file = FileIco());
        }

        public static Bitmap Up()
        {
            return _up != null ? _up : (_up = UpIco());
        }

        static Bitmap New()
        {
            var b = new Bitmap(18, 18);
            return b;
        }

        static Pen P(float w, Color c) { return new Pen(c, w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }; }

        static Bitmap Folder()
        {
            var b = New();
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float w = Pk.Dk ? 1.4f : 1.3f;
                using (var body = new SolidBrush(Color.FromArgb(Pk.Dk ? 46 : 30, Pk.Accent)))
                using (var p = P(w, Pk.Accent))
                using (var path = new GraphicsPath())
                {
                    path.AddLines(new[] { new PointF(2.5f, 5.5f), new PointF(2.5f, 4.2f), new PointF(6.6f, 4.2f),
                        new PointF(7.9f, 5.7f), new PointF(15.5f, 5.7f), new PointF(15.5f, 14.2f),
                        new PointF(2.5f, 14.2f) });
                    path.CloseFigure();
                    g.FillPath(body, path);
                    g.DrawPath(p, path);
                }
            }
            return b;
        }

        static Bitmap Drive()
        {
            var b = New();
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = P(1.3f, Pk.Text2))
                using (var dot = new SolidBrush(Pk.Text2))
                {
                    g.DrawRectangle(p, 2.5f, 5.5f, 13f, 7f);
                    g.FillEllipse(dot, 11.4f, 9.3f, 2.4f, 2.4f);
                    g.DrawLine(p, 5.5f, 9f, 8f, 9f);
                }
            }
            return b;
        }

        static Bitmap Video()
        {
            var b = New();
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = P(1.3f, Pk.Text2))
                using (var fl = new SolidBrush(Pk.Text2))
                {
                    g.DrawRectangle(p, 2f, 4.5f, 14f, 9.5f);
                    g.FillPolygon(fl, new[] { new PointF(7.4f, 7f), new PointF(7.4f, 11.6f), new PointF(11.4f, 9.3f) });
                }
            }
            return b;
        }

        static Bitmap ImageIco()
        {
            var b = New();
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = P(1.3f, Pk.Text2))
                using (var fl = new SolidBrush(Pk.Text2))
                {
                    g.DrawRectangle(p, 2f, 4.2f, 14f, 10f);
                    g.FillEllipse(fl, 5f, 6.6f, 2.2f, 2.2f);
                    g.DrawLines(p, new[] { new PointF(3.4f, 13f), new PointF(7.6f, 9.2f), new PointF(10.4f, 11.6f), new PointF(12.6f, 9.6f), new PointF(15f, 12.2f) });
                }
            }
            return b;
        }

        static Bitmap Zip()
        {
            var b = New();
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = P(1.3f, Pk.Text2))
                {
                    g.DrawRectangle(p, 3.5f, 2.5f, 11f, 13f);
                    g.DrawLine(p, 9f, 2.8f, 9f, 4.2f);
                    g.DrawLine(p, 9f, 5.4f, 9f, 6.8f);
                    g.DrawLine(p, 9f, 8f, 9f, 9.4f);
                    g.DrawRectangle(p, 7.4f, 10.4f, 3.2f, 3.2f);
                }
            }
            return b;
        }

        static Bitmap Csv()
        {
            var b = New();
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = P(1.3f, Pk.Text2))
                {
                    g.DrawRectangle(p, 3.5f, 2.5f, 11f, 13f);
                    g.DrawLine(p, 6f, 7f, 12f, 7f);
                    g.DrawLine(p, 6f, 9.4f, 12f, 9.4f);
                    g.DrawLine(p, 6f, 11.8f, 10f, 11.8f);
                }
            }
            return b;
        }

        static Bitmap FileIco()
        {
            var b = New();
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = P(1.3f, Pk.Text2))
                {
                    g.DrawLines(p, new[] { new PointF(4.5f, 2.5f), new PointF(10f, 2.5f), new PointF(13.5f, 6f), new PointF(13.5f, 15.5f), new PointF(4.5f, 15.5f), new PointF(4.5f, 2.5f) });
                    g.DrawLines(p, new[] { new PointF(10f, 2.5f), new PointF(10f, 6f), new PointF(13.5f, 6f) });
                }
            }
            return b;
        }

        static Bitmap UpIco()
        {
            var b = New();
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = P(1.6f, Pk.Text2))
                    g.DrawLines(p, new[] { new PointF(9f, 14.5f), new PointF(9f, 4f) });
                using (var p = P(1.6f, Pk.Text2))
                    g.DrawLines(p, new[] { new PointF(4.2f, 8.6f), new PointF(9f, 3.8f), new PointF(13.8f, 8.6f) });
            }
            return b;
        }
    }

    /// <summary>自绘按钮：无系统边框、hover/press/accent 三态，替代 Button 的原生长相。</summary>
    class PickBtn : Control
    {
        public bool Primary, LeftAlign;
        bool _hover, _down;

        public PickBtn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = T.F(13f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color bg;
            if (Primary)
            {
                bg = Pk.Accent;
                if (!Enabled) bg = Pk.AccentDim;
                else if (_down) bg = ControlPaint.Dark(Pk.Accent, 0.08f);
                else if (_hover) bg = ControlPaint.Light(Pk.Accent, 0.10f);
            }
            else
            {
                bg = Pk.Card2;
                if (_hover && Enabled) bg = Pk.Hover;
                if (_down && Enabled) bg = Pk.Sel;
            }
            T.FillRound(g, r, 7f, bg);
            if (!Primary) T.DrawRound(g, r, 7f, Pk.Line, 1f);

            Color tc = Primary ? (Enabled ? Color.White : Pk.Text3) : (Enabled ? Pk.Text : Pk.Text3);
            var sf = new StringFormat
            {
                Alignment = LeftAlign ? StringAlignment.Near : StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };
            var rr = new RectangleF(LeftAlign ? 11f : 2f, 0f, Width - (LeftAlign ? 15f : 4f), Height);
            T.DrawStr(g, Text, Font, tc, rr, sf);
        }
    }

    /// <summary>占位提示文本框（WinForms 没有原生 placeholder）。</summary>
    class PhBox : TextBox
    {
        public string Placeholder;
        public PhBox()
        {
            BorderStyle = BorderStyle.None;
            Font = T.F(13f);
            BackColor = Pk.Well;
            ForeColor = Pk.Text;
        }
        protected override void OnGotFocus(EventArgs e) { Parent.Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Parent.Invalidate(); base.OnLostFocus(e); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F && !Focused && Text.Length == 0 && !string.IsNullOrEmpty(Placeholder))
            {
                using (var g = CreateGraphics())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    T.DrawStr(g, Placeholder, Font, Pk.Text3, 1, 1);
                }
            }
        }
    }

    /// <summary>列表：自绘表头 + 行 + 滚动条，支持排序、滚轮、键盘、输入跳转。</summary>
    class PickList : Control
    {
        public readonly List<PickEntry> Items = new List<PickEntry>();
        public event EventHandler SelChanged;
        public event EventHandler Activated;
        public event EventHandler SortChanged;

        const int HEAD_H = 30;
        const int ROW_H = 30;
        const int SB_W = 7;

        int _hover = -1, _sel = -1, _scroll;
        bool _loading;
        string _loadingText = "正在读取…";
        int _sortKey;
        bool _desc;
        bool _thumbDrag;
        int _thumbGrab;
        string _jump = "";
        DateTime _jumpAt;

        public PickList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Pk.List;
        }

        public string EmptyText = "这个文件夹里没有子文件夹";
        public PickEntry Selected { get { return (_sel >= 0 && _sel < Items.Count) ? Items[_sel] : null; } }
        public bool Loading { get { return _loading; } }

        int DateX { get { return Width - 12 - 148; } }
        int SizeX { get { return DateX - 96; } }

        public void SetLoading(bool on, string text)
        {
            _loading = on;
            if (text != null) _loadingText = text;
            Invalidate();
        }

        public void SetItems(List<PickEntry> items)
        {
            Items.Clear();
            if (items != null) Items.AddRange(items);
            Sort();
            _sel = -1;
            _hover = -1;
            _scroll = 0;
            Invalidate();
            if (SelChanged != null) SelChanged(this, EventArgs.Empty);
        }

        public void ClearSelection()
        {
            if (_sel == -1) return;
            _sel = -1;
            Invalidate();
            if (SelChanged != null) SelChanged(this, EventArgs.Empty);
        }

        public void SelectPath(string full)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (string.Equals(Items[i].Full, full, StringComparison.OrdinalIgnoreCase))
                {
                    Select(i);
                    EnsureVisible(i);
                    return;
                }
            }
        }

        public void Select(int i)
        {
            if (i < 0 || i >= Items.Count) return;
            if (_sel == i) return;
            _sel = i;
            Invalidate();
            if (SelChanged != null) SelChanged(this, EventArgs.Empty);
        }

        void Sort()
        {
            Items.Sort(delegate(PickEntry a, PickEntry b)
            {
                if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
                int c;
                switch (_sortKey)
                {
                    case 1: c = a.Size.CompareTo(b.Size); break;
                    case 2: c = a.Modified.CompareTo(b.Modified); break;
                    default: c = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); break;
                }
                if (c == 0) c = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                return _desc ? -c : c;
            });
            _sel = -1;
        }

        public void ToggleSort(int key)
        {
            if (_sortKey == key) _desc = !_desc;
            else { _sortKey = key; _desc = false; }
            Sort();
            _scroll = 0;
            Invalidate();
            if (SortChanged != null) SortChanged(this, EventArgs.Empty);
            if (SelChanged != null) SelChanged(this, EventArgs.Empty);
        }

        bool Scrollable { get { return Items.Count * ROW_H > Height - HEAD_H; } }

        int MaxScroll
        {
            get { return Math.Max(0, Items.Count * ROW_H - (Height - HEAD_H)); }
        }

        void ScrollTo(int v)
        {
            v = Math.Max(0, Math.Min(MaxScroll, v));
            if (v == _scroll) return;
            _scroll = v;
            Invalidate();
        }

        void EnsureVisible(int i)
        {
            if (i < 0) return;
            int top = i * ROW_H, bottom = top + ROW_H;
            if (top < _scroll) ScrollTo(top);
            else if (bottom > _scroll + (Height - HEAD_H)) ScrollTo(bottom - (Height - HEAD_H));
        }

        int RowAt(int y)
        {
            if (y < HEAD_H) return -1;
            int i = (y - HEAD_H + _scroll) / ROW_H;
            return (i >= 0 && i < Items.Count) ? i : -1;
        }

        Rectangle Thumb()
        {
            int trackY = HEAD_H + 4, trackH = Height - HEAD_H - 8;
            int total = Items.Count * ROW_H;
            int view = Height - HEAD_H;
            int th = Math.Max(28, (int)((double)trackH * view / Math.Max(1, total)));
            int max = MaxScroll;
            int ty = trackY + (max <= 0 ? 0 : (int)((double)(trackH - th) * _scroll / max));
            return new Rectangle(Width - SB_W - 3, ty, SB_W, th);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(Pk.List)) g.FillRectangle(b, ClientRectangle);

            int dateX = DateX, sizeX = SizeX;
            using (var b = new SolidBrush(Pk.Head)) g.FillRectangle(b, 0, 0, Width, HEAD_H);
            using (var p = new Pen(Pk.Line)) g.DrawLine(p, 0, HEAD_H - 0.5f, Width, HEAD_H - 0.5f);

            var sfR = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            var sfL = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
            T.DrawStr(g, "名称" + (_sortKey == 0 ? (_desc ? " ↓" : " ↑") : ""), T.F(12f), _sortKey == 0 ? Pk.Text2 : Pk.Text3,
                new RectangleF(18, 0, sizeX - 18, HEAD_H), sfL);
            T.DrawStr(g, "大小" + (_sortKey == 1 ? (_desc ? " ↓" : " ↑") : ""), T.F(12f), _sortKey == 1 ? Pk.Text2 : Pk.Text3,
                new RectangleF(sizeX, 0, 90, HEAD_H), sfR);
            T.DrawStr(g, "修改时间" + (_sortKey == 2 ? (_desc ? " ↓" : " ↑") : ""), T.F(12f), _sortKey == 2 ? Pk.Text2 : Pk.Text3,
                new RectangleF(dateX, 0, 140, HEAD_H), sfL);

            var st = g.Save();
            g.SetClip(new Rectangle(0, HEAD_H, Width, Math.Max(0, Height - HEAD_H)));
            var fName = T.F(13f);
            var fMeta = T.F(12f);
            int n = Items.Count;
            for (int i = 0; i < n; i++)
            {
                int y = HEAD_H + i * ROW_H - _scroll;
                if (y > Height) break;
                if (y + ROW_H < HEAD_H) continue;
                var it = Items[i];
                bool sel = (i == _sel), hov = (i == _hover);
                int right = Scrollable ? Width - SB_W - 6 : Width - 8;
                if (sel)
                {
                    T.FillRound(g, new RectangleF(8, y + 2, right - 8, ROW_H - 4), 5f, Pk.Sel);
                    using (var b = new SolidBrush(Pk.Accent)) g.FillRectangle(b, 8, y + 8, 2.5f, ROW_H - 16);
                }
                else if (hov)
                {
                    T.FillRound(g, new RectangleF(8, y + 2, right - 8, ROW_H - 4), 5f, Pk.Hover);
                }
                var ico = PickIcons.Get(it);
                if (ico != null) g.DrawImage(ico, 21, y + (ROW_H - 18) / 2, 18, 18);
                int ty = y + (ROW_H - fName.Height) / 2;
                T.DrawStr(g, Pk.Ell(g, it.Name, fName, sizeX - 52), fName, Pk.Text, 46, ty);
                if (it.IsDir)
                {
                    T.DrawStr(g, it.Note != null ? it.Note : it.Modified.ToString("yyyy-MM-dd  HH:mm"), fMeta, Pk.Text3,
                        new RectangleF(dateX, y, 140, ROW_H), sfL);
                }
                else
                {
                    T.DrawStr(g, Pk.Size(it.Size), fMeta, Pk.Text3, new RectangleF(sizeX, y, 90, ROW_H), sfR);
                    T.DrawStr(g, it.Modified.ToString("yyyy-MM-dd  HH:mm"), fMeta, Pk.Text3,
                        new RectangleF(dateX, y, 140, ROW_H), sfL);
                }
            }
            g.Restore(st);

            if (Items.Count == 0 && !_loading)
                T.DrawStr(g, EmptyText, T.F(13f), Pk.Text3, new RectangleF(18, HEAD_H + 24, Width - 36, 22), sfL);

            if (Scrollable)
            {
                using (var b = new SolidBrush(Pk.Dk ? Color.FromArgb(60, 60, 66) : Color.FromArgb(214, 213, 207)))
                    g.FillRectangle(b, Thumb());
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_thumbDrag)
            {
                int trackY = HEAD_H + 4, trackH = Height - HEAD_H - 8;
                int th = Thumb().Height;
                int max = MaxScroll;
                int want = e.Y - _thumbGrab - trackY;
                if (max > 0 && trackH - th > 0) ScrollTo((int)((double)want * max / (trackH - th)));
                return;
            }
            int i = RowAt(e.Y);
            if (i != _hover) { _hover = i; Invalidate(); }
            if (Scrollable && Thumb().Contains(e.Location)) Cursor = Cursors.Hand;
            else Cursor = Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            if (e.Y < HEAD_H)
            {
                if (e.X < SizeX) ToggleSort(0);
                else if (e.X < DateX) ToggleSort(1);
                else ToggleSort(2);
                return;
            }
            if (Scrollable && Thumb().Contains(e.Location))
            {
                _thumbDrag = true;
                _thumbGrab = e.Y - Thumb().Y;
                Capture = true;
                return;
            }
            int i = RowAt(e.Y);
            if (i >= 0) Select(i);
            else ClearSelection();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _thumbDrag = false;
            Capture = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int i = RowAt(e.Y);
            if (i >= 0 && e.Y >= HEAD_H)
            {
                Select(i);
                if (Activated != null) Activated(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollTo(_scroll - Math.Sign(e.Delta) * ROW_H * 3);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right || k == Keys.Enter || k == Keys.Back) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Down:
                    Select(Math.Min(Items.Count - 1, _sel + 1)); EnsureVisible(_sel); e.Handled = true; break;
                case Keys.Up:
                    Select(_sel <= 0 ? 0 : _sel - 1); EnsureVisible(_sel); e.Handled = true; break;
                case Keys.PageDown:
                    Select(Math.Min(Items.Count - 1, _sel + (Height - HEAD_H) / ROW_H)); EnsureVisible(_sel); e.Handled = true; break;
                case Keys.PageUp:
                    Select(Math.Max(0, _sel - (Height - HEAD_H) / ROW_H)); EnsureVisible(_sel); e.Handled = true; break;
                case Keys.Home:
                    Select(0); EnsureVisible(0); e.Handled = true; break;
                case Keys.End:
                    Select(Items.Count - 1); EnsureVisible(_sel); e.Handled = true; break;
                case Keys.Enter:
                    if (Activated != null) Activated(this, EventArgs.Empty);
                    e.Handled = true; break;
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (char.IsControl(e.KeyChar)) return;
            if ((DateTime.Now - _jumpAt).TotalMilliseconds > 900) _jump = "";
            _jumpAt = DateTime.Now;
            _jump += char.ToLowerInvariant(e.KeyChar);
            for (int i = 0; i < Items.Count; i++)
            {
                string nm = Items[i].Name.ToLowerInvariant();
                if (nm.StartsWith(_jump))
                {
                    Select(i);
                    EnsureVisible(i);
                    break;
                }
            }
            e.Handled = true;
        }
    }

    /// <summary>面包屑路径条：每段可点，过宽时中间用 … 折叠。</summary>
    class Crumb : Control
    {
        public Action<string> OnGo;
        public event EventHandler EditRequested;

        List<KeyValuePair<string, string>> _segs = new List<KeyValuePair<string, string>>();
        List<RectangleF> _rects = new List<RectangleF>();
        int _hover = -1;
        RectangleF _editRt;
        bool _dirty = true;

        public Crumb()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Pk.Bg;
        }

        public void SetPath(string path)
        {
            _segs.Clear();
            if (string.IsNullOrEmpty(path))
            {
                _segs.Add(new KeyValuePair<string, string>("此电脑", ""));
            }
            else
            {
                _segs.Add(new KeyValuePair<string, string>("此电脑", ""));
                try
                {
                    string full = Path.GetFullPath(path);
                    string root = Path.GetPathRoot(full);
                    if (!string.IsNullOrEmpty(root))
                    {
                        _segs.Add(new KeyValuePair<string, string>(root.TrimEnd('\\'), root));
                        string rest = full.Substring(root.Length).TrimEnd('\\');
                        string acc = root;
                        if (rest.Length > 0)
                        {
                            foreach (var part in rest.Split('\\'))
                            {
                                if (part.Length == 0) continue;
                                acc = Path.Combine(acc, part);
                                _segs.Add(new KeyValuePair<string, string>(part, acc));
                            }
                        }
                    }
                }
                catch { }
            }
            _dirty = true;
            Invalidate();
        }

        void Relayout()
        {
            _rects.Clear();
            var g = CreateGraphics();
            try
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                var f = T.F(13f);
                float x = 0;
                var widths = new List<float>();
                foreach (var s in _segs) widths.Add(g.MeasureString(s.Key, f).Width + 14);
                float sepW = g.MeasureString("›", f).Width + 6;
                float total = 0;
                for (int i = 0; i < widths.Count; i++) total += widths[i] + (i > 0 ? sepW : 0);
                int first = 0, last = _segs.Count - 1;
                // 过宽：从中间折叠，保留首尾
                while (total > Width - 40 && last - first > 2)
                {
                    total -= widths[first + 1] + sepW;
                    first++;
                }
                x = 0;
                for (int i = first; i <= last; i++)
                {
                    if (i > first)
                    {
                        if (first > 0 && i == first + 1) x += sepW;
                        else x += sepW;
                    }
                    float w = widths[i];
                    _rects.Add(new RectangleF(x, 0, w, Height));
                    x += w;
                }
            }
            finally { g.Dispose(); }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); _dirty = true; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(Pk.Bg)) g.FillRectangle(b, ClientRectangle);
            if (_dirty) { Relayout(); _dirty = false; }
            var f = T.F(13f);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            // 从右往左对齐，保证末级总在右侧
            for (int i = 0; i < _segs.Count && i < _rects.Count; i++)
            {
                var r = _rects[i];
                bool last = (i == _segs.Count - 1);
                bool hov = (i == _hover) && !last;
                if (hov) T.FillRound(g, new RectangleF(r.X + 1, 3, r.Width - 2, Height - 6), 5f, Pk.Hover);
                T.DrawStr(g, Pk.Ell(g, _segs[i].Key, f, r.Width - 6), f, last ? Pk.Text : Pk.Text2,
                    new RectangleF(r.X, 0, r.Width, Height), sf);
                if (i < _segs.Count - 1)
                {
                    var sep = T.F(13f);
                    T.DrawStr(g, "›", sep, Pk.Text3, r.Right - 4, (Height - sep.Height) / 2);
                }
            }
            // 右侧「编辑路径」小按钮
            _editRt = new RectangleF(Width - 24, (Height - 20) / 2, 20, 20);
            bool eh = _editRt.Contains(PointToClient(Cursor.Position));
            if (eh) T.FillRound(g, new RectangleF(_editRt.X, _editRt.Y, _editRt.Width, _editRt.Height), 4f, Pk.Hover);
            using (var p = new Pen(eh ? Pk.Accent : Pk.Text3, 1.4f))
            {
                g.DrawLine(p, _editRt.X + 5, _editRt.Y + 14, _editRt.X + 14, _editRt.Y + 5);
                g.DrawLine(p, _editRt.X + 4.5f, _editRt.Y + 15.5f, _editRt.X + 8, _editRt.Y + 15);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            if (_editRt.Contains(e.Location)) h = -2;
            else
                for (int i = 0; i < _rects.Count; i++) if (_rects[i].Contains(e.Location)) { h = i; break; }
            if (h != _hover) { _hover = h; Invalidate(); }
            Cursor = (h != -1 && h != _segs.Count - 1) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_editRt.Contains(e.Location)) { if (EditRequested != null) EditRequested(this, EventArgs.Empty); return; }
            for (int i = 0; i < _rects.Count && i < _segs.Count; i++)
            {
                if (_rects[i].Contains(e.Location))
                {
                    if (OnGo != null) OnGo(_segs[i].Value);
                    return;
                }
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
    }

    /// <summary>左侧常用位置 + 磁盘列表。</summary>
    class SideBar : Control
    {
        public Action<string> OnGo;

        class Item
        {
            public string Label, Path, Note;
            public int Kind;      // 0 文件夹 1 磁盘 2 标题
        }

        readonly List<Item> _items = new List<Item>();
        int _hover = -1;
        string _cur = "";

        public SideBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Pk.Side;
        }

        public void Build()
        {
            _items.Clear();
            _items.Add(new Item { Label = "常用位置", Kind = 2 });
            Add("桌面", SafeFolder(Environment.SpecialFolder.Desktop));
            Add("文档", SafeFolder(Environment.SpecialFolder.MyDocuments));
            string home = "";
            try { home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
            if (!string.IsNullOrEmpty(home))
            {
                Add("下载", Path.Combine(home, "Downloads"));
                Add("图片", Path.Combine(home, "Pictures"));
            }
            _items.Add(new Item { Label = "此电脑", Kind = 2, Path = "" });
            foreach (var d in SafeDrives())
            {
                _items.Add(new Item { Label = d.Name, Path = d.Name + "\\", Note = d.Note, Kind = 1 });
            }
            Invalidate();
        }

        static string SafeFolder(Environment.SpecialFolder s)
        {
            try { return Environment.GetFolderPath(s); } catch { return ""; }
        }

        public void SetCurrent(string path) { _cur = path ?? ""; Invalidate(); }

        void Add(string label, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _items.Add(new Item { Label = label, Path = path, Kind = 0 });
        }

        struct Dr { public string Name, Note; }

        static List<Dr> SafeDrives()
        {
            var l = new List<Dr>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        string note = null;
                        try
                        {
                            long free = d.AvailableFreeSpace, total = d.TotalSize;
                            note = (string.IsNullOrEmpty(d.VolumeLabel) ? "本地磁盘" : d.VolumeLabel) + "  ·  可用 " + Pk.Size(free);
                            if (total > 0) note += " / " + Pk.Size(total);
                        }
                        catch { }
                        l.Add(new Dr { Name = d.Name.TrimEnd('\\'), Note = note });
                    }
                    catch { }
                }
            }
            catch { }
            return l;
        }

        int RowH = 32;

        Rectangle RectOf(int idx)
        {
            int y = 12;
            for (int i = 0; i < idx && i < _items.Count; i++)
                y += _items[i].Kind == 2 ? 26 : RowH;
            int h = (idx < _items.Count && _items[idx].Kind == 2) ? 26 : RowH;
            return new Rectangle(8, y, Width - 16, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(Pk.Side)) g.FillRectangle(b, ClientRectangle);
            using (var p = new Pen(Pk.Line)) g.DrawLine(p, Width - 0.5f, 0, Width - 0.5f, Height);

            var fCap = T.F(11.5f);
            var fItem = T.F(13f);
            var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                var r = RectOf(i);
                if (it.Kind == 2)
                {
                    bool isPc = string.IsNullOrEmpty(it.Path) && it.Label == "此电脑";
                    bool act = isPc && string.IsNullOrEmpty(_cur);
                    T.DrawStr(g, it.Label, fCap, act ? Pk.Accent : Pk.Text3, new RectangleF(r.X + 6, r.Y, r.Width - 12, r.Height), sf);
                    continue;
                }
                bool on = PathOn(it.Path);
                if (_hover == i || on)
                {
                    T.FillRound(g, new RectangleF(r.X, r.Y + 1, r.Width, r.Height - 2), 6f, on ? Pk.AccentDim : Pk.Hover);
                }
                var ico = it.Kind == 1 ? PickIcons.Get(new PickEntry { Kind = 2 }) : PickIcons.Get(new PickEntry { Kind = 1 });
                if (ico != null) g.DrawImage(ico, r.X + 9, r.Y + (r.Height - 18) / 2, 18, 18);
                Color tc = on ? Pk.Accent : Pk.Text2;
                T.DrawStr(g, it.Label, fItem, tc, new RectangleF(r.X + 34, r.Y, r.Width - 42, r.Height), sf);
            }
        }

        bool PathOn(string p)
        {
            if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(_cur)) return false;
            string a = p.TrimEnd('\\'), b = _cur.TrimEnd('\\');
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
                || b.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            int y = 12;
            for (int i = 0; i < _items.Count; i++)
            {
                int hh = _items[i].Kind == 2 ? 26 : RowH;
                if (e.Y >= y && e.Y < y + hh) { h = _items[i].Kind == 2 ? -1 : i; break; }
                y += hh;
            }
            if (h != _hover) { _hover = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int y = 12;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                int hh = it.Kind == 2 ? 26 : RowH;
                if (e.Y >= y && e.Y < y + hh)
                {
                    if (it.Kind != 2 && OnGo != null) OnGo(it.Path);
                    return;
                }
                y += hh;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
    }

    /// <summary>
    /// 自绘「文件 / 文件夹」选择器。这台机器上系统文件对话框（IFileDialog / GetOpenFileName /
    /// FolderBrowserDialog）在创建阶段就会卡死（2026-11-16 用独立最小 exe 复现），所以完全不用它们。
    /// 三种模式：选文件夹 / 打开文件 / 保存文件；列表为自绘表头 + 行，可排序、可键盘操作。
    /// </summary>
    public class FolderPickForm : Form
    {
        public string Result;

        const int TITLE_H = 44, SIDE_W = 196, TOOL_H = 48, FOOT_H = 58, NAME_H = 38;
        const int MIN_W = 820, MIN_H = 500;

        readonly string _title;
        readonly PickMode _mode;
        readonly List<string> _exts = new List<string>();
        string _filterLabel = "";
        readonly string _suggest;

        readonly SideBar _side = new SideBar();
        readonly PickList _list = new PickList();
        readonly Crumb _crumb = new Crumb();
        readonly PickBtn _up = new PickBtn(), _ok = new PickBtn(), _cancel = new PickBtn();
        readonly PhBox _search = new PhBox(), _addr = new PhBox(), _name = new PhBox();
        readonly Label _hint = new Label();
        readonly System.Windows.Forms.Timer _anim = new System.Windows.Forms.Timer();

        readonly Dictionary<TextBox, Rectangle> _wells = new Dictionary<TextBox, Rectangle>();

        string _cur = "";
        int _loadGen;
        int _animStep;
        bool _addrMode;
        Rectangle _closeRt;
        List<PickEntry> _all = new List<PickEntry>();

        public FolderPickForm(string title, string start)
            : this(title, start, PickMode.Folder, null, null) { }

        public FolderPickForm(string title, string start, PickMode mode, string filter, string suggest)
        {
            _title = title ?? "选择";
            _mode = mode;
            _suggest = suggest;
            ParseFilter(filter);

            Text = _title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            ClientSize = new Size(1120, 700);
            MinimumSize = new Size(MIN_W, MIN_H);
            BackColor = Pk.Bg;
            Font = T.F(13f);
            KeyPreview = true;

            _side.Build();
            _side.OnGo = delegate(string p) { Navigate(p); };

            _crumb.OnGo = delegate(string p) { Navigate(p); };
            _crumb.EditRequested += delegate { ToggleAddr(true); };

            _list.SetLoading(false, null);
            _list.EmptyText = _mode == PickMode.Folder ? "这个文件夹里没有子文件夹"
                : (_exts.Count > 0 ? "这个文件夹里没有符合条件的文件" : "这个文件夹里没有文件");
            _list.SelChanged += delegate { UpdateHint(); UpdateOk(); };
            _list.Activated += delegate { ActivateSelected(); };
            _list.SortChanged += delegate { UpdateHint(); };

            _up.Text = "↑";
            _up.Font = T.F(15f);
            _up.Click += delegate { GoUp(); };

            _ok.Primary = true;
            _ok.Text = _mode == PickMode.Folder ? "选这个文件夹" : (_mode == PickMode.SaveFile ? "保存到此处" : "选择文件");
            _ok.Click += delegate { DoOk(); };
            _ok.Enabled = false;

            _cancel.Text = "取消";
            _cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            SetupBox(_search, "搜索当前文件夹…");
            _search.TextChanged += delegate { ApplyFilter(); };
            _search.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { DeepSearch(); e.Handled = true; e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Escape) { _search.Text = ""; _list.Focus(); e.Handled = true; e.SuppressKeyPress = true; }
            };

            SetupBox(_addr, "输入路径后回车，例如 D:\\A");
            _addr.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    ToggleAddr(false);
                    Navigate(_addr.Text.Trim());
                    e.Handled = true; e.SuppressKeyPress = true;
                }
                if (e.KeyCode == Keys.Escape) { ToggleAddr(false); e.Handled = true; e.SuppressKeyPress = true; }
            };

            SetupBox(_name, _mode == PickMode.SaveFile ? "文件名" : "也可以在这里直接输入文件名");
            _name.TextChanged += delegate { UpdateOk(); };
            _name.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { DoOk(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            if (_mode == PickMode.SaveFile && !string.IsNullOrEmpty(_suggest)) _name.Text = _suggest;

            _hint.AutoEllipsis = true;
            _hint.BackColor = Color.Transparent;
            _hint.ForeColor = Pk.Text3;
            _hint.Font = T.F(12f);
            _hint.TextAlign = ContentAlignment.MiddleLeft;

            Controls.AddRange(new Control[] { _side, _list, _crumb, _addr, _up, _search, _name, _ok, _cancel, _hint });
            _addr.Visible = false;
            _name.Visible = _mode != PickMode.Folder;

            LayoutAll();
            Load += delegate(object s, EventArgs e) { OnLoaded(start); };
            Resize += delegate { LayoutAll(); };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    if (_addrMode) { ToggleAddr(false); return; }
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
                else if (e.Control && e.KeyCode == Keys.L) { ToggleAddr(true); e.Handled = true; }
                else if (e.KeyCode == Keys.Back && !(_search.Focused || _name.Focused || _addr.Focused)) { GoUp(); e.Handled = true; }
                else if (e.KeyCode == Keys.F5) { Navigate(_cur); e.Handled = true; }
            };
            MouseDown += delegate(object s, MouseEventArgs e) { DragTitle(e); };
            MouseMove += delegate(object s, MouseEventArgs e)
            {
                Cursor = _closeRt.Contains(e.Location) ? Cursors.Hand
                    : (e.Y < TITLE_H ? Cursors.SizeAll : Cursors.Default);
            };
            MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (_closeRt.Contains(e.Location)) { DialogResult = DialogResult.Cancel; Close(); }
            };
        }

        // ────────────────────────── 基础装配 ──────────────────────────

        void ParseFilter(string filter)
        {
            if (string.IsNullOrEmpty(filter)) return;
            string[] parts = filter.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                // 形如 "视频文件|*.mp4;*.webm"：偶数位是名字，奇数位是通配符
                string p = parts[i].Trim();
                if (p.StartsWith("*."))
                {
                    foreach (var one in p.Split(';'))
                    {
                        string e = one.Trim();
                        if (e.StartsWith("*.") && e.Length > 2 && e != "*.*")
                        {
                            string ext = e.Substring(1).ToLowerInvariant();
                            if (!_exts.Contains(ext)) _exts.Add(ext);
                        }
                    }
                }
            }
            string label = parts.Length > 0 ? parts[0].Trim() : "";
            int paren = label.IndexOf("(*");
            if (paren > 0) label = label.Substring(0, paren).Trim();
            if (label.Length > 0 && label.IndexOf('.') < 0) _filterLabel = label;
        }

        void SetupBox(PhBox b, string ph)
        {
            b.Placeholder = ph;
            b.Font = T.F(13f);
            b.BackColor = Pk.Well;
            b.ForeColor = Pk.Text;
            b.Placeholder = ph;
        }

        void OnLoaded(string start)
        {
            CenterOnParent();
            string p = (start ?? "").Trim().Trim('"');
            try
            {
                if (p.Length > 0 && File.Exists(p)) p = Path.GetDirectoryName(p);
            }
            catch { }
            Navigate(p);
            if (_mode == PickMode.SaveFile && !string.IsNullOrEmpty(_suggest))
            {
                try
                {
                    _name.Select(0, Path.GetFileNameWithoutExtension(_suggest).Length);
                    _name.Focus();
                }
                catch { }
            }
            else _list.Focus();
            StartFade();
        }

        void CenterOnParent()
        {
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (Owner != null) work = Screen.FromControl(Owner).WorkingArea;
            int w = Math.Min(Width, work.Width - 24);
            int h = Math.Min(Height, work.Height - 24);
            int x, y;
            if (Owner != null)
            {
                x = Owner.Left + (Owner.Width - w) / 2;
                y = Owner.Top + (Owner.Height - h) / 2;
            }
            else
            {
                x = work.Left + (work.Width - w) / 2;
                y = work.Top + (work.Height - h) / 2;
            }
            x = Math.Max(work.Left + 8, Math.Min(x, work.Right - w - 8));
            y = Math.Max(work.Top + 8, Math.Min(y, work.Bottom - h - 8));
            Bounds = new Rectangle(x, y, w, h);
        }

        void StartFade()
        {
            Opacity = 0;
            _animStep = 0;
            _anim.Interval = 12;
            _anim.Tick += delegate
            {
                _animStep++;
                double k = Math.Min(1.0, _animStep / 8.0);
                Opacity = k;
                if (k >= 1) { _anim.Stop(); Opacity = 1; }
            };
            _anim.Start();
        }

        void LayoutAll()
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            int contentX = SIDE_W + 16;
            int cy = TITLE_H + 9;
            _side.SetBounds(0, TITLE_H, SIDE_W, h - TITLE_H - FOOT_H);

            int searchW = 214;
            int searchX = w - 16 - searchW;
            int upW = 32;
            _up.SetBounds(contentX, cy + 1, upW, 28);
            _crumb.SetBounds(contentX + upW + 10, cy + 1, Math.Max(120, searchX - contentX - upW - 26), 28);
            SetWell(_addr, new Rectangle(contentX + upW + 10, cy - 1, Math.Max(120, searchX - contentX - upW - 26), 32));
            SetWell(_search, new Rectangle(searchX, cy - 1, searchW, 32));

            int nameRowY = h - FOOT_H - NAME_H;
            int nameW = Math.Min(440, Math.Max(180, w - contentX - 58 - 16 - 150));
            if (_mode != PickMode.Folder) SetWell(_name, new Rectangle(contentX + 58, nameRowY + 4, nameW, 30));

            int listY = TITLE_H + TOOL_H;
            _list.SetBounds(SIDE_W, listY, w - SIDE_W, Math.Max(60, nameRowY - listY));

            _hint.SetBounds(contentX, h - FOOT_H + 16, Math.Max(120, w - contentX - 270), 22);
            _cancel.SetBounds(w - 16 - 92, h - FOOT_H + 11, 92, 36);
            _ok.SetBounds(w - 16 - 92 - 10 - 128, h - FOOT_H + 11, 128, 36);
            _closeRt = new Rectangle(w - 46, 10, 30, 30);

            _addr.Visible = _addrMode;
            _crumb.Visible = !_addrMode;
            Invalidate();
        }

        void SetWell(TextBox tb, Rectangle well)
        {
            tb.Bounds = new Rectangle(well.X + 10, well.Y + (well.Height - tb.PreferredHeight) / 2, well.Width - 20, tb.PreferredHeight);
            _wells[tb] = well;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(Pk.Bg)) g.FillRectangle(b, ClientRectangle);

            // 标题栏
            T.DrawStr(g, Pk.Ell(g, _title, T.B(15f), ClientSize.Width - 300), T.B(15f), Pk.Text, 20, 14);
            T.DrawStr(g, "双击进入  ·  Backspace 上一级  ·  Ctrl+L 输入路径", T.F(11.5f), Pk.Text3,
                new RectangleF(0, 0, ClientSize.Width - 64, TITLE_H),
                new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });
            // 关闭按钮
            bool ch = _closeRt.Contains(PointToClient(Cursor.Position));
            if (ch) T.FillRound(g, new RectangleF(_closeRt.X, _closeRt.Y, _closeRt.Width, _closeRt.Height), 6f, Pk.Hover);
            using (var p = new Pen(ch ? Pk.Text : Pk.Text2, 1.5f))
            {
                float cx = _closeRt.X + _closeRt.Width / 2f, cyy = _closeRt.Y + _closeRt.Height / 2f, d = 5f;
                g.DrawLine(p, cx - d, cyy - d, cx + d, cyy + d);
                g.DrawLine(p, cx + d, cyy - d, cx - d, cyy + d);
            }

            // 分隔线
            using (var p = new Pen(Pk.Line))
            {
                g.DrawLine(p, 0, TITLE_H - 0.5f, ClientSize.Width, TITLE_H - 0.5f);
                g.DrawLine(p, SIDE_W - 0.5f, TITLE_H, SIDE_W - 0.5f, ClientSize.Height - FOOT_H);
                g.DrawLine(p, 0, ClientSize.Height - FOOT_H - 0.5f, ClientSize.Width, ClientSize.Height - FOOT_H - 0.5f);
            }

            // 输入框底槽
            foreach (var kv in _wells)
            {
                if (!kv.Key.Visible) continue;
                var r = new RectangleF(kv.Value.X + 0.5f, kv.Value.Y + 0.5f, kv.Value.Width - 1, kv.Value.Height - 1);
                T.FillRound(g, r, 7f, Pk.Well);
                bool foc = kv.Key.Focused;
                T.DrawRound(g, r, 7f, foc ? Pk.Accent : Pk.Line, foc ? 1.6f : 1f);
            }

            // 文件名行
            if (_mode != PickMode.Folder)
            {
                int ny = ClientSize.Height - FOOT_H - NAME_H;
                T.DrawStr(g, "文件名", T.F(12.5f), Pk.Text2, 20 + SIDE_W - 4, ny + 12);
                if (_exts.Count > 0)
                {
                    Rectangle nw;
                    string tip = "只看 " + (_filterLabel.Length > 0 ? _filterLabel + " · " : "") + string.Join("  ", _exts.ToArray());
                    if (_wells.TryGetValue(_name, out nw))
                        T.DrawStr(g, Pk.Ell(g, tip, T.F(11.5f), ClientSize.Width - nw.Right - 24), T.F(11.5f), Pk.Text3,
                            new RectangleF(nw.Right + 14, ny, Math.Max(40, ClientSize.Width - nw.Right - 24), NAME_H),
                            new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
                }
            }

            // 外框
            using (var p = new Pen(Pk.Line)) g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW：无边框窗口也有柔和投影，不贴着桌面
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0084 && WindowState == FormWindowState.Normal)
            {
                Point p = PointToClient(Cursor.Position);
                int b = 5;
                bool l = p.X <= b, r = p.X >= ClientSize.Width - b, t = p.Y <= b, bt = p.Y >= ClientSize.Height - b;
                if (t || bt || l || r)
                {
                    if (l && t) m.Result = (IntPtr)13;
                    else if (r && t) m.Result = (IntPtr)14;
                    else if (l && bt) m.Result = (IntPtr)16;
                    else if (r && bt) m.Result = (IntPtr)17;
                    else if (l) m.Result = (IntPtr)10;
                    else if (r) m.Result = (IntPtr)11;
                    else if (t) m.Result = (IntPtr)12;
                    else m.Result = (IntPtr)15;
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width < MIN_W || Height < MIN_H)
            {
                Size = new Size(Math.Max(MIN_W, Width), Math.Max(MIN_H, Height));
                return;
            }
            LayoutAll();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        void DragTitle(MouseEventArgs e)
        {
            if (e.Y >= TITLE_H || _closeRt.Contains(e.Location)) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
        }

        void ToggleAddr(bool on)
        {
            _addrMode = on;
            _addr.Visible = on;
            _crumb.Visible = !on;
            if (on)
            {
                _addr.Text = string.IsNullOrEmpty(_cur) ? "" : _cur;
                _addr.Focus();
                _addr.SelectAll();
            }
            else if (_addr.Visible)
            {
                _addr.Visible = false;
            }
            Invalidate();
        }

        // ────────────────────────── 导航 ──────────────────────────

        void Navigate(string path)
        {
            path = (path ?? "").Trim().Trim('"');
            if (path.Length > 0)
            {
                try
                {
                    if (!Directory.Exists(path))
                    {
                        string parent = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) path = parent;
                        else { _hint.ForeColor = Pk.Danger; _hint.Text = "找不到这个文件夹：" + path; return; }
                    }
                    path = Path.GetFullPath(path);
                }
                catch (Exception ex) { _hint.ForeColor = Pk.Danger; _hint.Text = "路径无效：" + ex.Message; return; }
            }
            _cur = path;
            _crumb.SetPath(_cur);
            _side.SetCurrent(_cur);
            if (_addrMode) ToggleAddr(false);
            if (_search.Text.Length > 0) { _search.Text = ""; }
            LoadDir();
        }

        void LoadDir()
        {
            int gen = ++_loadGen;
            _list.SetLoading(true, "正在读取…");
            _all = new List<PickEntry>();
            _list.SetItems(new List<PickEntry>());
            string path = _cur;
            string[] exts = _exts.ToArray();
            bool wantFiles = _mode != PickMode.Folder;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                var items = new List<PickEntry>();
                try { items = ReadDir(path, exts, wantFiles, out err); }
                catch (Exception ex2) { err = ex2.Message; }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (gen != _loadGen) return;
                        _all = items;
                        _list.SetLoading(false, null);
                        ApplyFilter();
                        if (err != null) { _hint.ForeColor = Pk.Danger; _hint.Text = "读不到这个位置：" + err; }
                        UpdateOk();
                    });
                }
                catch { }
            });
        }

        static List<PickEntry> ReadDir(string path, string[] exts, bool wantFiles, out string err)
        {
            err = null;
            var list = new List<PickEntry>();
            if (string.IsNullOrEmpty(path))
            {
                try
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (!d.IsReady) continue;
                            string note = null;
                            try
                            {
                                note = (string.IsNullOrEmpty(d.VolumeLabel) ? "本地磁盘" : d.VolumeLabel)
                                    + "  ·  可用 " + Pk.Size(d.AvailableFreeSpace) + " / " + Pk.Size(d.TotalSize);
                            }
                            catch { }
                            list.Add(new PickEntry { Name = d.Name.TrimEnd('\\'), Full = d.Name, IsDir = true, Kind = 2, Note = note });
                        }
                        catch { }
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                return list;
            }
            try
            {
                var di = new DirectoryInfo(path);
                foreach (var d in di.GetDirectories())
                {
                    try
                    {
                        if ((d.Attributes & FileAttributes.Hidden) != 0) continue;
                        if ((d.Attributes & FileAttributes.System) != 0) continue;
                        list.Add(new PickEntry { Name = d.Name, Full = d.FullName, IsDir = true, Kind = 1, Modified = d.LastWriteTime });
                    }
                    catch { }
                }
                if (wantFiles)
                {
                    foreach (var f in di.GetFiles())
                    {
                        try
                        {
                            if ((f.Attributes & FileAttributes.Hidden) != 0) continue;
                            if (exts.Length > 0 && !MatchExt(f.Name, exts)) continue;
                            list.Add(new PickEntry { Name = f.Name, Full = f.FullName, IsDir = false, Kind = 0, Size = f.Length, Modified = f.LastWriteTime });
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { err = ex.Message; }
            return list;
        }

        static bool MatchExt(string name, string[] exts)
        {
            string e = "";
            try { e = Path.GetExtension(name).ToLowerInvariant(); } catch { }
            for (int i = 0; i < exts.Length; i++) if (string.Equals(exts[i], e, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void ApplyFilter()
        {
            string key = _search.Text.Trim();
            if (key.Length == 0) { _list.SetItems(_all); }
            else
            {
                var hit = new List<PickEntry>();
                foreach (var it in _all) if (it.Name.IndexOf(key, StringComparison.CurrentCultureIgnoreCase) >= 0) hit.Add(it);
                _list.SetItems(hit);
            }
            UpdateHint();
        }

        void DeepSearch()
        {
            string key = _search.Text.Trim();
            if (key.Length == 0) { ApplyFilter(); return; }
            int gen = ++_loadGen;
            _list.SetLoading(true, "正在搜索「" + key + "」…");
            string root = _cur;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var res = new List<PickEntry>();
                try
                {
                    if (!string.IsNullOrEmpty(root))
                    {
                        var stack = new Stack<string>();
                        stack.Push(root);
                        int visited = 0;
                        while (stack.Count > 0 && res.Count < 400 && visited < 6000)
                        {
                            if (gen != _loadGen) return;
                            string d = stack.Pop();
                            visited++;
                            string[] subs = null;
                            try { subs = Directory.GetDirectories(d); } catch { }
                            if (subs == null) continue;
                            foreach (var s in subs)
                            {
                                string nm = s;
                                try { nm = Path.GetFileName(s); } catch { }
                                if (nm.IndexOf(key, StringComparison.CurrentCultureIgnoreCase) >= 0)
                                    res.Add(new PickEntry { Name = nm, Full = s, IsDir = true, Kind = 1, Note = d });
                                stack.Push(s);
                            }
                        }
                    }
                }
                catch { }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (gen != _loadGen) return;
                        _all = res;
                        _list.SetLoading(false, null);
                        _list.SetItems(res);
                        _hint.ForeColor = Pk.Text3;
                        _hint.Text = res.Count == 0 ? "没找到匹配的文件夹" : ("在当前位置以下找到 " + res.Count + " 个文件夹");
                    });
                }
                catch { }
            });
        }

        void GoUp()
        {
            if (string.IsNullOrEmpty(_cur)) return;
            try
            {
                var parent = Directory.GetParent(_cur);
                Navigate(parent == null ? "" : parent.FullName);
            }
            catch { Navigate(""); }
        }

        void ActivateSelected()
        {
            var sel = _list.Selected;
            if (sel == null) return;
            if (sel.IsDir)
            {
                if (_search.Text.Length > 0) { _search.Text = ""; }
                Navigate(sel.Full);
                return;
            }
            if (_mode == PickMode.Folder) return;
            if (_mode == PickMode.SaveFile) { _name.Text = sel.Name; _name.Focus(); }
            else DoOk();
        }

        // ────────────────────────── 结果 ──────────────────────────

        bool CanOk()
        {
            if (_mode == PickMode.Folder) return !string.IsNullOrEmpty(_cur);
            if (_mode == PickMode.SaveFile)
            {
                string n = _name.Text.Trim();
                if (n.Length == 0) return false;
                if (n.IndexOf('\\') >= 0) return true;
                return !string.IsNullOrEmpty(_cur);
            }
            var sel = _list.Selected;
            if (sel != null && !sel.IsDir) return true;
            string typed = _name.Text.Trim();
            if (typed.Length > 0)
            {
                try
                {
                    string full = typed.IndexOf('\\') >= 0 ? typed : Path.Combine(_cur, typed);
                    return File.Exists(full);
                }
                catch { return false; }
            }
            return false;
        }

        void UpdateOk() { _ok.Enabled = CanOk(); }

        void UpdateHint()
        {
            var sel = _list.Selected;
            _hint.ForeColor = Pk.Text3;
            if (sel != null)
            {
                if (sel.Kind == 2) { _hint.Text = sel.Name + "  " + (sel.Note ?? ""); return; }
                if (sel.Note != null) { _hint.Text = "位于 " + sel.Note; return; }
                _hint.Text = (sel.IsDir ? "文件夹：" : "文件：") + sel.Full;
                return;
            }
            _hint.Text = string.IsNullOrEmpty(_cur) ? "此电脑：选一个磁盘开始" : ("当前位置：" + _cur);
        }

        void DoOk()
        {
            if (_mode == PickMode.Folder)
            {
                var sel = _list.Selected;
                string dir = (sel != null && sel.IsDir) ? sel.Full : _cur;
                if (string.IsNullOrEmpty(dir))
                {
                    _hint.ForeColor = Pk.Danger;
                    _hint.Text = "请先进入一个磁盘或文件夹";
                    return;
                }
                Result = dir;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            if (_mode == PickMode.SaveFile)
            {
                string n = _name.Text.Trim();
                if (n.Length == 0) { _name.Focus(); return; }
                string full;
                if (n.IndexOf('\\') >= 0 || n.IndexOf(':') >= 0) full = n;
                else
                {
                    if (string.IsNullOrEmpty(_cur)) { _hint.ForeColor = Pk.Danger; _hint.Text = "请先进入一个文件夹"; return; }
                    full = Path.Combine(_cur, n);
                }
                if (!Path.HasExtension(full) && _exts.Count > 0) full += _exts[0];
                try
                {
                    string dir2 = Path.GetDirectoryName(full);
                    if (string.IsNullOrEmpty(dir2) || !Directory.Exists(dir2)) { _hint.ForeColor = Pk.Danger; _hint.Text = "这个文件夹不存在"; return; }
                }
                catch { }
                Result = full;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            // OpenFile
            var s2 = _list.Selected;
            string file = null;
            if (s2 != null && !s2.IsDir) file = s2.Full;
            else
            {
                string typed = _name.Text.Trim();
                if (typed.Length > 0)
                {
                    try { file = typed.IndexOf('\\') >= 0 ? typed : Path.Combine(_cur, typed); }
                    catch { file = null; }
                }
            }
            if (string.IsNullOrEmpty(file)) { _hint.ForeColor = Pk.Danger; _hint.Text = "先选中一个文件"; return; }
            if (!File.Exists(file)) { _hint.ForeColor = Pk.Danger; _hint.Text = "找不到这个文件：" + file; return; }
            Result = file;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>各处弹「选文件/选文件夹」的统一入口。自绘实现，不碰系统对话框（本机会卡死）。</summary>
    static class FilePick
    {
        public static string Pick(IWin32Window owner, string title, PickMode mode, string filter, string start, string suggest)
        {
            string r = null;
            try
            {
                using (var f = new FolderPickForm(title, start, mode, filter, suggest))
                {
                    if (f.ShowDialog(owner) == DialogResult.OK) r = f.Result;
                }
            }
            catch { }
            return r;
        }
    }
}