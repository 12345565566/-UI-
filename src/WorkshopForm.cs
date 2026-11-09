using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Zogos
{
    public class WorkshopForm : Form
    {
        private Settings _st;
        private PackContext _ctx;
        private ComboBox _cboVersion, _cboType;
        private Image _bgImage;
        private System.Windows.Forms.Timer _bgTimer;
        private TextBox _txtQuery;
        private CheckBox _chkTranslate;
        private ListView _list;
        private Label _lbStatus;
        private ZButton _btnSearch, _btnOpen, _btnDownload, _btnAi, _btnClose, _btnSync;
        private BackgroundWorker _worker;
        private LoadingOverlay _overlay;

        public WorkshopForm(Settings st, PackContext ctx)
        {
            _st = st;
            _ctx = ctx;
            Text = L.T("RimWorld 创意工坊搜索");
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(980, 640);
            MinimumSize = new Size(980, 520);
            BackColor = Color.FromArgb(4, 7, 16);
            Font = Fonts.F(9f);
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label title = new Label();
            title.Text = L.T("RimWorld 创意工坊搜索 / 下载");
            title.Font = Fonts.B(13f);
            title.ForeColor = Color.FromArgb(198, 62, 112);
            title.Bounds = new Rectangle(22, 14, 500, 30);
            Controls.Add(title);

            Label desc = new Label();
            desc.Text = L.T("输入中文也可以：勾选自动翻译后，会调用苏苏里配置的 AI 接口翻成英文再搜。下载按钮通过 Steam 订阅，订阅后 RimWorld 会直接加载。");
            desc.Font = Fonts.F(8.5f);
            desc.ForeColor = Color.FromArgb(165, 200, 220);
            desc.Bounds = new Rectangle(22, 44, 930, 22);
            Controls.Add(desc);

            _txtQuery = new TextBox();
            _txtQuery.Font = Fonts.F(10f);
            _txtQuery.BorderStyle = BorderStyle.None;
            _txtQuery.BackColor = Color.FromArgb(8, 15, 28);
            _txtQuery.ForeColor = Color.White;
            _txtQuery.Bounds = new Rectangle(22, 76, 500, 28);
            _txtQuery.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(); } };
            Controls.Add(_txtQuery);

            _chkTranslate = new CheckBox();
            _chkTranslate.Text = L.T("中文自动翻译成英文");
            _chkTranslate.Checked = true;
            _chkTranslate.Font = Fonts.F(8.5f);
            _chkTranslate.ForeColor = Color.FromArgb(225, 246, 255);
            _chkTranslate.Bounds = new Rectangle(536, 78, 180, 24);
            Controls.Add(_chkTranslate);

            _cboVersion = new ComboBox();
            _cboVersion.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboVersion.Font = Fonts.F(9f);
            _cboVersion.BackColor = Color.FromArgb(8, 15, 28);
            _cboVersion.ForeColor = Color.White;
            _cboVersion.Items.AddRange(new object[] { L.T("全部版本"), "1.7", "1.6", "1.5", "1.4" });
            _cboVersion.SelectedIndex = 0;
            Controls.Add(_cboVersion);

            _cboType = new ComboBox();
            _cboType.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboType.Font = Fonts.F(9f);
            _cboType.BackColor = Color.FromArgb(8, 15, 28);
            _cboType.ForeColor = Color.White;
            _cboType.Items.AddRange(new object[] { L.T("模组"), L.T("合集") });
            _cboType.SelectedIndex = 0;
            Controls.Add(_cboType);

            _btnSearch = MkBtn(L.T("搜索创意工坊"), 128, 30);
            _btnSearch.Click += delegate(object s, EventArgs e) { DoSearch(); };
            Controls.Add(_btnSearch);

            _btnAi = MkBtn(L.T("AI 接口设置"), 120, 30);
            _btnAi.Click += delegate(object s, EventArgs e) { OpenAiSetup(); };
            Controls.Add(_btnAi);

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.GridLines = false;
            _list.HideSelection = false;
            _list.Font = Fonts.F(9f);
            _list.BackColor = Color.FromArgb(7, 13, 27);
            _list.ForeColor = Color.White;
            _list.Columns.Add(L.T("模组"), 360);
            _list.Columns.Add(L.T("订阅"), 80);
            _list.Columns.Add(L.T("简介"), 480);
            _list.DoubleClick += delegate(object s, EventArgs e) { OpenSelectedPage(); };
            Controls.Add(_list);

            _lbStatus = new Label();
            _lbStatus.Text = L.T("输入关键词后点搜索。中文会自动翻译。");
            _lbStatus.Font = Fonts.B(9f);
            _lbStatus.ForeColor = Color.FromArgb(252, 253, 255);
            _lbStatus.BackColor = Color.FromArgb(10, 14, 24);
            _lbStatus.AutoEllipsis = true;
            Controls.Add(_lbStatus);

            _btnDownload = MkBtn(L.T("通过 Steam 下载 / 订阅"), 190, 34);
            _btnDownload.Click += delegate(object s, EventArgs e) { DownloadSelected(); };
            Controls.Add(_btnDownload);

            _btnSync = MkBtn(L.T("工坊同步 / 按排序补齐"), 196, 34);
            _btnSync.Click += delegate(object s, EventArgs e) { OpenSync(); };
            Controls.Add(_btnSync);

            _btnOpen = MkBtn(L.T("打开创意工坊页面"), 160, 34);
            _btnOpen.Click += delegate(object s, EventArgs e) { OpenSelectedPage(); };
            Controls.Add(_btnOpen);

            _btnClose = MkBtn(L.T("关闭"), 90, 34);
            _btnClose.Click += delegate(object s, EventArgs e) { Close(); };
            FormClosed += delegate(object s, FormClosedEventArgs e) { try { if (_bgTimer != null) { _bgTimer.Stop(); _bgTimer.Dispose(); } } catch { } };
            Controls.Add(_btnClose);

            _overlay = new LoadingOverlay();
            _overlay.Bounds = ClientRectangle;
            Controls.Add(_overlay);

            Resize += delegate(object s, EventArgs e) { LayoutControls(); };
            LayoutControls();
            InitBackground();
            // InitBackground 里把所有 Label 设成了透明，这里再把状态条压回深色底，
            // 否则深色小字压在花哨背景上根本看不清。
            _lbStatus.BackColor = Color.FromArgb(10, 14, 24);
        }

        private void InitBackground()
        {
            try
            {
                if (_st == null) return;
                if (_st.BgMode == 4 && !string.IsNullOrEmpty(_st.CustomVideo))
                {
                    string dir = Path.GetDirectoryName(_st.CustomVideo);
                    string gif = Path.Combine(dir, L.T("自定义背景视频_兼容.gif"));
                    if (File.Exists(gif)) _bgImage = Image.FromFile(gif);
                }
                else if (_st.BgMode == 1) _bgImage = AppLogo.GetByName("bg1");
                else if (_st.BgMode == 2) _bgImage = AppLogo.GetByName("bg2");
                else if (_st.BgMode == 3 && !string.IsNullOrEmpty(_st.CustomBg) && File.Exists(_st.CustomBg)) _bgImage = AppLogo.LoadFile(_st.CustomBg);
            }
            catch { }
            // 背景图是静态的：Image.FromFile 加载 GIF 只会拿到第一帧，不会播。
            // 所以有背景图时逐帧重绘毫无意义 —— 之前每 90ms 把整张背景缩放重画一遍，
            // 正是这个窗口频闪的根源。没有背景图（纯渐变）才需要动画。
            if (_bgImage == null)
            {
                _bgTimer = new System.Windows.Forms.Timer();
                _bgTimer.Interval = 150;
                _bgTimer.Tick += delegate(object s, EventArgs e) { if (IsHandleCreated && !IsDisposed) Invalidate(); };
                _bgTimer.Start();
            }
            foreach (Control c in Controls)
            {
                if (c is Label) c.BackColor = Color.Transparent;
            }
            Invalidate();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED：整窗离屏合成，子控件不再跟着闪
                return cp;
            }
        }

        private Bitmap _bgScaled;
        private Size _bgScaledFor = Size.Empty;

        /// <summary>把背景图按窗口大小预缩放一次并缓存，避免每次重绘都缩放原图。</summary>
        private void EnsureScaledBg(int w, int h)
        {
            if (_bgImage == null) { _bgScaled = null; return; }
            if (_bgScaled != null && _bgScaledFor.Width == w && _bgScaledFor.Height == h) return;
            try
            {
                float sc = Math.Max((float)w / _bgImage.Width, (float)h / _bgImage.Height);
                int dw = Math.Max(1, (int)Math.Round(_bgImage.Width * sc));
                int dh = Math.Max(1, (int)Math.Round(_bgImage.Height * sc));
                Bitmap nb = new Bitmap(dw, dh);
                using (Graphics ng = Graphics.FromImage(nb))
                {
                    ng.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    ng.DrawImage(_bgImage, new Rectangle(0, 0, dw, dh));
                }
                if (_bgScaled != null) _bgScaled.Dispose();
                _bgScaled = nb;
                _bgScaledFor = new Size(w, h);
            }
            catch { _bgScaled = null; }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            try
            {
                Rectangle r = ClientRectangle;
                CyberTheme.DrawBackdrop(e.Graphics, r, 0f, Pal.Accent);
                if (_bgImage != null)
                {
                    EnsureScaledBg(r.Width, r.Height);
                    Image draw = _bgScaled != null ? (Image)_bgScaled : _bgImage;
                    int dx = r.X + (r.Width - draw.Width) / 2;
                    int dy = r.Y + (r.Height - draw.Height) / 2;
                    e.Graphics.DrawImageUnscaled(draw, dx, dy);
                    using (SolidBrush ov = new SolidBrush(Color.FromArgb(70, 4, 7, 16)))
                        e.Graphics.FillRectangle(ov, r);
                }
                CyberTheme.DrawGridOverlay(e.Graphics, r, 0f, Pal.Accent);
            }
            catch { base.OnPaintBackground(e); }
        }

        private void LayoutControls()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            _txtQuery.Bounds = new Rectangle(22, 76, Math.Max(180, w - 760), 28);
            _cboVersion.Bounds = new Rectangle(_txtQuery.Right + 8, 76, 110, 28);
            _cboType.Bounds = new Rectangle(_cboVersion.Right + 8, 76, 90, 28);
            _chkTranslate.Bounds = new Rectangle(_cboType.Right + 8, 78, 160, 24);
            _btnSearch.Bounds = new Rectangle(w - 270, 76, 128, 30);
            _btnAi.Bounds = new Rectangle(w - 136, 76, 114, 30);
            _list.Bounds = new Rectangle(22, 116, w - 44, Math.Max(160, h - 210));
            _lbStatus.Bounds = new Rectangle(22, h - 82, w - 240, 24);
            _btnSync.Bounds = new Rectangle(w - 958, h - 50, 196, 34);
            _btnDownload.Bounds = new Rectangle(w - 754, h - 50, 190, 34);
            _btnOpen.Bounds = new Rectangle(w - 556, h - 50, 160, 34);
            _btnClose.Bounds = new Rectangle(w - 130, h - 50, 90, 34);
            if (_overlay != null) _overlay.Bounds = ClientRectangle;
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
            b.CornerColor = Color.FromArgb(6, 11, 22);
            b.ClipCorners = true;
            b.Size = new Size(w, h);
            return b;
        }

        private void DoSearch()
        {
            if (_worker != null && _worker.IsBusy) return;
            string q = _txtQuery.Text.Trim();
            if (q.Length == 0) { _lbStatus.Text = L.T("先输入搜索词，例如：脸部动画、种族、存储。"); return; }
            bool translate = _chkTranslate.Checked;
            SetBusy(true, L.T("正在搜索创意工坊…"));
            _worker = new BackgroundWorker();
            _worker.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                SearchResult r = new SearchResult();
                r.Original = q;
                string search = q;
                string terr = null;
                bool usedAi = false;
                if (translate) search = WorkshopClient.TranslateQuery(_st, q, out usedAi, out terr);
                r.SearchText = search;
                r.UsedAi = usedAi;
                r.TranslateMessage = terr;
                string err = null;
                string ver = L.T("全部版本").Equals(_cboVersion.Text) ? "" : _cboVersion.Text;
                bool coll = L.T("合集").Equals(_cboType.Text);
                string backend = "";
                int total = 0;
                string modsDir = (_ctx == null) ? "" : _ctx.ModsDir;
                r.Items = WorkshopClient.SearchAuto(_st, search, ver, coll, modsDir, q, out err, out backend, out total);
                r.Error = err;
                r.Backend = backend;
                r.Total = total;
                e.Result = r;
            };
            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, "");
                SearchResult r = e.Result as SearchResult;
                if (r == null) { _lbStatus.Text = L.T("搜索失败：没有返回结果。"); return; }
                _list.Items.Clear();
                for (int i = 0; i < r.Items.Count; i++)
                {
                    WorkshopItem it = r.Items[i];
                    string desc = it.Description == null ? "" : it.Description.Replace("\r", " ").Replace("\n", " ");
                    if (desc.Length > 220) desc = desc.Substring(0, 220) + "...";
                    ListViewItem row = new ListViewItem(it.Title);
                    row.SubItems.Add(it.Subscriptions.ToString());
                    row.SubItems.Add(desc);
                    row.Tag = it;
                    _list.Items.Add(row);
                }
                bool isLocal = r.Backend != null && r.Backend.IndexOf(L.T("本地"), StringComparison.Ordinal) >= 0;
                if (_list.Columns.Count > 0)
                    _list.Columns[0].Text = isLocal ? L.T("本地已装模组（不是工坊结果）") : L.T("模组");

                string msg = L.T("搜索词：") + r.SearchText + L.T("  ·  找到 ") + r.Items.Count + L.T(" 个结果");
                if (!string.IsNullOrEmpty(r.Backend)) msg += L.T("  ·  来源：") + r.Backend;
                if (isLocal) msg = L.T("【注意】工坊连不上（官方接口缺 API Key，网页被墙），下面列的是你【本地已装】的模组，不是工坊搜索结果。")
                    + L.T("要搜真正的工坊，请先在「工坊同步 / 按排序补齐」里填 Steam API Key。");
                if (r.UsedAi) msg += L.T("  ·  已用 AI 翻译");
                if (r.Items.Count == 0)
                {
                    msg = L.T("工坊搜索不可用（缺 API Key / 网页被 Steam 屏蔽）。请用「工坊同步」里的 API Key，或「按 ID / 链接导入」。");
                    if (!string.IsNullOrEmpty(r.Error)) msg += L.T("   [原因] ") + r.Error;
                }
                else if (!string.IsNullOrEmpty(r.Error)) msg += "  ·  " + r.Error;
                _lbStatus.Text = msg;
                if (r.Items.Count == 0 && !WorkshopWebApi.HasKey(_st))
                {
                    DialogResult dr = MinMsg.Show(this,
                        L.T("没有配置 Steam API Key，工坊关键词搜索走不通。") + Environment.NewLine + Environment.NewLine
                        + L.T("现在去申请一个吗？大约 1 分钟。") + Environment.NewLine
                        + L.T("（申请页需要梯子打开；拿到 key 填好之后就可以把梯子关掉）"),
                        L.T("需要 Steam API Key"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (dr == DialogResult.Yes) OpenSync();
                }
            };
            _worker.RunWorkerAsync();
        }

        private void SetBusy(bool busy, string text)
        {
            _btnSearch.Enabled = !busy;
            _btnDownload.Enabled = !busy;
            _btnOpen.Enabled = !busy;
            _btnSync.Enabled = !busy;
            _txtQuery.Enabled = !busy;
            _chkTranslate.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (!string.IsNullOrEmpty(text)) _lbStatus.Text = text;
            if (_overlay != null)
            {
                if (busy) _overlay.ShowOverlay(L.T("正在加载中，请稍候…"), text);
                else _overlay.HideOverlay();
            }
        }

        private WorkshopItem SelectedItem()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as WorkshopItem;
        }

        private void DownloadSelected()
        {
            WorkshopItem it = SelectedItem();
            if (it == null) { _lbStatus.Text = L.T("先在列表里点一个模组或合集。"); return; }
            if (_ctx == null)
            {
                string e0;
                bool ok0 = WorkshopClient.OpenSteamDownload(it.Id, out e0);
                _lbStatus.Text = ok0 ? L.T("已交给 Steam 下载：") + it.Title : L.T("启动 Steam 下载失败：") + e0;
                return;
            }
            bool coll = it.ItemType == "collection" || L.T("合集").Equals(_cboType.Text);
            SetBusy(true, L.T("正在下载并复制到整合包 Mods…（最多等待 4 分钟）"));
            BackgroundWorker dl = new BackgroundWorker();
            dl.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                string dest, err;
                bool ok = WorkshopClient.DownloadToPack(_ctx, it.Id, coll, out dest, out err);
                e.Result = new object[] { ok, dest, err };
            };
            dl.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, "");
                object[] arr = e.Result as object[];
                bool ok = arr != null && (bool)arr[0];
                string dest = arr != null ? Convert.ToString(arr[1]) : "";
                string err = arr != null ? Convert.ToString(arr[2]) : "";
                if (ok) _lbStatus.Text = L.T("下载完成并已复制到：") + dest;
                else _lbStatus.Text = L.T("下载失败：") + err;
            };
            dl.RunWorkerAsync();
        }

        private void OpenSelectedPage()
        {
            WorkshopItem it = SelectedItem();
            if (it == null) { _lbStatus.Text = L.T("先在列表里点一个模组。"); return; }
            string err;
            if (!WorkshopClient.OpenPage(it.Id, out err)) _lbStatus.Text = L.T("打开页面失败：") + err;
        }

        private void OpenSync()
        {
            using (WorkshopSyncForm f = new WorkshopSyncForm(_st, _ctx))
                f.ShowDialog(this);
        }

        private void OpenAiSetup()
        {
            using (AiSetupForm f = new AiSetupForm(_st))
            {
                f.ShowDialog(this);
                if (f.Saved) _lbStatus.Text = L.T("AI 接口已保存：") + _st.ApiBase + " / " + _st.ApiModel;
            }
        }

        private class SearchResult
        {
            public string Original;
            public string SearchText;
            public bool UsedAi;
            public string TranslateMessage;
            public string Error;
            public string Backend;
            public int Total;
            public List<WorkshopItem> Items = new List<WorkshopItem>();
        }
    }
}
