using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>苏苏悬浮窗：贴着启动器显示，不遮挡主窗口。</summary>
    public class SusuWindow : Form
    {
        private const int Margin = 12;
        private const int PortraitH = 322;
        private const int BubbleH = 124;
        private const int BtnH = 26;
        private const int Gap = 10;

        private Image _pic;
        private int _picNo;
        private GuideNode _node;
        private int _lineIdx;
        private string _shown = "";
        private string _full = "";
        private bool _typing;
        private Timer _typeTimer, _autoTimer;
        private ZButton _bNext, _bLog, _bSkip, _bMute;
        private List<string> _history = new List<string>();
        private bool _choicesShown;

        public bool Skipped;
        public PackContext Ctx;
        public Settings St;
        public event EventHandler ClosedByUser;
        public event EventHandler RequestToc;

        public SusuWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(255, 240, 246);
            DoubleBuffered = true;
            TopMost = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Width = 300;
            Height = Margin + PortraitH + Gap + BubbleH + 8 + BtnH + Margin;

            _typeTimer = new Timer();
            _typeTimer.Interval = 26;
            _typeTimer.Tick += delegate(object s, EventArgs e) { TickType(); };
            _autoTimer = new Timer();
            _autoTimer.Interval = 2000;
            _autoTimer.Tick += delegate(object s, EventArgs e) { _autoTimer.Stop(); Advance(); };

            _bNext = MkBtn(L.T("下一句 ▸"), 96);
            _bNext.Click += delegate(object s, EventArgs e) { Advance(); };
            _bLog = MkBtn(L.T("记录"), 64);
            _bLog.Click += delegate(object s, EventArgs e) { ShowBacklog(); };
            _bSkip = MkBtn(L.T("跳过"), 64);
            _bSkip.Click += delegate(object s, EventArgs e) { DoSkip(); };
            _bMute = MkBtn(L.T("配音"), 44);
            _bMute.Click += delegate(object s, EventArgs e) { ToggleMute(); };
            Controls.Add(_bNext);
            Controls.Add(_bLog);
            Controls.Add(_bSkip);
            Controls.Add(_bMute);
            RefreshMuteButton();

            Resize += delegate(object s, EventArgs e) { LayoutBtns(); };
            MouseClick += delegate(object s, MouseEventArgs e) { OnBubbleClick(); };
        }

        private ZButton MkBtn(string text, int w)
        {
            ZButton b = new ZButton();
            b.Text = text;
            b.Size = new Size(w, BtnH);
            b.SetFont(Fonts.F(8.5f));
            b.Radius = 13;
            b.BorderColor = Color.FromArgb(244, 154, 186);
            b.TextColor = Color.FromArgb(198, 62, 112);
            b.BackNormal = Color.White;
            b.BackHover = Color.FromArgb(255, 246, 250);
            b.BackDown = Color.FromArgb(255, 232, 242);
            b.CornerColor = Color.FromArgb(255, 238, 245);
            return b;
        }

        private void LayoutBtns()
        {
            if (_bNext == null || _bLog == null || _bSkip == null || _bMute == null) return;   // 构造期间 Resize 会先触发
            int y = Height - Margin - BtnH;
            _bNext.Bounds = new Rectangle(Margin, y, 88, BtnH);
            _bLog.Bounds = new Rectangle(Margin + 88 + 6, y, 56, BtnH);
            _bSkip.Bounds = new Rectangle(Margin + 88 + 6 + 56 + 6, y, 56, BtnH);
            _bMute.Bounds = new Rectangle(Margin + 88 + 6 + 56 + 6 + 56 + 6, y, 44, BtnH);
        }

        public void StartAt(string nodeId)
        {
            _history.Clear();
            Skipped = false;
            _autoTimer.Stop();
            _typeTimer.Stop();
            GotoNode(nodeId);
        }

        public void PlaceNear(Form main)
        {
            try
            {
                if (main == null) return;
                Rectangle s = Screen.FromControl(main).WorkingArea;
                int x, y = main.Top + 40;
                if (main.Right + 12 + Width <= s.Right) x = main.Right + 12;
                else if (main.Left - 12 - Width >= s.Left) x = main.Left - 12 - Width;
                else
                {
                    x = Math.Min(main.Right - Width - 12, s.Right - Width - 8);
                    y = main.Bottom + 8;
                    if (y + Height > s.Bottom) y = s.Top + 20;
                }
                if (x < s.Left) x = s.Left;
                if (y + Height > s.Bottom) y = s.Bottom - Height - 8;
                if (y < s.Top) y = s.Top;
                Location = new Point(x, y);
            }
            catch { }
        }

        private void SetPic(int no)
        {
            if (no <= 0 || no == _picNo) return;
            try
            {
                Image im = AppLogo.GetByName("susu" + no.ToString("D2"));
                if (im != null) { _pic = im; _picNo = no; Invalidate(); }
            }
            catch { }
        }

        private void GotoNode(string id)
        {
            GuideNode n = (id == "diag") ? SusuDiag.BuildNode(Ctx, St) : GuideScript.Get(id);
            if (n == null) { Close2(); return; }
            _node = n;
            _lineIdx = 0;
            _choicesShown = false;
            if (n.Lines.Count == 0) { ShowChoices(); return; }
            ShowLine();
        }

        /// <summary>显示一个运行时构造的节点（AI 诊断结果等）。</summary>
        public void ShowDynamicNode(GuideNode n)
        {
            if (n == null) { Close2(); return; }
            _node = n;
            _lineIdx = 0;
            _choicesShown = false;
            if (n.Lines.Count == 0) { ShowChoices(); return; }
            ShowLine();
        }

        /// <summary>AI 诊断：配置接口 → 上传日志 → 显示建议。</summary>
        private void RunAiDiagnosis()
        {
            if (St == null) return;
            if (string.IsNullOrEmpty(St.ApiBase) || string.IsNullOrEmpty(St.ApiModel))
            {
                AiSetupForm setup = new AiSetupForm(St);
                setup.ShowDialog(this);
                if (!setup.Saved) return;
            }
            string ask = L.T("会把下面这些内容上传到：") + Environment.NewLine + St.ApiBase
                + Environment.NewLine + L.T("模型：") + St.ApiModel
                + Environment.NewLine + Environment.NewLine
                + L.T("· 启动器日志（尾部 60 行）") + Environment.NewLine
                + L.T("· RimWorld 日志里的报错（最近 40 条）") + Environment.NewLine
                + L.T("· 本机环境（路径 / 显卡 / 显存 / 模组数）") + Environment.NewLine + Environment.NewLine
                + L.T("确定要上传并让 AI 分析吗？");
            if (MinMsg.Show(this, ask, L.T("AI 诊断"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            _typeTimer.Stop();
            _autoTimer.Stop();
            _typing = false;
            _choicesShown = false;
            _full = L.T("苏苏正在翻你的日志……");
            _shown = _full;
            Invalidate();

            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                try
                {
                    List<DiagItem> items = SusuDiag.Run(Ctx, St);
                    string user = AiClient.BuildUserPrompt(Ctx, St, items);
                    string err;
                    string ans = AiClient.Chat(St.ApiBase, St.ApiKey, St.ApiModel, AiClient.SystemPrompt(), user, 90000, out err);
                    e.Result = ans == null ? (L.T("AI 诊断失败：") + (err == null ? L.T("未知错误") : err)) : ans;
                }
                catch (Exception ex) { e.Result = L.T("AI 诊断出错：") + ex.Message; }
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                try
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    string ans = "";
                    if (e.Error != null) ans = L.T("AI 诊断出错：") + e.Error.Message;
                    else ans = Convert.ToString(e.Result);
                    _full = L.T("看完了，我把建议放到旁边的窗口里了。");
                    _shown = _full;
                    Invalidate();
                    List<string> one = new List<string>();
                    one.Add(ans);
                    SusuBacklogForm rf = new SusuBacklogForm(one, L.T("苏苏的 AI 诊断建议"));
                    rf.ShowDialog(this);
                    ShowDynamicNode(SusuDiag.BuildHelpNode());
                }
                catch { }
            };
            bw.RunWorkerAsync();
        }

        private void ShowLine()
        {
            if (_node == null || _lineIdx >= _node.Lines.Count) { ShowChoices(); return; }
            GuideLine ln = _node.Lines[_lineIdx];
            SusuVoice.Play(ln);
            SetPic(ln.Pic);
            _full = ln.Text;
            _shown = "";
            _typing = true;
            _autoTimer.Stop();
            _typeTimer.Start();
            try { if (!string.IsNullOrEmpty(ln.Who)) _history.Add(ln.Who + "：" + ln.Text); else _history.Add("· " + ln.Text); }
            catch { }
            Invalidate();
        }

        private void TickType()
        {
            if (!_typing) { _typeTimer.Stop(); return; }
            int step = 2;
            if (_shown.Length + step >= _full.Length) { _shown = _full; _typing = false; _typeTimer.Stop(); ArmAuto(); }
            else _shown = _full.Substring(0, _shown.Length + step);
            Invalidate();
        }

        /// <summary>
        /// 排下一句的自动推进。
        /// 有配音就等这句念完（再留 400ms 喘息），没配音才用默认 2 秒。
        /// </summary>
        private void ArmAuto()
        {
            _autoTimer.Stop();
            int ms = 2000;
            try
            {
                int rem = SusuVoice.Muted ? 0 : SusuVoice.RemainingMs;
                if (rem > 0) ms = rem + 400;
            }
            catch { }
            if (ms < 1200) ms = 1200;
            if (ms > 60000) ms = 60000;
            _autoTimer.Interval = ms;
            _autoTimer.Start();
        }

        private void Advance()
        {
            _autoTimer.Stop();
            if (_typing) { _shown = _full; _typing = false; _typeTimer.Stop(); ArmAuto(); Invalidate(); return; }
            if (_node == null) { Close2(); return; }
            if (_choicesShown) return;
            _lineIdx++;
            if (_lineIdx >= _node.Lines.Count) { ShowChoices(); return; }
            ShowLine();
        }

        private void ShowChoices()
        {
            SusuVoice.Stop();
            _autoTimer.Stop();
            _typeTimer.Stop();
            _typing = false;
            if (_node != null && _node.Choices.Count > 0)
            {
                _choicesShown = true;
                _full = L.T("你选一个吧～");
                _shown = _full;
                Invalidate();
                SusuChoiceForm f = new SusuChoiceForm(_node.Choices, _node.Choices.Count > 3 ? L.T("苏苏的目录") : L.T("苏苏问你"));
                f.ShowDialog(this);
                if (!string.IsNullOrEmpty(f.PickedAction)) DoAction(f.PickedAction, f.Picked);
                else if (!string.IsNullOrEmpty(f.Picked)) GotoNode(f.Picked);
                else if (f.Dismissed) Close2();
                _choicesShown = false;
                return;
            }
            string next = _node == null ? "" : _node.Next;
            if (string.IsNullOrEmpty(next)) { Close2(); return; }
            GotoNode(next);
        }

        /// <summary>选项带动作时执行动作（去社区 / 打开导入对话框）。</summary>
        private void DoAction(string action, string next)
        {
            try
            {
                if (action == "community")
                {
                    ShellOpen.Url(SusuDiag.CommunityUrl);
                    Log.Info(L.T("苏苏打开了社区链接"));
                    return;
                }
                if (action == "ai")
                {
                    RunAiDiagnosis();
                    return;
                }
                if (action == "qr")
                {
                    Image q = AppLogo.GetByName("partyqr");
                    SusuQrForm qf = new SusuQrForm(q);
                    qf.ShowDialog(this);
                    return;
                }
                if (action == "import")
                {
                    MainForm mf = this.Owner as MainForm;
                    if (mf != null) mf.OpenImportDialog();
                    return;
                }
            }
            catch (Exception ex) { Log.Warn(L.T("执行选项动作失败: ") + ex.Message); }
            if (!string.IsNullOrEmpty(next)) GotoNode(next);
        }

        private void OnBubbleClick() { if (!_choicesShown) Advance(); }

        private void DoSkip()
        {
            Skipped = true;
            _autoTimer.Stop();
            _typeTimer.Stop();
            Close2();
        }

        private void ToggleMute()
        {
            try
            {
                SusuVoice.Muted = !SusuVoice.Muted;
                RefreshMuteButton();
                if (!SusuVoice.Muted) PlayCurrentLine();
            }
            catch { }
        }

        private void RefreshMuteButton()
        {
            if (_bMute == null) return;
            bool muted = SusuVoice.Muted;
            _bMute.Text = muted ? L.T("静音") : L.T("配音");
            _bMute.TextColor = muted ? Color.FromArgb(170, 176, 188) : Color.FromArgb(198, 62, 112);
            _bMute.BorderColor = muted ? Color.FromArgb(206, 212, 222) : Color.FromArgb(244, 154, 186);
            _bMute.Invalidate();
        }

        /// <summary>刚取消静音时，把当前这句补播一次。</summary>
        private void PlayCurrentLine()
        {
            if (_node == null || _lineIdx >= _node.Lines.Count) return;
            SusuVoice.Play(_node.Lines[_lineIdx]);
        }

        private void Close2()
        {
            try
            {
                SusuVoice.Stop();   // Hide() 不会触发 OnFormClosed，这里必须自己停，否则下次同一句会被吞
                _autoTimer.Stop();
                _typeTimer.Stop();
                Hide();
                if (ClosedByUser != null) ClosedByUser(this, EventArgs.Empty);
            }
            catch { }
        }

        private void ShowBacklog()
        {
            SusuBacklogForm f = new SusuBacklogForm(_history);
            f.ShowDialog(this);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            try
            {
                using (GraphicsPath p = R(new Rectangle(0, 0, Width, Height), 18))
                    Region = new Region(p);
            }
            catch { }
            LayoutBtns();
        }

        private static GraphicsPath R(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(2, rad * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>立刻把当前这句显示完整（测试/截图用）。</summary>
        public void FinishTypingNow()
        {
            _typeTimer.Stop();
            _typing = false;
            _shown = _full == null ? "" : _full;
            Invalidate();
        }

        /// <summary>把整个界面画到指定 Graphics（OnPaint 与截图共用）。</summary>
        public void RenderTo(Graphics g)
        {
            DrawAll(g);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { DrawAll(e.Graphics); } catch { }
        }

        private void DrawAll(Graphics g)
        {
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle all = new Rectangle(0, 0, Width, Height);
                using (LinearGradientBrush bg = new LinearGradientBrush(all, Color.FromArgb(255, 246, 250), Color.FromArgb(255, 230, 242), 90f))
                    g.FillRectangle(bg, all);

                // 相框
                Rectangle pr = new Rectangle(Margin, Margin, Width - Margin * 2, PortraitH);
                using (GraphicsPath pp = R(new Rectangle(pr.X - 2, pr.Y - 1, pr.Width + 4, pr.Height + 4), 20))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(26, 236, 120, 165)))
                    g.FillPath(sb, pp);
                using (GraphicsPath ip = R(pr, 18))
                {
                    using (SolidBrush wb = new SolidBrush(Color.White)) g.FillPath(wb, ip);
                    GraphicsState st = g.Save();
                    g.SetClip(ip);
                    if (_pic != null)
                    {
                        using (SolidBrush padb = new SolidBrush(Color.FromArgb(255, 240, 246))) g.FillRectangle(padb, pr);
                        float sc = Math.Min((float)pr.Width / _pic.Width, (float)pr.Height / _pic.Height);
                        int dw = (int)(_pic.Width * sc), dh = (int)(_pic.Height * sc);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(_pic, new Rectangle(pr.X + (pr.Width - dw) / 2, pr.Y + (pr.Height - dh) / 2, dw, dh));
                    }
                    else
                    {
                        using (SolidBrush pb = new SolidBrush(Color.FromArgb(255, 214, 230))) g.FillRectangle(pb, pr);
                        TextRenderer.DrawText(g, L.T("苏"), Fonts.B(40f), pr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                    g.Restore(st);
                    using (Pen pen = new Pen(Color.FromArgb(245, 146, 180), 4f)) g.DrawPath(pen, ip);
                }


                // 版本号（画在相框右下角，方便一眼看出跑的是哪一版）
                Rectangle vr = new Rectangle(pr.Right - 76, pr.Bottom - 22, 66, 16);
                TextRenderer.DrawText(g, "v" + Log.Version, Fonts.F(7.5f), vr, Color.FromArgb(235, 255, 255, 255),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                // 气泡
                Rectangle br = new Rectangle(Margin, Margin + PortraitH + Gap, Width - Margin * 2, BubbleH);
                using (GraphicsPath bp = R(br, 14))
                {
                    using (SolidBrush wb = new SolidBrush(Color.White)) g.FillPath(wb, bp);
                    using (Pen pen = new Pen(Color.FromArgb(245, 158, 190), 2f)) g.DrawPath(pen, bp);
                }
                int cx = Width / 2;
                Point[] tail = new Point[3];
                tail[0] = new Point(cx - 9, br.Y + 2);
                tail[1] = new Point(cx + 9, br.Y + 2);
                tail[2] = new Point(cx, br.Y - 9);
                using (SolidBrush wb = new SolidBrush(Color.White)) g.FillPolygon(wb, tail);
                using (Pen pen = new Pen(Color.FromArgb(245, 158, 190), 2f))
                {
                    g.DrawLine(pen, tail[0], tail[2]);
                    g.DrawLine(pen, tail[2], tail[1]);
                }

                bool isSusu = false;
                if (_node != null && _lineIdx < _node.Lines.Count && _node.Lines[_lineIdx].Who == L.T("苏苏")) isSusu = true;
                int ty = br.Y + 8;
                if (isSusu)
                {
                    Rectangle tag = new Rectangle(br.X + 12, ty, 56, 22);
                    using (GraphicsPath tp = R(tag, 11))
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(255, 226, 238)))
                        g.FillPath(tb, tp);
                    TextRenderer.DrawText(g, L.T("苏苏"), Fonts.B(9.5f), tag, Color.FromArgb(206, 58, 112),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                int textY = isSusu ? ty + 26 : ty + 2;
                Rectangle tr = new Rectangle(br.X + 14, textY, br.Width - 28, br.Bottom - textY - 10);
                TextRenderer.DrawText(g, _shown == null ? "" : _shown, Fonts.F(9.5f), tr, Color.FromArgb(64, 44, 58),
                    TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);

                if (_node != null && _node.Lines.Count > 0 && !_choicesShown)
                {
                    string prog = (_lineIdx + 1) + "/" + _node.Lines.Count;
                    Rectangle pgr = new Rectangle(br.Right - 58, br.Bottom - 22, 48, 18);
                    TextRenderer.DrawText(g, prog, Fonts.F(8f), pgr, Color.FromArgb(198, 160, 178),
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                Rectangle hnt = new Rectangle(br.X + 14, br.Bottom - 21, br.Width - 90, 16);
                TextRenderer.DrawText(g, _typing ? L.T("点气泡可直接显示全文") : L.T("点气泡/下一句 继续（2 秒后自动）"),
                    Fonts.F(7.5f), hnt, Color.FromArgb(206, 176, 190),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                if (_typeTimer != null) { _typeTimer.Stop(); _typeTimer.Dispose(); }
                if (_autoTimer != null) { _autoTimer.Stop(); _autoTimer.Dispose(); }
            }
            catch { }
            base.OnFormClosed(e);
        }
    }
}
