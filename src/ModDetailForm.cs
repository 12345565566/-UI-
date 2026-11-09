using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 模组详情（极简重构版）：自绘分层弹窗，与全局极简风格一致（squircle + 顶部青色装饰条）。
    /// 原赛博风真控件版已废弃。功能不变：信息总览 / 说明（滚轮滚动）/ AI 翻译 / 启用关闭切换。
    /// 交互：标题区拖动 / Esc 或 × 或 Enter=关闭。
    /// </summary>
    public class ModDetailForm : LayeredForm
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        class Btn
        {
            public string Text = "";
            public bool Primary, Danger, Hover, Down, Enabled = true;
            public Rectangle R;
            public Action Click;
        }

        readonly ModGraphNode _node;
        readonly ModGraphData _data;
        readonly Settings _st;
        readonly Action<bool> _onToggle;
        readonly List<Btn> _btns = new List<Btn>();
        Btn _btnToggle, _btnTranslate;
        readonly List<string> _descLines = new List<string>();
        string _descRaw = "";
        int _descScroll, _descMax;
        Rectangle _descRect;
        string _status = "";
        bool _statusErr;
        BackgroundWorker _worker;
        Rectangle _xRect;
        bool _xHover;
        string[] _meta = new string[0];
        bool _metaWarn;

        const int W = 720, H = 560;      // 逻辑尺寸
        const int DescTop = 178;         // 说明卡片顶（元信息区之下）
        const int LineH = 18;            // 说明区行高

        protected override float ContentScale { get { return LayeredForm.UiScale; } }

        public ModDetailForm(ModGraphNode node, ModGraphData data, Settings st, Action<bool> onToggle)
        {
            _node = node; _data = data; _st = st; _onToggle = onToggle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            SetLogicalSize(W, H);
            BuildButtons();
            LayoutAll();
            FillInfo();
            Load += delegate(object s, EventArgs e)
            {
                CenterOnOwner();
                if (HasEnglishDescription(_descRaw)) Translate(); // 保留原行为：英文说明自动翻
            };
        }

        void BuildButtons()
        {
            _btnTranslate = new Btn { Text = L.T("AI 翻译为中文") };
            _btnTranslate.Click = delegate { Translate(); };
            _btnToggle = new Btn { Text = "" };
            _btnToggle.Click = delegate { ToggleNode(); };
            Btn close = new Btn { Text = L.T("关闭"), Primary = true };
            close.Click = delegate { Close(); };
            _btns.Add(_btnTranslate);
            _btns.Add(_btnToggle);
            _btns.Add(close);
        }

        void LayoutAll()
        {
            int pad = 22, btnRowW = 0;
            Font bf = T.B(12);
            foreach (var b in _btns)
            {
                int bw = Math.Max(96, TextRenderer.MeasureText(b.Text, bf).Width + 34);
                b.R = new Rectangle(0, 0, bw, 34);
                btnRowW += bw + 10;
            }
            btnRowW -= 10;
            int by = H - 16 - 34;
            int bx = W - pad - btnRowW;
            foreach (var b in _btns) { b.R = new Rectangle(bx, by, b.R.Width, 34); bx += b.R.Width + 10; }
            _xRect = new Rectangle(W - 42, 12, 26, 26);
            _descRect = new Rectangle(pad, DescTop, W - pad * 2, H - DescTop - 96);
        }

        void CenterOnOwner()
        {
            try
            {
                Rectangle area = Owner != null && Owner.Visible ? Owner.Bounds : Screen.FromHandle(Handle).WorkingArea;
                Location = new Point(area.X + Math.Max(0, (area.Width - Width) / 2), area.Y + Math.Max(0, (area.Height - Height) / 2));
            }
            catch { }
        }

        void FillInfo()
        {
            if (_node == null) return;
            InstalledMod m = _node.Mod;
            _metaWarn = _node.Warning;
            _meta = new string[]
            {
                "packageId：" + _node.Id,
                L.T("作者：") + (m == null || string.IsNullOrEmpty(m.Author) ? "-" : m.Author),
                L.T("状态：") + (_node.Enabled ? L.T("已启用") : L.T("已关闭")) + "    " + (_node.Warning ? L.T("⚠ 前置依赖缺失/被关闭") : L.T("依赖正常")),
                L.T("路径：") + (m == null || string.IsNullOrEmpty(m.RootDir) ? "-" : m.RootDir),
                L.T("支持版本：") + (m == null || m.SupportedVersions.Count == 0 ? L.T("未标注") : string.Join(", ", m.SupportedVersions.ToArray())),
                L.T("依赖：") + (m == null || m.Dependencies.Count == 0 ? L.T("无") : string.Join(", ", m.Dependencies.ToArray())),
                m == null ? null : (L.T("内容：Defs=") + (m.HasDefs ? L.T("有") : L.T("无")) + "  Assemblies=" + (m.HasAssemblies ? L.T("有") : L.T("无")) + "  Patches=" + (m.HasPatches ? L.T("有") : L.T("无")))
            };
            string desc = (m == null || string.IsNullOrEmpty(m.Description)) ? L.T("（这个模组没有写说明）") : m.Description;
            _descRaw = L.T("【原文 / Original】\n") + desc;
            _btnToggle.Text = _node.Enabled ? L.T("关闭此模组") : L.T("启用此模组");
            _btnToggle.Danger = _node.Enabled;
            // 按钮宽度随文字变化，重排一次
            LayoutAll();
            RewrapDesc();
            if (IsHandleCreated) Render();
        }

        void RewrapDesc()
        {
            _descLines.Clear();
            _descLines.AddRange(MinDialog.WrapText(_descRaw, T.F(12), _descRect.Width - 20));
            _descMax = Math.Max(0, _descLines.Count * LineH - (_descRect.Height - 12));
            _descScroll = Math.Max(0, Math.Min(_descScroll, _descMax));
        }

        static bool HasEnglishDescription(string text)
        {
            int ascii = 0, cjk = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) ascii++;
                if (c >= 0x4E00 && c <= 0x9FFF) cjk++;
            }
            return ascii > 30 && ascii > cjk * 2;
        }

        void Translate()
        {
            if (_worker != null && _worker.IsBusy) return;
            string original = _descRaw;
            if (original.IndexOf(L.T("【中文翻译】"), StringComparison.Ordinal) >= 0) return;
            _btnTranslate.Enabled = false;
            _status = L.T("AI 正在翻译模组说明…");
            _statusErr = false;
            Render();
            _worker = new BackgroundWorker();
            _worker.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                string err;
                string tr = ModClassifier.TranslateDescription(_st, original.Replace(L.T("【原文 / Original】"), ""), out err);
                e.Result = new object[] { tr, err };
            };
            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                _btnTranslate.Enabled = true;
                object[] arr = e.Result as object[];
                string tr = arr == null ? null : Convert.ToString(arr[0]);
                string err = arr == null ? "" : Convert.ToString(arr[1]);
                if (!string.IsNullOrEmpty(tr))
                {
                    _descRaw = original + L.T("\n\n【中文翻译 / Translation】\n") + tr;
                    _status = L.T("已生成双语对照。");
                    _statusErr = false;
                    RewrapDesc();
                }
                else
                {
                    _status = L.T("自动翻译失败：") + (string.IsNullOrEmpty(err) ? L.T("请先配置 AI 接口") : err);
                    _statusErr = true;
                }
                Render();
            };
            _worker.RunWorkerAsync();
        }

        void ToggleNode()
        {
            if (_node == null) return;
            _node.Enabled = !_node.Enabled;
            ModGraphBuilder.RecomputeWarnings(_data);
            if (_onToggle != null) _onToggle(_node.Enabled);
            FillInfo();
        }

        Btn BtnAt(Point p)
        {
            foreach (var b in _btns) if (b.Enabled && b.R.Contains(p)) return b;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point p = ToLogical(e.Location);
            bool dirty = false;
            foreach (var b in _btns) { bool hv = b.Enabled && b.R.Contains(p); if (hv != b.Hover) { b.Hover = hv; dirty = true; } }
            bool xh = _xRect.Contains(p);
            if (xh != _xHover) { _xHover = xh; dirty = true; }
            Cursor = (xh || BtnAt(p) != null) ? Cursors.Hand : (_descRect.Contains(p) ? Cursors.IBeam : Cursors.Default);
            if (dirty) Render();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point p = ToLogical(e.Location);
            Btn b = BtnAt(p);
            if (b != null) { b.Down = true; Render(); return; }
            if (p.Y < 46 && !_xRect.Contains(p))
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Point p = ToLogical(e.Location);
            foreach (var x in _btns) x.Down = false;
            if (_xRect.Contains(p)) { Close(); return; }
            Btn b = BtnAt(p);
            if (b != null && b.Click != null) { b.Click(); return; }
            Render();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_descMax <= 0) return;
            Point p = ToLogical(PointToClient(MousePosition));
            if (!_descRect.Contains(p)) return;
            _descScroll = Math.Max(0, Math.Min(_descMax, _descScroll - Math.Sign(e.Delta) * LineH * 3));
            Render();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void PaintContent(Graphics g)
        {
            float w = Lw, h = Lh;
            T.FillRound(g, new RectangleF(0, 0, w, h), 12, T.Window);
            T.DrawRound(g, new RectangleF(0.5f, 0.5f, w - 1, h - 1), 12, T.Line, 1f);
            // 顶部装饰条（信息级：主题青）
            using (var path = T.Round(new RectangleF(0, 0, w, h), 12))
            {
                g.SetClip(path);
                using (var b = new SolidBrush(T.Accent)) g.FillRectangle(b, 0, 0, w, 5);
                g.ResetClip();
            }
            // 标题
            string title = _node == null ? L.T("模组详情") : _node.Name + "  ·  " + _node.Category;
            using (var b = new SolidBrush(T.Text))
                g.DrawString(title, T.B(15), b, new RectangleF(22, 14, w - 22 - 48, 28),
                    new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
            // ×
            using (var b = new SolidBrush(_xHover ? T.Text : T.Text3))
                g.DrawString("×", T.B(16), b, _xRect, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            // 元信息
            int my = 52;
            using (var b2 = new SolidBrush(T.Text2))
            using (var bw = new SolidBrush(T.WarnText))
            {
                var fmt = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
                for (int i = 0; i < _meta.Length; i++)
                {
                    if (_meta[i] == null) continue;
                    bool warnLine = _metaWarn && i == 2;
                    g.DrawString(_meta[i], T.F(12), warnLine ? bw : b2, new RectangleF(22, my, w - 44, 16), fmt);
                    my += 17;
                }
            }
            // 说明卡片
            T.FillRound(g, new RectangleF(_descRect.X + 0.5f, _descRect.Y + 0.5f, _descRect.Width - 1, _descRect.Height - 1), 8, T.Card);
            T.DrawRound(g, new RectangleF(_descRect.X + 0.5f, _descRect.Y + 0.5f, _descRect.Width - 1, _descRect.Height - 1), 8, T.Line, 1f);
            g.SetClip(_descRect);
            using (var b = new SolidBrush(T.Text))
            {
                Font f = T.F(12);
                int y = _descRect.Y + 6 - _descScroll;
                for (int i = 0; i < _descLines.Count; i++, y += LineH)
                {
                    if (y + LineH < _descRect.Y) continue;
                    if (y > _descRect.Bottom) break;
                    g.DrawString(_descLines[i], f, b, _descRect.X + 10, y);
                }
            }
            g.ResetClip();
            if (_descMax > 0)
            {
                float th = Math.Max(18f, _descRect.Height * (_descRect.Height / (float)(_descRect.Height + _descMax)));
                float ty = _descRect.Y + (_descRect.Height - th) * (_descScroll / (float)_descMax);
                T.FillRound(g, new RectangleF(_descRect.Right - 6, ty, 3, th), 1.5f, T.Line);
            }
            // 状态行（翻译进度/结果）
            if (!string.IsNullOrEmpty(_status))
                using (var b = new SolidBrush(_statusErr ? MinDialog.AccentError : T.Text3))
                    g.DrawString(_status, T.F(11), b, new RectangleF(22, _descRect.Bottom + 8, w - 44, 16),
                        new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
            // 按钮
            foreach (var b in _btns)
            {
                var rr = new RectangleF(b.R.X + 0.5f, b.R.Y + 0.5f, b.R.Width - 1, b.R.Height - 1);
                Color edge = !b.Enabled ? T.Line : b.Danger ? MinDialog.AccentError : b.Primary ? T.Accent : T.Line;
                Color fg = !b.Enabled ? T.Text3 : b.Danger ? MinDialog.AccentError : b.Primary ? T.Accent : T.Text2;
                Color fill = b.Down ? T.Line : b.Hover ? (b.Primary ? T.AccentSoft : T.Line2) : T.Card;
                T.FillRound(g, rr, 8, fill);
                T.DrawRound(g, rr, 8, edge, 1f);
                using (var br = new SolidBrush(fg))
                    g.DrawString(b.Text, T.B(12), br, rr, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }
        }
    }
}
