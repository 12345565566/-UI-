using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 分层窗口基类：整窗渲染到 32bpp 位图后用 UpdateLayeredWindow 推出，
    /// 窗口四角（squircle）获得真抗锯齿；代价是不能放任何 Win32 真控件（不会显示），
    /// 所以全部交互元素都是自绘 El。移动窗口时只更新位置不重绘。
    /// </summary>
    public class LayeredForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize, IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, byte[] bmi, uint usage, out IntPtr bits, IntPtr hSection, uint offset);

        Bitmap _cache;   // 推送用位图（直接包裹 DIB 段内存，推送零拷贝）
        Bitmap _base;    // 内容位图（不含特效层）；仅特效开启时分配
        IntPtr _hbmDib, _dibBits, _memDC, _oldBmp;

        /// <summary>子类覆盖返回 true 表示启用特效层（呼吸框等），需要内容/特效双缓冲合成</summary>
        protected virtual bool FxEnabled { get { return false; } }

        /// <summary>创建/复用 DIB 段：GDI+ 直接画进这块内存，UpdateLayeredWindow 直接推，省掉每次 GetHbitmap 的全图转换</summary>
        void EnsureBitmaps()
        {
            if (_cache != null && (_cache.Width != Width || _cache.Height != Height)) DisposeDib();
            if (_cache == null)
            {
                // BITMAPINFOHEADER：32bpp、自上而下（负高度）
                byte[] bmi = new byte[40];
                BitConverter.GetBytes(40).CopyTo(bmi, 0);            // biSize
                BitConverter.GetBytes(Width).CopyTo(bmi, 4);         // biWidth
                BitConverter.GetBytes(-Height).CopyTo(bmi, 8);       // biHeight（负 = 顶向下）
                BitConverter.GetBytes((short)1).CopyTo(bmi, 12);     // biPlanes
                BitConverter.GetBytes((short)32).CopyTo(bmi, 14);    // biBitCount
                IntPtr screen = GetDC(IntPtr.Zero);
                _hbmDib = CreateDIBSection(screen, bmi, 0, out _dibBits, IntPtr.Zero, 0);
                _memDC = CreateCompatibleDC(screen);
                _oldBmp = SelectObject(_memDC, _hbmDib);
                ReleaseDC(IntPtr.Zero, screen);
                _cache = new Bitmap(Width, Height, Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, _dibBits);
            }
            if (FxEnabled)
            {
                if (_base != null && (_base.Width != Width || _base.Height != Height)) { _base.Dispose(); _base = null; }
                if (_base == null) _base = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            }
        }

        void DisposeDib()
        {
            if (_cache != null) { _cache.Dispose(); _cache = null; }
            if (_memDC != IntPtr.Zero)
            {
                if (_oldBmp != IntPtr.Zero) SelectObject(_memDC, _oldBmp);
                DeleteDC(_memDC); _memDC = IntPtr.Zero; _oldBmp = IntPtr.Zero;
            }
            if (_hbmDib != IntPtr.Zero) { DeleteObject(_hbmDib); _hbmDib = IntPtr.Zero; }
            _dibBits = IntPtr.Zero;
        }

        /// <summary>
        /// 全局 UI 缩放：布局代码全部使用逻辑坐标（主窗 1010×650），渲染时统一放大 UiScale 倍，
        /// 文字/图形矢量等比放大且保持清晰。仅主窗口生效（见 MainForm 对 ContentScale 的覆盖）；
        /// MenuPopup 等小型分层窗不缩放。
        /// </summary>
        public static float UiScale = 1.3f;

        /// <summary>当前窗体的内容缩放系数；默认 1（不缩放），MainForm 覆盖为 UiScale。</summary>
        protected virtual float ContentScale { get { return 1f; } }

        /// <summary>逻辑宽度（布局代码专用，= 物理宽 / 缩放）</summary>
        public int Lw { get { float s = ContentScale; return s == 1f ? Width : (int)(Width / s + 0.5f); } }
        /// <summary>逻辑高度（布局代码专用）</summary>
        public int Lh { get { float s = ContentScale; return s == 1f ? Height : (int)(Height / s + 0.5f); } }
        /// <summary>按逻辑尺寸设置窗口大小（内部乘缩放系数换算成物理像素）</summary>
        public void SetLogicalSize(int w, int h)
        {
            float s = ContentScale;
            Size = new Size((int)(w * s + 0.5f), (int)(h * s + 0.5f));
        }
        /// <summary>物理鼠标坐标 → 逻辑坐标（命中分发前换算）</summary>
        public Point ToLogical(Point p)
        {
            float s = ContentScale;
            return s == 1f ? p : new Point((int)(p.X / s), (int)(p.Y / s));
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000; return cp; } // WS_EX_LAYERED
        }

        // 显示内容完全由 UpdateLayeredWindow 提供，常规绘制全部置空
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        /// <summary>重绘整窗并推送到屏幕（任何状态变化后调用）；跨线程调用自动封送回 UI 线程</summary>
        public void Render()
        {
            if (IsHandleCreated && InvokeRequired) // GDI+ 对象不是线程安全的，工作线程回调里必须封送
            {
                try { BeginInvoke(new Action(Render)); } catch { }
                return;
            }
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
            EnsureBitmaps();
            if (_cache == null) return; // DIB 创建失败的极端兜底
            Bitmap target = FxEnabled ? _base : _cache; // 特效关闭时直接画进推送位图，省掉一次全图拷贝
            using (var g = Graphics.FromImage(target))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                if (ContentScale != 1f) g.ScaleTransform(ContentScale, ContentScale); // 逻辑坐标 → 物理像素
                PaintContent(g);
            }
            if (FxEnabled) CompositeFx();
            Push();
        }

        /// <summary>
        /// 廉价刷新：内容没变、只有特效层（呼吸框等）相位变化时使用。
        /// 复用内容位图，只重新合成特效层再推送，不重绘全部元素（成本低一个数量级）。
        /// </summary>
        public void RenderFxOnly()
        {
            if (!FxEnabled) return; // 特效关闭时没有特效层可刷
            if (IsHandleCreated && InvokeRequired)
            {
                try { BeginInvoke(new Action(RenderFxOnly)); } catch { }
                return;
            }
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
            EnsureBitmaps();
            CompositeFx();
            Push();
        }

        void CompositeFx()
        {
            using (var g = Graphics.FromImage(_cache))
            {
                g.DrawImageUnscaled(_base, 0, 0); // 内容层原样拷贝（纯内存搬运，很快）
                if (ContentScale != 1f) g.ScaleTransform(ContentScale, ContentScale);
                PaintFx(g); // 特效层（呼吸装饰框 / 扫描线 / 暗角），子类可覆盖
            }
        }

        /// <summary>子类可选：在内容之上叠特效层（每帧都画，包括 RenderFxOnly 的廉价刷新）</summary>
        protected virtual void PaintFx(Graphics g) { }

        byte _pushAlpha = 255;
        /// <summary>整窗不透明度 0-255（乘在逐像素 alpha 上；弹窗淡入动画用，默认 255 不透明）</summary>
        public byte PushAlpha { get { return _pushAlpha; } set { _pushAlpha = value; } }

        void Push()
        {
            if (_cache == null || _memDC == IntPtr.Zero || WindowState == FormWindowState.Minimized) return;
            IntPtr screen = GetDC(IntPtr.Zero);
            var size = new Size(_cache.Width, _cache.Height);
            var src = new Point(0, 0);
            var dst = new Point(Left, Top);
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = _pushAlpha, AlphaFormat = 1 }; // AC_SRC_OVER + AC_SRC_ALPHA
            bool ok = UpdateLayeredWindow(Handle, screen, ref dst, ref size, _memDC, ref src, 0, ref blend, 2); // ULW_ALPHA；DIB 已选进 _memDC，零拷贝直推
            if (!ok) // 推送失败 = 界面冻结的直接原因，必须留痕
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_trace.log"),
                        DateTime.Now.ToString("HH:mm:ss.fff") + " UpdateLayeredWindow FAILED err=" + Marshal.GetLastWin32Error() + "\r\n");
                }
                catch { }
            ReleaseDC(IntPtr.Zero, screen);
        }

        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); Push(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Render(); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); Render(); }

        /// <summary>子类在透明底上绘制整窗内容</summary>
        protected virtual void PaintContent(Graphics g) { }

        /// <summary>自检用：强制渲染一帧并把当前画面存为 PNG（不需要显示窗口）</summary>
        public void SavePreview(string path)
        {
            if (!IsHandleCreated) { IntPtr h = Handle; } // CreateControl() 对不可见窗口不建句柄，访问 Handle 属性才能强制创建
            Render();
            if (_cache != null) _cache.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        /// <summary>自检用：扫描内部区域（四边各缩 20px）的透明度，找半透明/全透明破洞</summary>
        public string DebugAlphaReport()
        {
            if (_cache == null) return "no-cache";
            int trans = 0, partial = 0;
            var pts = new System.Text.StringBuilder();
            for (int y = 20; y < _cache.Height - 20; y += 4)
                for (int x = 20; x < _cache.Width - 20; x += 4)
                {
                    int a = _cache.GetPixel(x, y).A;
                    if (a == 0) { trans++; if (pts.Length < 300) pts.Append(x + "," + y + "  "); }
                    else if (a < 255) partial++;
                }
            return "interior-transparent=" + trans + " partial=" + partial + "  at: " + pts;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_base != null) { _base.Dispose(); _base = null; }
                DisposeDib();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>logo 的 96px 缓存版（顶栏 22px / 启动按钮共用，避免每帧缩放原图）；
    /// 优先用内嵌的圆形星徽 launcher_logo.png，没有再退回 AppLogo（外部图片/旧内嵌 logo.png）</summary>
    static class LogoCache
    {
        static Bitmap _small;
        static bool _tried;
        public static Image Small()
        {
            if (_tried) return _small;
            _tried = true;
            Image src = null;
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("Zogos.LauncherLogo.png"))
                    if (s != null) src = Image.FromStream(s);
            }
            catch { }
            if (src == null) src = AppLogo.Get(); // AppLogo 自身已缓存原图
            if (src == null) return null;
            _small = new Bitmap(96, 96);
            using (var g = Graphics.FromImage(_small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, 96, 96);
            }
            return _small;
        }
    }

    /// <summary>
    /// 轻量自绘元素：Bounds 用窗体绝对坐标；鼠标事件由 MainForm 命中分发。
    /// 颜色一律通过 Func 或实时读取 T.*，保证深色切换即时生效。
    /// </summary>
    class El
    {
        public Rectangle Bounds;
        public bool Visible = true;
        /// <summary>宿主窗口（任何 LayeredForm 都行；需要 MainForm 专有功能的地方自行 as 转换）</summary>
        public LayeredForm Host;

        public void SetBounds(int x, int y, int w, int h)
        {
            Bounds = new Rectangle(x, y, w, h);
            OnBoundsChanged();
        }
        protected virtual void OnBoundsChanged() { }

        public virtual void Paint(Graphics g) { }
        public virtual bool HitTest(Point p) { return Visible && Bounds.Contains(p); }
        public virtual void OnMouseMove(Point p) { }
        public virtual void OnMouseDown(Point p) { }
        public virtual void OnMouseUp(Point p) { }
        public virtual void OnMouseLeave() { }
        public virtual bool OnMouseWheel(int delta) { return false; }
        public virtual Cursor CursorOf(Point p) { return Cursors.Default; }
        protected void Redraw() { if (Host != null) Host.Render(); }
    }

    /// <summary>圆角卡片（对应原 ZCard / RoundPanel）</summary>
    class ElCard : El
    {
        public float Radius = 10f;
        public Func<Color> FillOf = () => T.Card;
        public Func<Color> BorderOf = () => T.Line;
        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r, Radius, FillOf());
            T.DrawRound(g, r, Radius, BorderOf(), 1f);
        }
    }

    /// <summary>自绘文本（对应 Label）</summary>
    class ElText : El
    {
        public string Text = "";
        public Func<Font> FontOf = () => T.F(12);
        public Func<Color> ColorOf = () => T.Text;
        public bool Center;    // 水平居中
        public bool Ellipsis;  // 超宽省略号
        public ElText() { }
        public ElText(string text) { Text = text; }
        public override void Paint(Graphics g)
        {
            var f = FontOf();
            string s = Text;
            if (Ellipsis && TextRenderer.MeasureText(s, f).Width > Bounds.Width)
            {
                int lo = 0, hi = s.Length;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (TextRenderer.MeasureText(s.Substring(0, mid) + "…", f).Width <= Bounds.Width) lo = mid; else hi = mid - 1;
                }
                s = s.Substring(0, lo) + "…";
            }
            using (var b = new SolidBrush(ColorOf()))
                g.DrawString(s, f, b, Bounds, new StringFormat
                {
                    Alignment = Center ? StringAlignment.Center : StringAlignment.Near,
                    LineAlignment = StringAlignment.Center,
                });
        }
    }

    /// <summary>描边小按钮（对应原 FlatBtn / 网页 .qbtn/.bbtn）</summary>
    class ElBtn : El
    {
        public string Text = "";
        public bool Bold;       // 文字加粗（底部功能按钮用）
        public bool Primary;    // 主按钮：强调色填充 + 白字（对应原 ZButton 的 Accent 描边变体）
        public bool AccentEdge; // 强调色描边变体（对应原「绑定整合包文件夹…」按钮）
        public bool Enabled = true; // false=禁用：灰掉且不响应点击（对应原 ZButton.Enabled）
        public event EventHandler Clicked;
        bool _hover, _down;
        public ElBtn() { }
        public ElBtn(string text) { Text = text; }
        public override bool HitTest(Point p) { return Enabled && base.HitTest(p); }
        public override void OnMouseMove(Point p) { if (!_hover) { _hover = true; Redraw(); } }
        public override void OnMouseLeave() { _hover = false; _down = false; Redraw(); }
        public override void OnMouseDown(Point p) { _down = true; Redraw(); }
        public override void OnMouseUp(Point p)
        {
            bool was = _down; _down = false; Redraw();
            if (was && Bounds.Contains(p) && Clicked != null) Clicked(this, EventArgs.Empty);
        }
        public override Cursor CursorOf(Point p) { return Enabled ? Cursors.Hand : Cursors.Default; }
        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            if (Primary || AccentEdge) // 主按钮/强调按钮统一为：青描边 + 青字（减弱后不再实心填充）
            {
                T.FillRound(g, r, 8, _down ? T.Line : _hover ? T.AccentSoft : T.Card);
                T.DrawRound(g, r, 8, Enabled ? T.Accent : T.Line, 1f);
            }
            else
            {
                T.FillRound(g, r, 8, _down ? T.Line : _hover ? T.Line2 : T.Card);
                T.DrawRound(g, r, 8, T.Line, 1f);
            }
            Color fg = !Enabled ? T.Text3 : Primary || AccentEdge ? T.Accent : Bold ? T.Text : T.Text2; // Bold=加粗加黑（主要功能按钮）
            using (var b = new SolidBrush(fg))
                g.DrawString(Text, (Bold || Primary || AccentEdge) ? T.B(12) : T.F(12), b, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }
    }

    /// <summary>
    /// 整合包选择列表（切换整合包覆盖层用，对应原 PackSwitchForm 的 ListBox）：
    /// 版本徽章 + 名称（当前包 ▶ 加粗）+ 模组数/路径副行；单击选中、双击直接切换；滚轮滚动。
    /// </summary>
    class ElPackList : El
    {
        public readonly List<PackEntry> Items = new List<PackEntry>();
        public string CurrentId = "";
        public string EmptyHint = "";
        public int Sel = -1;
        public event EventHandler SelChanged;
        public event EventHandler ItemActivated; // 双击已选中行 = 直接切换
        int _hover = -1, _scroll;
        int _lastIdx = -1; long _lastTick;
        const int ROW = 48;

        public bool IsCurrent(PackEntry e) { return e != null && string.Equals(e.PackId, CurrentId, StringComparison.OrdinalIgnoreCase); }
        int MaxScroll { get { return Math.Max(0, Items.Count * ROW + 4 - Bounds.Height); } }

        public int IndexAt(Point p)
        {
            int i = (p.Y - Bounds.Y - 2 + _scroll) / ROW;
            return (i >= 0 && i < Items.Count) ? i : -1;
        }

        public override void OnMouseMove(Point p) { int i = IndexAt(p); if (i != _hover) { _hover = i; Redraw(); } }
        public override void OnMouseLeave() { _hover = -1; Redraw(); }
        public override void OnMouseUp(Point p)
        {
            int i = IndexAt(p);
            if (i < 0) return;
            bool dbl = i == Sel && i == _lastIdx && (Environment.TickCount - _lastTick) < 450;
            _lastIdx = i; _lastTick = Environment.TickCount;
            if (i != Sel) { Sel = i; if (SelChanged != null) SelChanged(this, EventArgs.Empty); }
            Redraw();
            if (dbl && ItemActivated != null) ItemActivated(this, EventArgs.Empty);
        }
        public override Cursor CursorOf(Point p) { return IndexAt(p) >= 0 ? Cursors.Hand : Cursors.Default; }
        public override bool OnMouseWheel(int delta)
        {
            if (MaxScroll <= 0) return false;
            int next = Math.Max(0, Math.Min(MaxScroll, _scroll - Math.Sign(delta) * 48));
            if (next == _scroll) return false;
            _scroll = next; _hover = -1; Redraw();
            return true;
        }

        public override void Paint(Graphics g)
        {
            var r0 = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r0, 8, T.LogBg);
            T.DrawRound(g, r0, 8, T.Line2, 1f);
            if (Items.Count == 0)
            {
                using (var b = new SolidBrush(T.Text3))
                    g.DrawString(EmptyHint, T.F(11.5f), b, r0,
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                return;
            }
            g.SetClip(new Rectangle(Bounds.X + 1, Bounds.Y + 1, Bounds.Width - 2, Bounds.Height - 2));
            for (int i = 0; i < Items.Count; i++)
            {
                PackEntry e = Items[i];
                int ry = Bounds.Y + 2 + i * ROW - _scroll;
                if (ry + ROW < Bounds.Y || ry > Bounds.Bottom) continue;
                var rr = new RectangleF(Bounds.X + 4.5f, ry + 2.5f, Bounds.Width - 10, ROW - 5);
                bool cur = IsCurrent(e), sel = i == Sel, hov = i == _hover;
                if (sel) { T.FillRound(g, rr, 8, T.AccentSoft); T.DrawRound(g, rr, 8, T.Accent, 1.2f); }
                else if (hov) T.FillRound(g, rr, 8, T.Line2);

                // 版本徽章（1.6 暖色 / 其余冷色，深浅两套）；宽度随文字自适应，长版本号不再挤两行
                bool v16 = e.VersionText == "1.6";
                Color bb, bf;
                if (T.Dark) { bb = v16 ? Color.FromArgb(72, 56, 30) : Color.FromArgb(34, 54, 72); bf = v16 ? Color.FromArgb(255, 205, 140) : Color.FromArgb(150, 205, 255); }
                else { bb = v16 ? Color.FromArgb(255, 236, 209) : Color.FromArgb(220, 238, 255); bf = v16 ? Color.FromArgb(160, 100, 10) : Color.FromArgb(30, 100, 170); }
                string badgeText = e.Badge ?? "";
                float bw2 = Math.Min(110, Math.Max(46, TextRenderer.MeasureText(badgeText, T.B(10.5f)).Width + 12));
                var badge = new RectangleF(rr.X + 8, ry + ROW / 2 - 11, bw2, 22);
                T.FillRound(g, badge, 6, bb);
                using (var b = new SolidBrush(bf))
                    g.DrawString(badgeText, T.B(10.5f), b, badge,
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });

                float tx = rr.X + 8 + bw2 + 10; // 徽章右缘之后的文字起点
                // 名称（当前包 ▶ 前缀 + 加粗）
                string name = (cur ? "▶ " : "") + (e.DisplayName ?? "");
                using (var b = new SolidBrush(e.Missing ? T.Text3 : T.Text))
                    g.DrawString(name, cur ? T.B(12.5f) : T.F(12.5f), b, new RectangleF(tx, ry + 6, rr.Right - 12 - tx, 20),
                        new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
                string sub = e.Missing ? L.T("文件夹找不到了：") + e.Root : (e.Mods + L.T(" 个模组 · ") + e.Root);
                using (var b = new SolidBrush(T.Text3))
                    g.DrawString(sub, T.F(10.5f), b, new RectangleF(tx, ry + 26, rr.Right - 12 - tx, 16),
                        new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
            }
            g.ResetClip();
            if (MaxScroll > 0)
            {
                float th = Math.Max(24f, Bounds.Height * Bounds.Height / (float)(Items.Count * ROW + 4));
                float ty = Bounds.Y + (Bounds.Height - th) * (_scroll / (float)MaxScroll);
                T.FillRound(g, new RectangleF(Bounds.Right - 5, ty, 3, th), 1.5f, T.Line);
            }
        }
    }

    /// <summary>
    /// 通用确认弹窗面板（替代 MessageBox）：卡片 + 顶部黑色装饰条 + 标题 + 多行正文。
    /// 按钮不归它管——外层在覆盖层里另放两个 ElBtn（是 / 否），点遮罩或 Esc 视为取消。
    /// </summary>
    class ElConfirm : El
    {
        public string Title = "";
        public string Message = "";
        public const int BarH = 6;   // 顶部黑色装饰条高度
        public const int Pad = 20;   // 面板内边距
        public const int BtnRowH = 48; // 底部按钮行预留高度（按钮 32 + 上下间距）

        /// <summary>按正文内容量出面板需要的高度（逻辑像素）</summary>
        public static int MeasureHeight(string msg, int width)
        {
            int textW = width - Pad * 2;
            var size = TextRenderer.MeasureText(msg, T.F(12), new Size(textW, 100000), TextFormatFlags.WordBreak);
            return BarH + 14 + 24 + 8 + size.Height + 12 + BtnRowH + 8; // 条+题+距+文+距+按钮行+底距
        }

        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r, 12, T.Card);
            // 顶部黑色装饰条：用卡片圆角路径裁剪，避免直角戳出圆角外；深色模式下随主题反白
            var oldClip = g.Clip;
            using (var path = T.Round(r, 12))
            {
                g.SetClip(path, CombineMode.Intersect);
                using (var b = new SolidBrush(T.Text))
                    g.FillRectangle(b, r.X, r.Y, r.Width, BarH);
            }
            g.Clip = oldClip;
            T.DrawRound(g, r, 12, T.Line, 1f);

            float y = Bounds.Y + BarH + 14;
            using (var b = new SolidBrush(T.Text))
                g.DrawString(Title, T.B(14), b, new RectangleF(Bounds.X + Pad, y, Bounds.Width - Pad * 2, 24),
                    new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });
            y += 32;
            float mh = Bounds.Bottom - BtnRowH - 8 - y;
            using (var b = new SolidBrush(T.Text2))
                g.DrawString(Message, T.F(12), b, new RectangleF(Bounds.X + Pad, y, Bounds.Width - Pad * 2, mh),
                    new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near });
        }
    }

    /// <summary>
    /// 顶栏：logo + 标题 + 5 页签 + 主题切换 + 最小化/关闭；空白区拖动窗口。
    /// 对应原 ZTabBar（W:803-977）；契约见 07 表第二节（switchTab / tabMinimize / tabClose）。
    /// </summary>
    class ElTopBar : El
    {
        static readonly string[] TABS = { "主页", "排序选择", "设置", "分享", "诊断" };
        const string TitleText = "ASTRA // 创世神协议 UI up↑";
        public event Action<int> TabChanged;
        public event EventHandler MinClicked, CloseClicked, ThemeClicked, CoffeeClicked;
        public int Selected;

        readonly Rectangle[] _tabRect = new Rectangle[TABS.Length];
        Rectangle _minRect, _closeRect, _themeRect, _coffeeRect;
        int _hover = -9, _down = -9; // -9 无；-1 min；-2 close；-3 主题；-4 咖啡杯；0..4 页签

        /// <summary>咖啡杯图标位置（主页页签左侧；供外面定位弹出菜单用）</summary>
        public Rectangle CoffeeRect { get { return _coffeeRect; } }

        // 选中胶囊滑动动画（页签过渡效果）
        readonly Timer _pillAnim;
        RectangleF _pill;
        Rectangle _pillTarget;
        bool _pillOn;

        public ElTopBar()
        {
            _pillAnim = new Timer { Interval = 15 };
            _pillAnim.Tick += delegate
            {
                var t = _pillTarget;
                float nx = _pill.X + (t.X - _pill.X) * 0.35f;
                float nw = _pill.Width + (t.Width - _pill.Width) * 0.35f;
                if (Math.Abs(nx - t.X) < 1f && Math.Abs(nw - t.Width) < 1f)
                {
                    _pill = new RectangleF(t.X, t.Y, t.Width, t.Height);
                    _pillAnim.Stop();
                }
                else _pill = new RectangleF(nx, t.Y, nw, t.Height);
                Redraw();
            };
        }

        /// <summary>切换选中页签（带胶囊滑动过渡；窄模式下无胶囊）</summary>
        public void SetSelected(int i)
        {
            Selected = i;
            var r = (i >= 0 && i < _tabRect.Length) ? _tabRect[i] : Rectangle.Empty;
            if (r.IsEmpty) { _pillOn = false; _pillAnim.Stop(); }
            else if (!_pillOn) { _pill = r; _pillTarget = r; _pillOn = true; } // 首次直接到位
            else if (_pillTarget != r) { _pillTarget = r; _pillAnim.Start(); }
            Redraw();
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        void Compute()
        {
            int h = Bounds.Height;
            _closeRect = new Rectangle(Bounds.Right - 8 - 36, Bounds.Y + 8, 36, 30);
            _minRect = new Rectangle(Bounds.Right - 8 - 72, Bounds.Y + 8, 36, 30);
            _themeRect = new Rectangle(Bounds.Right - 8 - 108, Bounds.Y + 8, 36, 30);
            // 窄模式（右栏折叠后窗口很窄）：空间不足，不排页签
            if (Bounds.Width < 720)
            {
                for (int i = 0; i < _tabRect.Length; i++) _tabRect[i] = Rectangle.Empty;
                _coffeeRect = Rectangle.Empty;
                _pillOn = false;
                return;
            }
            int x = _themeRect.Left - 8;
            for (int i = TABS.Length - 1; i >= 0; i--)
            {
                int w = TextRenderer.MeasureText(TABS[i], T.B(12.5f)).Width + 28;
                x -= w;
                _tabRect[i] = new Rectangle(x, Bounds.Y + (h - 30) / 2, w, 30);
                x -= 2;
            }
            // 咖啡杯：主页页签左侧（赞助 / 作者主页入口）
            _coffeeRect = new Rectangle(_tabRect[0].Left - 10 - 30, Bounds.Y + (h - 30) / 2, 30, 30);
            // 窗口尺寸变化（折叠/展开）时胶囊直接跟随，不播动画
            if (Selected >= 0 && Selected < _tabRect.Length && !_tabRect[Selected].IsEmpty)
            {
                _pill = _tabRect[Selected];
                _pillTarget = _tabRect[Selected];
                _pillOn = true;
            }
            else _pillOn = false;
        }
        protected override void OnBoundsChanged() { Compute(); }

        int Hit(Point p)
        {
            if (_closeRect.Contains(p)) return -2;
            if (_minRect.Contains(p)) return -1;
            if (_themeRect.Contains(p)) return -3;
            if (!_coffeeRect.IsEmpty && _coffeeRect.Contains(p)) return -4;
            for (int i = 0; i < _tabRect.Length; i++) if (_tabRect[i].Contains(p)) return i;
            return -9;
        }

        public override void OnMouseMove(Point p)
        {
            int h = Hit(p);
            if (h != _hover) { _hover = h; Redraw(); }
        }
        public override void OnMouseLeave() { _hover = -9; _down = -9; Redraw(); }
        public override void OnMouseDown(Point p)
        {
            _down = Hit(p);
            if (_down == -9 && Host != null)
            {
                // 无边框拖动：空白区交给系统（与原实现一致，见 02 文档 4.3）
                ReleaseCapture();
                SendMessage(Host.Handle, 0xA1, 0x2, 0);
            }
        }
        public override void OnMouseUp(Point p)
        {
            int up = Hit(p);
            if (up == _down)
            {
                if (up == -2 && CloseClicked != null) CloseClicked(this, EventArgs.Empty);
                else if (up == -1 && MinClicked != null) MinClicked(this, EventArgs.Empty);
                else if (up == -3 && ThemeClicked != null) ThemeClicked(this, EventArgs.Empty);
                else if (up == -4 && CoffeeClicked != null) CoffeeClicked(this, EventArgs.Empty);
                else if (up >= 0 && up != Selected) { SetSelected(up); if (TabChanged != null) TabChanged(up); }
            }
            _down = -9;
        }
        public override Cursor CursorOf(Point p) { return Hit(p) != -9 ? Cursors.Hand : Cursors.Default; }

        public override void Paint(Graphics g)
        {
            int h = Bounds.Height;
            using (var pen = new Pen(T.Line2, 1f)) g.DrawLine(pen, Bounds.X, Bounds.Bottom - 1, Bounds.Right, Bounds.Bottom - 1);

            // logo 块 + 标题：logo 图片裁成正圆（星徽本体就是圆形徽章），加载失败时退回字母占位块
            var logoR = new RectangleF(Bounds.X + 16, Bounds.Y + (h - 22) / 2, 22, 22);
            Image logo = LogoCache.Small();
            if (logo != null)
            {
                var oldI = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var path = T.Round(logoR, 11)) { g.SetClip(path); g.DrawImage(logo, logoR); g.ResetClip(); }
                g.InterpolationMode = oldI;
                T.DrawRound(g, logoR, 11, T.Line, 1f); // 细描边，深色主题下也有边界
            }
            else
            {
                T.FillRound(g, logoR, 6, T.AccentSoft);
                using (var b = new SolidBrush(T.Accent))
                    g.DrawString("A", T.B(11), b, logoR,
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }
            using (var b = new SolidBrush(T.Text))
                g.DrawString(TitleText, T.B(13), b, Bounds.X + 46, Bounds.Y + h / 2 - 13);
            using (var b = new SolidBrush(T.Text3))
                g.DrawString("v1.7.0", T.F(11), b, Bounds.X + 46 + TextRenderer.MeasureText(TitleText, T.B(13)).Width + 8, Bounds.Y + h / 2 - 10);

            // 页签（窄模式下为空矩形，跳过）；选中胶囊带滑动过渡
            if (_pillOn && !_pill.IsEmpty)
                T.FillRound(g, _pill, 15, T.Text);
            for (int i = 0; i < TABS.Length; i++)
            {
                var r = _tabRect[i];
                if (r.IsEmpty) continue;
                bool sel = i == Selected, hov = i == _hover;
                if (!sel && hov) T.FillRound(g, r, 15, T.Line2);
                Color tc = T.Text; // 页签统一加粗加黑
                using (var b = new SolidBrush(tc))
                    g.DrawString(TABS[i], T.B(12.5f), b, r,
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }
            // 胶囊滑动经过处把选中文字「擦」成反色（裁剪到胶囊区域重绘选中文字）
            if (_pillOn && !_pill.IsEmpty && Selected >= 0 && Selected < TABS.Length && !_tabRect[Selected].IsEmpty)
            {
                g.SetClip(_pill);
                using (var b = new SolidBrush(T.Window))
                    g.DrawString(TABS[Selected], T.B(12.5f), b, _tabRect[Selected],
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                g.ResetClip();
            }

            DrawCoffee(g, _coffeeRect, _hover == -4);
            DrawThemeBtn(g, _themeRect, _hover == -3);
            DrawWinBtn(g, _minRect, _hover == -1, false);
            DrawWinBtn(g, _closeRect, _hover == -2, true);
        }

        /// <summary>咖啡杯（赞助/作者主页入口）：纯线条矢量——杯身 + 把手 + 碟子 + 两缕热气</summary>
        void DrawCoffee(Graphics g, Rectangle r, bool hov)
        {
            if (r.IsEmpty) return;
            if (hov) T.FillRound(g, r, 15, T.Line2);
            Color c = hov ? T.Accent : T.Text2;
            using (var pen = new Pen(c, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                // 杯身（圆角矩形轮廓）
                using (var cup = T.Round(new RectangleF(r.X + 6, r.Y + 12, 13, 10), 3))
                    g.DrawPath(pen, cup);
                // 把手（右侧半环）
                g.DrawArc(pen, r.X + 17f, r.Y + 14f, 6f, 6f, -75, 150);
                // 碟子
                g.DrawLine(pen, r.X + 5, r.Y + 24.5f, r.X + 22, r.Y + 24.5f);
                // 热气两缕
                g.DrawBezier(pen, r.X + 10f, r.Y + 9.5f, r.X + 8.8f, r.Y + 7.6f, r.X + 11.2f, r.Y + 5.8f, r.X + 10f, r.Y + 3.8f);
                g.DrawBezier(pen, r.X + 14.5f, r.Y + 9.5f, r.X + 13.3f, r.Y + 7.6f, r.X + 15.7f, r.Y + 5.8f, r.X + 14.5f, r.Y + 3.8f);
            }
        }

        // 太阳（浅色模式，点击切深色）/ 月亮（深色模式，点击切浅色）
        void DrawThemeBtn(Graphics g, Rectangle r, bool hov)
        {
            if (hov) T.FillRound(g, r, 6, T.Line2);
            var c = hov ? T.Text : T.Text3;
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            if (!T.Dark)
            {
                using (var pen = new Pen(c, 1.4f))
                {
                    g.DrawEllipse(pen, cx - 4.5f, cy - 4.5f, 9, 9);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        g.DrawLine(pen,
                            (float)(cx + 7 * Math.Cos(a)), (float)(cy + 7 * Math.Sin(a)),
                            (float)(cx + 9.5 * Math.Cos(a)), (float)(cy + 9.5 * Math.Sin(a)));
                    }
                }
            }
            else
            {
                using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - 5, cy - 5, 10, 10);
                using (var b = new SolidBrush(hov ? T.Line2 : T.Window)) g.FillEllipse(b, cx - 1.5f, cy - 6.5f, 10, 10);
            }
        }

        void DrawWinBtn(Graphics g, Rectangle r, bool hov, bool close)
        {
            if (hov) T.FillRound(g, r, 6, close ? Color.FromArgb(251, 233, 231) : T.Line2);
            var c = hov ? (close ? Color.FromArgb(179, 38, 30) : T.Text) : T.Text3;
            using (var pen = new Pen(c, 1.4f))
            {
                int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                if (close) { g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4); g.DrawLine(pen, cx + 4, cy - 4, cx - 4, cy + 4); }
                else g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
            }
        }
    }

    /// <summary>
    /// 方形启动键：悬停浮起 + 跟随鼠标倾斜（真 3D 透视投影，面正中心为固定旋转轴），
    /// 22px 厚度侧壁随倾斜淡入，主标题钉死面正中心、随面透视压缩（无漂移）。
    /// 对应原 CyberHub 的中央圆键；契约 [launch] 接 MainForm.StartGame()（07 表第三节）。
    /// </summary>
    class ElLaunch : El
    {
        public event EventHandler LaunchClicked;
        public bool Busy; // 部署中状态（文字变色，对应 SetBusy(true) 的视觉侧）
        public bool Enabled = true; // false=禁用（部署中 / 未就绪，对应原 _btnLaunch.Enabled）
        public string SubText = "请先选择排序方案";

        public const int SIZE = 248;  // 控件边长（面 216 + 四周 16px 余量，极限倾斜不裁剪）
        const int FACE = 216;
        const double D = 640;         // 透视视距

        bool _hover;
        float _px, _py; // 鼠标相对中心 -1..1
        int _lastRepaintTick;

        public override void OnMouseMove(Point p)
        {
            _hover = true;
            _px = ((p.X - Bounds.X) / (float)Bounds.Width - 0.5f) * 2f;
            _py = ((p.Y - Bounds.Y) / (float)Bounds.Height - 0.5f) * 2f;
            // 高回报率鼠标每秒上百个 move，不能每次都整窗重绘：倾斜状态照记，重绘节流 ~30fps
            int now = Environment.TickCount;
            if (now - _lastRepaintTick >= 33) { _lastRepaintTick = now; Redraw(); }
        }
        public override void OnMouseLeave() { _hover = false; _px = _py = 0; Redraw(); }
        public override void OnMouseUp(Point p)
        {
            if (Enabled && Bounds.Contains(p) && LaunchClicked != null) LaunchClicked(this, EventArgs.Empty);
        }
        public override Cursor CursorOf(Point p) { return Enabled ? Cursors.Hand : Cursors.Default; }

        /// <summary>自检用：强制设置悬停倾斜（--tilt=x,y，x/y 取 -1..1）</summary>
        public void DebugSetTilt(float px, float py)
        {
            _hover = true;
            _px = px; _py = py;
        }

        /// <summary>自检用：当前是否处于悬停态（--uitest 读取）</summary>
        public bool HoverForDebug { get { return _hover; } }

        // 以控件正中心为固定轴：先绕 X 轴再绕 Y 轴旋转，然后透视投影回 2D（中心点数学上不动）
        PointF ProjS(float x, float y, float z, double cosX, double sinX, double cosY, double sinY, double k, out double sOut)
        {
            double y1 = y * cosX - z * sinX;
            double z1 = y * sinX + z * cosX;
            double x2 = x * cosY - z1 * sinY;
            double z2 = x * sinY + z1 * cosY;
            sOut = D / (D - z2) * k;
            float cx = Bounds.X + Bounds.Width / 2f, cy = Bounds.Y + Bounds.Height / 2f;
            return new PointF((float)(cx + x2 * sOut), (float)(cy + y1 * sOut));
        }

        // 面轮廓（squircle）摊平成点集，逐点透视投影后重建路径；z：面的深度（0=正面，负值=背面厚度层）
        GraphicsPath ProjectedFace(float z, double cosX, double sinX, double cosY, double sinY, double k)
        {
            using (var src = T.Round(new RectangleF(-FACE / 2f, -FACE / 2f, FACE, FACE), 30))
            {
                src.Flatten(null, 0.3f);
                var pts = src.PathPoints;
                var dst = new PointF[pts.Length];
                for (int i = 0; i < pts.Length; i++)
                {
                    double s;
                    dst[i] = ProjS(pts[i].X, pts[i].Y, z, cosX, sinX, cosY, sinY, k, out s);
                }
                var path = new GraphicsPath();
                path.AddPolygon(dst);
                return path;
            }
        }

        // 正面轮廓 + 背面轮廓（反向）拼成环带路径，即按钮的「侧壁」
        static GraphicsPath BuildBand(GraphicsPath front, GraphicsPath back)
        {
            var f = front.PathPoints;
            var b = back.PathPoints;
            var pts = new PointF[f.Length + b.Length];
            Array.Copy(f, pts, f.Length);
            for (int i = 0; i < b.Length; i++) pts[f.Length + i] = b[b.Length - 1 - i];
            var path = new GraphicsPath();
            path.AddPolygon(pts);
            return path;
        }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        // 文字：锚定在面平面上（z 可调，0=与面一体、正中心投影后不动），随面透视压缩——文字是按钮本体的一部分
        void DrawFaceText(Graphics g, string text, Font font, Color color, float x, float y, float z,
            double cosX, double sinX, double cosY, double sinY, double k, double rx, double ry)
        {
            double s;
            var p = ProjS(x, y, z, cosX, sinX, cosY, sinY, k, out s);
            var sz = g.MeasureString(text, font);
            var old = g.Transform; // 先存全局变换（含 UI 缩放），不能直接 ResetTransform
            using (var m = new Matrix())
            {
                m.Translate(p.X, p.Y);
                m.Scale((float)(s * Math.Cos(ry)), (float)(s * Math.Cos(rx)));
                m.Multiply(old, MatrixOrder.Append); // 全局缩放最后应用（GDI+ 行向量约定：Append=后乘=最外层）
                g.Transform = m;
                using (var b = new SolidBrush(color))
                    g.DrawString(text, font, b, -sz.Width / 2f, -sz.Height / 2f);
                g.Transform = old; // 恢复全局变换，后续元素才能画对位置
            }
            old.Dispose();
        }

        // logo：与文字一样钉在面平面上（z=0），随面一起倾斜/透视压缩，是按钮本体的一部分
        void DrawFaceImage(Graphics g, Image img, float x, float y, float z, float w, float h,
            double cosX, double sinX, double cosY, double sinY, double k, double rx, double ry)
        {
            double s;
            var p = ProjS(x, y, z, cosX, sinX, cosY, sinY, k, out s);
            var old = g.Transform;
            var oldI = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var m = new Matrix())
            {
                m.Translate(p.X, p.Y);
                m.Scale((float)(s * Math.Cos(ry)), (float)(s * Math.Cos(rx)));
                m.Multiply(old, MatrixOrder.Append); // 全局缩放最后应用
                g.Transform = m;
                g.DrawImage(img, -w / 2f, -h / 2f, w, h);
                g.Transform = old;
            }
            g.InterpolationMode = oldI;
            old.Dispose();
        }

        public override void Paint(Graphics g)
        {
            double rx = _py * 9.0 * Math.PI / 180.0;   // 上下倾（跟随鼠标）
            double ry = _px * 11.0 * Math.PI / 180.0;  // 左右倾（跟随鼠标）
            double cosX = Math.Cos(rx), sinX = Math.Sin(rx);
            double cosY = Math.Cos(ry), sinY = Math.Sin(ry);
            double k = _hover ? D / (D - 14.0) : 1.0;  // 悬停浮起 14px，等效轻微放大

            float cx = Bounds.X + Bounds.Width / 2f, cy = Bounds.Y + Bounds.Height / 2f;

            using (var faceBack = ProjectedFace(-22, cosX, sinX, cosY, sinY, k)) // 背面：深 22px，与正面连成侧壁
            using (var face = ProjectedFace(0, cosX, sinX, cosY, sinY, k))
            {
                if (_hover)
                {
                    // 浮起阴影：屏幕空间柔边（多层透明叠加），不随面倾斜
                    for (int i = 3; i >= 1; i--)
                        T.FillRound(g, new RectangleF(cx - 92 - i * 3, cy + 96 + i * 2, 184 + i * 6, 6 + i * 2),
                            5 + i, Color.FromArgb(4 + i * 3, 28, 27, 23));
                    // 光晕：沿投影后的轮廓描一圈
                    using (var glow = new Pen(T.AccentSoft, 6f)) g.DrawPath(glow, face);
                }

                // 侧壁（厚度）：正面与背面轮廓之间的实心环带，透明度随倾斜量淡入
                float t = Math.Min(1f, (Math.Abs(_px) + Math.Abs(_py)) * 0.8f);
                if (t > 0.02f)
                {
                    int alpha = (int)(255 * t);
                    Color side = _hover ? Mix(T.Line2, T.Accent, 0.20f) : Mix(T.Line2, T.Text3, 0.30f);
                    using (var band = BuildBand(face, faceBack))
                    using (var b = new SolidBrush(Color.FromArgb(alpha, side)))
                        g.FillPath(b, band);
                    using (var pen = new Pen(Color.FromArgb(alpha, T.Line), 1.2f))
                        g.DrawPath(pen, faceBack);
                }

                // 面板：轻微纵向渐变（上亮下暗），深浅主题各自成立
                var fb = face.GetBounds();
                using (var b = new LinearGradientBrush(fb, Mix(T.Card, Color.White, 0.10f), Mix(T.Card, Color.Black, 0.08f), LinearGradientMode.Vertical))
                    g.FillPath(b, face);
                using (var pen = new Pen(_hover ? T.Accent : T.Line, 1.4f)) g.DrawPath(pen, face);
            }

            // 排版（自上而下）：大 logo 图片 → 主标题 → 副标题，全部钉在面平面上随倾斜一体运动；
            // 图片放大后文字整体下移，主标题仍锚在面平面上（z=0，与面一体）
            Image logo = LogoCache.Small();
            if (logo != null)
                DrawFaceImage(g, logo, 0, -46, 0, 84, 84, cosX, sinX, cosY, sinY, k, rx, ry);
            DrawFaceText(g, "启 动", T.B(23), Busy ? T.Accent : T.Text, 0, 32, 0, cosX, sinX, cosY, sinY, k, rx, ry);
            DrawFaceText(g, SubText, T.F(11.5f), T.Text3, 0, 68, 0, cosX, sinX, cosY, sinY, k, rx, ry);
        }
    }

    /// <summary>
    /// 整合包信息块：「排序选择 ›」胶囊在最上方（与下面文字的左边对齐），其下三行文字居左。
    /// 胶囊点击契约 [hubSort]/[pickSchemePage] 接 MainForm.SwitchPage(1)（07 表第三、四节）。
    /// </summary>
    class ElInfo : El
    {
        public event EventHandler SortClicked;
        public string PackName = "整合包";
        public string Meta = "RimWorld 版本未知";
        public string Scheme = "未选择排序方案";

        Rectangle _pill;
        bool _downPill;

        protected override void OnBoundsChanged()
        {
            _pill = new Rectangle(Bounds.X, Bounds.Y, 104, 28); // 胶囊在顶部，左缘与文字平齐
        }
        public override void OnMouseDown(Point p) { _downPill = _pill.Contains(p); }
        public override void OnMouseUp(Point p)
        {
            bool was = _downPill; _downPill = false;
            if (was && _pill.Contains(p) && SortClicked != null) SortClicked(this, EventArgs.Empty);
        }
        public override void OnMouseMove(Point p) { Redraw(); }
        public override Cursor CursorOf(Point p) { return _pill.Contains(p) ? Cursors.Hand : Cursors.Default; }

        public override void Paint(Graphics g)
        {
            bool pillHov = _pill.Contains(Host is MainForm ? ((MainForm)Host).MousePos : Point.Empty);
            T.FillRound(g, _pill, 14, T.Card);
            T.DrawRound(g, _pill, 14, pillHov ? T.Text3 : T.Line, 1f); // 悬停灰边（减弱，不用青色）
            using (var b = new SolidBrush(pillHov ? T.Text : T.Text2))
                g.DrawString("排序选择  ›", T.F(12), b, _pill,
                    new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });

            using (var b = new SolidBrush(T.Text)) g.DrawString(PackName, T.B(14), b, Bounds.X, Bounds.Y + 36);
            using (var b = new SolidBrush(T.Text2)) g.DrawString(Meta, T.F(11.5f), b, Bounds.X, Bounds.Y + 60);
            using (var b = new SolidBrush(T.Text3)) g.DrawString(Scheme, T.F(11.5f), b, Bounds.X, Bounds.Y + 80);
        }
    }

    /// <summary>
    /// 折叠分组：头部（标题 + 提示 + 箭头）+ 子元素内容区，动画展开。
    /// 子元素由 Fold 自己裁剪绘制；手风琴互斥由 MainForm 协调。
    /// </summary>
    class ElFold : El
    {
        public string Title, Hint;
        public int ContentHeight = 120;
        public readonly List<El> Children = new List<El>();
        public bool IsOpen { get; private set; }
        public event EventHandler OpenChanged;

        public const int HEAD = 38;
        readonly Timer _anim;
        float _cur;

        public ElFold()
        {
            _anim = new Timer { Interval = 15 };
            _anim.Tick += delegate
            {
                float target = IsOpen ? ContentHeight : 0;
                float d = target - _cur;
                if (Math.Abs(d) < 2f) { _cur = target; _anim.Stop(); }
                else _cur += d * 0.35f;
                if (Host is MainForm) ((MainForm)Host).RelayoutRight(); // 高度变化 → 右栏重排（内含 Render）
            };
        }

        public int TotalHeight { get { return HEAD + (int)_cur; } }

        public void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;
            _anim.Start();
            if (OpenChanged != null) OpenChanged(this, EventArgs.Empty);
        }
        /// <summary>立即到位（无动画），供 --shot 自检用</summary>
        public void SetOpenImmediate(bool open)
        {
            IsOpen = open;
            _anim.Stop();
            _cur = open ? ContentHeight : 0;
            if (OpenChanged != null) OpenChanged(this, EventArgs.Empty);
        }

        protected override void OnBoundsChanged() { LayoutChildren(); }

        /// <summary>子元素排在内容区（头部以下），子类可重写</summary>
        protected virtual void LayoutChildren()
        {
            int y = Bounds.Y + HEAD;
            foreach (var c in Children)
            {
                c.SetBounds(Bounds.X + 12, y, Bounds.Width - 24, c.Bounds.Height);
                y += c.Bounds.Height;
            }
        }

        /// <summary>命中子元素（只认完整落在当前可见区内的）；没有则命中自己</summary>
        public El PickChild(Point p)
        {
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                var c = Children[i];
                if (c.Visible && c.Bounds.Bottom <= Bounds.Bottom - 4 && c.HitTest(p)) return c;
            }
            return null;
        }

        public override void OnMouseDown(Point p) { if (p.Y - Bounds.Y <= HEAD) SetOpen(!IsOpen); }
        public override Cursor CursorOf(Point p) { return p.Y - Bounds.Y <= HEAD ? Cursors.Hand : Cursors.Default; }

        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r, 8, T.Card);
            T.DrawRound(g, r, 8, T.Line, 1f);

            using (var b = new SolidBrush(T.Text)) g.DrawString(Title, T.F(12.5f), b, Bounds.X + 12, Bounds.Y + 10);
            if (!string.IsNullOrEmpty(Hint))
            {
                var sz = TextRenderer.MeasureText(Hint, T.F(11));
                using (var b = new SolidBrush(T.Text3)) g.DrawString(Hint, T.F(11), b, Bounds.Right - 30 - sz.Width, Bounds.Y + 13);
            }
            using (var pen = new Pen(T.Text3, 1.4f))
            {
                int cx = Bounds.Right - 18, cy = Bounds.Y + HEAD / 2;
                if (IsOpen) { g.DrawLine(pen, cx - 4, cy + 2, cx, cy - 3); g.DrawLine(pen, cx, cy - 3, cx + 4, cy + 2); }
                else { g.DrawLine(pen, cx - 4, cy - 2, cx, cy + 3); g.DrawLine(pen, cx, cy + 3, cx + 4, cy - 2); }
            }

            // 内容区：裁剪后绘制子元素（展开动画中途不溢出）
            if (Bounds.Height > HEAD + 2)
            {
                g.SetClip(new RectangleF(Bounds.X + 1, Bounds.Y + 1, Bounds.Width - 2, Bounds.Height - 2));
                foreach (var c in Children) if (c.Visible) c.Paint(g);
                g.ResetClip();
            }
        }
    }

    /// <summary>
    /// 右栏收起柄：窗口最右侧的细长竖条，加粗 &gt; / &lt;。
    /// TODO(状态同步)：右栏是否展开当前只存在本次会话内；
    /// 正式版启动时需读取上次的展开状态自动同步（对应原 Settings 的 UI 状态持久化）。
    /// </summary>
    class ElRail : El
    {
        public bool Collapsed; // true=右栏已收起，显示 >
        public event EventHandler Clicked;
        bool _hover, _down;
        public override void OnMouseMove(Point p) { if (!_hover) { _hover = true; Redraw(); } }
        public override void OnMouseLeave() { _hover = false; _down = false; Redraw(); }
        public override void OnMouseDown(Point p) { _down = true; }
        public override void OnMouseUp(Point p)
        {
            bool was = _down; _down = false;
            if (was && Bounds.Contains(p) && Clicked != null) Clicked(this, EventArgs.Empty);
        }
        public override Cursor CursorOf(Point p) { return Cursors.Hand; }
        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r, 11, _hover ? T.Line2 : T.Card);        // 悬停不再用青色（减弱）
            T.DrawRound(g, r, 11, _hover ? T.Text3 : T.Line, 1f);
            using (var b = new SolidBrush(_hover ? T.Text : T.Text3))
                g.DrawString(Collapsed ? ">" : "<", T.B(14), b, r,
                    new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }
    }

    /// <summary>
    /// 自绘日志区（对应原只读 TextBox）：新日志自动贴底，滚轮回看，右侧细滚动条。
    /// 支持按行拖选：按住左键拖动选中整行（中英混排日志按行选比按字符选稳），
    /// Ctrl+C 复制选区、Ctrl+A 全选，单击取消选择。
    /// </summary>
    class ElLogView : El
    {
        public readonly List<string> Lines = new List<string>();
        public int MaxLines = 500; // 超出丢弃最旧的行（同步报告等长输出场景可调大）
        int _scroll; // 0=贴底；向上回看的行数
        readonly Font _font = new Font("Consolas", 11, GraphicsUnit.Pixel);

        // —— 行选择状态（Lines 下标，含两端）——
        int _selAnchor = -1, _selCaret = -1;
        bool _dragging, _moved; // _moved：拖动中换过行才算选择（区分单击）
        static ElLogView _active; // 最近交互过的日志区，Ctrl+C / Ctrl+A 找它

        public void Add(string line)
        {
            Lines.Add(line);
            if (Lines.Count > MaxLines)
            {
                Lines.RemoveAt(0);
                if (_selAnchor >= 0) _selAnchor--;
                if (_selCaret >= 0) _selCaret--;
                if (_selAnchor < 0 || _selCaret < 0) ClearSel(); // 选区头部被挤出 → 放弃选区
            }
            _scroll = 0;
            Redraw();
        }

        /// <summary>批量追加（整段文本按行拆入，只重绘一次；长报告输出用）</summary>
        public void AddBulk(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var ln in text.Replace("\r", "").Split('\n')) Lines.Add(ln);
            while (Lines.Count > MaxLines) Lines.RemoveAt(0);
            _scroll = 0;
            Redraw();
        }

        /// <summary>整体替换内容（对应原 _txtLog.Text = Log.TailText(n)）。</summary>
        public void SetText(string text)
        {
            Lines.Clear();
            if (!string.IsNullOrEmpty(text))
                foreach (var ln in text.Replace("\r", "").Split('\n')) Lines.Add(ln);
            while (Lines.Count > MaxLines) Lines.RemoveAt(0);
            _scroll = 0;
            ClearSel();
            Redraw();
        }

        void ClearSel() { _selAnchor = _selCaret = -1; _moved = false; }
        public bool HasSelection { get { return _selAnchor >= 0 && _selCaret >= 0 && _moved; } }

        int LineHeight { get { return 15; } }
        int VisibleLines { get { return Math.Max(1, (Bounds.Height - 8) / LineHeight); } }

        /// <summary>鼠标位置 → Lines 下标（限制在当前可视区范围内）</summary>
        int LineAt(Point p)
        {
            if (Lines.Count == 0) return -1;
            int end = Lines.Count - _scroll;
            int start = Math.Max(0, end - VisibleLines);
            int idx = start + (p.Y - Bounds.Y - 4) / LineHeight;
            if (idx < start) idx = start;
            if (idx > end - 1) idx = end - 1;
            if (idx < 0) idx = 0;
            if (idx > Lines.Count - 1) idx = Lines.Count - 1;
            return idx;
        }

        public override void OnMouseDown(Point p)
        {
            _active = this;
            _dragging = true;
            _moved = false;
            _selAnchor = _selCaret = LineAt(p);
            Redraw();
        }
        public override void OnMouseMove(Point p)
        {
            if (!_dragging) return;
            int idx = LineAt(p);
            if (idx != _selCaret) { _selCaret = idx; _moved = true; Redraw(); }
        }
        public override void OnMouseUp(Point p)
        {
            _dragging = false;
            if (!_moved) { ClearSel(); Redraw(); } // 单击 = 取消选择
        }
        public override Cursor CursorOf(Point p) { return Cursors.IBeam; }

        /// <summary>复制当前选区到剪贴板，返回复制行数（0 = 无选区/失败）</summary>
        public int CopySelection()
        {
            if (!HasSelection) return 0;
            int a = Math.Min(_selAnchor, _selCaret), b = Math.Max(_selAnchor, _selCaret);
            var sb = new System.Text.StringBuilder();
            for (int i = a; i <= b && i < Lines.Count; i++)
            {
                if (i < 0) continue;
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append(Lines[i]);
            }
            if (sb.Length == 0) return 0;
            try { Clipboard.SetText(sb.ToString()); } catch { return 0; }
            return b - a + 1;
        }

        public void SelectAll()
        {
            if (Lines.Count == 0) return;
            _active = this;
            _selAnchor = 0; _selCaret = Lines.Count - 1; _moved = true;
            Redraw();
        }

        /// <summary>Ctrl+C：复制最近交互日志区的选区；无选区返回 false 让按键继续传播</summary>
        public static bool CopyActiveSelection()
        {
            if (_active == null || !_active.HasSelection) return false;
            int n = _active.CopySelection();
            if (n > 0 && _active.Host is MainForm) ((MainForm)_active.Host).Toast("已复制 " + n + " 行日志");
            return n > 0;
        }
        /// <summary>Ctrl+A：全选最近交互日志区（输入框聚焦时不抢）</summary>
        public static bool SelectAllActive()
        {
            if (_active == null || _active.Lines.Count == 0) return false;
            _active.SelectAll();
            return true;
        }

        public override bool OnMouseWheel(int delta)
        {
            int max = Math.Max(0, Lines.Count - VisibleLines);
            if (max == 0) return false;
            _scroll = Math.Max(0, Math.Min(max, _scroll - Math.Sign(delta) * 3));
            Redraw();
            return true;
        }

        public override void Paint(Graphics g)
        {
            g.SetClip(Bounds);
            int vis = VisibleLines;
            int end = Lines.Count - _scroll;
            int start = Math.Max(0, end - vis);
            int selA = -1, selB = -1;
            if (HasSelection) { selA = Math.Min(_selAnchor, _selCaret); selB = Math.Max(_selAnchor, _selCaret); }
            using (var b = new SolidBrush(T.Text2))
            using (var bSel = new SolidBrush(T.Text))
            using (var bSelBg = new SolidBrush(Color.FromArgb(70, T.Accent))) // 半透明青底，深浅色下都可读
            {
                for (int i = start, row = 0; i < end; i++, row++)
                {
                    int y = Bounds.Y + 4 + row * LineHeight;
                    bool sel = selA >= 0 && i >= selA && i <= selB;
                    if (sel) g.FillRectangle(bSelBg, Bounds.X, y - 1, Bounds.Width, LineHeight);
                    g.DrawString(Lines[i], _font, sel ? bSel : b, Bounds.X + 2, y);
                }
            }
            g.ResetClip();
            if (Lines.Count > vis)
            {
                float th = Math.Max(16f, Bounds.Height * vis / (float)Lines.Count);
                float ty = Bounds.Y + (Bounds.Height - th) * (1f - (Lines.Count - vis - _scroll) / (float)(Lines.Count - vis));
                T.FillRound(g, new RectangleF(Bounds.Right - 5, ty, 3, th), 1.5f, T.Line);
            }
        }
    }

    /// <summary>
    /// 窗口内右缘滑入的轻提示条（Toast）：顶部青色进度条随剩余时间缩短，走完自动滑出。
    /// 滑入/收回动画与位置由 MainForm 的计时器驱动（Bounds 由外部每帧设置），点击立即收回。
    /// </summary>
    class ElToast : El
    {
        public string Msg = "";
        public float Progress = 1f; // 剩余停留比例 1→0（顶部进度条）
        public int Tick;            // 动画帧计数（由 MainForm 的计时器推进）
        public event EventHandler Clicked;
        public override void OnMouseUp(Point p) { if (Bounds.Contains(p) && Clicked != null) Clicked(this, EventArgs.Empty); }
        public override Cursor CursorOf(Point p) { return Cursors.Hand; }
        public override void Paint(Graphics g)
        {
            float w = Bounds.Width, h = Bounds.Height;
            // 柔影
            T.FillRound(g, new RectangleF(Bounds.X + 2, Bounds.Y + 4, w, h), 10, Color.FromArgb(28, 28, 27, 23));
            T.FillRound(g, new RectangleF(Bounds.X, Bounds.Y, w, h), 10, T.Card);
            T.DrawRound(g, new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, w - 1, h - 1), 10, T.Line, 1f);
            float pw = w * Progress;
            if (pw > 0.5f) T.FillRound(g, new RectangleF(Bounds.X, Bounds.Y, pw, 3), 1.5f, T.Accent); // 顶部进度条
            using (var b = new SolidBrush(T.Text))
                g.DrawString(Msg, T.F(12.5f), b, new RectangleF(Bounds.X + 14, Bounds.Y + 3, w - 28, h - 6),
                    new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
        }
    }

    /// <summary>
    /// 自绘弹出菜单（替代 ContextMenuStrip；分层窗口里不能用真菜单）。
    /// 自身也是分层窗口，圆角抗锯齿；失焦自动关闭。
    /// </summary>
    class MenuPopup : LayeredForm
    {
        class Item { public string Text; public Action Act; public bool Sep; public bool Label; }
        readonly List<Item> _items = new List<Item>();
        int _hover = -1;

        /// <summary>菜单也吃全局界面缩放：布局全用逻辑坐标，渲染层统一放大（与主窗口同一套机制）</summary>
        protected override float ContentScale { get { return UiScale; } }
        /// <summary>逻辑最小宽度（调用方传触发按钮的宽度，让菜单和按钮一样宽）</summary>
        public int LogicalMinWidth;

        // 逻辑尺寸：行高 / 侧距 / 字号（物理像素 = ×UiScale，随设置页缩放档位一起变）
        int ROW { get { return 38; } }
        const int PADX = 16;
        static float MenuFontPx { get { return 13.5f; } }

        public MenuPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
        }

        public void AddItem(string text, Action act) { _items.Add(new Item { Text = text, Act = act }); }
        public void AddSep() { _items.Add(new Item { Sep = true }); }
        /// <summary>灰色说明行（不可点、不悬停，行高较矮）</summary>
        public void AddLabel(string text) { _items.Add(new Item { Text = text, Label = true }); }

        System.Windows.Forms.Timer _openAnim;

        public void ShowAt(Point screenPos)
        {
            int w = LogicalMinWidth, rows = 0;
            using (var g = CreateGraphics())
                foreach (var it in _items)
                {
                    if (it.Sep) { rows += 9; continue; }
                    rows += it.Label ? 22 : ROW;
                    w = Math.Max(w, TextRenderer.MeasureText(it.Text, it.Label ? T.F(11) : T.F(MenuFontPx)).Width + PADX * 2 + 8);
                }
            Size = new Size((int)(w * UiScale + 0.5f), (int)((rows + 12) * UiScale + 0.5f)); // 逻辑 → 物理
            Location = screenPos;
            var scr = Screen.FromPoint(screenPos).WorkingArea;
            if (Bottom > scr.Bottom) Top = scr.Bottom - Height - 4;
            if (Right > scr.Right) Left = scr.Right - Width - 4;

            // 展开动画：从上偏 10px 滑落到目标位 + 整体淡入（约 120ms ease-out，跟主窗动画同一套时长/曲线）
            int slide = (int)(10 * UiScale + 0.5f);
            int targetY = Top;
            Top = targetY - slide;
            PushAlpha = 0;
            if (_openAnim != null) { _openAnim.Stop(); _openAnim.Dispose(); _openAnim = null; }
            int step = 0;
            const int STEPS = 8;
            _openAnim = new System.Windows.Forms.Timer { Interval = 15 };
            _openAnim.Tick += delegate
            {
                if (IsDisposed || Disposing) { _openAnim.Stop(); _openAnim.Dispose(); _openAnim = null; return; } // 动画中途被关（点外面/Esc）
                step++;
                float t = Math.Min(1f, step / (float)STEPS);
                float e = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic
                PushAlpha = (byte)(255 * e);
                Top = targetY - (int)(slide * (1f - e)); // 改 Top 会触发 Push（带着当前透明度）
                if (step >= STEPS)
                {
                    _openAnim.Stop(); _openAnim.Dispose(); _openAnim = null;
                    PushAlpha = 255; Top = targetY;
                    Render(); // 兜底一帧，确保完全不透明
                }
            };
            Show();
            _openAnim.Start();
        }

        int RowAt(int logicalY)
        {
            int yy = 6;
            for (int i = 0; i < _items.Count; i++)
            {
                int h = _items[i].Sep ? 9 : (_items[i].Label ? 22 : ROW);
                if (logicalY >= yy && logicalY < yy + h) return (_items[i].Sep || _items[i].Label) ? -1 : i;
                yy += h;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = RowAt((int)(e.Y / UiScale)); // 物理 → 逻辑
            if (h != _hover) { _hover = h; Render(); }
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int h = RowAt((int)(e.Y / UiScale));
            if (h >= 0) { var act = _items[h].Act; Close(); if (act != null) act(); }
        }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }

        protected override void PaintContent(Graphics g)
        {
            // 到这里坐标系已被渲染层乘过 UiScale，全部用逻辑坐标（Lw/Lh）画
            T.FillRound(g, new RectangleF(0, 0, Lw, Lh), 8, T.Card);
            T.DrawRound(g, new RectangleF(0.5f, 0.5f, Lw - 1, Lh - 1), 8, T.Line, 1f);
            int y = 6;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.Sep)
                {
                    using (var pen = new Pen(T.Line2, 1f)) g.DrawLine(pen, 10, y + 4, Lw - 10, y + 4);
                    y += 9;
                    continue;
                }
                if (it.Label) // 说明行：灰色小字，不可点
                {
                    using (var b = new SolidBrush(T.Text3))
                        g.DrawString(it.Text, T.F(11), b, new RectangleF(PADX, y, Lw - PADX * 2, 22),
                            new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });
                    y += 22;
                    continue;
                }
                if (i == _hover) T.FillRound(g, new RectangleF(4, y, Lw - 8, ROW), 6, T.Line2);
                using (var b = new SolidBrush(T.Text))
                    g.DrawString(it.Text, T.F(MenuFontPx), b, new RectangleF(PADX, y, Lw - PADX * 2, ROW),
                        new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center });
                y += ROW;
            }
        }
    }
}