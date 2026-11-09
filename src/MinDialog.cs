using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 极简模态弹窗：squircle 面板 + 顶部彩色装饰条（危险等级标识）+ 正文 + 按钮行。
    /// 红=错误、黄=警告（启动流程相关）、青=普通提示/询问。全部自绘，不放真控件。
    /// 交互：标题区拖动 / 滚轮滚动长文本 / Enter=默认按钮 / Esc 或 ×=取消（「是/否」无 ×，与 MessageBox 一致）。
    /// </summary>
    class MinDialog : LayeredForm
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        public static Color AccentError { get { return Color.FromArgb(214, 69, 62); } }
        public static Color AccentWarn { get { return Color.FromArgb(219, 158, 46); } }
        public static Color AccentInfo { get { return T.Accent; } }

        internal class Btn
        {
            public string Text;
            public DialogResult Result = DialogResult.None;
            public bool Primary, Danger, Hover, Down, Enabled = true;
            public Rectangle R;
            public Func<bool> Action; // 返回 true=执行后关闭弹窗（DialogResult 取 Result）
        }

        protected readonly string DlgTitle;
        protected readonly Color AccentColor;
        protected readonly List<Btn> Buttons = new List<Btn>();
        protected Btn DefaultBtn, CancelBtn; // CancelBtn 不在 Buttons 里时 = 合成取消（×/Esc 用）
        protected bool AutoHeight = true;    // false=外部已用 SetLogicalSize 定好尺寸（模组详情窗）

        readonly string _msg;
        readonly int _logicalW;
        int _scroll, _scrollMax;
        Rectangle _msgRect;
        Rectangle _xRect;
        bool _xHover, _laidOut;

        protected override float ContentScale { get { return LayeredForm.UiScale; } }

        public MinDialog(string title, string msg, Color accent, int logicalWidth)
        {
            DlgTitle = title ?? "";
            _msg = msg ?? "";
            AccentColor = accent;
            _logicalW = logicalWidth;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            SetLogicalSize(logicalWidth, 200); // 真实高度在 LayoutAll 里按正文高度定
        }

        protected Btn AddBtn(string text, DialogResult res, bool primary, bool danger, Func<bool> action)
        {
            Btn b = new Btn { Text = text, Result = res, Primary = primary, Danger = danger, Action = action };
            Buttons.Add(b);
            if (primary && DefaultBtn == null) DefaultBtn = b;
            return b;
        }

        /// <summary>构造收尾：算高度、摆按钮。子类加完按钮后调用一次。</summary>
        protected void LayoutAll()
        {
            int pad = 22;
            Font bf = T.B(12);
            int btnRowW = 0;
            foreach (var b in Buttons)
            {
                int bw = Math.Max(88, TextRenderer.MeasureText(b.Text, bf).Width + 34);
                b.R = new Rectangle(0, 0, bw, 34);
                btnRowW += bw + 10;
            }
            if (Buttons.Count > 0) btnRowW -= 10;

            if (AutoHeight)
            {
                int msgW = _logicalW - pad * 2;
                int msgH = TextRenderer.MeasureText(_msg, T.F(13), new Size(msgW, 10000), TextFormatFlags.WordBreak).Height + 4;
                int bodyH = Math.Min(msgH, 300);
                SetLogicalSize(_logicalW, 50 + bodyH + 18 + 34 + 16);
                _scrollMax = Math.Max(0, msgH - bodyH);
                _msgRect = new Rectangle(pad, 50, msgW, bodyH);
            }
            int h = Lh, w = Lw;
            int by = h - 16 - 34;
            int bx = w - pad - btnRowW;
            foreach (var b in Buttons) { b.R = new Rectangle(bx, by, b.R.Width, 34); bx += b.R.Width + 10; }
            _xRect = new Rectangle(w - 42, 12, 26, 26);
            _laidOut = true;
            if (IsHandleCreated) Render();
        }

        void CenterOn(IWin32Window owner)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            try
            {
                Control c = owner as Control;
                Form f = c as Form;
                if (f == null && c != null) f = c.FindForm();
                if (f != null && f.Visible) area = f.Bounds;
                else if (f != null) area = Screen.FromControl(f).WorkingArea;
            }
            catch { }
            Location = new Point(area.X + Math.Max(0, (area.Width - Width) / 2), area.Y + Math.Max(0, (area.Height - Height) / 2));
        }

        /// <summary>构建并显示一个标准弹窗（MessageBox 的极简替代）</summary>
        public static DialogResult Show(IWin32Window owner, string title, string msg, Color accent, MessageBoxButtons buttons)
        {
            using (MinDialog d = new MinDialog(title, msg, accent, 480))
            {
                switch (buttons)
                {
                    case MessageBoxButtons.OKCancel:
                        d.AddBtn(L.T("确定"), DialogResult.OK, true, false, null);
                        d.AddBtn(L.T("取消"), DialogResult.Cancel, false, false, null);
                        d.CancelBtn = d.Buttons[1];
                        break;
                    case MessageBoxButtons.YesNo:
                        d.AddBtn(L.T("是"), DialogResult.Yes, true, false, null);
                        d.AddBtn(L.T("否"), DialogResult.No, false, false, null);
                        // 是/否询问没有 ×、Esc 无效（与 MessageBox 一致）
                        break;
                    case MessageBoxButtons.YesNoCancel:
                        d.AddBtn(L.T("是"), DialogResult.Yes, true, false, null);
                        d.AddBtn(L.T("否"), DialogResult.No, false, false, null);
                        d.AddBtn(L.T("取消"), DialogResult.Cancel, false, false, null);
                        d.CancelBtn = d.Buttons[2];
                        break;
                    default: // OK
                        d.AddBtn(L.T("确定"), DialogResult.OK, true, false, null);
                        d.CancelBtn = new Btn { Result = DialogResult.Cancel }; // Esc/× = Cancel（与 MessageBox 一致）
                        break;
                }
                d.LayoutAll();
                d.CenterOn(owner);
                if (owner == null) d.TopMost = true;
                return d.ShowDialog(owner);
            }
        }

        Btn BtnAt(Point p)
        {
            foreach (var b in Buttons) if (b.Enabled && b.R.Contains(p)) return b;
            return null;
        }

        void ActivateBtn(Btn b)
        {
            if (b == null || !b.Enabled) return;
            bool close = b.Action == null ? true : b.Action();
            if (close) { DialogResult = b.Result; Close(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point p = ToLogical(e.Location);
            bool dirty = false;
            foreach (var b in Buttons) { bool hv = b.Enabled && b.R.Contains(p); if (hv != b.Hover) { b.Hover = hv; dirty = true; } }
            bool xh = CancelBtn != null && _xRect.Contains(p);
            if (xh != _xHover) { _xHover = xh; dirty = true; }
            Cursor = (xh || BtnAt(p) != null) ? Cursors.Hand : Cursors.Default;
            if (dirty) Render();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point p = ToLogical(e.Location);
            Btn b = BtnAt(p);
            if (b != null) { b.Down = true; Render(); return; }
            if (p.Y < 46 && !_xRect.Contains(p)) // 标题区按住 = 拖动窗口
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0); // WM_NCLBUTTONDOWN + HTCAPTION
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Point p = ToLogical(e.Location);
            foreach (var x in Buttons) x.Down = false;
            if (CancelBtn != null && _xRect.Contains(p)) { ActivateBtn(CancelBtn); return; }
            Btn b = BtnAt(p);
            if (b != null) { ActivateBtn(b); return; }
            Render();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_scrollMax <= 0) return;
            _scroll = Math.Max(0, Math.Min(_scrollMax, _scroll - Math.Sign(e.Delta) * 48));
            Render();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && DefaultBtn != null) { ActivateBtn(DefaultBtn); return true; }
            if (keyData == Keys.Escape && CancelBtn != null) { ActivateBtn(CancelBtn); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void PaintContent(Graphics g)
        {
            if (!_laidOut) return;
            float w = Lw, h = Lh;
            T.FillRound(g, new RectangleF(0, 0, w, h), 12, T.Window);
            T.DrawRound(g, new RectangleF(0.5f, 0.5f, w - 1, h - 1), 12, T.Line, 1f);
            // 顶部危险等级装饰条（裁进 squircle，不外溢）
            using (var path = T.Round(new RectangleF(0, 0, w, h), 12))
            {
                g.SetClip(path);
                using (var b = new SolidBrush(AccentColor)) g.FillRectangle(b, 0, 0, w, 5);
                g.ResetClip();
            }
            // 标题
            using (var b = new SolidBrush(T.Text))
                g.DrawString(DlgTitle, T.B(15), b, new RectangleF(22, 14, w - 22 - 48, 28),
                    new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
            // ×（有取消语义时才提供）
            if (CancelBtn != null)
                using (var b = new SolidBrush(_xHover ? T.Text : T.Text3))
                    g.DrawString("×", T.B(16), b, _xRect, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            PaintBody(g);
            PaintButtons(g);
            // 正文滚动条指示
            if (_scrollMax > 0)
            {
                float th = Math.Max(18f, _msgRect.Height * _msgRect.Height / (float)(_msgRect.Height + _scrollMax));
                float ty = _msgRect.Y + (_msgRect.Height - th) * (_scroll / (float)_scrollMax);
                T.FillRound(g, new RectangleF(_msgRect.Right - 5, ty, 3, th), 1.5f, T.Line);
            }
        }

        /// <summary>正文区：默认是「可滚动的折行文本」，子类（模组详情）可覆盖</summary>
        protected virtual void PaintBody(Graphics g)
        {
            g.SetClip(_msgRect);
            var r = new Rectangle(_msgRect.X, _msgRect.Y - _scroll, _msgRect.Width, 10000);
            TextRenderer.DrawText(g, _msg, T.F(13), r, T.Text, TextFormatFlags.WordBreak);
            g.ResetClip();
        }

        protected void PaintButtons(Graphics g)
        {
            foreach (var b in Buttons)
            {
                var rr = new RectangleF(b.R.X + 0.5f, b.R.Y + 0.5f, b.R.Width - 1, b.R.Height - 1);
                Color edge = !b.Enabled ? T.Line : b.Danger ? AccentError : b.Primary ? T.Accent : T.Line;
                Color fg = !b.Enabled ? T.Text3 : b.Danger ? AccentError : b.Primary ? T.Accent : T.Text2;
                Color fill = b.Down ? T.Line : b.Hover ? (b.Primary ? T.AccentSoft : T.Line2) : T.Card;
                T.FillRound(g, rr, 8, fill);
                T.DrawRound(g, rr, 8, edge, 1f);
                using (var br = new SolidBrush(fg))
                    g.DrawString(b.Text, T.B(12), br, rr, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }
        }

        /// <summary>按像素宽度把文本折成若干行（CJK 友好，模组详情说明区用）</summary>
        internal static List<string> WrapText(string text, Font font, int width)
        {
            var lines = new List<string>();
            if (text == null) text = "";
            foreach (var para in text.Replace("\r", "").Split('\n'))
            {
                if (para.Length == 0) { lines.Add(""); continue; }
                var sb = new StringBuilder();
                foreach (char ch in para)
                {
                    if (sb.Length > 0 && TextRenderer.MeasureText(sb.ToString() + ch, font).Width > width)
                    {
                        lines.Add(sb.ToString());
                        sb.Length = 0;
                    }
                    sb.Append(ch);
                }
                if (sb.Length > 0) lines.Add(sb.ToString());
            }
            if (lines.Count == 0) lines.Add("");
            return lines;
        }

        /// <summary>自检用：三种装饰条弹窗各渲染一张 PNG</summary>
        public static void PreviewShots(string dir)
        {
            using (MinDialog d = new MinDialog(L.T("找不到游戏"),
                L.T("没有找到游戏主程序：") + "\nD:\\RimWorldPack\\RimWorld\\RimWorldWin64.exe\n\n" + L.T("请到【设置】页确认整合包根目录是否正确。"),
                AccentError, 480))
            {
                d.AddBtn(L.T("确定"), DialogResult.OK, true, false, null);
                d.CancelBtn = new Btn { Result = DialogResult.Cancel };
                d.LayoutAll();
                d.SavePreview(System.IO.Path.Combine(dir, "dlg_error.png"));
            }
            using (MinDialog d = new MinDialog(L.T("启动前模组体检"),
                "Core · 依赖正常\nHugsLib · 缺少前置 Harmony\n\n" + L.T("是否生成新的排序方案并继续启动？\n是：生成智能/安全排序（不会删除模组）\n否：按当前排序继续\n取消：先不启动"),
                AccentWarn, 480))
            {
                d.AddBtn(L.T("是"), DialogResult.Yes, true, false, null);
                d.AddBtn(L.T("否"), DialogResult.No, false, false, null);
                d.AddBtn(L.T("取消"), DialogResult.Cancel, false, false, null);
                d.CancelBtn = d.Buttons[2];
                d.LayoutAll();
                d.SavePreview(System.IO.Path.Combine(dir, "dlg_warn.png"));
            }
            using (MinDialog d = new MinDialog(L.T("导出整合包"),
                L.T("导出完成：") + "\nD:\\RimWorldPack\\整合包导出.zip\n\n" + L.T("要打开所在文件夹吗？"),
                AccentInfo, 480))
            {
                d.AddBtn(L.T("是"), DialogResult.Yes, true, false, null);
                d.AddBtn(L.T("否"), DialogResult.No, false, false, null);
                d.LayoutAll();
                d.SavePreview(System.IO.Path.Combine(dir, "dlg_info.png"));
            }
        }
    }

    /// <summary>
    /// MessageBox 的全局极简替代：同名 Show 重载，调用处只需把 MessageBox.Show 换成 MinMsg.Show。
    /// 图标映射装饰条颜色：Error/Stop/Hand=红条，Warning/Exclamation=黄条，Question/Information/无=青条。
    /// </summary>
    static class MinMsg
    {
        public static DialogResult Show(string text) { return Show(null, text, null, MessageBoxButtons.OK, MessageBoxIcon.None); }
        public static DialogResult Show(string text, string caption) { return Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None); }
        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons) { return Show(null, text, caption, buttons, MessageBoxIcon.None); }
        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) { return Show(null, text, caption, buttons, icon); }
        public static DialogResult Show(IWin32Window owner, string text) { return Show(owner, text, null, MessageBoxButtons.OK, MessageBoxIcon.None); }
        public static DialogResult Show(IWin32Window owner, string text, string caption) { return Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None); }
        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons) { return Show(owner, text, caption, buttons, MessageBoxIcon.None); }
        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            if (string.IsNullOrEmpty(caption)) caption = "Astra 创世神协议";
            Color accent;
            int v = (int)icon; // Hand=Stop=Error=16，Exclamation=Warning=48，Question=32，Asterisk=Information=64
            if (v == 16) accent = MinDialog.AccentError;
            else if (v == 48) accent = MinDialog.AccentWarn;
            else accent = MinDialog.AccentInfo;
            return MinDialog.Show(owner, caption, text ?? "", accent, buttons);
        }
    }
}
