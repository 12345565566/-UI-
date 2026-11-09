using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Zogos
{
    // =====================================================================
    //  创意工坊同步 / 按排序补齐 —— 极简重构版（自绘分层窗口，原赛博风真控件版已废弃）
    //  功能与原窗口一致：扫描预览 / 执行同步 / 按排序补齐 / 连通性自检 / steamcmd / API Key / 导入下载。
    //  交互：标题区拖动 / Esc 或 × 关闭 / Tab 在输入框间切换 / Ctrl+V 粘贴 / 报告区可选中 Ctrl+C 复制。
    // =====================================================================
    public class WorkshopSyncForm : LayeredForm
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        readonly Settings _st;
        readonly PackContext _ctx;
        readonly List<El> _els = new List<El>();

        ElText _status;
        ElLogView _report;
        ElInput _apiKey;
        ElInputMulti _import;
        ElCheck _chkReplace;
        ElBtn _btnScan, _btnApply, _btnFill, _btnDiag, _btnSteamCmd, _btnOpenMods, _btnImport, _btnSaveKey, _btnGetKey, _btnBundleCmd;

        BackgroundWorker _worker;
        List<SyncAction> _plan;
        bool _busy;

        string _modsDir = "";
        string _wspDir = "";
        string _modsConfig = "";
        string[] _dirLines = new string[0];

        Rectangle _xRect;
        bool _xHover;
        El _hover, _capture;
        object _focus; // ElInput 或 ElInputMulti

        const int W = 860, H = 620;

        protected override float ContentScale { get { return LayeredForm.UiScale; } }

        public WorkshopSyncForm(Settings st, PackContext ctx)
        {
            _st = st;
            _ctx = ctx;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            SetLogicalSize(W, H);
            BuildUi();
            DetectPaths();
            _status.Text = L.T("先点「扫描预览」看会发生什么，再点「执行同步」。");
            Load += delegate(object s, EventArgs e) { CenterOnOwner(); };
        }

        T AddEl<T>(T el) where T : El { el.Host = this; _els.Add(el); return el; }

        void BuildUi()
        {
            _xRect = new Rectangle(W - 42, 12, 26, 26);

            _apiKey = AddEl(new ElInput());
            _apiKey.SetBounds(126, 106, 240, 26);
            _btnSaveKey = AddEl(new ElBtn(L.T("保存")));
            _btnSaveKey.SetBounds(374, 106, 64, 26);
            _btnSaveKey.Clicked += delegate { SaveApiKey(); };
            _btnGetKey = AddEl(new ElBtn(L.T("获取 Key")));
            _btnGetKey.SetBounds(446, 106, 86, 26);
            _btnGetKey.Clicked += delegate { GetApiKey(); };
            _btnBundleCmd = AddEl(new ElBtn(L.T("内置 steamcmd")));
            _btnBundleCmd.SetBounds(540, 106, 120, 26);
            _btnBundleCmd.Clicked += delegate { BundleSteamCmd(); };
            _chkReplace = AddEl(new ElCheck(L.T("本地已有也覆盖（谨慎）"), false));
            _chkReplace.SetBounds(676, 106, 168, 26);

            // 动作按钮行：宽度按文字量
            int bx = 22;
            _btnScan = MkAction(L.T("① 扫描预览"), ref bx, delegate { DoScan(); });
            _btnApply = MkAction(L.T("② 执行同步"), ref bx, delegate { DoApply(); });
            _btnFill = MkAction(L.T("③ 按排序补齐缺失"), ref bx, delegate { DoFill(); });
            _btnDiag = MkAction(L.T("连通性自检"), ref bx, delegate { DoDiag(); });
            _btnSteamCmd = MkAction(L.T("设置 steamcmd"), ref bx, delegate { PickSteamCmd(); });
            _btnOpenMods = MkAction(L.T("打开 Mods"), ref bx, delegate { FileOps.OpenInExplorer(_modsDir); });

            _import = AddEl(new ElInputMulti());
            _import.Hint = L.T("粘贴工坊 ID 或链接，可多行，也支持合集链接…");
            _import.SetBounds(22, 210, 678, 60);
            _btnImport = AddEl(new ElBtn(L.T("导入并下载")));
            _btnImport.AccentEdge = true;
            _btnImport.SetBounds(708, 210, 130, 60);
            _btnImport.Clicked += delegate { DoImport(); };

            ElCard reportWrap = AddEl(new ElCard()); // 报告区底板（否则空荡荡看不出边界）
            reportWrap.SetBounds(22, 282, W - 44, H - 66 - 282);
            _report = AddEl(new ElLogView());
            _report.MaxLines = 5000; // 同步预览可能上千行
            _report.SetBounds(28, 288, W - 44 - 12, H - 66 - 282 - 12);

            _status = AddEl(new ElText(""));
            _status.Ellipsis = true;
            _status.FontOf = () => T.F(12);
            _status.ColorOf = () => T.Text2;
            _status.SetBounds(22, H - 56, W - 160, 22);

            ElBtn close = AddEl(new ElBtn(L.T("关闭")));
            close.SetBounds(W - 112, H - 62, 90, 32);
            close.Clicked += delegate { Close(); };
        }

        ElBtn MkAction(string text, ref int bx, Action act)
        {
            int w = TextRenderer.MeasureText(text, T.F(12)).Width + 26;
            ElBtn b = AddEl(new ElBtn(text));
            b.SetBounds(bx, 146, w, 30);
            b.Clicked += delegate { act(); };
            bx += w + 8;
            return b;
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

        // ---------------------------------------------------------------
        //  鼠标分发 / 键盘焦点（小型 MainForm 调度）
        // ---------------------------------------------------------------
        El ElAt(Point p)
        {
            for (int i = _els.Count - 1; i >= 0; i--)
                if (_els[i].Visible && _els[i].HitTest(p)) return _els[i];
            return null;
        }

        void SetFocus(object inp)
        {
            if (_focus == inp) return;
            if (_focus is ElInput) ((ElInput)_focus).SetFocus(false);
            if (_focus is ElInputMulti) ((ElInputMulti)_focus).SetFocus(false);
            _focus = inp;
            if (_focus is ElInput) ((ElInput)_focus).SetFocus(true);
            if (_focus is ElInputMulti) ((ElInputMulti)_focus).SetFocus(true);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_busy) return;
            Point p = ToLogical(e.Location);
            El t = _capture ?? ElAt(p);
            if (t != _hover) { if (_hover != null) _hover.OnMouseLeave(); _hover = t; }
            if (t != null) t.OnMouseMove(p);
            bool xh = _xRect.Contains(p);
            if (xh != _xHover) { _xHover = xh; Render(); }
            Cursor = xh ? Cursors.Hand : (t != null ? t.CursorOf(p) : Cursors.Default);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_busy) return;
            Point p = ToLogical(e.Location);
            El t = ElAt(p);
            if (t == _apiKey) SetFocus(_apiKey);
            else if (t == _import) SetFocus(_import);
            else SetFocus(null);
            if (t != null) { _capture = t; t.OnMouseDown(p); return; }
            if (p.Y < 46 && !_xRect.Contains(p)) // 标题区拖动
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_busy) return;
            Point p = ToLogical(e.Location);
            if (_xRect.Contains(p)) { Close(); return; }
            El t = _capture ?? ElAt(p);
            _capture = null;
            if (t != null) t.OnMouseUp(p);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_busy) return;
            Point p = ToLogical(e.Location);
            El t = ElAt(p);
            if (t != null) t.OnMouseWheel(e.Delta);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (_focus is ElInput) { ((ElInput)_focus).OnChar(e.KeyChar); e.Handled = true; }
            else if (_focus is ElInputMulti) { ((ElInputMulti)_focus).OnChar(e.KeyChar); e.Handled = true; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys k)
        {
            if (k == Keys.Escape)
            {
                if (_focus != null) { SetFocus(null); return true; }
                Close(); return true;
            }
            if ((k & (Keys.Control | Keys.V)) == (Keys.Control | Keys.V))
            {
                string txt = null;
                try { txt = Clipboard.GetText(); } catch { }
                if (txt != null)
                {
                    if (_focus is ElInput) { ((ElInput)_focus).InsertText(txt); return true; }
                    if (_focus is ElInputMulti) { ((ElInputMulti)_focus).InsertText(txt); return true; }
                }
            }
            if ((k & (Keys.Control | Keys.C)) == (Keys.Control | Keys.C) && _focus == null)
            {
                if (ElLogView.CopyActiveSelection()) return true;
            }
            if ((k & (Keys.Control | Keys.A)) == (Keys.Control | Keys.A) && _focus == null)
            {
                if (ElLogView.SelectAllActive()) return true;
            }
            if (k == Keys.Tab)
            {
                SetFocus(_focus == null || _focus == _import ? (object)_apiKey : _import);
                return true;
            }
            Keys plain = k & ~(Keys.Shift | Keys.Control | Keys.Alt);
            if (_focus is ElInput && ((ElInput)_focus).OnKey(plain)) return true;
            if (_focus is ElInputMulti && ((ElInputMulti)_focus).OnKey(plain)) return true;
            if (k == Keys.Enter && _focus == null) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, k);
        }

        // ---------------------------------------------------------------
        //  绘制
        // ---------------------------------------------------------------
        protected override void PaintContent(Graphics g)
        {
            float w = Lw, h = Lh;
            T.FillRound(g, new RectangleF(0, 0, w, h), 12, T.Window);
            T.DrawRound(g, new RectangleF(0.5f, 0.5f, w - 1, h - 1), 12, T.Line, 1f);
            using (var path = T.Round(new RectangleF(0, 0, w, h), 12))
            {
                g.SetClip(path);
                using (var b = new SolidBrush(T.Accent)) g.FillRectangle(b, 0, 0, w, 5);
                g.ResetClip();
            }
            var left = new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
            using (var b = new SolidBrush(T.Text))
                g.DrawString(L.T("创意工坊同步 · 按排序补齐"), T.B(15), b, new RectangleF(22, 14, w - 80, 28), left);
            using (var b = new SolidBrush(_xHover ? T.Text : T.Text3))
                g.DrawString("×", T.B(16), b, _xRect, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            // 路径三行
            using (var b = new SolidBrush(T.Text2))
            {
                var f = T.F(11);
                for (int i = 0; i < _dirLines.Length; i++)
                    g.DrawString(_dirLines[i], f, b, new RectangleF(22, 46 + i * 17, w - 44, 16), left);
            }
            using (var b = new SolidBrush(T.Text2))
            {
                g.DrawString("Steam API Key", T.F(12), b, new RectangleF(22, 106, 100, 26), left);
                g.DrawString(L.T("工坊 ID / 链接（可粘贴多行，也支持合集链接）"), T.F(12), b, new RectangleF(22, 188, 560, 18), left);
            }
            foreach (var el in _els) if (el.Visible) el.Paint(g);
            // 忙碌遮罩
            if (_busy)
            {
                using (var b = new SolidBrush(Color.FromArgb(170, T.Window)))
                    g.FillRectangle(b, new RectangleF(0, 46, w, h - 46));
                string t1 = L.T("正在加载中，请稍候…");
                var f1 = T.B(15);
                var s1 = TextRenderer.MeasureText(t1, f1);
                using (var b = new SolidBrush(T.Text))
                    g.DrawString(t1, f1, b, (w - s1.Width) / 2, (h - s1.Height) / 2);
            }
        }

        // ---------------------------------------------------------------
        //  路径探测
        // ---------------------------------------------------------------
        void DetectPaths()
        {
            if (_ctx != null) _modsDir = _ctx.ModsDir;
            if (string.IsNullOrEmpty(_modsDir)) _modsDir = "";

            _wspDir = WorkshopSync.FindWorkshopContentDir();
            if (string.IsNullOrEmpty(_wspDir)) _wspDir = "";

            _modsConfig = ResolveModsConfig();
            ModIdIndex.Load(IndexPath());
            if (_st != null) _apiKey.Text = _st.SteamApiKey == null ? "" : _st.SteamApiKey;
            RefreshDirLabel();
        }

        string IndexPath()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "workshop_index.txt");
            }
            catch { return ""; }
        }

        string ResolveModsConfig()
        {
            List<string> cands = new List<string>();
            try
            {
                if (_ctx != null)
                {
                    if (!string.IsNullOrEmpty(_ctx.TargetConfigDir)) cands.Add(Path.Combine(_ctx.TargetConfigDir, "ModsConfig.xml"));
                    if (!string.IsNullOrEmpty(_ctx.PackConfigDir)) cands.Add(Path.Combine(_ctx.PackConfigDir, "ModsConfig.xml"));
                    if (!string.IsNullOrEmpty(_ctx.GameConfigDir)) cands.Add(Path.Combine(_ctx.GameConfigDir, "ModsConfig.xml"));
                    if (!string.IsNullOrEmpty(_ctx.SaveDataDir)) cands.Add(Path.Combine(Path.Combine(_ctx.SaveDataDir, "Config"), "ModsConfig.xml"));
                }
                cands.Add(Path.Combine(Path.Combine(PathHelp.LocalLowRimWorldDir(), "Config"), "ModsConfig.xml"));
            }
            catch { }
            for (int i = 0; i < cands.Count; i++)
                if (!string.IsNullOrEmpty(cands[i]) && File.Exists(cands[i])) return cands[i];
            return "";
        }

        void RefreshDirLabel()
        {
            _dirLines = new string[]
            {
                L.T("工坊缓存 : ") + (string.IsNullOrEmpty(_wspDir) ? L.T("（没找到）") : _wspDir),
                L.T("本地 Mods : ") + (string.IsNullOrEmpty(_modsDir) ? L.T("（没找到）") : _modsDir),
                L.T("排序文件 : ") + (string.IsNullOrEmpty(_modsConfig) ? L.T("（没找到）") : _modsConfig)
            };
        }

        // ---------------------------------------------------------------
        //  报告面板
        // ---------------------------------------------------------------
        void ReportClear()
        {
            if (InvokeRequired) { try { Invoke(new Action(ReportClear)); } catch { } return; }
            _report.Lines.Clear();
            Render();
        }

        void Append(string text)
        {
            if (text == null) return;
            if (InvokeRequired) { try { Invoke(new Action<string>(Append), new object[] { text }); } catch { } return; }
            _report.AddBulk(text);
        }

        void SetStatus(string text)
        {
            if (InvokeRequired) { try { Invoke(new Action<string>(SetStatus), new object[] { text }); } catch { } return; }
            _status.Text = text;
            Render();
        }

        void SetBusy(bool busy, string text)
        {
            _busy = busy;
            if (_btnScan != null)
            {
                _btnScan.Enabled = !busy;
                _btnApply.Enabled = !busy;
                _btnFill.Enabled = !busy;
                _btnDiag.Enabled = !busy;
                _btnSteamCmd.Enabled = !busy;
                _btnImport.Enabled = !busy;
            }
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (!string.IsNullOrEmpty(text)) _status.Text = text;
            Render();
        }

        // ---------------------------------------------------------------
        //  ① 扫描预览
        // ---------------------------------------------------------------
        void DoScan()
        {
            if (_worker != null && _worker.IsBusy) return;
            if (string.IsNullOrEmpty(_wspDir) || string.IsNullOrEmpty(_modsDir))
            {
                SetStatus(L.T("工坊缓存或 Mods 目录没找到，先确认整合包已绑定。"));
                return;
            }
            bool replace = _chkReplace.Checked;
            SetBusy(true, L.T("正在扫描并比对…"));
            _worker = new BackgroundWorker();
            _worker.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                e.Result = WorkshopSync.Plan(_wspDir, _modsDir, replace);
            };
            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, "");
                SyncReport rep = e.Result as SyncReport;
                if (rep == null) { SetStatus(L.T("扫描失败。")); return; }
                _plan = rep.Actions;
                ReportClear();
                Append(L.T("── 同步预览 ──"));
                Append(L.T("工坊缓存：") + _wspDir);
                Append(L.T("本地 Mods：") + _modsDir);
                Append("");
                int nCopy = 0, nRepl = 0, nSkip = 0;
                for (int i = 0; i < rep.Actions.Count; i++)
                {
                    SyncAction a = rep.Actions[i];
                    if (a.Action == L.T("复制")) nCopy++;
                    else if (a.Action == L.T("覆盖")) nRepl++;
                    else nSkip++;
                }
                Append(L.T("工坊共解析到 ") + rep.Actions.Count + L.T(" 个模组：待复制 ") + nCopy + L.T("，待覆盖 ") + nRepl + L.T("，跳过 ") + nSkip);
                Append("");
                for (int i = 0; i < rep.Actions.Count; i++)
                {
                    SyncAction a = rep.Actions[i];
                    if (a.Action == L.T("跳过")) continue;
                    Append("[" + a.Action + "] " + a.Name + "  (" + a.PublishedId + ")");
                    Append("        " + a.SourceDir);
                    Append("     →  " + a.TargetDir);
                }
                if (rep.Errors.Count > 0)
                {
                    Append("");
                    Append(L.T("── 问题 ──"));
                    for (int i = 0; i < rep.Errors.Count; i++) Append("· " + rep.Errors[i]);
                }
                Append("");
                Append(L.T("── 注意 ──"));
                Append(L.T("· 如果这台机器上的游戏会直接读取工坊目录，那么复制到 Mods 后同一个模组会同时存在于两处，"));
                Append(L.T("  游戏可能报「重复 packageId」。只把 Mods 目录打进整合包使用就没有这个问题。"));
                Append(L.T("· 复制进来的模组不会自动勾选，需要在游戏 Mod 管理器里启用。"));
                Append("");
                Append(L.T("确认无误后点「② 执行同步」。"));
                SetStatus(L.T("预览完成：待复制 ") + nCopy + L.T("，待覆盖 ") + nRepl + L.T("，已存在 ") + nSkip);
            };
            _worker.RunWorkerAsync();
        }

        // ---------------------------------------------------------------
        //  ② 执行同步
        // ---------------------------------------------------------------
        void DoApply()
        {
            if (_worker != null && _worker.IsBusy) return;
            if (_plan == null || _plan.Count == 0)
            {
                SetStatus(L.T("先点「① 扫描预览」。"));
                return;
            }
            bool replace = _chkReplace.Checked;
            SetBusy(true, L.T("正在复制到 Mods…"));
            _worker = new BackgroundWorker();
            _worker.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                SyncReport rep = WorkshopSync.Plan(_wspDir, _modsDir, replace);
                WorkshopSync.Apply(rep, _modsDir);
                ModIdIndex.Save(IndexPath());
                e.Result = rep;
            };
            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, "");
                SyncReport rep = e.Result as SyncReport;
                if (rep == null) { SetStatus(L.T("同步失败。")); return; }
                ReportClear();
                Append(L.T("── 同步结果 ──"));
                Append(rep.Summary());
                Append("");
                if (rep.Errors.Count > 0)
                {
                    Append(L.T("── 失败明细 ──"));
                    for (int i = 0; i < rep.Errors.Count; i++) Append("· " + rep.Errors[i]);
                }
                else Append(L.T("全部完成，没有报错。"));
                Append("");
                Append(L.T("提示：复制进来的模组需要在游戏「Mod 管理器」里勾选启用；"));
                Append(L.T("      或者用启动器的【部署排序】把排序方案写进 ModsConfig.xml。"));
                _plan = null;
                SetStatus(L.T("同步完成：") + rep.Summary());
            };
            _worker.RunWorkerAsync();
        }

        // ---------------------------------------------------------------
        //  ③ 按排序补齐缺失
        // ---------------------------------------------------------------
        void DoFill()
        {
            if (_worker != null && _worker.IsBusy) return;
            if (string.IsNullOrEmpty(_modsConfig)) { SetStatus(L.T("没有找到 ModsConfig.xml，无法读取排序。")); return; }
            if (string.IsNullOrEmpty(_modsDir)) { SetStatus(L.T("没有找到本地 Mods 目录。")); return; }

            string cfg = _modsConfig;
            string mods = _modsDir;
            string wsp = _wspDir;
            Settings st = _st;

            SetBusy(true, L.T("正在按排序比对缺失…"));
            ReportClear();

            _worker = new BackgroundWorker();
            _worker.WorkerReportsProgress = true;
            _worker.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                BackgroundWorker me = (BackgroundWorker)s;

                me.ReportProgress(0, L.T("读取排序：") + cfg);
                List<string> order = WorkshopSync.ReadActiveMods(cfg);
                me.ReportProgress(0, L.T("排序里共 ") + order.Count + L.T(" 个模组"));

                List<string> missing = WorkshopSync.FindMissing(cfg, mods, wsp);
                me.ReportProgress(0, L.T("本地缺失 ") + missing.Count + L.T(" 个"));

                if (missing.Count == 0)
                {
                    e.Result = new List<string>(new string[] { "__NONE__" });
                    return;
                }

                List<string> resolvedIds = new List<string>();
                List<string> resolvedPkg = new List<string>();
                List<string> unresolved = new List<string>();
                for (int i = 0; i < missing.Count; i++)
                {
                    string pub = ModIdIndex.PublishedOf(missing[i]);
                    if (!string.IsNullOrEmpty(pub))
                    {
                        resolvedIds.Add(pub);
                        resolvedPkg.Add(missing[i]);
                    }
                    else unresolved.Add(missing[i]);
                }
                me.ReportProgress(0, L.T("能解析到工坊 id 的 ") + resolvedIds.Count + L.T(" 个，无法解析 ") + unresolved.Count + L.T(" 个"));

                string sc = SteamCmd.Find(st);
                List<string> lines = new List<string>();
                lines.Add("__RESULT__");
                lines.Add(L.T("排序模组总数 : ") + order.Count);
                lines.Add(L.T("本地缺失     : ") + missing.Count);
                lines.Add(L.T("可解析 id    : ") + resolvedIds.Count);
                lines.Add(L.T("无法解析     : ") + unresolved.Count);
                lines.Add(L.T("下载后端     : ") + (string.IsNullOrEmpty(sc) ? L.T("Steam 客户端协议（steam://）") : "steamcmd"));
                lines.Add("");
                if (unresolved.Count > 0)
                {
                    lines.Add(L.T("── 无法解析工坊 id（本地索引里没有）──"));
                    for (int i = 0; i < unresolved.Count; i++) lines.Add("· " + unresolved[i]);
                    lines.Add(L.T("（这些通常是被删档/私人模组，或从没在本机出现过的新模组）"));
                    lines.Add("");
                }

                if (resolvedIds.Count > 0)
                {
                    lines.Add(L.T("── 开始下载 ──"));
                    for (int i = 0; i < resolvedIds.Count; i++)
                    {
                        string id = resolvedIds[i];
                        string pkg = resolvedPkg[i];
                        me.ReportProgress(0, L.T("下载 ") + (i + 1) + "/" + resolvedIds.Count + "  id=" + id);
                        if (!string.IsNullOrEmpty(sc))
                        {
                            string log, err;
                            string content = SteamCmd.DownloadItem(sc, id, 420, out log, out err);
                            if (content != null)
                            {
                                string dest, cerr;
                                if (WorkshopSync.CopyItemToMods(content, id, mods, out dest, out cerr))
                                    lines.Add(L.T("[下载+复制] ") + id + "  → " + dest);
                                else
                                    lines.Add(L.T("[下载成功/复制失败] ") + id + " : " + cerr);
                            }
                            else lines.Add(L.T("[下载失败] ") + id + " : " + err);
                        }
                        else
                        {
                            string oerr;
                            if (WorkshopClient.OpenSteamDownload(id, out oerr))
                                lines.Add(L.T("[已交给 Steam] ") + id + L.T("  —— 等 Steam 下完后点「② 执行同步」把它复制进来"));
                            else
                                lines.Add(L.T("[失败] ") + id + " : " + oerr);
                        }
                    }
                }

                ModIdIndex.Save(IndexPath());
                e.Result = lines;
            };
            _worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs e)
            {
                string msg = e.UserState as string;
                if (!string.IsNullOrEmpty(msg)) SetStatus(msg);
            };
            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, L.T("补齐完成。"));
                List<string> lines = e.Result as List<string>;
                ReportClear();
                if (lines == null) { Append(L.T("补齐失败：没有结果。")); return; }
                if (lines.Count == 1 && lines[0] == "__NONE__")
                {
                    Append(L.T("本地已经齐全，排序里的模组全都在，不需要补下载。"));
                    SetStatus(L.T("本地已齐全。"));
                    return;
                }
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i] == "__RESULT__") continue;
                    Append(lines[i]);
                }
            };
            _worker.RunWorkerAsync();
        }

        // ---------------------------------------------------------------
        //  连通性自检
        // ---------------------------------------------------------------
        void DoDiag()
        {
            if (_worker != null && _worker.IsBusy) return;
            SetBusy(true, L.T("正在测试网络端点…"));
            _worker = new BackgroundWorker();
            _worker.DoWork += delegate(object s, DoWorkEventArgs e) { e.Result = WorkshopApi.SelfTest(); };
            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, L.T("自检完成。"));
                List<string> lines = e.Result as List<string>;
                ReportClear();
                if (lines == null) { Append(L.T("自检没有结果。")); return; }
                for (int i = 0; i < lines.Count; i++) Append(lines[i]);
            };
            _worker.RunWorkerAsync();
        }

        // ---------------------------------------------------------------
        //  导入并下载
        // ---------------------------------------------------------------
        void DoImport()
        {
            if (_worker != null && _worker.IsBusy) return;
            string raw = _import.Text == null ? "" : _import.Text;
            List<string> ids = WorkshopSync.ExtractWorkshopIds(raw);
            if (ids.Count == 0)
            {
                SetStatus(L.T("没识别到工坊 id —— 粘贴纯数字 id 或创意工坊链接都可以。"));
                return;
            }
            if (string.IsNullOrEmpty(_modsDir))
            {
                SetStatus(L.T("没有找到本地 Mods 目录，先确认整合包已绑定。"));
                return;
            }

            string modsDir = _modsDir;
            Settings st = _st;
            SetBusy(true, L.T("正在导入…"));
            ReportClear();

            _worker = new BackgroundWorker();
            _worker.WorkerReportsProgress = true;
            _worker.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                BackgroundWorker me = (BackgroundWorker)s;
                List<string> lines = new List<string>();
                lines.Add("__RESULT__");
                lines.Add(L.T("识别到 ") + ids.Count + L.T(" 个 id"));
                lines.Add("");

                string sc = SteamCmd.Find(st);
                if (string.IsNullOrEmpty(sc))
                {
                    me.ReportProgress(0, L.T("没找到 steamcmd，正在自动安装…"));
                    Action<string> plog = delegate(string m) { me.ReportProgress(0, m); };
                    string ierr;
                    if (SteamCmdInstall.Ensure(plog, 300, out ierr)) sc = SteamCmdInstall.ExePath();
                    else lines.Add(L.T("!! 内置 steamcmd 安装失败：") + ierr);
                }
                bool useCmd = !string.IsNullOrEmpty(sc);
                lines.Add(useCmd ? L.T("下载后端：steamcmd（不需要梯子、不需要 Steam 客户端）")
                                 : L.T("下载后端：Steam 客户端协议（需要 Steam 已登录）"));
                lines.Add("");

                List<string> all = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < ids.Count; i++)
                {
                    string cerr;
                    List<string> kids = WorkshopWebApi.GetCollectionChildren(ids[i], out cerr);
                    if (kids != null && kids.Count > 0)
                    {
                        lines.Add(L.T("合集 ") + ids[i] + L.T(" → 展开出 ") + kids.Count + L.T(" 个物品"));
                        for (int k = 0; k < kids.Count; k++) if (seen.Add(kids[k])) all.Add(kids[k]);
                    }
                    else if (seen.Add(ids[i])) all.Add(ids[i]);
                }
                lines.Add("");
                lines.Add(L.T("共待处理 ") + all.Count + L.T(" 个物品"));
                if (all.Count == 0) { e.Result = lines; return; }

                Dictionary<string, string> titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string terr;
                    for (int i = 0; i < all.Count; i += 50)
                    {
                        List<string> chunk = all.GetRange(i, Math.Min(50, all.Count - i));
                        Dictionary<string, string> part = WorkshopWebApi.GetTitles(chunk, out terr);
                        foreach (KeyValuePair<string, string> kv in part) titles[kv.Key] = kv.Value;
                    }
                }
                catch { }
                lines.Add("");

                for (int i = 0; i < all.Count; i++)
                {
                    string id = all[i];
                    string title;
                    if (!titles.TryGetValue(id, out title)) title = "";
                    me.ReportProgress(0, L.T("正在处理 ") + (i + 1) + "/" + all.Count + "：" + (title.Length > 0 ? title : id));

                    if (useCmd)
                    {
                        string dlog, derr;
                        string content = SteamCmd.DownloadItem(sc, id, 480, out dlog, out derr);
                        if (content != null)
                        {
                            string dest, cerr2;
                            if (WorkshopSync.CopyItemToMods(content, id, modsDir, out dest, out cerr2))
                                lines.Add(L.T("[成功] ") + id + "  " + title + "  →  " + dest);
                            else
                                lines.Add(L.T("[已下载/复制失败] ") + id + "  " + title + "  : " + cerr2);
                        }
                        else lines.Add(L.T("[下载失败] ") + id + "  " + title + "  : " + derr);
                    }
                    else
                    {
                        string oerr;
                        if (WorkshopClient.OpenSteamDownload(id, out oerr))
                            lines.Add(L.T("[已交给 Steam] ") + id + "  " + title + L.T("  —— 下完后点「② 执行同步」复制进 Mods"));
                        else
                            lines.Add(L.T("[失败] ") + id + "  " + title + "  : " + oerr);
                    }
                }
                e.Result = lines;
            };
            _worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs e)
            {
                string m = e.UserState as string;
                if (!string.IsNullOrEmpty(m)) SetStatus(m);
            };
            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, L.T("导入结束。"));
                List<string> lines = e.Result as List<string>;
                ReportClear();
                if (lines == null) { Append(L.T("导入失败：没有结果。")); return; }
                for (int i = 0; i < lines.Count; i++) { if (lines[i] == "__RESULT__") continue; Append(lines[i]); }
            };
            _worker.RunWorkerAsync();
        }

        // ---------------------------------------------------------------
        //  Steam API Key / steamcmd
        // ---------------------------------------------------------------
        void SaveApiKey()
        {
            string k = _apiKey.Text == null ? "" : _apiKey.Text.Trim();
            if (_st == null) return;
            _st.SteamApiKey = k;
            _st.Save();
            ReportClear();
            if (k.Length < 16)
            {
                Append(L.T("API Key 已清空。搜索将退回网页抓取（需要梯子）。"));
                SetStatus(L.T("API Key 已清空。"));
            }
            else
            {
                Append(L.T("Steam API Key 已保存（") + k.Substring(0, 6) + "…）。");
                Append(L.T("之后「搜索创意工坊」会优先走 api.steampowered.com —— 这个地址不被墙，不需要梯子。"));
                SetStatus(L.T("API Key 已保存。"));
            }
        }

        /// <summary>
        /// 引导用户自己去申请一个 Steam API Key。
        /// 申请页在被墙的那一侧，必须开梯子才能打开；
        /// 但 key 拿到手后，本软件的搜索走不被墙的地址，可以关掉梯子。
        /// </summary>
        void GetApiKey()
        {
            ReportClear();
            Append(L.T("── 获取你自己的 Steam API Key ──"));
            Append("");
            Append(L.T("1. 已经帮你在浏览器打开了申请页：https://steamcommunity.com/dev/apikey"));
            Append(L.T("   打不开就说明需要先开梯子/加速器（那个页面在被墙的一侧，没办法）。"));
            Append(L.T("2. 页面里 Domain Name 随便填一个，例如：zogos.local ，勾选同意后点 Register。"));
            Append(L.T("3. 页面会出现一串 32 位的十六进制字符，那就是 API Key。"));
            Append(L.T("4. 复制它，粘到上面「Steam API Key」输入框，点「保存」。"));
            Append(L.T("5. 保存之后就可以把梯子关掉了 —— 搜索走的是不被墙的地址。"));
            Append("");
            Append(L.T("※ 每位用户请用自己申请的 key。多人共用一个 key 会互相挤占调用配额，"));
            Append(L.T("  被滥用时 Valve 会直接吊销。也请不要把自己的 key 发到群里或公开。"));
            Append("");
            Append(L.T("打不开页面时，也可以手动在浏览器输入：https://steamcommunity.com/dev/apikey"));
            try { System.Diagnostics.Process.Start("https://steamcommunity.com/dev/apikey"); }
            catch (Exception ex) { Append(L.T("【打不开浏览器】请手动访问上面的地址：") + ex.Message); }
            SetStatus(L.T("已尝试打开申请页；如果没反应，请手动在浏览器输入那个地址。"));
        }

        void BundleSteamCmd()
        {
            if (_worker != null && _worker.IsBusy) return;
            SetBusy(true, L.T("正在安装内置 steamcmd…"));
            ReportClear();
            _worker = new BackgroundWorker();
            _worker.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                string err;
                Action<string> log = delegate(string m) { Append("· " + m); };
                bool ok = SteamCmdInstall.Ensure(log, 300, out err);
                e.Result = new object[] { ok, err };
            };
            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, "");
                object[] arr = e.Result as object[];
                bool ok = arr != null && (bool)arr[0];
                string err = arr == null ? "" : Convert.ToString(arr[1]);
                Append("");
                if (ok)
                {
                    Append(L.T("steamcmd 已就位，之后下载工坊内容不需要梯子，也不需要开 Steam 客户端。"));
                    if (_st != null && string.IsNullOrEmpty(_st.SteamCmdPath))
                    {
                        _st.SteamCmdPath = SteamCmdInstall.ExePath();
                        _st.Save();
                    }
                    SetStatus(L.T("steamcmd 安装完成。"));
                }
                else
                {
                    Append(L.T("安装失败：") + err);
                    Append(L.T("可以手动下载 steamcmd.zip 解压到：") + SteamCmdInstall.RootDir());
                    SetStatus(L.T("steamcmd 安装失败。"));
                }
            };
            _worker.RunWorkerAsync();
        }

        void PickSteamCmd()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = L.T("选择 steamcmd.exe");
                d.Filter = L.T("steamcmd.exe|steamcmd.exe|可执行文件|*.exe");
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    if (_st != null) { _st.SteamCmdPath = d.FileName; _st.Save(); }
                    ReportClear();
                    Append(L.T("steamcmd 已设为：") + d.FileName);
                    Append("");
                    Append(L.T("有了 steamcmd，就可以不开 Steam 客户端、不开梯子直接下载工坊内容。"));
                    SetStatus(L.T("steamcmd 路径已保存。"));
                }
            }
        }
    }
}
