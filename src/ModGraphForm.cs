using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Zogos
{
    public class ModGraphForm : Form
    {
        private PackContext _ctx;
        private Settings _st;
        private SortScheme _scheme;
        private ModGraphControl _graph;
        private Label _lbTitle, _lbStatus, _lbHint, _lbListTitle;
        private ZButton _btnClassify, _btnDetect, _btnSave, _btnIgnore, _btnClose, _btnSearch;
        private TextBox _txtSearch;
        private CheckBox _chkSearchTrans;
        private ComboBox _cboMatches;
        private ListView _list;
        private LoadingOverlay _overlay;
        private ZButton _btnTranslate;
        private bool _fillingList;
        private bool _showZh;
        public string SavedSchemePath;
        public bool LaunchAfter;

        public ModGraphForm(PackContext ctx, Settings st, SortScheme scheme)
        {
            _ctx = ctx; _st = st; _scheme = scheme;
            Text = L.T("Astra 模组管理 · 依赖网状图");
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(900, 600);
            BackColor = Color.FromArgb(4, 7, 16);
            Font = Fonts.F(9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _lbTitle = new Label();
            _lbTitle.Text = L.T("模组管理 / 依赖网状图");
            _lbTitle.Font = Fonts.B(13f);
            _lbTitle.ForeColor = Pal.Accent;
            _lbTitle.Bounds = new Rectangle(22, 12, 420, 28);
            Controls.Add(_lbTitle);

            _btnClassify = MkBtn(L.T("AI 分类并生成网状图"), 210, 32);
            _btnClassify.Click += delegate(object s, EventArgs e) { Classify(); };
            Controls.Add(_btnClassify);

            _btnDetect = MkBtn(L.T("AI 检测并启动"), 160, 32);
            _btnDetect.Click += delegate(object s, EventArgs e) { DetectAndLaunch(); };
            Controls.Add(_btnDetect);

            _lbHint = new Label();
            _lbHint.Text = L.T("滚轮缩放 · 拖拽平移 · 左键节点详情 · 右键节点启停 · 红色节点表示前置被关闭 · 放大后显示模组缩略图");
            _lbHint.Font = Fonts.F(8.5f);
            _lbHint.ForeColor = Pal.TextSub;
            _lbHint.AutoEllipsis = true;
            Controls.Add(_lbHint);

            _txtSearch = new TextBox();
            _txtSearch.Font = Fonts.F(9.5f);
            _txtSearch.BorderStyle = BorderStyle.FixedSingle;
            _txtSearch.BackColor = Color.FromArgb(8, 15, 28);
            _txtSearch.ForeColor = Color.White;
            _txtSearch.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SearchMods(); } };
            Controls.Add(_txtSearch);

            _btnSearch = MkBtn(L.T("搜索模组"), 100, 30);
            _btnSearch.Click += delegate(object s, EventArgs e) { SearchMods(); };
            Controls.Add(_btnSearch);

            _chkSearchTrans = new CheckBox();
            _chkSearchTrans.Text = L.T("中文自动转英文");
            _chkSearchTrans.Checked = true;
            _chkSearchTrans.Font = Fonts.F(8.5f);
            _chkSearchTrans.ForeColor = Pal.TextSub;
            _chkSearchTrans.BackColor = Color.Transparent;
            Controls.Add(_chkSearchTrans);

            _cboMatches = new ComboBox();
            _cboMatches.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboMatches.FlatStyle = FlatStyle.Flat;
            _cboMatches.Font = Fonts.F(9f);
            _cboMatches.BackColor = Color.FromArgb(8, 15, 28);
            _cboMatches.ForeColor = Color.White;
            _cboMatches.SelectedIndexChanged += delegate(object s, EventArgs e)
            {
                if (_cboMatches.SelectedItem == null) return;
                string sel = Convert.ToString(_cboMatches.SelectedItem);
                int bar = sel.IndexOf("  |  ");
                string id = bar >= 0 ? sel.Substring(bar + 5).Trim() : sel.Trim();
                _graph.FocusNode(id, true);
                _lbStatus.Text = L.T("已跳转到：") + sel;
                _lbStatus.ForeColor = Pal.Accent;
            };
            Controls.Add(_cboMatches);

            _graph = new ModGraphControl();
            _graph.NodeSelected += delegate(object s, EventArgs e) { OpenDetail(); };
            _graph.NodeToggled += delegate(object s, EventArgs e) { ShowWarnings(); FillModList(); };
            Controls.Add(_graph);

            // ── 右侧：排序列表（启用/关闭一目了然）──
            _lbListTitle = new Label();
            _lbListTitle.Text = L.T("排序列表");
            _lbListTitle.Font = Fonts.B(9f);
            _lbListTitle.ForeColor = Pal.Accent;
            Controls.Add(_lbListTitle);

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.CheckBoxes = true;
            _list.ShowItemToolTips = true;
            _list.HideSelection = false;
            _list.Font = Fonts.F(8.5f);
            _list.BackColor = Color.FromArgb(7, 13, 27);
            _list.ForeColor = Color.White;
            _list.Columns.Add("#", 42);
            _list.Columns.Add(L.T("模组"), 168);
            _list.Columns.Add(L.T("分类"), 72);
            _list.ItemChecked += delegate(object s, ItemCheckedEventArgs e)
            {
                if (_fillingList) return;
                ModGraphNode node = e.Item.Tag as ModGraphNode;
                if (node == null) return;
                if (node.Enabled == e.Item.Checked) return;
                node.Enabled = e.Item.Checked;
                ModGraphBuilder.RecomputeWarnings(_graph.Data);
                _graph.Invalidate();
                UpdateListTitle();
                ShowWarnings();
            };
            _list.SelectedIndexChanged += delegate(object s, EventArgs e)
            {
                if (_fillingList) return;
                if (_list.SelectedItems.Count == 0) return;
                ModGraphNode node = _list.SelectedItems[0].Tag as ModGraphNode;
                if (node != null) _graph.FocusNode(node.Id, true);
            };
            _list.DoubleClick += delegate(object s, EventArgs e) { OpenDetail(); };
            Controls.Add(_list);

            _lbStatus = new Label();
            _lbStatus.Font = Fonts.F(8.5f);
            _lbStatus.ForeColor = Pal.TextSub;
            _lbStatus.AutoEllipsis = true;
            Controls.Add(_lbStatus);

            _btnSave = MkBtn(L.T("信任此排序方案并保存"), 240, 36);
            _btnSave.Click += delegate(object s, EventArgs e)
            {
                if (SaveCurrentScheme(true)) { DialogResult = DialogResult.OK; Close(); }
            };
            Controls.Add(_btnSave);

            _btnIgnore = MkBtn(L.T("无视风险启动"), 160, 36);
            _btnIgnore.Click += delegate(object s, EventArgs e)
            {
                if (SaveCurrentScheme(false)) { LaunchAfter = true; DialogResult = DialogResult.OK; Close(); }
            };
            Controls.Add(_btnIgnore);

            _btnClose = MkBtn(L.T("关闭"), 90, 36);
            _btnClose.Click += delegate(object s, EventArgs e) { Close(); };
            Controls.Add(_btnClose);

            _btnTranslate = MkBtn(L.T("翻译模组名"), 160, 36);
            _btnTranslate.Click += delegate(object s, EventArgs e) { ToggleTranslate(); };
            Controls.Add(_btnTranslate);

            _overlay = new LoadingOverlay();
            _overlay.Bounds = ClientRectangle;
            Controls.Add(_overlay);

            Resize += delegate(object s, EventArgs e) { LayoutControls(); };
            LayoutControls();
            UseWaitCursor = false;

            // 初次建图放到后台，避免打开窗口就卡住
            BuildGraphAsync(null, L.T("正在扫描本地模组并生成网状图…"));
        }

        // =================================================================
        //  布局
        // =================================================================
        private void LayoutControls()
        {
            int w = ClientSize.Width, h = ClientSize.Height;

            _btnClassify.Bounds = new Rectangle(w - 530, 10, 210, 32);
            _btnDetect.Bounds = new Rectangle(w - 310, 10, 160, 32);

            _lbHint.Bounds = new Rectangle(22, 44, w - 44, 20);

            _txtSearch.Bounds = new Rectangle(22, 70, 300, 28);
            _btnSearch.Bounds = new Rectangle(330, 69, 100, 30);
            _chkSearchTrans.Bounds = new Rectangle(438, 72, 140, 22);
            _cboMatches.Bounds = new Rectangle(586, 69, Math.Max(180, w - 586 - 22), 28);

            int listW = 320;
            int top = 110;
            int bottom = h - 112;
            int graphH = Math.Max(200, bottom - top);

            _graph.Bounds = new Rectangle(22, top, Math.Max(240, w - 44 - listW - 8), graphH);
            _lbListTitle.Bounds = new Rectangle(_graph.Right + 8, top, listW, 20);
            _list.Bounds = new Rectangle(_graph.Right + 8, top + 24, listW, Math.Max(160, graphH - 24));

            _lbStatus.Bounds = new Rectangle(22, h - 96, w - 44, 22);
            _btnTranslate.Bounds = new Rectangle(22, h - 58, 160, 36);
            _btnSave.Bounds = new Rectangle(w - 540, h - 58, 240, 36);
            _btnIgnore.Bounds = new Rectangle(w - 290, h - 58, 160, 36);
            _btnClose.Bounds = new Rectangle(w - 120, h - 58, 90, 36);

            if (_overlay != null) _overlay.Bounds = ClientRectangle;
        }

        private ZButton MkBtn(string text, int w, int h)
        {
            ZButton b = new ZButton();
            b.Text = text;
            b.SetFont(Fonts.B(9f));
            b.BackNormal = Color.FromArgb(10, 18, 32);
            b.BackHover = Color.FromArgb(18, 48, 74);
            b.BackDown = Color.FromArgb(8, 36, 60);
            b.BorderColor = Pal.Accent;
            b.TextColor = Pal.TextMain;
            b.CornerColor = Color.FromArgb(4, 7, 16);
            b.Size = new Size(w, h);
            return b;
        }

        private void SetBusy(bool busy, string text)
        {
            _btnClassify.Enabled = !busy;
            _btnDetect.Enabled = !busy;
            _btnSave.Enabled = !busy;
            _btnIgnore.Enabled = !busy;
            _btnSearch.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (_overlay != null)
            {
                if (busy) _overlay.ShowOverlay(L.T("正在加载中，请稍候…"), text);
                else _overlay.HideOverlay();
            }
        }

        // =================================================================
        //  排序列表
        // =================================================================
        private void FillModList()
        {
            if (_list == null || _graph == null || _graph.Data == null) return;
            _fillingList = true;
            try
            {
                _list.BeginUpdate();
                _list.Items.Clear();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int idx = 0;
                if (_scheme != null && _scheme.Mods != null)
                {
                    for (int i = 0; i < _scheme.Mods.Count; i++)
                    {
                        string id = _scheme.Mods[i] == null ? "" : _scheme.Mods[i].Trim();
                        if (id.Length == 0 || !seen.Add(id)) continue;
                        ModGraphNode n = _graph.FindNode(id);
                        if (n == null) continue;
                        idx++;
                        AddListRow(idx, n);
                    }
                }
                for (int i = 0; i < _graph.Data.Nodes.Count; i++)
                {
                    ModGraphNode n = _graph.Data.Nodes[i];
                    if (!seen.Add(n.Id)) continue;
                    idx++;
                    AddListRow(idx, n);
                }
                _list.EndUpdate();
            }
            catch { }
            finally { _fillingList = false; }
            UpdateListTitle();
        }

        private void AddListRow(int idx, ModGraphNode n)
        {
            string en = n.Name == null ? n.Id : n.Name;
            string shown = string.IsNullOrEmpty(n.NameZh) ? en : (n.NameZh + "  |  " + en);
            string tip = string.IsNullOrEmpty(n.NameZh)
                ? en + "\r\n" + n.Id
                : L.T("中文：") + n.NameZh + L.T("\r\n原名：") + en + "\r\nID：" + n.Id;
            ListViewItem row = new ListViewItem(idx.ToString());
            row.SubItems.Add(shown);
            row.ToolTipText = tip;
            row.SubItems.Add(n.Category == null ? "" : n.Category);
            row.Checked = n.Enabled;
            row.Tag = n;
            if (n.Warning) row.ForeColor = Color.FromArgb(255, 120, 140);
            else if (!n.Enabled) row.ForeColor = Pal.TextFaint;
            _list.Items.Add(row);
        }

        private void UpdateListTitle()
        {
            if (_lbListTitle == null || _graph == null || _graph.Data == null) return;
            int on = 0, total = _graph.Data.Nodes.Count;
            for (int i = 0; i < total; i++) if (_graph.Data.Nodes[i].Enabled) on++;
            _lbListTitle.Text = L.T("排序列表 · 启用 ") + on + L.T(" / 关闭 ") + (total - on);
        }

        // =================================================================
        //  中文名对照
        // =================================================================
        private void ToggleTranslate()
        {
            if (_graph == null || _graph.Data == null) return;

            // 已经在显示 -> 收起
            if (_showZh)
            {
                _showZh = false;
                ApplyZh(false);
                _btnTranslate.Text = L.T("翻译模组名");
                _btnTranslate.Invalidate();
                _lbStatus.Text = L.T("已切回英文原名。");
                _lbStatus.ForeColor = Pal.TextSub;
                return;
            }

            ModNameZh.Load();

            // 先统计还有多少没翻过
            List<ModGraphNode> need = new List<ModGraphNode>();
            for (int i = 0; i < _graph.Data.Nodes.Count; i++)
            {
                ModGraphNode n = _graph.Data.Nodes[i];
                if (n == null || string.IsNullOrEmpty(n.Name)) continue;
                if (ModNameZh.HasChinese(n.Name)) continue;
                if (!string.IsNullOrEmpty(ModNameZh.Get(n.Id))) continue;
                need.Add(n);
            }

            if (need.Count == 0)
            {
                _showZh = true;
                ApplyZh(true);
                _btnTranslate.Text = L.T("隐藏中文译名");
                _btnTranslate.Invalidate();
                _lbStatus.Text = L.T("已显示中文对照（缓存里都有了，不用再翻）。");
                _lbStatus.ForeColor = Color.FromArgb(0, 220, 170);
                return;
            }

            SetBusy(true, L.T("正在翻译 ") + need.Count + L.T(" 个模组名…"));
            ModGraphData data = _graph.Data;
            Settings st = _st;
            BackgroundWorker bw = new BackgroundWorker();
            bw.WorkerReportsProgress = true;
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                BackgroundWorker me = (BackgroundWorker)s;
                Action<int, int, string> prog = delegate(int done, int total, string msg)
                {
                    try { me.ReportProgress(total <= 0 ? 0 : done * 100 / total, msg); }
                    catch { }
                };
                string err;
                int got = ModNameZh.TranslateMissing(data.Nodes, st, prog, out err);
                e.Result = new object[] { got, err };
            };
            bw.ProgressChanged += delegate(object s, ProgressChangedEventArgs e)
            {
                string msg = e.UserState as string;
                if (!string.IsNullOrEmpty(msg) && _overlay != null && _overlay.Visible)
                    _overlay.ShowOverlay(L.T("正在加载中，请稍候…"), msg);
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, "");
                object[] arr = e.Result as object[];
                int got = arr == null ? 0 : Convert.ToInt32(arr[0]);
                string err = arr == null ? "" : Convert.ToString(arr[1]);

                _showZh = true;
                ApplyZh(true);
                _btnTranslate.Text = L.T("隐藏中文译名");
                _btnTranslate.Invalidate();

                int withZh = 0;
                for (int i = 0; i < _graph.Data.Nodes.Count; i++)
                    if (!string.IsNullOrEmpty(_graph.Data.Nodes[i].NameZh)) withZh++;

                if (!string.IsNullOrEmpty(err))
                {
                    _lbStatus.Text = L.T("翻译完成：") + withZh + L.T(" 个有中文对照（新增 ") + got + L.T("）。部分失败：") + err;
                    _lbStatus.ForeColor = Color.FromArgb(255, 180, 60);
                }
                else
                {
                    _lbStatus.Text = L.T("翻译完成：新增 ") + got + L.T(" 个，共 ") + withZh + L.T(" 个有中文对照。结果已缓存，下次不用再翻。");
                    _lbStatus.ForeColor = Color.FromArgb(0, 220, 170);
                }
            };
            bw.RunWorkerAsync();
        }

        private void ApplyZh(bool show)
        {
            if (_graph == null || _graph.Data == null) return;
            for (int i = 0; i < _graph.Data.Nodes.Count; i++)
            {
                ModGraphNode n = _graph.Data.Nodes[i];
                if (n == null) continue;
                n.NameZh = show ? ModNameZh.Get(n.Id) : null;
            }
            _graph.Invalidate();
            FillModList();
        }

        // =================================================================
        //  搜索
        // =================================================================
        private void SearchMods()
        {
            if (_graph == null || _graph.Data == null) return;
            string q = _txtSearch.Text.Trim();
            if (q.Length == 0) { _lbStatus.Text = L.T("请输入模组关键词，例如：鼠族、武器、界面。"); return; }
            string search = q;
            string terr = null;
            bool usedAi = false;
            if (_chkSearchTrans.Checked && HasChinese(q))
                search = WorkshopClient.TranslateQuery(_st, q, out usedAi, out terr);

            // 搜索词可能是一串，按词逐个匹配
            string[] tokens = search.Split(new char[] { ' ', ',', ';', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) tokens = new string[] { search };

            _cboMatches.Items.Clear();
            int count = 0;
            for (int i = 0; i < _graph.Data.Nodes.Count; i++)
            {
                ModGraphNode n = _graph.Data.Nodes[i];
                string name = n.Name == null ? "" : n.Name;
                string id = n.Id == null ? "" : n.Id;
                bool hit = false;
                for (int t = 0; t < tokens.Length && !hit; t++)
                {
                    string tk = tokens[t].Trim();
                    if (tk.Length < 2) continue;
                    if (name.IndexOf(tk, StringComparison.OrdinalIgnoreCase) >= 0
                        || id.IndexOf(tk, StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
                }
                if (!hit && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
                if (!hit) continue;
                _cboMatches.Items.Add(name + "  |  " + id);
                count++;
                if (count >= 200) break;
            }
            string msg = L.T("搜索：") + q + (string.IsNullOrEmpty(search) || search == q ? "" : " → " + search) + L.T("  找到 ") + count + L.T(" 个");
            if (usedAi) msg += L.T("  ·  已用 AI 翻译");
            if (!string.IsNullOrEmpty(terr)) msg += "  ·  " + terr;
            _lbStatus.Text = msg;
            _lbStatus.ForeColor = Pal.TextSub;
            if (_cboMatches.Items.Count > 0)
            {
                _cboMatches.SelectedIndex = 0;
                string sel = Convert.ToString(_cboMatches.SelectedItem);
                int bar = sel.IndexOf("  |  ");
                string id = bar >= 0 ? sel.Substring(bar + 5).Trim() : sel.Trim();
                _graph.FocusNode(id, true);
            }
        }

        private bool HasChinese(string s)
        {
            for (int i = 0; i < s.Length; i++) if (s[i] >= 0x4E00 && s[i] <= 0x9FFF) return true;
            return false;
        }

        private void OpenDetail()
        {
            if (_graph == null || _graph.Data == null) return;
            string sel = _graph.SelectedId;
            if (string.IsNullOrEmpty(sel) && _list.SelectedItems.Count > 0)
            {
                ModGraphNode picked = _list.SelectedItems[0].Tag as ModGraphNode;
                if (picked != null) sel = picked.Id;
            }
            if (string.IsNullOrEmpty(sel)) return;
            ModGraphNode node = _graph.FindNode(sel);
            if (node == null) return;
            using (ModDetailForm f = new ModDetailForm(node, _graph.Data, _st, delegate(bool enabled)
            {
                ShowWarnings();
                _graph.Invalidate();
                FillModList();
            }))
            {
                f.ShowDialog(this);
            }
            ShowWarnings();
            FillModList();
        }

        // =================================================================
        //  建图
        // =================================================================
        private void Classify()
        {
            SetBusy(true, L.T("AI 正在分类模组，模组多时可能需要几十秒…"));
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                string err;
                Dictionary<string, string> cats = ModClassifier.Classify(_ctx, _scheme, _st, out err);
                e.Result = new object[] { cats, err };
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, "");
                object[] arr = e.Result as object[];
                Dictionary<string, string> cats = arr == null ? null : arr[0] as Dictionary<string, string>;
                string err = arr == null ? L.T("分类失败") : Convert.ToString(arr[1]);
                BuildGraphAsync(cats, L.T("AI 分类完成") + (string.IsNullOrEmpty(err) ? "" : "（" + err + "）"));
            };
            bw.RunWorkerAsync();
        }

        private void BuildGraphAsync(Dictionary<string, string> cats, string status)
        {
            SetBusy(true, status);
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                e.Result = ModGraphBuilder.Build(_ctx, _scheme, cats);
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, "");
                ModGraphData data = e.Result as ModGraphData;
                if (data == null || data.Nodes.Count == 0)
                {
                    _lbStatus.Text = L.T("生成网状图失败，或者当前排序方案里没有模组。");
                    _lbStatus.ForeColor = Color.FromArgb(255, 120, 140);
                    return;
                }
                _graph.SetData(data);
                _lbStatus.Text = status + L.T("  ·  模组 ") + data.Nodes.Count + L.T(" 个  ·  依赖边 ") + data.Edges.Count + L.T(" 条");
                _lbStatus.ForeColor = Pal.TextSub;
                ShowWarnings();
                FillModList();
            };
            bw.RunWorkerAsync();
        }

        private void ShowWarnings()
        {
            if (_graph == null || _graph.Data == null) return;
            ModGraphBuilder.RecomputeWarnings(_graph.Data);
            int n = _graph.Data.Warnings.Count;
            if (n > 0)
            {
                string first = _graph.Data.Warnings[0];
                _lbStatus.Text = L.T("警告：") + n + L.T(" 个模组的前置被关闭。例如：") + first;
                _lbStatus.ForeColor = Color.FromArgb(255, 90, 110);
            }
            else
            {
                _lbStatus.Text = L.T("依赖检查：当前没有发现前置被关闭的模组。");
                _lbStatus.ForeColor = Color.FromArgb(0, 220, 170);
            }
            _graph.Invalidate();
        }

        private SortScheme CurrentEnabledScheme()
        {
            SortScheme s = new SortScheme();
            s.Name = (_scheme == null ? L.T("AI网状排序") : _scheme.Name + L.T(" · 网状排序"));
            s.Version = _scheme == null ? "" : _scheme.Version;
            s.Expansions = _scheme == null ? new List<string>() : new List<string>(_scheme.Expansions);
            for (int i = 0; i < _graph.Data.Nodes.Count; i++)
            {
                ModGraphNode n = _graph.Data.Nodes[i];
                if (n.Enabled) s.Mods.Add(n.Id);
            }
            return s;
        }

        private bool SaveCurrentScheme(bool showResult)
        {
            try
            {
                SortScheme temp = CurrentEnabledScheme();
                if (temp.Mods.Count == 0) { MinMsg.Show(this, L.T("所有模组都被关闭了。"), L.T("保存失败"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
                ModAnalysisResult analysis = ModAnalyzer.Analyze(_ctx, temp);
                ModAnalyzer.AnalyzePlayerLog(_ctx, temp, analysis);
                string path = ModAnalyzer.WriteSuggestedScheme(_ctx, temp, analysis);
                if (string.IsNullOrEmpty(path)) { MinMsg.Show(this, L.T("写入排序方案失败，详见日志。"), L.T("保存失败"), MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
                SavedSchemePath = path;
                _st.LastScheme = path;
                _st.Save();
                if (showResult) MinMsg.Show(this, L.T("已保存排序方案：\r\n") + path + L.T("\r\n\r\n下次启动会默认使用它。"), L.T("保存成功"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                MinMsg.Show(this, L.T("保存排序失败：") + ex.Message, L.T("保存失败"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void DetectAndLaunch()
        {
            if (_graph == null || _graph.Data == null) return;
            SortScheme temp = CurrentEnabledScheme();
            if (temp.Mods.Count == 0) { MinMsg.Show(this, L.T("所有模组都被关闭了。"), L.T("无法启动"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            SetBusy(true, L.T("AI 正在检测模组依赖与风险…"));
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                ModAnalysisResult analysis = ModAnalyzer.Analyze(_ctx, temp);
                ModAnalyzer.AnalyzePlayerLog(_ctx, temp, analysis);
                string aiErr;
                string ai = ModClassifier.AiRiskCheck(_ctx, temp, analysis, _st, out aiErr);
                e.Result = new object[] { analysis, ai, aiErr };
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, "");
                object[] arr = e.Result as object[];
                if (arr == null) { _lbStatus.Text = L.T("检测失败。"); return; }
                ModAnalysisResult analysis = arr[0] as ModAnalysisResult;
                string ai = Convert.ToString(arr[1]);
                string aiErr = Convert.ToString(arr[2]);
                bool risky = analysis == null || analysis.HasErrors || analysis.Suspects.Count > 0
                    || (ai != null && ai.StartsWith("RISK", StringComparison.OrdinalIgnoreCase));
                if (risky)
                {
                    string msg = (analysis == null ? "" : analysis.SummaryText()) + "\r\nAI：" + ai
                        + (string.IsNullOrEmpty(aiErr) ? "" : "（" + aiErr + "）")
                        + L.T("\r\n\r\n检测到风险。你可以选择【无视风险启动】，或先调整网状图中的模组。");
                    MinMsg.Show(this, msg, L.T("AI 检测发现风险"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (SaveCurrentScheme(false))
                {
                    LaunchAfter = true;
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };
            bw.RunWorkerAsync();
        }
    }
}
