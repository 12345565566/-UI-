using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>细长的赛博风进度条。</summary>
    public class ZProgress : Control
    {
        private int _v;
        public int Value { get { return _v; } set { _v = value < 0 ? 0 : (value > 100 ? 100 : value); Invalidate(); } }
        public ZProgress()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Pal.Panel1;
            Height = 10;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Draw.Round(r, 5))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(10, 18, 34))) g.FillPath(b, p);
                if (_v > 0)
                {
                    int w = (int)(r.Width * (_v / 100f));
                    if (w < 8) w = 8;
                    Rectangle rf = new Rectangle(1, 1, w - 2, r.Height - 2);
                    if (rf.Width > 2)
                    {
                        using (GraphicsPath pf = Draw.Round(rf, 4))
                        using (LinearGradientBrush lb = new LinearGradientBrush(rf, Pal.AccentDeep, Pal.Accent, 0f))
                            g.FillPath(lb, pf);
                    }
                }
                using (Pen pen = new Pen(Color.FromArgb(70, Pal.Accent), 1f)) g.DrawPath(pen, p);
            }
        }
    }

    /// <summary>端游整合包 → 手机版整合包（给 RimDroid 导入用）。</summary>
    public class MobileExportForm : Form
    {
        private TextBox _txtSrc, _txtOut, _txtCjk;
        private Label _lbTitle, _lbDesc, _lbInfo, _lbStatus, _lbCur;
        private CheckBox _ckGame, _ckMods, _ckConfig, _ckWrap, _ckCjk;
        private ZButton _btnPickDir, _btnPickZip, _btnOut, _btnCjk, _btnStart, _btnCancel, _btnOpen;
        private ZProgress _prog;
        private TextBox _log;
        private volatile bool _cancel;
        private bool _busy;

        public MobileExportForm()
        {
            Text = L.T("手机版整合包导出器 · ASTRA // 创世神协议");
            ClientSize = new Size(920, 640);
            MinimumSize = new Size(820, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Pal.PageBack;
            ForeColor = Pal.TextMain;
            Font = Fonts.F(9f);
            DoubleBuffered = true;

            _lbTitle = new Label();
            _lbTitle.Text = L.T("手机版整合包导出器");
            _lbTitle.Font = Fonts.B(15f);
            _lbTitle.ForeColor = PaletteAccent();
            _lbTitle.Bounds = new Rectangle(22, 14, 420, 30);
            Controls.Add(_lbTitle);

            _lbDesc = new Label();
            _lbDesc.Text = L.T("把端游整合包转成 RimDroid 能直接导入的手机包：自动带上全部模组和模组排序。");
            _lbDesc.ForeColor = Pal.TextSub;
            _lbDesc.Bounds = new Rectangle(24, 46, 860, 20);
            Controls.Add(_lbDesc);

            // 源
            Label l1 = MkLabel(L.T("源整合包"), 24, 82);
            Controls.Add(l1);
            _txtSrc = MkBox(110, 80, 600);
            _txtSrc.ReadOnly = true;
            Controls.Add(_txtSrc);
            _btnPickDir = MkBtn(L.T("选文件夹"), 96, 26);
            _btnPickDir.Bounds = new Rectangle(716, 79, 96, 28);
            _btnPickDir.Click += delegate { PickDir(); };
            Controls.Add(_btnPickDir);
            _btnPickZip = MkBtn(L.T("选 zip"), 80, 26);
            _btnPickZip.Bounds = new Rectangle(818, 79, 80, 28);
            _btnPickZip.Click += delegate { PickZip(); };
            Controls.Add(_btnPickZip);

            _lbInfo = new Label();
            _lbInfo.Bounds = new Rectangle(110, 112, 790, 38);
            _lbInfo.ForeColor = Pal.TextSub;
            _lbInfo.Text = L.T("还没有选择整合包。");
            Controls.Add(_lbInfo);

            // 输出
            Label l2 = MkLabel(L.T("输出到"), 24, 160);
            Controls.Add(l2);
            _txtOut = MkBox(110, 158, 600);
            Controls.Add(_txtOut);
            _btnOut = MkBtn(L.T("另存为…"), 96, 26);
            _btnOut.Bounds = new Rectangle(716, 157, 96, 28);
            _btnOut.Click += delegate { PickOut(); };
            Controls.Add(_btnOut);

            // 中文字体 mod（RimDroid 上不装它中文全是方框）
            Label l2b = MkLabel(L.T("中文字体"), 24, 194);
            Controls.Add(l2b);
            _txtCjk = MkBox(110, 192, 600);
            Controls.Add(_txtCjk);
            _btnCjk = MkBtn(L.T("选择…"), 96, 26);
            _btnCjk.Bounds = new Rectangle(716, 191, 96, 28);
            _btnCjk.Click += delegate { PickCjk(); };
            Controls.Add(_btnCjk);
            _ckCjk = MkCheck(L.T("自动注入并启用（中文必需）"), 110, 220, true);
            _ckCjk.ForeColor = Pal.WarnText;
            Controls.Add(_ckCjk);
            AutoFindCjk();

            // 选项
            Label l3 = MkLabel(L.T("导出内容"), 24, 250);
            Controls.Add(l3);
            _ckGame = MkCheck(L.T("游戏本体文件"), 110, 248, true);
            _ckMods = MkCheck(L.T("Mods 模组（全部）"), 260, 248, true);
            _ckConfig = MkCheck(L.T("Config（含 ModsConfig.xml 排序）"), 430, 248, true);
            _ckWrap = MkCheck(L.T("在外层包一个文件夹"), 110, 276, true);
            _ckWrap.ForeColor = Pal.TextSub;
            Controls.Add(_ckGame); Controls.Add(_ckMods); Controls.Add(_ckConfig); Controls.Add(_ckWrap);

            // 按钮
            _btnStart = MkBtn(L.T("开始导出"), 130, 34);
            _btnStart.Bounds = new Rectangle(24, 312, 130, 36);
            _btnStart.Click += delegate { StartExport(); };
            Controls.Add(_btnStart);
            _btnCancel = MkBtn(L.T("取消"), 90, 34);
            _btnCancel.Bounds = new Rectangle(164, 312, 90, 36);
            _btnCancel.Enabled = false;
            _btnCancel.Click += delegate { _cancel = true; _lbStatus.Text = L.T("正在取消…"); };
            Controls.Add(_btnCancel);
            _btnOpen = MkBtn(L.T("打开所在文件夹"), 140, 34);
            _btnOpen.Bounds = new Rectangle(264, 312, 140, 36);
            _btnOpen.Enabled = false;
            _btnOpen.Click += delegate { OpenOutDir(); };
            Controls.Add(_btnOpen);

            // 进度
            _prog = new ZProgress();
            _prog.Bounds = new Rectangle(24, 362, 872, 12);
            Controls.Add(_prog);

            _lbCur = new Label();
            _lbCur.Bounds = new Rectangle(24, 378, 872, 18);
            _lbCur.ForeColor = Pal.TextFaint;
            Controls.Add(_lbCur);

            _lbStatus = new Label();
            _lbStatus.Bounds = new Rectangle(24, 400, 872, 20);
            _lbStatus.ForeColor = Pal.Accent;
            Controls.Add(_lbStatus);

            _log = new TextBox();
            _log.Multiline = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.ReadOnly = true;
            _log.BackColor = Color.FromArgb(7, 13, 27);
            _log.ForeColor = Pal.TextSub;
            _log.BorderStyle = BorderStyle.FixedSingle;
            _log.Font = Fonts.F(8.5f);
            _log.Bounds = new Rectangle(24, 426, 872, 194);
            _log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_log);

            // 变形时重排
            Resize += delegate { Relayout(); };
        }

        private static Color PaletteAccent() { return Pal.Accent; }

        private void Relayout()
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            if (_txtSrc == null) return;
            int right = w - 24;
            _txtSrc.Width = right - 230 - 110;
            _btnPickDir.Left = right - 180;
            _btnPickZip.Left = right - 80;
            _txtOut.Width = right - 230 - 110;
            _btnOut.Left = right - 180;
            _lbInfo.Width = right - 110;
            _prog.Width = right - 24;
            _lbCur.Width = right - 24;
            _lbStatus.Width = right - 24;
            _log.Width = right - 24;
            _log.Height = h - 426 - 20;
            _lbDesc.Width = right - 24;
            _btnOpen.Left = 264;
        }

        private Label MkLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Bounds = new Rectangle(x, y, 86, 22);
            l.ForeColor = Pal.TextSub;
            return l;
        }

        private TextBox MkBox(int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.Bounds = new Rectangle(x, y, w, 26);
            t.BackColor = Color.FromArgb(10, 18, 34);
            t.ForeColor = Pal.TextMain;
            t.BorderStyle = BorderStyle.FixedSingle;
            return t;
        }

        private CheckBox MkCheck(string text, int x, int y, bool on)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Bounds = new Rectangle(x, y, 200, 22);
            c.Checked = on;
            c.ForeColor = Pal.TextMain;
            return c;
        }

        private ZButton MkBtn(string text, int w, int h)
        {
            ZButton b = new ZButton();
            b.Text = text;
            b.SetFont(Fonts.B(9f));
            b.BackNormal = Color.FromArgb(226, 74, 130);
            b.BackHover = Color.FromArgb(238, 96, 148);
            b.BackDown = Color.FromArgb(200, 60, 112);
            b.TextColor = Color.White;
            b.BorderColor = Color.FromArgb(226, 74, 130);
            b.CornerColor = Color.FromArgb(4, 7, 16);
            b.Size = new Size(w, h);
            return b;
        }

        private void PickDir()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = L.T("选择端游整合包目录（里面应该有 Mods 文件夹）");
                if (d.ShowDialog(this) == DialogResult.OK) SetSource(d.SelectedPath);
            }
        }

        private void PickZip()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = L.T("整合包 (*.zip)|*.zip|所有文件 (*.*)|*.*");
                d.Title = L.T("选择整合包 zip");
                if (d.ShowDialog(this) == DialogResult.OK) SetSource(d.FileName);
            }
        }

        private void SetSource(string path)
        {
            _txtSrc.Text = path;
            _lbInfo.ForeColor = Pal.TextSub;
            _lbInfo.Text = L.T("正在分析…");
            Application.DoEvents();

            int mods; string ver, err;
            MobileExporter.Analyze(path, out mods, out ver, out err);
            if (!string.IsNullOrEmpty(err))
            {
                _lbInfo.ForeColor = Pal.WarnText;
                _lbInfo.Text = L.T("分析失败：") + err;
                return;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(L.T("检测到 ") + mods + L.T(" 个模组"));
            if (!string.IsNullOrEmpty(ver)) sb.Append(L.T(" · 游戏版本 ") + ver);
            string cfg = Directory.Exists(path)
                ? Path.Combine(Path.Combine(path, "Config"), "ModsConfig.xml")
                : null;
            bool hasCfg = cfg != null && File.Exists(cfg);
            sb.Append(hasCfg ? L.T(" · 找到 ModsConfig.xml（排序会一起带过去）") : L.T(" · 没找到 Config/ModsConfig.xml，排序需要手机上重新调"));
            _lbInfo.Text = sb.ToString();
            if (!hasCfg) _lbInfo.ForeColor = Pal.WarnText;

            // 自动生成输出名
            if (string.IsNullOrEmpty(_txtOut.Text))
            {
                string baseName = Directory.Exists(path)
                    ? new DirectoryInfo(path).Name
                    : Path.GetFileNameWithoutExtension(path);
                string dir = Directory.Exists(path) ? Path.GetDirectoryName(path) : Path.GetDirectoryName(path);
                _txtOut.Text = Path.Combine(dir, baseName + L.T("_手机版_") + DateTime.Now.ToString("yyyyMMdd") + ".zip");
            }
        }

        private void PickCjk()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = L.T("字体 mod (*.zip)|*.zip|所有文件 (*.*)|*.*");
                d.Title = L.T("选择 RimDroidCJK 中文字体 mod 的 zip");
                if (d.ShowDialog(this) == DialogResult.OK) _txtCjk.Text = d.FileName;
            }
        }

        /// <summary>在几个常见位置自动找 RimDroidCJK 的 zip。</summary>
        private void AutoFindCjk()
        {
            string[] roots = new string[] {
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools")
            };
            for (int i = 0; i < roots.Length; i++)
            {
                try
                {
                    if (!Directory.Exists(roots[i])) continue;
                    string[] files = Directory.GetFiles(roots[i], "*.zip");
                    for (int k = 0; k < files.Length; k++)
                    {
                        string nm = Path.GetFileName(files[k]).ToUpperInvariant();
                        if (nm.Contains("CJK")) { _txtCjk.Text = files[k]; return; }
                    }
                }
                catch { }
            }
        }

        private void PickOut()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = L.T("手机版整合包 (*.zip)|*.zip");
                d.FileName = string.IsNullOrEmpty(_txtOut.Text) ? L.T("手机版整合包.zip") : Path.GetFileName(_txtOut.Text);
                if (d.ShowDialog(this) == DialogResult.OK) _txtOut.Text = d.FileName;
            }
        }

        private void OpenOutDir()
        {
            try
            {
                string p = _txtOut.Text;
                if (File.Exists(p)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + p + "\"");
                else if (Directory.Exists(Path.GetDirectoryName(p)))
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(p) + "\"");
            }
            catch { }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _btnStart.Enabled = !busy;
            _btnPickDir.Enabled = !busy;
            _btnPickZip.Enabled = !busy;
            _btnOut.Enabled = !busy;
            _btnCjk.Enabled = !busy;
            _btnOpen.Enabled = !busy;
            _btnCancel.Enabled = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void StartExport()
        {
            if (_busy) return;
            if (string.IsNullOrEmpty(_txtSrc.Text)) { MinMsg.Show(this, L.T("请先选择源整合包。"), L.T("提示")); return; }
            if (string.IsNullOrEmpty(_txtOut.Text)) { MinMsg.Show(this, L.T("请先选择输出文件。"), L.T("提示")); return; }
            if (!_ckGame.Checked && !_ckMods.Checked && !_ckConfig.Checked)
            { MinMsg.Show(this, L.T("三项内容全都没勾，导出的包是空的。"), L.T("提示")); return; }

            _cancel = false;
            _prog.Value = 0;
            _log.Text = "";
            _lbStatus.Text = L.T("开始导出…");
            SetBusy(true);

            MobileExportOptions o = new MobileExportOptions();
            o.SourcePath = _txtSrc.Text;
            o.OutputZip = _txtOut.Text;
            o.IncludeGame = _ckGame.Checked;
            o.IncludeMods = _ckMods.Checked;
            o.IncludeConfig = _ckConfig.Checked;
            o.WrapInFolder = _ckWrap.Checked;
            o.IncludeCjk = _ckCjk.Checked;
            o.CjkModZip = _txtCjk.Text.Trim();

            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                e.Result = MobileExporter.Run(o, delegate(string cur, int n)
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _lbCur.Text = "[" + n + "] " + (cur.Length > 100 ? cur.Substring(0, 100) : cur);
                            if ((n % 200) == 0) _prog.Value = Math.Min(95, n / 40);
                        });
                    }
                    catch { }
                }, delegate { return _cancel; });
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false);
                MobileExportResult r = e.Result as MobileExportResult;
                if (r == null) { _lbStatus.Text = L.T("导出失败：未知错误"); return; }
                if (!r.Ok)
                {
                    _lbStatus.Text = L.T("导出失败：") + r.Err;
                    _prog.Value = 0;
                    return;
                }
                _prog.Value = 100;
                _lbCur.Text = "";
                string msg = L.T("完成：") + r.FileCount + L.T(" 个文件，") + FmtSize(r.Bytes) + "，"
                    + r.ModCount + L.T(" 个模组，用时 ") + r.Seconds.ToString("0.0") + L.T(" 秒");
                _lbStatus.Text = msg;
                _log.AppendText(L.T("输出：") + o.OutputZip + "\r\n");
                _log.AppendText(L.T("顶层文件夹：") + r.TopFolderUsed + "\r\n");
                _log.AppendText(L.T("中文字体 mod：") + (r.CjkInjected ? L.T("已注入 rimdroid.cjkfont") : L.T("未注入（手机上中文会变方框）")) + "\r\n");
                _log.AppendText(L.T("平均压缩比：") + (r.Bytes > 0 ? (100.0 * new FileInfo(o.OutputZip).Length / r.Bytes).ToString("0.0") : "0") + "%\r\n\r\n");
                _log.AppendText(r.Note);
                try
                {
                    string notePath = Path.Combine(Path.GetDirectoryName(o.OutputZip), L.T("RimDroid导入说明.txt"));
                    File.WriteAllText(notePath, r.Note, new UTF8Encoding(true));
                    _log.AppendText(L.T("\r\n说明文件已写到：\r\n") + notePath + "\r\n");
                }
                catch { }
                _btnOpen.Enabled = true;
                MinMsg.Show(this, msg + L.T("\r\n\r\n旁边还生成了一份《RimDroid导入说明.txt》，一起拷到手机上。"),
                    L.T("导出完成"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            bw.RunWorkerAsync();
        }

        private static string FmtSize(long b)
        {
            if (b >= 1073741824L) return (b / 1073741824.0).ToString("0.00") + " GB";
            if (b >= 1048576L) return (b / 1048576.0).ToString("0.0") + " MB";
            if (b >= 1024L) return (b / 1024.0).ToString("0") + " KB";
            return b + " B";
        }
    }

    /// <summary>独立工具入口。</summary>
    public static class MobileExportMain
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Pal.ApplyTheme(0); } catch { }
            Application.Run(new MobileExportForm());
        }
    }
}
