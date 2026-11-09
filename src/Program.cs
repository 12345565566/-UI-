using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Zogos
{
    public static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static void Main(string[] args)
        {
            // 极简 UI 全部按像素字体/绝对坐标绘制：必须先声明 DPI 感知，
            // 否则 2K/4K 屏会被系统位图拉伸（模糊 + 截图错位）
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string launcherDir = Application.StartupPath;
            string appData = null, rootOverride = null, targetOverride = null, shotDir = null, smokeDir = null, susuDir = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--root" && i + 1 < args.Length) rootOverride = args[++i];
                else if (a == "--target" && i + 1 < args.Length) targetOverride = args[++i];
                else if (a == "--appdata" && i + 1 < args.Length) appData = args[++i];
                else if (a == "--shot" && i + 1 < args.Length) shotDir = args[++i];
                else if (a == "--susu" && i + 1 < args.Length) susuDir = args[++i];
                else if (a == "--smoke" && i + 1 < args.Length) smokeDir = args[++i];
            }
            if (appData == null) appData = AppDataDir();

            Settings st = Settings.Load(appData);
            try { Pal.ApplyTheme(Edition.Sponsor ? st.ThemeIndex : 0); } catch { }
            PackContext ctx = PackContext.Detect(launcherDir, st, rootOverride, targetOverride, appData);
            Log.Init(ctx.SettingsDir);
            try
            {
                if (!string.IsNullOrEmpty(ctx.Root)) { st.Root = ctx.Root; st.Save(); }
            }
            catch { }

            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Log.Error("界面线程未处理异常", e.Exception);
                MinMsg.Show("启动器遇到问题：" + e.Exception.Message + "\n\n日志：" + (Log.FilePath == null ? "(无)" : Log.FilePath),
                    "Astra 创世神协议", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                Log.Error("未处理异常", ex);
            };

            if (!File.Exists(ctx.GameExe) && string.IsNullOrEmpty(shotDir) && string.IsNullOrEmpty(smokeDir))
            {
                Log.Warn("启动器旁边没有找到游戏主程序，准备让玩家手动选择整合包目录");
                DialogResult dr = MinMsg.Show(
                    "没有在启动器所在目录找到 RimWorld 整合包。\n\n"
                    + "启动器所在目录：\n" + launcherDir + "\n\n"
                    + "请把本启动器（Astra创世神协议.exe）放到整合包文件夹里，\n"
                    + "也就是和 RimWorld、Config、排序文件 这几个文件夹放在一起。\n\n"
                    + "是否现在手动选择整合包文件夹？",
                    "Astra 创世神协议", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (dr == DialogResult.Yes)
                {
                    using (FolderBrowserDialog d = new FolderBrowserDialog())
                    {
                        d.Description = "选择整合包文件夹（里面要有 RimWorld 文件夹）";
                        if (d.ShowDialog() == DialogResult.OK)
                        {
                            rootOverride = d.SelectedPath;
                            p_rootOverride = rootOverride;
                            ctx = PackContext.Detect(launcherDir, st, rootOverride, targetOverride, appData);
                            Log.Info("玩家手动选择整合包目录: " + ctx.Root);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(susuDir))
            {
                SusuShots(susuDir);
                return;
            }

            if (!string.IsNullOrEmpty(smokeDir))
            {
                SmokeTest(ctx, st, smokeDir);
                return;
            }

            if (!string.IsNullOrEmpty(shotDir))
            {
                ShotPages(ctx, st, shotDir);
                return;
            }

            MainForm form = new MainForm(ctx, st);
            Application.Run(form);
        }

        public static void Tr(string dir, string msg)
        {
            try { File.AppendAllText(Path.Combine(dir, "trace.txt"), DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine, new UTF8Encoding(true)); }
            catch { }
        }

        private static string p_rootOverride;

        public static string AppDataDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZOGOS创世神启动器");
        }

        /// <summary>无窗口自检：构建界面、切换每个页面、导出控件坐标，用于自动验证布局。</summary>
        /// <summary>把苏苏窗口在几个对话节点上渲染成 PNG，用于自动检查排版与换图。</summary>
        public static void SusuShots(string dir)
        {
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Tr(dir, "SusuShots 开始");
            SusuWindow w = null;
            try { w = new SusuWindow(); Tr(dir, "构造完成 " + w.Width + "x" + w.Height); }
            catch (Exception ex) { Tr(dir, "构造失败: " + ex.GetType().Name + " :: " + ex.Message); return; }
            // 不创建窗口，直接画到位图（避免任何窗口过程层面的不确定因素）
            string[] nods = new string[] { "hub", "diag", "vram", "toc", "s0", "end" };
            for (int i = 0; i < nods.Length; i++)
            {
                try
                {
                    Tr(dir, "节点 " + nods[i] + " 开始");
                    w.StartAt(nods[i]);
                    Tr(dir, "节点 " + nods[i] + " StartAt 完成");
                    w.FinishTypingNow();
                    using (Bitmap b = new Bitmap(w.Width, w.Height))
                    {
                        using (Graphics g = Graphics.FromImage(b)) w.RenderTo(g);
                        b.Save(Path.Combine(dir, "susu_" + nods[i] + ".png"), ImageFormat.Png);
                        Tr(dir, "节点 " + nods[i] + " 已保存");
                    }
                }
                catch (Exception ex) { Tr(dir, "节点 " + nods[i] + " 失败: " + ex.Message); }
            }
            try { w.Dispose(); } catch { }
        }

        public static void SmokeTest(PackContext ctx, Settings st, string dir)
        {
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch (Exception ex) { Tr(dir, "无法创建目录: " + ex.Message); return; }

            Tr(dir, "SmokeTest 开始");
            MainForm form = new MainForm(ctx, st);
            Tr(dir, "MainForm 构造完成");
            form.CreateControl();
            Application.DoEvents();
            Tr(dir, "句柄创建完成");
            try { Image _lg = AppLogo.Get(); Tr(dir, "界面图标 = " + (_lg == null ? "未加载(会用渐变+创字)" : (_lg.Width + "x" + _lg.Height))); } catch { }

            try
            {
                MethodInfo onLoaded = typeof(MainForm).GetMethod("OnLoaded", BindingFlags.NonPublic | BindingFlags.Instance);
                if (onLoaded != null) { onLoaded.Invoke(form, null); Tr(dir, "OnLoaded 已执行"); }
                Application.DoEvents();
                System.Threading.Thread.Sleep(1500);
                Application.DoEvents();
                Tr(dir, "等待后台索引完成");
            }
            catch (Exception ex) { Tr(dir, "OnLoaded 异常: " + ex.GetType().Name + " :: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message)); }

            MethodInfo doLayout = typeof(MainForm).GetMethod("DoLayout", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo switchPage = typeof(MainForm).GetMethod("SwitchPage", BindingFlags.NonPublic | BindingFlags.Instance);
            StringBuilder all = new StringBuilder();

            int pageCount = 5; // 极简版固定 5 页（外观页已移除，深浅色切换在顶栏）
            for (int p = 0; p < pageCount; p++)
            {
                try
                {
                    if (switchPage != null) switchPage.Invoke(form, new object[] { p });
                    if (doLayout != null) doLayout.Invoke(form, null);
                    Application.DoEvents();
                    all.AppendLine("############ 页面 " + p + " ############");
                    StringBuilder sb = new StringBuilder();
                    Action<Control, int> dump = null;
                    dump = delegate(Control c, int d)
                    {
                        if (c == null) return;
                        sb.Append(new string(' ', d * 2));
                        sb.Append(c.GetType().Name).Append("  ").Append(c.Bounds.ToString());
                        sb.Append(c.Visible ? "  [可见]" : "  [隐藏]");
                        if (c.Text != null && c.Text.Length > 0)
                            sb.Append("  ").Append(c.Text.Replace("\r", " ").Replace("\n", " "));
                        sb.AppendLine();
                        for (int i = 0; i < c.Controls.Count; i++) dump(c.Controls[i], d + 1);
                    };
                    dump(form, 0);

                    StringBuilder ov = new StringBuilder();
                    FieldInfo sfld = typeof(Control).GetField("state", BindingFlags.NonPublic | BindingFlags.Instance);
                    Func<Control, bool> ownVis = delegate(Control c)
                    {
                        if (sfld == null) return c.Visible;
                        try { return (((int)sfld.GetValue(c)) & 2) != 0; } catch { return c.Visible; }
                    };
                    Action<Control> scan = null;
                    scan = delegate(Control parent)
                    {
                        for (int i = 0; i < parent.Controls.Count; i++)
                        {
                            Control ci = parent.Controls[i];
                            if (!ownVis(ci)) continue;
                            for (int j = i + 1; j < parent.Controls.Count; j++)
                            {
                                Control cj = parent.Controls[j];
                                if (!ownVis(cj)) continue;
                                Rectangle rr = Rectangle.Intersect(ci.Bounds, cj.Bounds);
                                if (rr.Width > 2 && rr.Height > 2)
                                    ov.AppendLine("    " + ci.GetType().Name + "[" + (ci.Text == null ? "" : ci.Text) + "] " + ci.Bounds + " <-> " + cj.GetType().Name + "[" + (cj.Text == null ? "" : cj.Text) + "] " + cj.Bounds);
                            }
                        }
                        for (int i = 0; i < parent.Controls.Count; i++) scan(parent.Controls[i]);
                    };
                    scan(form);
                    if (ov.Length > 0) { all.AppendLine("  !! 同级控件重叠（上层会把下层刷掉）："); all.Append(ov.ToString()); }
                    Tr(dir, "页面 " + p + " 重叠字符数=" + ov.Length);

                    try
                    {
                        FieldInfo frp = typeof(MainForm).GetField("_rightPanel", BindingFlags.NonPublic | BindingFlags.Instance);
                        Control rp = frp == null ? null : (frp.GetValue(form) as Control);
                        if (rp != null)
                        {
                            Control hit = rp.GetChildAtPoint(new Point(474 + 64, 12 + 15));
                            Tr(dir, "页面 " + p + " 赞助按钮处最上层 = " + (hit == null ? "(null)" : (hit.GetType().Name + " / " + hit.Text)));
                        }
                    }
                    catch (Exception ex) { Tr(dir, "z序探测失败: " + ex.Message); }
                    all.Append(sb.ToString());
                    Tr(dir, "页面 " + p + " 布局完成");
                }
                catch (Exception ex)
                {
                    Exception e2 = ex;
                    while (e2.InnerException != null) e2 = e2.InnerException;
                    all.AppendLine("页面 " + p + " 失败: " + e2.GetType().Name + " :: " + e2.Message);
                    all.AppendLine(e2.StackTrace);
                    Tr(dir, "页面 " + p + " 失败: " + e2.GetType().Name + " :: " + e2.Message);
                }
            }

            // ---- 热切换主题自检 ----
            try
            {
                MethodInfo pk = typeof(MainForm).GetMethod("PickTheme", BindingFlags.NonPublic | BindingFlags.Instance);
                if (pk != null)
                {
                    pk.Invoke(form, new object[] { 1 });
                    Application.DoEvents();
                    if (doLayout != null) doLayout.Invoke(form, null);
                    Application.DoEvents();
                    Tr(dir, "热切换主题成功: Accent=" + Pal.Accent.R + "," + Pal.Accent.G + "," + Pal.Accent.B
                        + "  ThemeIndex=" + Pal.ThemeIndex + "  子控件数=" + form.Controls.Count);
                    StringBuilder sb3 = new StringBuilder();
                    Action<Control, int> dump3 = null;
                    dump3 = delegate(Control c, int d)
                    {
                        if (c == null) return;
                        sb3.Append(new string((char)32, d * 2)).Append(c.GetType().Name).Append("  ").Append(c.Bounds.ToString()).AppendLine();
                        for (int i = 0; i < c.Controls.Count; i++) dump3(c.Controls[i], d + 1);
                    };
                    dump3(form, 0);
                    all.AppendLine("===== 热切换主题到「" + Pal.ThemeNames[1] + "」后 =====");
                    all.Append(sb3.ToString());
                }
            }
            catch (Exception ex)
            {
                Exception e3 = ex;
                while (e3.InnerException != null) e3 = e3.InnerException;
                Tr(dir, "热切换主题失败: " + e3.GetType().Name + " :: " + e3.Message);
            }

            File.WriteAllText(Path.Combine(dir, "layout_all.txt"), all.ToString(), new UTF8Encoding(true));
            Tr(dir, "layout_all.txt 已写出");
            try { form.Dispose(); } catch { }
            Tr(dir, "SmokeTest 结束");
        }

        /// <summary>把每个页面渲染成 PNG（用于自动化检查界面布局）。</summary>
        private static void ShotPages(PackContext ctx, Settings st, string dir)
        {
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch (Exception ex) { Console.WriteLine("无法创建目录: " + ex.Message); return; }

            MainForm form = new MainForm(ctx, st);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(0, 0);
            form.Show();
            Application.DoEvents();
            Thread.Sleep(500);
            Application.DoEvents();

            try
            {
                StringBuilder lay = new StringBuilder();
                Action<Control, int> dump = null;
                dump = delegate(Control c, int d)
                {
                    if (c == null) return;
                    lay.Append(new string(' ', d * 2));
                    lay.Append(c.GetType().Name).Append("  bounds=").Append(c.Bounds.ToString());
                    lay.Append("  visible=").Append(c.Visible);
                    if (c.Text != null && c.Text.Length > 0) lay.Append("  text=").Append(c.Text.Replace("\r", " ").Replace("\n", " "));
                    lay.AppendLine();
                    for (int i = 0; i < c.Controls.Count; i++) dump(c.Controls[i], d + 1);
                };
                dump(form, 0);
                File.WriteAllText(Path.Combine(dir, "layout.txt"), lay.ToString(), new UTF8Encoding(true));
                Console.WriteLine("layout dumped");
            }
            catch (Exception ex) { Console.WriteLine("dump 失败: " + ex.Message); }

            MethodInfo mi = typeof(MainForm).GetMethod("SwitchPage", BindingFlags.NonPublic | BindingFlags.Instance);
            string[] names = new string[] { "home", "sort", "settings", "logs", "share" };
            int pageCount = 5; // 极简版固定 5 页（外观页已移除，深浅色切换在顶栏）
            for (int p = 0; p < pageCount; p++)
            {
                Tr(dir, "page " + p + " start");
                try
                {
                    if (mi != null) mi.Invoke(form, new object[] { p });
                    Tr(dir, "page " + p + " switched");
                    Application.DoEvents();
                    Thread.Sleep(500);
                    Application.DoEvents();
                    Rectangle gr = form.Bounds;
                    using (Bitmap bmp = new Bitmap(gr.Width, gr.Height))
                    {
                        Tr(dir, "page " + p + " pumped");
                        // 分层窗口（WS_EX_LAYERED）不响应 WM_PRINT，DrawToBitmap 会出黑图，必须直接抓屏
                        using (Graphics rg = Graphics.FromImage(bmp)) rg.CopyFromScreen(gr.Location, Point.Empty, gr.Size);
                        Tr(dir, "page " + p + " drawn");
                        string f = Path.Combine(dir, "page_" + p + "_" + names[p] + ".png");
                        bmp.Save(f, ImageFormat.Png);
                        Tr(dir, "page " + p + " saved");
                        Console.WriteLine("saved " + f + "  " + bmp.Width + "x" + bmp.Height);
                    }
                }
                catch (Exception ex) { Tr(dir, "page " + p + " EX: " + ex.GetType().Name + " :: " + ex.Message); }
            }
            // 覆盖层自检：打开创意工坊模态面板，等动画播完再抓一张
            try
            {
                MethodInfo mw = typeof(MainForm).GetMethod("OpenWorkshop", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mw != null)
                {
                    mw.Invoke(form, null);
                    Application.DoEvents();
                    Thread.Sleep(600); // 覆盖层动画 9 帧×15ms，多等一点余量
                    Application.DoEvents();
                    Rectangle gr = form.Bounds;
                    using (Bitmap bmp = new Bitmap(gr.Width, gr.Height))
                    {
                        using (Graphics rg = Graphics.FromImage(bmp)) rg.CopyFromScreen(gr.Location, Point.Empty, gr.Size);
                        string f = Path.Combine(dir, "overlay_workshop.png");
                        bmp.Save(f, ImageFormat.Png);
                        Console.WriteLine("saved " + f + "  " + bmp.Width + "x" + bmp.Height);
                    }
                    // AI 接口配置覆盖层再抓一张
                    MethodInfo ma = typeof(MainForm).GetMethod("OpenAiSetup", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (ma != null)
                    {
                        ma.Invoke(form, new object[] { false });
                        Application.DoEvents();
                        Thread.Sleep(600);
                        Application.DoEvents();
                        using (Bitmap bmp2 = new Bitmap(gr.Width, gr.Height))
                        {
                            using (Graphics rg = Graphics.FromImage(bmp2)) rg.CopyFromScreen(gr.Location, Point.Empty, gr.Size);
                            string f2 = Path.Combine(dir, "overlay_ai.png");
                            bmp2.Save(f2, ImageFormat.Png);
                            Console.WriteLine("saved " + f2 + "  " + bmp2.Width + "x" + bmp2.Height);
                        }
                    }
                    // 整合包资源获取覆盖层再抓一张（在线拉取在后台跑，抓到的多半是加载中/空态）
                    MethodInfo mf = typeof(MainForm).GetMethod("OpenPackFeed", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mf != null)
                    {
                        mf.Invoke(form, null);
                        Application.DoEvents();
                        Thread.Sleep(600);
                        Application.DoEvents();
                        using (Bitmap bmp3 = new Bitmap(gr.Width, gr.Height))
                        {
                            using (Graphics rg = Graphics.FromImage(bmp3)) rg.CopyFromScreen(gr.Location, Point.Empty, gr.Size);
                            string f3 = Path.Combine(dir, "overlay_feed.png");
                            bmp3.Save(f3, ImageFormat.Png);
                            Console.WriteLine("saved " + f3 + "  " + bmp3.Width + "x" + bmp3.Height);
                        }
                    }
                    // 模组管理覆盖层：有选中方案时才有效；建图在后台跑，多等几秒抓带节点的画面
                    MethodInfo mg = typeof(MainForm).GetMethod("OpenModGraph", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (mg != null)
                    {
                        mg.Invoke(form, null);
                        Application.DoEvents();
                        for (int i = 0; i < 10; i++) { Thread.Sleep(400); Application.DoEvents(); } // 等建图后台完成
                        using (Bitmap bmp4 = new Bitmap(gr.Width, gr.Height))
                        {
                            using (Graphics rg = Graphics.FromImage(bmp4)) rg.CopyFromScreen(gr.Location, Point.Empty, gr.Size);
                            string f4 = Path.Combine(dir, "overlay_modgraph.png");
                            bmp4.Save(f4, ImageFormat.Png);
                            Console.WriteLine("saved " + f4 + "  " + bmp4.Width + "x" + bmp4.Height);
                        }
                    }
                }
                // 通用确认弹窗再抓一张（诊断报告那种是/否询问的自绘替代版）
                MethodInfo mc = null;
                foreach (var m in typeof(MainForm).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
                    if (m.Name == "ShowConfirm" && m.GetParameters().Length == 3) { mc = m; break; }
                if (mc != null)
                {
                    mc.Invoke(form, new object[] {
                        "诊断报告",
                        "诊断报告已生成：\n\nD:\\工作区A1\\真实环境\\rim\\ZOGOS备份\\ZOGOS诊断报告_2026-11-08_234800.txt\n\n把这个 txt 文件发给社区管理员即可。\n\n现在打开它所在的文件夹吗？",
                        (Action)delegate { } });
                    Application.DoEvents();
                    Thread.Sleep(600);
                    Application.DoEvents();
                    Rectangle grc = form.Bounds;
                    using (Bitmap bmp5 = new Bitmap(grc.Width, grc.Height))
                    {
                        using (Graphics rg = Graphics.FromImage(bmp5)) rg.CopyFromScreen(grc.Location, Point.Empty, grc.Size);
                        string f5 = Path.Combine(dir, "overlay_confirm.png");
                        bmp5.Save(f5, ImageFormat.Png);
                        Console.WriteLine("saved " + f5 + "  " + bmp5.Width + "x" + bmp5.Height);
                    }
                }
                // 切换整合包覆盖层再抓一张
                MethodInfo mp = typeof(MainForm).GetMethod("PsOpen", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mp != null)
                {
                    mp.Invoke(form, null);
                    Application.DoEvents();
                    Thread.Sleep(600);
                    Application.DoEvents();
                    Rectangle grp = form.Bounds;
                    using (Bitmap bmp6 = new Bitmap(grp.Width, grp.Height))
                    {
                        using (Graphics rg = Graphics.FromImage(bmp6)) rg.CopyFromScreen(grp.Location, Point.Empty, grp.Size);
                        string f6 = Path.Combine(dir, "overlay_packswitch.png");
                        bmp6.Save(f6, ImageFormat.Png);
                        Console.WriteLine("saved " + f6 + "  " + bmp6.Width + "x" + bmp6.Height);
                    }
                }
                // 苏苏 AI 覆盖层再抓一张：直接推进到「选项展开」状态（hub 8 个选项，验证不溢出气泡）
                MethodInfo ms = typeof(MainForm).GetMethod("SusuOpen", BindingFlags.NonPublic | BindingFlags.Instance);
                if (ms != null)
                {
                    ms.Invoke(form, new object[] { false });
                    Application.DoEvents();
                    for (int i = 0; i < 6; i++) { Thread.Sleep(400); Application.DoEvents(); }
                    MethodInfo sg = typeof(MainForm).GetMethod("SusuGotoNode", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (sg != null) { sg.Invoke(form, new object[] { "toc" }); Application.DoEvents(); Thread.Sleep(200); Application.DoEvents(); }
                    MethodInfo sc = typeof(MainForm).GetMethod("SusuShowChoices", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (sc != null) { sc.Invoke(form, null); Application.DoEvents(); Thread.Sleep(300); Application.DoEvents(); }
                    Rectangle grs = form.Bounds;
                    using (Bitmap bmp7 = new Bitmap(grs.Width, grs.Height))
                    {
                        using (Graphics rg = Graphics.FromImage(bmp7)) rg.CopyFromScreen(grs.Location, Point.Empty, grs.Size);
                        string f7 = Path.Combine(dir, "overlay_susu.png");
                        bmp7.Save(f7, ImageFormat.Png);
                        Console.WriteLine("saved " + f7 + "  " + bmp7.Width + "x" + bmp7.Height);
                    }
                }
            }
            catch (Exception ex) { Tr(dir, "overlay EX: " + ex.GetType().Name + " :: " + ex.Message); }

            // 极简弹窗自检：错误（红条）/ 警告（黄条）/ 询问（青条）各渲染一张
            try
            {
                MinDialog.PreviewShots(dir);
                Console.WriteLine("saved dlg_error / dlg_warn / dlg_info");
            }
            catch (Exception ex) { Tr(dir, "dlg shots EX: " + ex.GetType().Name + " :: " + ex.Message); }

            // 模组详情窗预览（用模组图第一个节点）
            try
            {
                ModGraphData gd = form.DebugGraphData;
                if (gd != null && gd.Nodes.Count > 0)
                {
                    using (ModDetailForm mf = new ModDetailForm(gd.Nodes[0], gd, st, null))
                        mf.SavePreview(Path.Combine(dir, "dlg_moddetail.png"));
                    Console.WriteLine("saved dlg_moddetail");
                }
                else Tr(dir, "moddetail shot 跳过：模组图数据为空");
            }
            catch (Exception ex) { Tr(dir, "moddetail shot EX: " + ex.GetType().Name + " :: " + ex.Message); }

            // 创意工坊同步窗预览
            try
            {
                using (WorkshopSyncForm wf = new WorkshopSyncForm(st, ctx))
                    wf.SavePreview(Path.Combine(dir, "dlg_wssync.png"));
                Console.WriteLine("saved dlg_wssync");
            }
            catch (Exception ex) { Tr(dir, "wssync shot EX: " + ex.GetType().Name + " :: " + ex.Message); }

            form.Close();
            form.Dispose();
        }
    }
}
