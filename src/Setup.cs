using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>Astra 创世神协议 - 安装程序（自带启动器，不需要管理员权限）</summary>
    public class SetupForm : Form
    {
        public const string PayloadExe = "Zogos.Setup.Payload.exe";
        public const string PayloadReadme = "Zogos.Setup.Readme.txt";
        public const string PayloadCli = "Zogos.Setup.Cli.exe";
        public const string PayloadFFmpeg = "Zogos.Setup.FFmpeg.exe";

        private ZTabBar _bar;
        private Panel _body;
        private ZCard _card;
        private Panel _pgIntro, _pgOpt;

        // 引导页
        private Label _lbIntroTitle, _lbIntroText;
        private ZIntroPanel _intro;
        private ZButton _btnNext;

        // 选项页
        private Label _lbOptTitle, _lbDir, _lbHint, _lbStatus, _lbHowto, _lbFoot;
        private TextBox _tDir;
        private ZButton _btnBrowse, _btnToPack, _btnToUser, _btnInstall, _btnManual;
        private CheckBox _cDesktop, _cStart, _cRimSort, _cRun;

        private string _packRoot = "";
        private bool _busy;

        public SetupForm()
        {
            Text = L.T("Astra 创世神协议 - 安装");
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 600);
            BackColor = Pal.PageBack;
            Font = Fonts.F(9f);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Build();
            try { _packRoot = PackLocator.Detect(); } catch { }
            UpdatePackUi();
            ShowPage(0);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            try
            {
                using (System.Drawing.Drawing2D.GraphicsPath p = Draw.Round(new Rectangle(0, 0, Width, Height), 12))
                    Region = new Region(p);
            }
            catch { }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LayoutAll();
        }

        private Label Mk(string text, Font f, Color c, ContentAlignment a)
        {
            Label l = new Label();
            l.Text = text; l.Font = f; l.ForeColor = c; l.TextAlign = a;
            l.AutoSize = false; l.BackColor = Color.Transparent; l.UseMnemonic = false;
            return l;
        }

        private ZButton MkB(string text, int w, int h)
        {
            ZButton b = new ZButton();
            b.Text = text; b.Size = new Size(w, h);
            b.CornerColor = Pal.White;
            return b;
        }

        private CheckBox MkCheck(string text, bool val)
        {
            CheckBox c = new CheckBox();
            c.Text = text; c.Checked = val;
            c.Font = Fonts.F(9f); c.ForeColor = Pal.TextMain;
            c.BackColor = Pal.White; c.FlatStyle = FlatStyle.System; c.AutoSize = false;
            return c;
        }

        private void Build()
        {
            _bar = new ZTabBar();
            _bar.Tabs = new string[0];
            _bar.Title = Edition.Title + L.T(" · 安装程序");
            _bar.CloseClicked += delegate(object s, EventArgs e) { Close(); };
            _bar.MinimizeClicked += delegate(object s, EventArgs e) { WindowState = FormWindowState.Minimized; };
            Controls.Add(_bar);

            _body = new Panel();
            _body.BackColor = Pal.PageBack;
            Controls.Add(_body);

            _card = new ZCard();
            _card.CornerColor = Pal.PageBack;
            _body.Controls.Add(_card);

            // ---------------- 引导页 ----------------
            _pgIntro = new Panel();
            _pgIntro.BackColor = Pal.White;
            _card.Controls.Add(_pgIntro);

            _lbIntroTitle = Mk(L.T("这个启动器是干什么的？"), Fonts.B(12.5f), Pal.TextMain, ContentAlignment.MiddleLeft);
            _pgIntro.Controls.Add(_lbIntroTitle);

            _intro = new ZIntroPanel();
            _pgIntro.Controls.Add(_intro);

            _lbIntroText = Mk(
                L.T("整合包＝游戏本体＋一堆模组＋一份「排序表」。\r\n") +
                L.T("排序表告诉游戏先加载哪个模组、后加载哪个；顺序不对就会报错、闪退、模组不生效。\r\n\r\n") +
                L.T("以前你得自己装模组管理器、设路径、找排序文件、导入、保存……步骤多，新手很容易卡住。\r\n") +
                L.T("这个启动器把这些全做完了：\r\n") +
                L.T("　① 帮你备份原来的游戏配置（出问题能还原）\r\n") +
                L.T("　② 把整合包的配置和排序写好\r\n") +
                L.T("　③ 点一下就把游戏启动了\r\n\r\n") +
                L.T("你要做的只有两件事：选一个排序，点【启动游戏】。"),
                Fonts.F(9f), Pal.TextMain, ContentAlignment.TopLeft);
            _pgIntro.Controls.Add(_lbIntroText);

            _btnNext = MkB(L.T("下一步：选安装位置 →"), 540, 52);
            _btnNext.SetFont(Fonts.B(12f));
            _btnNext.Click += delegate(object s, EventArgs e) { ShowPage(1); };
            _pgIntro.Controls.Add(_btnNext);

            // ---------------- 选项页 ----------------
            _pgOpt = new Panel();
            _pgOpt.BackColor = Pal.White;
            _pgOpt.Visible = false;
            _card.Controls.Add(_pgOpt);

            _lbOptTitle = Mk(L.T("选择安装位置"), Fonts.B(12.5f), Pal.TextMain, ContentAlignment.MiddleLeft);
            _pgOpt.Controls.Add(_lbOptTitle);

            _lbDir = Mk(L.T("安装位置"), Fonts.F(9f), Pal.TextSub, ContentAlignment.MiddleLeft);
            _pgOpt.Controls.Add(_lbDir);
            _tDir = new TextBox();
            _tDir.BorderStyle = BorderStyle.FixedSingle;
            _tDir.Font = Fonts.F(9f);
            _tDir.BackColor = Color.White;
            _tDir.ForeColor = Pal.TextMain;
            _pgOpt.Controls.Add(_tDir);
            _btnBrowse = MkB(L.T("浏览…"), 86, 26);
            _btnBrowse.BorderColor = Color.FromArgb(196, 212, 230);
            _btnBrowse.TextColor = Pal.TextMain;
            _btnBrowse.Click += delegate(object s, EventArgs e) { Browse(); };
            _pgOpt.Controls.Add(_btnBrowse);

            _btnToPack = MkB(L.T("装到整合包里（推荐）"), 250, 32);
            _btnToPack.SetFont(Fonts.B(9f));
            _btnToPack.Click += delegate(object s, EventArgs e) { if (_packRoot.Length > 0) _tDir.Text = _packRoot; };
            _pgOpt.Controls.Add(_btnToPack);
            _btnToUser = MkB(L.T("只装到用户目录"), 180, 32);
            _btnToUser.BorderColor = Color.FromArgb(196, 212, 230);
            _btnToUser.TextColor = Pal.TextMain;
            _btnToUser.Click += delegate(object s, EventArgs e) { _tDir.Text = DefaultDir(); };
            _pgOpt.Controls.Add(_btnToUser);

            _lbHint = Mk("", Fonts.F(8f), Pal.TextFaint, ContentAlignment.MiddleLeft);
            _pgOpt.Controls.Add(_lbHint);

            _cDesktop = MkCheck(L.T("创建桌面快捷方式"), true);
            _cStart = MkCheck(L.T("创建开始菜单快捷方式"), true);
            _cRimSort = MkCheck(L.T("一并安装 RimSort 模组管理器"), true);
            _cRun = MkCheck(L.T("装完立即运行启动器"), true);
            _pgOpt.Controls.Add(_cDesktop);
            _pgOpt.Controls.Add(_cStart);
            _pgOpt.Controls.Add(_cRimSort);
            _pgOpt.Controls.Add(_cRun);

            _lbStatus = Mk("", Fonts.F(8.5f), Pal.TextSub, ContentAlignment.MiddleLeft);
            _pgOpt.Controls.Add(_lbStatus);

            _lbHowto = Mk(
                L.T("装完怎么用？\r\n") +
                L.T("　① 双击桌面上的「Astra 创世神协议」\r\n") +
                L.T("　② 到【排序】页选一个排序（带「含 RJW」的就是成人向那套）\r\n") +
                L.T("　③ 回【主页】点最大的那颗【启动游戏】\r\n") +
                L.T("　④ 出问题就去【日志】页点【生成诊断报告】，把报告发给社区管理员\r\n") +
                L.T("它只动游戏的 Config 配置，不碰你的存档，每次替换前都会自动备份。"),
                Fonts.F(8.5f), Pal.TextSub, ContentAlignment.TopLeft);
            _pgOpt.Controls.Add(_lbHowto);

            _btnManual = MkB(L.T("查看完整操作说明"), 200, 28);
            _btnManual.BorderColor = Color.FromArgb(196, 212, 230);
            _btnManual.TextColor = Pal.TextMain;
            _btnManual.Click += delegate(object s, EventArgs e) { ShowManual(); };
            _pgOpt.Controls.Add(_btnManual);

            _btnInstall = MkB(L.T("开始安装"), 540, 56);
            _btnInstall.SetFont(Fonts.B(13f));
            _btnInstall.SubText = L.T("只复制几个文件，几秒钟就好");
            _btnInstall.SetSubFont(Fonts.F(8.5f));
            _btnInstall.Click += delegate(object s, EventArgs e) { StartInstall(); };
            _pgOpt.Controls.Add(_btnInstall);

            _lbFoot = Mk(
                (Edition.Sponsor ? L.T("❤ 赞助版 · 感谢你的支持\r\n") : L.T("普通版\r\n")) + L.T("装到整合包目录：启动器直接认包，开箱即用（推荐）。\r\n") +
                L.T("只装到用户目录：只装启动器本身，之后用【设置】页的绑定功能指到整合包。\r\n") +
                L.T("祝你玩得开心～喵~ (=^･ω･^=)"),
                Fonts.F(8f), Pal.TextFaint, ContentAlignment.TopLeft);
            _pgOpt.Controls.Add(_lbFoot);

            _tDir.Text = DefaultDir();
        }

        private void ShowPage(int which)
        {
            _pgIntro.Visible = (which == 0);
            _pgOpt.Visible = (which == 1);
        }

        public static string DefaultDir()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, L.T("ZOGOS创世神启动器"));
        }

        private void Browse()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = L.T("选择安装位置（一般选整合包文件夹，或者随便找一个空文件夹）");
                try { if (Directory.Exists(_tDir.Text.Trim())) d.SelectedPath = _tDir.Text.Trim(); } catch { }
                if (d.ShowDialog(this) == DialogResult.OK) _tDir.Text = d.SelectedPath;
            }
        }

        private void UpdatePackUi()
        {
            if (_packRoot.Length > 0)
            {
                _btnToPack.Enabled = true;
                _lbHint.Text = L.T("已检测到整合包：") + _packRoot + L.T("　（点左边那颗按钮就装到这里）");
                _tDir.Text = _packRoot;
                if (!PackLocator.HasRimSort(_packRoot)) _cRimSort.Checked = true;
                else { _cRimSort.Checked = false; _cRimSort.Text = L.T("一并安装 RimSort（整合包里已有，跳过）"); }
            }
            else
            {
                _btnToPack.Enabled = false;
                _lbHint.Text = L.T("没检测到整合包目录，默认装到用户目录；也可以点【浏览…】自己选。");
            }
        }

        private void LayoutAll()
        {
            _bar.Bounds = new Rectangle(0, 0, ClientSize.Width, 46);
            _body.Bounds = new Rectangle(0, 46, ClientSize.Width, ClientSize.Height - 46);
            _card.Bounds = new Rectangle(16, 16, _body.Width - 32, _body.Height - 32);

            int m = 24;
            int cw = _card.ClientSize.Width;
            int ch = _card.ClientSize.Height;
            Rectangle full = new Rectangle(0, 0, cw, ch);
            _pgIntro.Bounds = full;
            _pgOpt.Bounds = full;

            // 引导页
            _lbIntroTitle.Bounds = new Rectangle(m, 16, cw - m * 2, 28);
            _intro.Bounds = new Rectangle(m, 56, cw - m * 2, 190);
            _lbIntroText.Bounds = new Rectangle(m, 258, cw - m * 2, 172);
            _btnNext.Bounds = new Rectangle(m, ch - 24 - 52, cw - m * 2, 52);

            // 选项页
            _lbOptTitle.Bounds = new Rectangle(m, 16, cw - m * 2, 28);
            _lbDir.Bounds = new Rectangle(m, 58, 80, 24);
            _tDir.Bounds = new Rectangle(m + 84, 56, cw - m * 2 - 84 - 94, 24);
            _btnBrowse.Bounds = new Rectangle(cw - m - 86, 55, 86, 26);
            _btnToPack.Bounds = new Rectangle(m, 92, 250, 32);
            _btnToUser.Bounds = new Rectangle(m + 260, 92, 180, 32);
            _lbHint.Bounds = new Rectangle(m, 128, cw - m * 2, 20);
            _cDesktop.Bounds = new Rectangle(m, 156, 270, 24);
            _cStart.Bounds = new Rectangle(m + 280, 156, 270, 24);
            _cRimSort.Bounds = new Rectangle(m, 184, 270, 24);
            _cRun.Bounds = new Rectangle(m + 280, 184, 270, 24);
            _lbStatus.Bounds = new Rectangle(m, 214, cw - m * 2, 20);
            _lbHowto.Bounds = new Rectangle(m, 240, cw - m * 2, 104);
            _btnManual.Bounds = new Rectangle(m, 350, 200, 28);
            _btnInstall.Bounds = new Rectangle(m, 388, cw - m * 2, 56);
            _lbFoot.Bounds = new Rectangle(m, 450, cw - m * 2, 56);
        }

        private void SetStatus(string s)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke((MethodInvoker)delegate { _lbStatus.Text = s; });
            }
            catch { }
        }

        private void ShowManual()
        {
            string text = "";
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(PayloadReadme))
                {
                    if (s != null)
                    {
                        using (StreamReader sr = new StreamReader(s, Encoding.UTF8)) text = sr.ReadToEnd();
                    }
                }
            }
            catch { }
            if (text.Length == 0) text = L.T("（安装包里没有找到操作说明）");
            ManualForm mf = new ManualForm(text);
            mf.ShowDialog(this);
        }

        private void StartInstall()
        {
            if (_busy) return;
            string dir = _tDir.Text.Trim();
            if (dir.Length == 0)
            {
                MinMsg.Show(this, L.T("请先选择安装位置。"), L.T("提示"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _busy = true;
            _btnInstall.Enabled = false;
            _btnNext.Enabled = false;
            SetStatus(L.T("正在安装…"));

            bool copyRS = _cRimSort.Checked;
            string packRoot = _packRoot;
            ThreadPool.QueueUserWorkItem(delegate(object o)
            {
                string err = "";
                bool ok = false;
                try { ok = InstallFiles(dir, copyRS, packRoot, out err); }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke((MethodInvoker)delegate { OnInstalled(ok, dir, err); });
                }
                catch { }
            });
        }

        private bool InstallFiles(string dir, bool copyRimSort, string packRoot, out string err)
        {
            err = "";
            try
            {
                SetStatus(L.T("正在创建目录…"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".zogos_write_test");
                File.WriteAllText(probe, "t");
                File.Delete(probe);

                SetStatus(L.T("正在释放启动器…"));
                if (!SetupPayload.Extract(PayloadExe, Path.Combine(dir, L.T("Astra创世神协议.exe")), out err)) return false;
                string e2;
                SetStatus(L.T("正在释放操作说明…"));
                SetupPayload.Extract(PayloadReadme, Path.Combine(dir, L.T("ZOGOS使用说明.txt")), out e2);
                SetupPayload.Extract(PayloadCli, Path.Combine(dir, "zogos-cli.exe"), out e2);
                try
                {
                    string toolsDir = Path.Combine(dir, "tools");
                    if (!Directory.Exists(toolsDir)) Directory.CreateDirectory(toolsDir);
                    string e3;
                    SetupPayload.Extract(PayloadFFmpeg, Path.Combine(toolsDir, "ffmpeg.exe"), out e3);
                }
                catch { }

                bool strict; List<string> notes; string pid;
                int sc = PackDetector.Score(dir, out strict, out notes, out pid);
                if (strict)
                {
                    string mErr;
                    if (PackMarker.Write(dir, pid, out mErr)) SetStatus(L.T("已写入识别令牌。"));
                }

                if (copyRimSort && !PackLocator.HasRimSort(dir))
                {
                    string src = PackLocator.FindRimSortSource(dir, packRoot);
                    if (src.Length > 0)
                    {
                        SetStatus(L.T("正在复制 RimSort（比较大，请稍等一下）…"));
                        string cErr;
                        SetupPayload.CopyTree(src, dir, delegate(string msg) { SetStatus(msg); }, out cErr);
                        if (cErr != null) SetStatus(L.T("RimSort 复制时有问题：") + cErr);
                    }
                    else SetStatus(L.T("没找到 RimSort 的来源，已跳过（不影响启动器使用）。"));
                }
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        private void OnInstalled(bool ok, string dir, string err)
        {
            _busy = false;
            _btnInstall.Enabled = true;
            _btnNext.Enabled = true;
            if (!ok)
            {
                _lbStatus.Text = L.T("安装失败：") + err;
                MinMsg.Show(this, L.T("安装失败：\r\n\r\n") + err
                    + L.T("\r\n\r\n如果是权限问题，换一个位置（比如桌面或 D 盘）再试一次。\r\n还是不行的话，把这段错误发给社区管理员～喵~ (´･ω･`)"),
                    L.T("安装失败"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string exe = Path.Combine(dir, L.T("Astra创世神协议.exe"));
            int made = 0;
            if (_cDesktop.Checked)
            {
                string dp = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (CreateShortcut(Path.Combine(dp, L.T("Astra创世神协议.lnk")), exe, dir)) made++;
            }
            if (_cStart.Checked)
            {
                string sp = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                if (CreateShortcut(Path.Combine(sp, L.T("Astra创世神协议.lnk")), exe, dir)) made++;
            }

            _lbStatus.Text = L.T("安装完成：") + dir;
            MinMsg.Show(this, (Edition.Sponsor ? L.T("安装完成啦～谢谢你支持！❤  喵~ (๑•̀ㅂ•́)و✧\r\n\r\n安装位置：\r\n") : L.T("安装完成啦～喵~  (๑•̀ㅂ•́)و✧\r\n\r\n安装位置：\r\n")) + dir
                + "\r\n\r\n" + (made > 0 ? (L.T("已经帮你建好 ") + made + L.T(" 个快捷方式。\r\n")) : "")
                + L.T("\r\n双击桌面上的「Astra 创世神协议」就能用啦～\r\n祝你在边缘世界玩得开心！ (=^･ω･^=)"),
                L.T("安装完成"), MessageBoxButtons.OK, MessageBoxIcon.Information);

            if (_cRun.Checked)
            {
                try
                {
                    // 安装程序是管理员权限，直接启动会让启动器也变管理员，
                    // 之后点社区/赞助会被浏览器拦一下；交给 explorer 让它以普通权限跑。
                    ShellOpen.LaunchNormal(exe);
                }
                catch { }
            }
            Close();
        }

        public static bool CreateShortcut(string lnk, string target, string workDir)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return false;
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { L.T("Astra 创世神协议") });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, new object[] { });
                Marshal.ReleaseComObject(sc);
                Marshal.ReleaseComObject(shell);
                return true;
            }
            catch { return false; }
        }
    }
}

namespace Zogos
{
    /// <summary>安装包内部资源：释放与复制</summary>
    public static class SetupPayload
    {
        public static bool Extract(string resName, string dest, out string err)
        {
            err = null;
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(resName))
                {
                    if (s == null) { err = L.T("安装包内部缺少资源：") + resName; return false; }
                    using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buf = new byte[131072];
                        int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
                    }
                }
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        public static int CopyTree(string srcDir, string dstDir, Action<string> status, out string err)
        {
            err = null;
            int n = 0;
            try
            {
                if (!Directory.Exists(dstDir)) Directory.CreateDirectory(dstDir);
                string[] files = Directory.GetFiles(srcDir);
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileName(files[i]);
                    if (status != null && (i % 20 == 0)) status(L.T("正在复制 RimSort 相关文件…（") + n + L.T(" 个）"));
                    File.Copy(files[i], Path.Combine(dstDir, name), true);
                    n++;
                }
                string[] dirs = Directory.GetDirectories(srcDir);
                for (int i = 0; i < dirs.Length; i++)
                {
                    string sub;
                    int got = CopyTree(dirs[i], Path.Combine(dstDir, Path.GetFileName(dirs[i])), status, out sub);
                    if (sub != null) err = sub;
                    n += got;
                }
            }
            catch (Exception ex) { err = ex.Message; }
            return n;
        }
    }

    /// <summary>找整合包 / 找 RimSort</summary>
    public static class PackLocator
    {
        public static string Here()
        {
            try { return Path.GetDirectoryName(Application.ExecutablePath); } catch { return ""; }
        }

        public static string Detect()
        {
            List<string> cands = new List<string>();
            string here = Here();
            if (!string.IsNullOrEmpty(here))
            {
                cands.Add(here);
                try
                {
                    string p1 = Path.GetDirectoryName(here.TrimEnd('\\'));
                    if (!string.IsNullOrEmpty(p1))
                    {
                        cands.Add(p1);
                        foreach (string d in Directory.GetDirectories(p1)) cands.Add(d);
                    }
                }
                catch { }
            }
            try
            {
                foreach (DriveInfo dr in DriveInfo.GetDrives())
                {
                    if (!dr.IsReady) continue;
                    string r = dr.RootDirectory.FullName;
                    string[] subs = new string[] { "steamapps\\common", "Steam\\steamapps\\common", "STeam\\steamapps\\common", "SteamLibrary\\steamapps\\common", "Games\\steamapps\\common" };
                    for (int i = 0; i < subs.Length; i++)
                    {
                        string sc = Path.Combine(r, subs[i]);
                        if (!Directory.Exists(sc)) continue;
                        cands.Add(sc);
                        try { foreach (string d in Directory.GetDirectories(sc)) cands.Add(d); } catch { }
                    }
                }
            }
            catch { }

            string best = null; int bestScore = 0;
            for (int i = 0; i < cands.Count; i++)
            {
                try
                {
                    bool strict; List<string> notes; string pid;
                    int sc = PackDetector.Score(cands[i], out strict, out notes, out pid);
                    if (strict && sc > bestScore) { bestScore = sc; best = cands[i]; }
                }
                catch { }
            }
            return best == null ? "" : best;
        }

        public static string FindRimSortExe(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return "";
                string[] f = Directory.GetFiles(dir, "*rimsort*.exe");
                return (f != null && f.Length > 0) ? f[0] : "";
            }
            catch { return ""; }
        }

        public static bool HasRimSort(string dir)
        {
            return FindRimSortExe(dir).Length > 0;
        }

        /// <summary>找 RimSort 的来源目录（安装包旁边 / RimSort工具 子目录 / 已检测到的整合包）</summary>
        public static string FindRimSortSource(string target, string packRoot)
        {
            List<string> cands = new List<string>();
            string here = Here();
            if (!string.IsNullOrEmpty(here))
            {
                cands.Add(here);
                cands.Add(Path.Combine(here, L.T("RimSort工具")));
                cands.Add(Path.Combine(here, "rimsort"));
                try
                {
                    string p1 = Path.GetDirectoryName(here.TrimEnd('\\'));
                    if (!string.IsNullOrEmpty(p1)) cands.Add(p1);
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(packRoot)) cands.Add(packRoot);
            try
            {
                if (!string.IsNullOrEmpty(target))
                {
                    string tp = Path.GetDirectoryName(target.TrimEnd('\\'));
                    if (!string.IsNullOrEmpty(tp)) cands.Add(tp);
                }
            }
            catch { }

            for (int i = 0; i < cands.Count; i++)
            {
                string d = cands[i];
                if (string.IsNullOrEmpty(d)) continue;
                if (string.Equals(d, target, StringComparison.OrdinalIgnoreCase)) continue;
                if (HasRimSort(d)) return d;
            }
            return "";
        }
    }

    /// <summary>操作说明窗口</summary>
    public class ManualForm : Form
    {
        public ManualForm(string text)
        {
            Text = L.T("操作说明 - Astra 创世神协议");
            ClientSize = new Size(700, 580);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(420, 320);
            Font = Fonts.F(9f);
            BackColor = Pal.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            TextBox t = new TextBox();
            t.Multiline = true;
            t.ReadOnly = true;
            t.ScrollBars = ScrollBars.Both;
            t.WordWrap = true;
            t.BorderStyle = BorderStyle.None;
            t.Font = Fonts.F(9f);
            t.BackColor = Pal.White;
            t.ForeColor = Pal.TextMain;
            t.Dock = DockStyle.Fill;
            t.Text = text;
            Controls.Add(t);
        }
    }
}

namespace Zogos
{
    public static class SetupMain
    {
        [STAThread]
        public static void Main(string[] args)
        {
            string dir = null;
            bool silent = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--dir" && i + 1 < args.Length) dir = args[++i];
                else if (args[i] == "--selftest" && i + 1 < args.Length) { SelfTest(args[++i]); return; }
                else if (args[i] == "--silent") silent = true;
            }
            if (silent)
            {
                Environment.Exit(SilentInstall(dir == null ? SetupForm.DefaultDir() : dir));
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }

        public static void SelfTest(string dir)
        {
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            SetupForm f = new SetupForm();
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(0, 0);
            f.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(1200);
            Application.DoEvents();
            MethodInfo mi = typeof(SetupForm).GetMethod("ShowPage", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int p = 0; p < 2; p++)
            {
                try
                {
                    if (mi != null) mi.Invoke(f, new object[] { p });
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(600);
                    Application.DoEvents();
                    using (Bitmap b = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
                        b.Save(Path.Combine(dir, "setup_page" + p + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                catch (Exception ex) { File.AppendAllText(Path.Combine(dir, "trace.txt"), "page " + p + " : " + ex.Message + Environment.NewLine); }
            }
            // 再渲染一次引导页，用于验证动画是否在动
            try
            {
                if (mi != null) mi.Invoke(f, new object[] { 0 });
                System.Threading.Thread.Sleep(2200);
                Application.DoEvents();
                using (Bitmap b = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
                    b.Save(Path.Combine(dir, "setup_page0_later.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { }
            try { f.Close(); } catch { }
        }

        private static int SilentInstall(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string err;
                if (!SetupPayload.Extract(SetupForm.PayloadExe, Path.Combine(dir, L.T("Astra创世神协议.exe")), out err)) return 1;
                SetupPayload.Extract(SetupForm.PayloadReadme, Path.Combine(dir, L.T("ZOGOS使用说明.txt")), out err);
                SetupPayload.Extract(SetupForm.PayloadCli, Path.Combine(dir, "zogos-cli.exe"), out err);
                try
                {
                    string toolsDir = Path.Combine(dir, "tools");
                    if (!Directory.Exists(toolsDir)) Directory.CreateDirectory(toolsDir);
                    string ee;
                    SetupPayload.Extract(SetupForm.PayloadFFmpeg, Path.Combine(toolsDir, "ffmpeg.exe"), out ee);
                }
                catch { }
                bool strict; List<string> notes; string pid;
                PackDetector.Score(dir, out strict, out notes, out pid);
                if (strict) { string me; PackMarker.Write(dir, pid, out me); }
                return 0;
            }
            catch { return 2; }
        }
    }
}
