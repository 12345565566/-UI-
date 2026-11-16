using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 窗口内「选文件 / 选文件夹」覆盖层（和创意工坊、个性化同一个 El 元素体系与开合动画）。
    /// 为什么不单独开窗：主窗是 UpdateLayeredWindow 的分层窗口，真控件放进去不显示；
    /// 而且用户要的是「像其它弹出窗口一样长在启动器里」。
    /// 三种模式：Folder / OpenFile / SaveFile。IO 全在后台线程，代际号取消。
    /// </summary>
    class PickOverlay
    {
        public readonly List<El> Els = new List<El>();
        public ElInput[] Inputs;

        // —— 元素 ——
        readonly ElPickPanel _panel = new ElPickPanel();
        readonly ElText _lbTitle = new ElText();
        readonly ElText _lbTip = new ElText();
        readonly ElText _lbFoot = new ElText();
        readonly ElText _lbNameTag = new ElText("文件名");
        readonly ElText _lbFilter = new ElText();
        readonly ElPickCrumb _crumb = new ElPickCrumb();
        readonly ElPickSide _side = new ElPickSide();
        readonly ElPickList _list = new ElPickList();
        readonly ElInput _search = new ElInput();
        readonly ElInput _addr = new ElInput();
        readonly ElInput _name = new ElInput();
        readonly ElPickPh _phSearch = new ElPickPh();
        readonly ElPickPh _phName = new ElPickPh();
        readonly ElPickPh _phAddr = new ElPickPh();
        readonly ElBtn _up = new ElBtn("↑");
        readonly ElBtn _cancel = new ElBtn("取消");
        readonly ElBtn _ok = new ElBtn("选择") { Primary = true };
        readonly ElBtn _close = new ElBtn("✕");

        readonly LayeredForm _host;

        // —— 状态 ——
        public PickMode Mode = PickMode.Folder;
        public string Result;
        public bool Ok;
        string _title = "";
        string _filterLabel = "";
        readonly List<string> _exts = new List<string>();
        string _cur = "";
        List<PickEntry> _all = new List<PickEntry>();
        int _gen;
        bool _addrMode;
        bool _done;
        bool _inLoop;
        Action _onFinish;

        public PickOverlay(LayeredForm host)
        {
            _host = host;
            Inputs = new[] { _search, _addr, _name };

            _panel.IsOverlay = true;
            _panel.CloseClicked += delegate { Cancel(); };

            _lbTitle.FontOf = () => T.B(15);
            _lbTitle.ColorOf = () => T.Text;
            _lbTip.FontOf = () => T.F(11.5f);
            _lbTip.ColorOf = () => T.Text3;
            _lbTip.Ellipsis = true;
            _lbFoot.FontOf = () => T.F(11.5f);
            _lbFoot.ColorOf = () => T.Text3;
            _lbFoot.Ellipsis = true;
            _lbNameTag.FontOf = () => T.F(12);
            _lbNameTag.ColorOf = () => T.Text2;
            _lbFilter.FontOf = () => T.F(11);
            _lbFilter.ColorOf = () => T.Text3;
            _lbFilter.Ellipsis = true;

            _crumb.OnGo = delegate(string p) { Navigate(p); };
            _crumb.OnEdit = delegate { ToggleAddr(true); };
            _side.OnGo = delegate(string p) { Navigate(p); };

            _list.SelectionChanged += delegate { UpdateHint(); UpdateOk(); };
            _list.Activated += delegate { ActivateSelected(); };
            _list.Sorted += delegate { UpdateHint(); };

            _up.Bold = true;
            _up.Clicked += delegate { GoUp(); };
            _cancel.Clicked += delegate { Cancel(); };
            _ok.Clicked += delegate { Confirm(); };
            _close.Clicked += delegate { Cancel(); };

            _search.TextChanged += delegate { ApplyFilter(); UpdatePlaceholders(); };
            _name.TextChanged += delegate { UpdateOk(); UpdatePlaceholders(); };
            _addr.TextChanged += delegate { UpdatePlaceholders(); };

            _phSearch.ColorOf = () => T.Text3;
            _phSearch.FontOf = () => T.F(12);
            _phSearch.Text = "搜索当前文件夹…";
            _phAddr.ColorOf = () => T.Text3;
            _phAddr.FontOf = () => T.F(12);
            _phAddr.Text = "输入路径后回车，例如 D:\\A";
            _phName.ColorOf = () => T.Text3;
            _phName.FontOf = () => T.F(12);
            _phName.Text = "也可以在这里直接输入文件名";

            Els.AddRange(new El[] {
                _panel, _lbTitle, _lbTip, _lbFoot, _lbNameTag, _lbFilter,
                _side, _crumb,
                _search, _addr, _phSearch, _phAddr,
                _up, _list, _name, _phName,
                _cancel, _ok, _close
            });
            foreach (var el in Els) el.Host = host;
        }

        // ────────────────────────── 打开 / 关闭 ──────────────────────────

        public void Begin(string title, PickMode mode, string filter, string start, string suggest)
        {
            _title = title ?? "选择";
            Mode = mode;
            _filterLabel = "";
            _exts.Clear();
            ParseFilter(filter);
            _all = new List<PickEntry>();
            _cur = "";
            Result = null;
            Ok = false;
            _done = false;
            _addrMode = false;

            _lbTitle.Text = _title;
            _lbTip.Text = "双击进入  ·  Backspace 上一级  ·  Ctrl+L 输入路径";
            _side.Build();
            _name.Text = (mode == PickMode.SaveFile && !string.IsNullOrEmpty(suggest)) ? suggest : "";
            _search.Text = "";
            _addr.Text = "";
            _list.EmptyText = mode == PickMode.Folder ? "这个文件夹里没有子文件夹"
                : (_exts.Count > 0 ? "这个文件夹里没有符合条件的文件" : "这个文件夹里没有文件");
            _ok.Text = mode == PickMode.Folder ? "选这个文件夹" : (mode == PickMode.SaveFile ? "保存到此处" : "选择文件");
            _ok.Enabled = false;
            _lbFilter.Text = _exts.Count == 0 ? "" :
                ("只看 " + (_filterLabel.Length > 0 ? _filterLabel + "  " : "") + string.Join("  ", _exts.ToArray()));
            _lbNameTag.Visible = mode != PickMode.Folder;
            _name.Visible = mode != PickMode.Folder;
            _lbFilter.Visible = mode != PickMode.Folder && _exts.Count > 0;
            _addr.Visible = false;
            _crumb.Visible = true;

            string p = (start ?? "").Trim().Trim('"');
            try { if (p.Length > 0 && File.Exists(p)) p = Path.GetDirectoryName(p); } catch { }
            Navigate(p);
            UpdatePlaceholders();
        }

        public void Layout(Rectangle card)
        {
            _panel.SetBounds(card.X, card.Y, card.Width, card.Height);

            int x = card.X, y = card.Y, w = card.Width, h = card.Height;
            _lbTitle.SetBounds(x + 20, y + 10, Math.Max(80, w - 340), 24);
            _lbTip.SetBounds(x + w - 320, y + 12, 264, 20);
            _close.SetBounds(x + w - 44, y + 9, 30, 26);

            int toolY = y + 44;
            _up.SetBounds(x + 16, toolY + 6, 30, 26);
            int crumbX = x + 52, crumbW = Math.Max(120, w - 52 - 16 - 214 - 10);
            _crumb.SetBounds(crumbX, toolY + 4, crumbW, 30);
            _addr.SetBounds(crumbX, toolY + 4, crumbW, 30);
            _phAddr.SetBounds(crumbX + 8, toolY + 4, crumbW - 16, 30);
            _search.SetBounds(x + w - 16 - 204, toolY + 5, 204, 28);
            _phSearch.SetBounds(_search.Bounds.X + 8, _search.Bounds.Y, _search.Bounds.Width - 16, _search.Bounds.Height);

            int sideW = 138;
            int bodyY = toolY + 40;
            int nameH = Mode == PickMode.Folder ? 0 : 34;
            int footY = y + h - 50;
            int bodyH = Math.Max(80, footY - bodyY - nameH);

            _side.SetBounds(x + 10, bodyY, sideW, bodyH);
            int lx = x + 10 + sideW + 10;
            int lw = Math.Max(200, w - (lx - x) - 16);
            _list.SetBounds(lx, bodyY, lw, bodyH);

            if (nameH > 0)
            {
                _lbNameTag.SetBounds(lx, bodyY + bodyH + 7, 44, 20);
                _name.SetBounds(lx + 46, bodyY + bodyH + 4, 360, 28);
                _phName.SetBounds(_name.Bounds.X + 8, _name.Bounds.Y, _name.Bounds.Width - 16, _name.Bounds.Height);
                _lbFilter.SetBounds(lx + 46 + 368, bodyY + bodyH + 8, Math.Max(80, lx + lw - (lx + 46 + 368)), 20);
            }

            _lbFoot.SetBounds(x + 20, footY + 14, Math.Max(120, w - 20 - 16 - 210 - 12), 22);
            _cancel.SetBounds(x + w - 16 - 92, footY + 10, 92, 30);
            _ok.SetBounds(x + w - 16 - 92 - 10 - 116, footY + 10, 116, 30);
        }

        public void UpdatePlaceholders()
        {
            _phSearch.Visible = _search.Text.Length == 0;
            _phAddr.Visible = _addr.Visible && _addr.Text.Length == 0;
            _phName.Visible = _name.Visible && _name.Text.Length == 0;
        }

        // ────────────────────────── 结果 ──────────────────────────

        public void Confirm()
        {
            if (!_list.Loading) { }
            if (Mode == PickMode.Folder)
            {
                var sel = _list.Selected;
                string dir = (sel != null && sel.IsDir) ? sel.Full : _cur;
                if (string.IsNullOrEmpty(dir)) { Hint("请先进入一个磁盘或文件夹", true); return; }
                Finish(dir, true);
                return;
            }
            if (Mode == PickMode.SaveFile)
            {
                string n = _name.Text.Trim();
                if (n.Length == 0) { Hint("请填写文件名", true); return; }
                string full;
                if (n.IndexOf('\\') >= 0 || n.IndexOf(':') >= 0) full = n;
                else
                {
                    if (string.IsNullOrEmpty(_cur)) { Hint("请先进入一个文件夹", true); return; }
                    full = Path.Combine(_cur, n);
                }
                if (!Path.HasExtension(full) && _exts.Count > 0) full += _exts[0];
                try
                {
                    string d = Path.GetDirectoryName(full);
                    if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) { Hint("这个文件夹不存在", true); return; }
                }
                catch { }
                Finish(full, true);
                return;
            }
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
            if (string.IsNullOrEmpty(file)) { Hint("先选中一个文件", true); return; }
            if (!File.Exists(file)) { Hint("找不到这个文件：" + file, true); return; }
            Finish(file, true);
        }

        public void Cancel()
        {
            if (_addrMode) { ToggleAddr(false); return; }
            Finish(null, false);
        }

        void Finish(string result, bool ok)
        {
            if (_done) return;
            Result = result;
            Ok = ok;
            _done = true;
            if (_onFinish != null) { var f = _onFinish; _onFinish = null; f(); }
            if (!_inLoop) { if (_host != null) _host.Render(); }
        }

        /// <summary>嵌套消息泵：等用户选完再返回（调用方是同步写法，保持调用点不变）。
        /// 注意必须走 Application.DoEvents：WinForms 的键盘（ProcessDialogKey / 输入框打字）
        /// 是在 PreTranslateMessage 里分发的，自己 TranslateMessage+DispatchMessage 会漏掉按键。</summary>
        public bool Modal(Action onFinish)
        {
            if (_inLoop) return false;
            _inLoop = true;
            _onFinish = onFinish;
            _done = false;
            try
            {
                while (!_done)
                {
                    MSG m;
                    if (PeekMessage(out m, IntPtr.Zero, 0, 0, 0)) Application.DoEvents(); // PM_NOREMOVE
                    else WaitMessage();
                }
            }
            finally { _inLoop = false; _onFinish = null; }
            return Ok;
        }

        // ────────────────────────── 键盘 ──────────────────────────

        /// <summary>覆盖层键盘（由 MainForm.ProcessDialogKey 转发）。返回 true 表示已消费。</summary>
        public bool HandleKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            bool inputFocused = _hasFocus;
            if (k == Keys.Escape)
            {
                if (_addrMode) { ToggleAddr(false); return true; }
                if (inputFocused) { SetFocusInput(null); UpdatePlaceholders(); return true; }
                Cancel();
                return true;
            }
            if (k == Keys.L && (keyData & Keys.Control) != 0) { ToggleAddr(true); return true; }
            if (k == Keys.Enter)
            {
                if (_addrMode) { ToggleAddr(false); Navigate(_addr.Text.Trim()); return true; }
                if (_search == _hostFocus) { DeepSearch(); return true; }
                Confirm();
                return true;
            }
            if (k == Keys.Back && !inputFocused) { GoUp(); return true; }
            if (k == Keys.Up || k == Keys.Down || k == Keys.PageUp || k == Keys.PageDown || k == Keys.Home || k == Keys.End)
            {
                if (inputFocused && (k == Keys.Up || k == Keys.Down)) return false; // 输入框里上下键不抢
                _list.KeyMove(k);
                return true;
            }
            return false;
        }

        ElInput _hostFocus
        {
            get { return _host is MainForm ? ((MainForm)_host).FocusInputPublic : null; }
        }

        bool _hasFocus { get { return _hostFocus != null; } }

        // ────────────────────────── 导航 ──────────────────────────

        void Hint(string s, bool warn)
        {
            _lbFoot.Text = s;
            _lbFoot.ColorOf = warn ? new Func<Color>(() => PickCol.Danger) : new Func<Color>(() => T.Text3);
            if (_host != null) _host.Render();
        }

        void SetFocusInput(ElInput inp)
        {
            var mf = _host as MainForm;
            if (mf != null) mf.SetFocusInputPublic(inp);
        }

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
                        else { Hint("找不到这个文件夹：" + path, true); return; }
                    }
                    path = Path.GetFullPath(path);
                }
                catch (Exception ex) { Hint("路径无效：" + ex.Message, true); return; }
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
            int gen = ++_gen;
            _list.SetLoading(true, "正在读取…");
            _all = new List<PickEntry>();
            _list.SetItems(new List<PickEntry>());
            string path = _cur;
            string[] exts = _exts.ToArray();
            bool wantFiles = Mode != PickMode.Folder;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                var items = new List<PickEntry>();
                try { items = ReadDir(path, exts, wantFiles, out err); }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    _host.BeginInvoke((Action)delegate
                    {
                        if (gen != _gen) return;
                        _all = items;
                        _list.SetLoading(false, null);
                        if (err != null) Hint("读不到这个位置：" + err, true);
                        ApplyFilter();
                        UpdateOk();
                        _host.Render();
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
            if (key.Length == 0) _list.SetItems(_all);
            else
            {
                var hit = new List<PickEntry>();
                foreach (var it in _all) if (it.Name.IndexOf(key, StringComparison.CurrentCultureIgnoreCase) >= 0) hit.Add(it);
                _list.SetItems(hit);
            }
            UpdateHint();
            UpdateOk();
            if (_host != null) _host.Render();
        }

        void DeepSearch()
        {
            string key = _search.Text.Trim();
            if (key.Length == 0) { ApplyFilter(); return; }
            int gen = ++_gen;
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
                        while (stack.Count > 0 && res.Count < 300 && visited < 5000)
                        {
                            if (gen != _gen) return;
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
                    _host.BeginInvoke((Action)delegate
                    {
                        if (gen != _gen) return;
                        _all = res;
                        _list.SetLoading(false, null);
                        _list.SetItems(res);
                        Hint(res.Count == 0 ? "没找到匹配的文件夹" : ("在当前位置以下找到 " + res.Count + " 个文件夹"), false);
                        _host.Render();
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
                if (_search.Text.Length > 0) _search.Text = "";
                Navigate(sel.Full);
                return;
            }
            if (Mode == PickMode.Folder) return;
            if (Mode == PickMode.SaveFile) { _name.Text = sel.Name; return; }
            Confirm();
        }

        void ToggleAddr(bool on)
        {
            _addrMode = on;
            _addr.Visible = on;
            _crumb.Visible = !on;
            if (on)
            {
                _addr.Text = "";   // ElInput 没有"全选"，预填会变成追加 → 留空让用户直接打整条路径
                SetFocusInput(_addr);
            }
            else SetFocusInput(null);
            UpdatePlaceholders();
            if (_host != null) _host.Render();
        }

        void UpdateHint()
        {
            var sel = _list.Selected;
            string s;
            if (sel != null)
            {
                if (sel.Kind == 2) s = sel.Name + "  " + (sel.Note ?? "");
                else if (sel.Note != null) s = "位于 " + sel.Note;
                else s = (sel.IsDir ? "文件夹：" : "文件：") + sel.Full;
            }
            else s = string.IsNullOrEmpty(_cur) ? "此电脑：选一个磁盘开始" : ("当前位置：" + _cur);
            Hint(s, false);
        }

        void UpdateOk()
        {
            bool can;
            if (Mode == PickMode.Folder) can = !string.IsNullOrEmpty(_cur);
            else if (Mode == PickMode.SaveFile)
            {
                string n = _name.Text.Trim();
                can = n.Length > 0 && (n.IndexOf('\\') >= 0 || !string.IsNullOrEmpty(_cur));
            }
            else
            {
                var sel = _list.Selected;
                can = (sel != null && !sel.IsDir);
                if (!can)
                {
                    string typed = _name.Text.Trim();
                    if (typed.Length > 0)
                    {
                        try { can = File.Exists(typed.IndexOf('\\') >= 0 ? typed : Path.Combine(_cur, typed)); }
                        catch { can = false; }
                    }
                }
            }
            _ok.Enabled = can;
        }

        void ParseFilter(string filter)
        {
            if (string.IsNullOrEmpty(filter)) return;
            string[] parts = filter.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (!p.StartsWith("*.")) continue;
                foreach (var one in p.Split(';'))
                {
                    string e = one.Trim();
                    if (!e.StartsWith("*.") || e.Length <= 2 || e == "*.*") continue;
                    string ext = e.Substring(1).ToLowerInvariant();
                    if (!_exts.Contains(ext)) _exts.Add(ext);
                }
            }
            string label = parts.Length > 0 ? parts[0].Trim() : "";
            int paren = label.IndexOf("(*");
            if (paren > 0) label = label.Substring(0, paren).Trim();
            if (label.Length > 0 && label.IndexOf('.') < 0) _filterLabel = label;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public Point pt;
        }
        [DllImport("user32.dll")] static extern bool PeekMessage(out MSG m, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] static extern bool WaitMessage();

    }

    /// <summary>覆盖层里用到、主题令牌还没定义的两个颜色（危险提示字 / 表头底）。</summary>
    static class PickCol
    {
        public static Color Danger { get { return T.Dark ? Color.FromArgb(226, 130, 120) : Color.FromArgb(176, 58, 42); } }
        public static Color Head { get { return T.Dark ? Color.FromArgb(35, 35, 40) : Color.FromArgb(250, 250, 247); } }
    }

    /// <summary>输入框占位提示：只画不吃点击（画在输入框之上，命中仍归输入框）。</summary>
    class ElPickPh : ElText
    {
        public override bool HitTest(Point p) { return false; }
    }

    /// <summary>覆盖层卡片：圆角底 + 描边 + 标题栏分隔线 + 右上角关闭。</summary>
    class ElPickPanel : El
    {
        public bool IsOverlay;
        public event EventHandler CloseClicked;
        Rectangle _x = Rectangle.Empty;
        bool _xHover;

        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r, 12, T.Card);
            T.DrawRound(g, r, 12, T.Line, 1f);
            // 标题栏分隔线 + 底部按钮行分隔线
            using (var p = new Pen(T.Line, 1f))
            {
                g.DrawLine(p, Bounds.X + 1, Bounds.Y + 44, Bounds.Right - 1, Bounds.Y + 44);
                g.DrawLine(p, Bounds.X + 1, Bounds.Bottom - 50, Bounds.Right - 1, Bounds.Bottom - 50);
                // 左侧栏与列表之间的竖线
                g.DrawLine(p, Bounds.X + 153, Bounds.Y + 84, Bounds.X + 153, Bounds.Bottom - 50);
            }
            _x = new Rectangle(Bounds.Right - 44, Bounds.Y + 9, 30, 26);
            if (_xHover) T.FillRound(g, new RectangleF(_x.X, _x.Y, _x.Width, _x.Height), 6, T.Line2);
        }

        public override void OnMouseMove(Point p)
        {
            bool h = _x.Contains(p);
            if (h != _xHover) { _xHover = h; Redraw(); }
        }
        public override void OnMouseLeave() { if (_xHover) { _xHover = false; Redraw(); } }
        public override void OnMouseUp(Point p)
        {
            if (_x.Contains(p) && CloseClicked != null) CloseClicked(this, EventArgs.Empty);
        }
        public override Cursor CursorOf(Point p) { return _x.Contains(p) ? Cursors.Hand : Cursors.Default; }
    }

    /// <summary>面包屑：可点跳转，过宽从中间折，右侧画笔进路径编辑。</summary>
    class ElPickCrumb : El
    {
        public Action<string> OnGo;
        public Action OnEdit;
        readonly List<KeyValuePair<string, string>> _segs = new List<KeyValuePair<string, string>>();
        readonly List<RectangleF> _rects = new List<RectangleF>();
        int _hover = -1;
        bool _penHover;
        bool _dirty = true;
        RectangleF _pen = RectangleF.Empty;

        public void SetPath(string path)
        {
            _segs.Clear();
            _segs.Add(new KeyValuePair<string, string>("此电脑", ""));
            if (!string.IsNullOrEmpty(path))
            {
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
            Redraw();
        }

        protected override void OnBoundsChanged() { _dirty = true; }

        void Rebuild(Graphics g)
        {
            _rects.Clear();
            var f = T.F(12);
            float sepW = 16;
            var widths = new List<float>();
            foreach (var s in _segs) widths.Add(Measure(g, s.Key, f) + 14);
            float total = 0;
            for (int i = 0; i < widths.Count; i++) total += widths[i] + (i > 0 ? sepW : 0);
            int first = 0, last = _segs.Count - 1;
            while (total > Bounds.Width - 34 && last - first > 2) { total -= widths[first + 1] + sepW; first++; }
            float x = Bounds.X;
            for (int i = first; i <= last; i++)
            {
                if (i > first) x += sepW;
                _rects.Add(new RectangleF(x, Bounds.Y, widths[i], Bounds.Height));
                x += widths[i];
            }
            _pen = new RectangleF(Bounds.Right - 22, Bounds.Y + (Bounds.Height - 20) / 2f, 20, 20);
            _dirty = false;
        }

        static float Measure(Graphics g, string s, Font f)
        {
            try { return g.MeasureString(s, f).Width; } catch { return s.Length * 12; }
        }

        public override void Paint(Graphics g)
        {
            if (_dirty) Rebuild(g);
            var f = T.F(12);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            int first = _segs.Count - _rects.Count;
            for (int i = 0; i < _rects.Count; i++)
            {
                int gi = i + first;
                if (gi >= _segs.Count) break;
                var r = _rects[i];
                bool last = (gi == _segs.Count - 1);
                if (_hover == gi && !last) T.FillRound(g, new RectangleF(r.X + 1, r.Y + 3, r.Width - 2, r.Height - 6), 5, T.Line2);
                using (var b = new SolidBrush(last ? T.Text : T.Text2))
                    T.DrawStr(g, Ell(g, _segs[gi].Key, f, r.Width - 4), f, b, r, sf);
                if (!last)
                    using (var b = new SolidBrush(T.Text3))
                        T.DrawStr(g, "›", f, b, new RectangleF(r.Right - 2, r.Y, 14, r.Height), sf);
            }
            if (_penHover) T.FillRound(g, _pen, 5, T.Line2);
            using (var p = new Pen(_penHover ? T.Accent : T.Text3, 1.4f))
            {
                g.DrawLine(p, _pen.X + 5, _pen.Y + 14, _pen.X + 14, _pen.Y + 5);
                g.DrawLine(p, _pen.X + 4.5f, _pen.Y + 15.5f, _pen.X + 8, _pen.Y + 15);
            }
        }

        static string Ell(Graphics g, string s, Font f, float maxW)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (Measure(g, s, f) <= maxW) return s;
            string t = s;
            while (t.Length > 1 && Measure(g, t + "…", f) > maxW) t = t.Substring(0, t.Length - 1);
            return t + "…";
        }

        public override void OnMouseMove(Point p)
        {
            int h = -1;
            bool pen = _pen.Contains(p);
            if (!pen)
            {
                int first = _segs.Count - _rects.Count;
                for (int i = 0; i < _rects.Count; i++)
                    if (_rects[i].Contains(p)) { h = i + first; break; }
            }
            if (h != _hover || pen != _penHover) { _hover = h; _penHover = pen; Redraw(); }
        }
        public override void OnMouseLeave() { _hover = -1; _penHover = false; Redraw(); }
        public override Cursor CursorOf(Point p)
        {
            if (_pen.Contains(p)) return Cursors.Hand;
            int first = _segs.Count - _rects.Count;
            for (int i = 0; i < _rects.Count; i++)
                if (_rects[i].Contains(p)) return (i + first == _segs.Count - 1) ? Cursors.Default : Cursors.Hand;
            return Cursors.Default;
        }
        public override void OnMouseUp(Point p)
        {
            if (_pen.Contains(p)) { if (OnEdit != null) OnEdit(); return; }
            int first = _segs.Count - _rects.Count;
            for (int i = 0; i < _rects.Count; i++)
                if (_rects[i].Contains(p))
                {
                    int gi = i + first;
                    if (gi < _segs.Count && OnGo != null) OnGo(_segs[gi].Value);
                    return;
                }
        }
    }

    /// <summary>左侧常用位置 + 磁盘。</summary>
    class ElPickSide : El
    {
        public Action<string> OnGo;
        class Item { public string Label, Path, Note; public int Kind; }   // 0 常用 1 磁盘 2 小标题
        readonly List<Item> _items = new List<Item>();
        int _hover = -1;
        string _cur = "";
        const int ROWH = 26, CAPH = 22;

        public void Build()
        {
            _items.Clear();
            _items.Add(new Item { Label = "常用位置", Kind = 2 });
            Add("桌面", Safe(Environment.SpecialFolder.Desktop));
            Add("文档", Safe(Environment.SpecialFolder.MyDocuments));
            string home = "";
            try { home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
            if (!string.IsNullOrEmpty(home))
            {
                Add("下载", Path.Combine(home, "Downloads"));
                Add("图片", Path.Combine(home, "Pictures"));
            }
            _items.Add(new Item { Label = "此电脑", Kind = 2, Path = "" });
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        string note = null;
                        try { note = Pk.Size(d.AvailableFreeSpace) + " 可用"; } catch { }
                        _items.Add(new Item { Label = d.Name.TrimEnd('\\'), Path = d.Name, Kind = 1, Note = note });
                    }
                    catch { }
                }
            }
            catch { }
            Redraw();
        }

        static string Safe(Environment.SpecialFolder s) { try { return Environment.GetFolderPath(s); } catch { return ""; } }
        void Add(string l, string p) { if (!string.IsNullOrEmpty(p)) _items.Add(new Item { Label = l, Path = p, Kind = 0 }); }

        public void SetCurrent(string p) { _cur = p ?? ""; Redraw(); }

        int RowOf(int idx)
        {
            int y = Bounds.Y + 4;
            for (int i = 0; i < idx; i++) y += _items[i].Kind == 2 ? CAPH : ROWH;
            return y;
        }

        int HitIndex(Point p)
        {
            int y = Bounds.Y + 4;
            for (int i = 0; i < _items.Count; i++)
            {
                int h = _items[i].Kind == 2 ? CAPH : ROWH;
                if (p.Y >= y && p.Y < y + h) return _items[i].Kind == 2 ? -1 : i;
                y += h;
            }
            return -1;
        }

        bool On(string path)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(_cur)) return false;
            string a = path.TrimEnd('\\'), b = _cur.TrimEnd('\\');
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public override void Paint(Graphics g)
        {
            var fCap = T.F(11);
            var fItem = T.F(12);
            var sf = new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                int y = RowOf(i);
                if (it.Kind == 2)
                {
                    using (var b = new SolidBrush(T.Text3))
                        T.DrawStr(g, it.Label, fCap, b, new RectangleF(Bounds.X + 8, y, Bounds.Width - 16, CAPH), sf);
                    continue;
                }
                bool on = On(it.Path);
                if (_hover == i || on)
                    T.FillRound(g, new RectangleF(Bounds.X + 2, y + 1, Bounds.Width - 4, ROWH - 2), 6, on ? T.AccentSoft : T.Line2);
                var ico = PickIcons.Get(new PickEntry { Kind = it.Kind == 1 ? 2 : 1 });
                if (ico != null) g.DrawImage(ico, Bounds.X + 8, y + (ROWH - 18) / 2, 18, 18);
                using (var b = new SolidBrush(on ? T.Accent : T.Text2))
                    T.DrawStr(g, it.Label, fItem, b, new RectangleF(Bounds.X + 32, y, Bounds.Width - 40, ROWH), sf);
            }
        }

        public override void OnMouseMove(Point p)
        {
            int h = HitIndex(p);
            if (h != _hover) { _hover = h; Redraw(); }
        }
        public override void OnMouseLeave() { if (_hover != -1) { _hover = -1; Redraw(); } }
        public override Cursor CursorOf(Point p) { return HitIndex(p) >= 0 ? Cursors.Hand : Cursors.Default; }
        public override void OnMouseUp(Point p)
        {
            int i = HitIndex(p);
            if (i >= 0 && OnGo != null) OnGo(_items[i].Path);
        }
    }

    /// <summary>文件/文件夹列表：自绘表头 + 行 + 滚动条，点表头排序，双击进入/确认。</summary>
    class ElPickList : El
    {
        public readonly List<PickEntry> Items = new List<PickEntry>();
        public string EmptyText = "";
        public bool Loading;
        string _loadingText = "正在读取…";
        public event EventHandler SelectionChanged;
        public event EventHandler Activated;
        public event EventHandler Sorted;

        const int HEAD = 26, ROWH = 28, SB = 6;
        int _sel = -1, _hover = -1, _scroll;
        int _sortKey;
        bool _desc;
        bool _thumbDrag;
        int _thumbGrab;
        int _lastRow = -1;
        DateTime _lastAt = DateTime.MinValue;

        public PickEntry Selected { get { return (_sel >= 0 && _sel < Items.Count) ? Items[_sel] : null; } }
        public int SelIndex { get { return _sel; } }
        public void SetLoading(bool on, string text)
        {
            Loading = on;
            if (text != null) _loadingText = text;
            Redraw();
        }

        public void SetItems(List<PickEntry> items)
        {
            Items.Clear();
            if (items != null) Items.AddRange(items);
            Sort();
            _sel = -1;
            _hover = -1;
            _scroll = 0;
            Redraw();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
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

        void SortBy(int key)
        {
            if (_sortKey == key) _desc = !_desc; else { _sortKey = key; _desc = false; }
            Sort();
            _scroll = 0;
            Redraw();
            if (Sorted != null) Sorted(this, EventArgs.Empty);
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        int DateX { get { return Bounds.Right - 8 - 118; } }
        int SizeX { get { return DateX - 78; } }
        int RowY(int i) { return Bounds.Y + HEAD + i * ROWH - _scroll; }
        int MaxScroll { get { return Math.Max(0, Items.Count * ROWH - (Bounds.Height - HEAD)); } }
        bool Scrollable { get { return MaxScroll > 0; } }

        void ScrollTo(int v)
        {
            v = Math.Max(0, Math.Min(MaxScroll, v));
            if (v == _scroll) return;
            _scroll = v;
            Redraw();
        }

        Rectangle Thumb()
        {
            int ty = Bounds.Y + HEAD + 3, th = Bounds.Height - HEAD - 6;
            int total = Math.Max(1, Items.Count * ROWH), view = Bounds.Height - HEAD;
            int hh = Math.Max(24, (int)((double)th * view / total));
            int max = MaxScroll;
            int y = ty + (max <= 0 ? 0 : (int)((double)(th - hh) * _scroll / max));
            return new Rectangle(Bounds.Right - SB - 2, y, SB, hh);
        }

        int RowAt(Point p)
        {
            if (p.Y < Bounds.Y + HEAD) return -1;
            int i = (p.Y - Bounds.Y - HEAD + _scroll) / ROWH;
            return (i >= 0 && i < Items.Count) ? i : -1;
        }

        public void KeyMove(Keys k)
        {
            if (Items.Count == 0) return;
            int i = _sel;
            switch (k)
            {
                case Keys.Up: i = i < 0 ? 0 : Math.Max(0, i - 1); break;
                case Keys.Down: i = i < 0 ? 0 : Math.Min(Items.Count - 1, i + 1); break;
                case Keys.PageUp: i = Math.Max(0, i - (Bounds.Height - HEAD) / ROWH); break;
                case Keys.PageDown: i = Math.Min(Items.Count - 1, i + (Bounds.Height - HEAD) / ROWH); break;
                case Keys.Home: i = 0; break;
                case Keys.End: i = Items.Count - 1; break;
            }
            Select(i);
            int top = i * ROWH, bottom = top + ROWH;
            if (top < _scroll) ScrollTo(top);
            else if (bottom > _scroll + (Bounds.Height - HEAD)) ScrollTo(bottom - (Bounds.Height - HEAD));
        }

        void Select(int i)
        {
            if (i < 0 || i >= Items.Count || i == _sel) return;
            _sel = i;
            Redraw();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        public void ActivateSel()
        {
            if (_sel >= 0 && Activated != null) Activated(this, EventArgs.Empty);
        }

        public override void Paint(Graphics g)
        {
            var st = g.Save();
            T.FillRound(g, new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height), 8, T.LogBg);
            g.SetClip(new Rectangle(Bounds.X + 1, Bounds.Y + 1, Bounds.Width - 2, Bounds.Height - 2));

            // 表头
            using (var b = new SolidBrush(PickCol.Head))
                g.FillRectangle(b, Bounds.X + 1, Bounds.Y + 1, Bounds.Width - 2, HEAD);
            using (var p = new Pen(T.Line))
                g.DrawLine(p, Bounds.X + 1, Bounds.Y + HEAD, Bounds.Right - 1, Bounds.Y + HEAD);

            var fh = T.F(11);
            var fN = T.F(12.5f);
            var fM = T.F(11);
            var sfL = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
            var sfR = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            using (var b = new SolidBrush(T.Text3))
            {
                T.DrawStr(g, "名称" + (_sortKey == 0 ? (_desc ? " ↓" : " ↑") : ""), fh, b, new RectangleF(Bounds.X + 12, Bounds.Y, SizeX - Bounds.X - 12, HEAD), sfL);
                T.DrawStr(g, "大小" + (_sortKey == 1 ? (_desc ? " ↓" : " ↑") : ""), fh, b, new RectangleF(SizeX, Bounds.Y, 70, HEAD), sfR);
                T.DrawStr(g, "修改时间" + (_sortKey == 2 ? (_desc ? " ↓" : " ↑") : ""), fh, b, new RectangleF(DateX, Bounds.Y, 110, HEAD), sfL);
            }

            // 行
            for (int i = 0; i < Items.Count; i++)
            {
                int y = RowY(i);
                if (y + ROWH < Bounds.Y + HEAD) continue;
                if (y > Bounds.Bottom) break;
                var it = Items[i];
                bool sel = i == _sel, hov = i == _hover;
                int right = Scrollable ? Bounds.Right - SB - 6 : Bounds.Right - 6;
                if (sel)
                {
                    T.FillRound(g, new RectangleF(Bounds.X + 4, y + 1, right - Bounds.X - 4, ROWH - 2), 5, T.AccentSoft);
                    using (var b = new SolidBrush(T.Accent)) g.FillRectangle(b, Bounds.X + 4, y + 6, 2.5f, ROWH - 12);
                }
                else if (hov)
                {
                    T.FillRound(g, new RectangleF(Bounds.X + 4, y + 1, right - Bounds.X - 4, ROWH - 2), 5, T.Line2);
                }
                var ico = PickIcons.Get(it);
                if (ico != null) g.DrawImage(ico, Bounds.X + 10, y + (ROWH - 18) / 2, 18, 18);
                using (var b = new SolidBrush(T.Text))
                    T.DrawStr(g, Ell(g, it.Name, fN, SizeX - Bounds.X - 40), fN, b, new RectangleF(Bounds.X + 32, y, SizeX - Bounds.X - 34, ROWH), sfL);
                using (var b = new SolidBrush(T.Text3))
                {
                    if (!it.IsDir) T.DrawStr(g, Pk.Size(it.Size), fM, b, new RectangleF(SizeX, y, 70, ROWH), sfR);
                    T.DrawStr(g, it.Note != null ? it.Note : it.Modified.ToString("yyyy-MM-dd  HH:mm"), fM, b,
                        new RectangleF(DateX, y, 112, ROWH), sfL);
                }
            }

            if (Items.Count == 0 && !Loading)
                using (var b = new SolidBrush(T.Text3))
                    T.DrawStr(g, EmptyText, T.F(12), b, new RectangleF(Bounds.X + 14, Bounds.Y + HEAD + 18, Bounds.Width - 28, 22), sfL);
            if (Loading)
            {
                string t = _loadingText;
                using (var b = new SolidBrush(T.Text3))
                {
                    var sz = g.MeasureString(t, T.F(12));
                    T.DrawStr(g, t, T.F(12), b, Bounds.X + (Bounds.Width - sz.Width) / 2f, Bounds.Y + HEAD + 24);
                }
            }
            if (Scrollable)
                using (var b = new SolidBrush(T.Text3))
                    g.FillRectangle(b, Thumb());
            g.Restore(st);
        }

        static string Ell(Graphics g, string s, Font f, float maxW)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (g.MeasureString(s, f).Width <= maxW) return s;
            string t = s;
            while (t.Length > 1 && g.MeasureString(t + "…", f).Width > maxW) t = t.Substring(0, t.Length - 1);
            return t + "…";
        }

        public override void OnMouseMove(Point p)
        {
            if (_thumbDrag)
            {
                int ty = Bounds.Y + HEAD + 3, th = Bounds.Height - HEAD - 6, hh = Thumb().Height;
                int want = p.Y - _thumbGrab - ty;
                int max = MaxScroll;
                if (max > 0 && th - hh > 0) ScrollTo((int)((double)want * max / (th - hh)));
                return;
            }
            int i = RowAt(p);
            if (i != _hover) { _hover = i; Redraw(); }
        }
        public override void OnMouseLeave() { if (_hover != -1) { _hover = -1; Redraw(); } }
        public override Cursor CursorOf(Point p) { return Cursors.Hand; }

        public override void OnMouseDown(Point p)
        {
            if (p.Y < Bounds.Y + HEAD)
            {
                if (p.X < SizeX - 20) SortBy(0);
                else if (p.X < DateX - 20) SortBy(1);
                else SortBy(2);
                return;
            }
            if (Scrollable && Thumb().Contains(p)) { _thumbDrag = true; _thumbGrab = p.Y - Thumb().Y; return; }
            int i = RowAt(p);
            if (i >= 0) Select(i);
        }

        public override void OnMouseUp(Point p)
        {
            if (_thumbDrag) { _thumbDrag = false; return; }
            int i = RowAt(p);
            if (i < 0 || i != _sel) { _lastRow = -1; return; }
            bool again = (i == _lastRow) && (DateTime.Now - _lastAt).TotalMilliseconds < 450;
            _lastRow = i; _lastAt = DateTime.Now;
            if (again && Activated != null) Activated(this, EventArgs.Empty);
        }

        public override bool OnMouseWheel(int delta)
        {
            ScrollTo(_scroll - Math.Sign(delta) * ROWH * 3);
            return true;
        }
    }
}
