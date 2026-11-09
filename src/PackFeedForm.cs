using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>资源表里的一张卡片：封面 + 名称 + 本体版本。</summary>
    public class PackCard : Control
    {
        public PackItem Item;
        public Image Cover;
        private bool _hover;

        /// <summary>所有卡片共享的默认预览图（下载一次）。</summary>
        private static Image _shared;
        public static Image SharedCover { get { return _shared; } }
        public static void SetSharedCover(Image img) { _shared = img; }

        public PackCard()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(224, 268);
            Cursor = Cursors.Hand;
            BackColor = Pal.Panel1;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath p = Draw.Round(r, 10))
                {
                    using (LinearGradientBrush bg = new LinearGradientBrush(r,
                        _hover ? Color.FromArgb(16, 30, 52) : Color.FromArgb(9, 16, 30),
                        _hover ? Color.FromArgb(22, 40, 66) : Color.FromArgb(13, 23, 44), 90f))
                        g.FillPath(bg, p);
                    using (Pen pen = new Pen(Color.FromArgb(_hover ? 220 : 110, Pal.Accent), _hover ? 1.8f : 1f))
                        g.DrawPath(pen, p);
                }

                // 封面
                Rectangle cover = new Rectangle(12, 12, Width - 24, 150);
                using (GraphicsPath cp = Draw.Round(cover, 8))
                {
                    GraphicsState st = g.Save();
                    g.SetClip(cp, CombineMode.Intersect);
                    Image use = Cover != null ? Cover : _shared;
                    if (use != null)
                    {
                        float sc = Math.Max((float)cover.Width / use.Width, (float)cover.Height / use.Height);
                        int dw = Math.Max(1, (int)(use.Width * sc));
                        int dh = Math.Max(1, (int)(use.Height * sc));
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(use, new Rectangle(cover.X + (cover.Width - dw) / 2,
                            cover.Y + (cover.Height - dh) / 2, dw, dh));
                    }
                    else
                    {
                        // 没有封面图时，用名称的哈希生成一张渐变占位图 + 大号首字，
                        // 比一个空盒子好看，也不至于让整面墙看着像加载失败。
                        string nmH = Item == null || Item.Name == null ? "" : Item.Name;
                        int hs = 0;
                        for (int i = 0; i < nmH.Length; i++) hs = hs * 31 + nmH[i];
                        Color baseC = PlaceholderColor(hs);
                        using (LinearGradientBrush ph = new LinearGradientBrush(cover,
                            BlendDark(baseC, 26), BlendDark(baseC, 58), 60f))
                            g.FillRectangle(ph, cover);
                        string ini = nmH.Length == 0 ? "?" : nmH.Substring(0, Math.Min(2, nmH.Length));
                        TextRenderer.DrawText(g, ini, Fonts.B(30f), cover, Color.FromArgb(80, 255, 255, 255),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        Rectangle rcTip = new Rectangle(cover.X, cover.Bottom - 22, cover.Width, 18);
                        TextRenderer.DrawText(g, L.T("暂无预览图"), Fonts.F(8f), rcTip, Color.FromArgb(130, 175, 205),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }
                    g.Restore(st);
                }

                // 维护中徽章：两个网盘都没有有效链接
                if (Item != null && Item.Dead)
                {
                    Rectangle bg = new Rectangle(Width - 84, 20, 68, 24);
                    using (GraphicsPath bp = Draw.Round(bg, 6))
                    {
                        using (SolidBrush bb = new SolidBrush(Color.FromArgb(235, 190, 52, 74)))
                            g.FillPath(bb, bp);
                        using (Pen bpen = new Pen(Color.FromArgb(255, 255, 150, 160), 1f))
                            g.DrawPath(bpen, bp);
                    }
                    TextRenderer.DrawText(g, L.T("维护中"), Fonts.B(8.5f), bg, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }

                // 名称
                string name = Item == null ? "" : Item.Name;
                Rectangle rn = new Rectangle(14, 172, Width - 28, 46);
                TextRenderer.DrawText(g, name, Fonts.B(9.5f), rn, Pal.TextMain,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                // 版本
                string ver = Item == null ? "" : Item.GameVersion;
                Rectangle rv = new Rectangle(14, Height - 42, Width - 28, 20);
                TextRenderer.DrawText(g, string.IsNullOrEmpty(ver) ? L.T("版本未知") : (L.T("本体 ") + ver),
                    Fonts.F(8.5f), rv, Pal.Accent,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            catch { }
        }

        private static Color PlaceholderColor(int h)
        {
            Color[] pal = new Color[] {
                Color.FromArgb(0, 229, 255), Color.FromArgb(190, 80, 255), Color.FromArgb(255, 80, 210),
                Color.FromArgb(120, 255, 170), Color.FromArgb(255, 190, 60), Color.FromArgb(80, 180, 255)
            };
            int i = h % pal.Length;
            if (i < 0) i += pal.Length;
            return pal[i];
        }

        /// <summary>把主色调压暗成深色底（a 越大越亮）。</summary>
        private static Color BlendDark(Color c, int a)
        {
            return Color.FromArgb(Math.Min(255, a),
                Math.Min(255, (c.R + 12) / 3),
                Math.Min(255, (c.G + 18) / 3),
                Math.Min(255, (c.B + 30) / 3));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Cover != null) { try { Cover.Dispose(); } catch { } Cover = null; }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 【整合包资源获取】：卡片墙 + 详情页。
    /// 每次打开都会尝试刷新数据源；失败则用上次缓存并明确标注时间。
    /// </summary>
    public class PackFeedForm : Form
    {
        private Settings _st;
        private Label _lbTitle, _lbStatus, _lbSource, _lbNotice;
        private ZButton _btnRefresh, _btnImport, _btnClose, _btnBack, _btnQuark, _btnXunlei, _btnSaveSource, _btnSubmit;
        private TextBox _txtSource;
        private FlowLayoutPanel _flow;
        private Panel _detail;
        private LoadingOverlay _overlay;
        private List<PackItem> _items = new List<PackItem>();
        private string _updatedAt = "";
        private PackItem _current;

        private Label _dName, _dBody;
        private Panel _dCoverBox;

        private PackContext _ctx;

        public PackFeedForm(Settings st) : this(st, null) { }

        public PackFeedForm(Settings st, PackContext ctx)
        {
            _st = st;
            _ctx = ctx;
            Text = L.T("整合包资源获取");
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1120, 760);
            MinimumSize = new Size(920, 620);
            BackColor = Color.FromArgb(4, 7, 16);
            Font = Fonts.F(9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _lbTitle = new Label();
            _lbTitle.Text = L.T("整合包资源获取");
            _lbTitle.Font = Fonts.B(14f);
            _lbTitle.ForeColor = Pal.Accent;
            Controls.Add(_lbTitle);

            _lbSource = new Label();
            _lbSource.Text = L.T("数据源（已内置，一般不用改）");
            _lbSource.Font = Fonts.F(8.5f);
            _lbSource.ForeColor = Pal.TextSub;
            Controls.Add(_lbSource);

            _txtSource = new TextBox();
            _txtSource.Font = Fonts.F(8.5f);
            _txtSource.BorderStyle = BorderStyle.FixedSingle;
            _txtSource.BackColor = Color.FromArgb(8, 15, 28);
            _txtSource.ForeColor = Color.White;
            Controls.Add(_txtSource);

            _btnSaveSource = MkBtn(L.T("保存地址"), 92, 28);
            _btnSaveSource.Click += delegate(object s, EventArgs e) { SaveSource(); };
            Controls.Add(_btnSaveSource);

            _btnSubmit = MkBtn(L.T("提交我的整合包"), 150, 32);
            _btnSubmit.Click += delegate(object s, EventArgs e) { SubmitPack(); };
            Controls.Add(_btnSubmit);

            _btnRefresh = MkBtn(L.T("刷新"), 92, 32);
            _btnRefresh.Click += delegate(object s, EventArgs e) { Reload(true); };
            Controls.Add(_btnRefresh);

            _btnImport = MkBtn(L.T("导入表格"), 110, 32);
            _btnImport.Click += delegate(object s, EventArgs e) { ImportTable(); };
            Controls.Add(_btnImport);

            _btnClose = MkBtn(L.T("关闭"), 90, 32);
            _btnClose.Click += delegate(object s, EventArgs e) { Close(); };
            Controls.Add(_btnClose);

            _lbNotice = new Label();
            _lbNotice.Text = L.T("【学习版资源声明】本页面所列整合包均由第三方网盘提供，仅供学习与交流使用；")
                + L.T("请于下载后 24 小时内删除，切勿用于任何商业用途。喜欢请支持正版。");
            _lbNotice.Font = Fonts.F(8.5f);
            _lbNotice.ForeColor = Color.FromArgb(255, 196, 92);
            _lbNotice.BackColor = Color.FromArgb(26, 20, 10);
            Controls.Add(_lbNotice);

            _lbStatus = new Label();
            _lbStatus.Font = Fonts.B(9f);
            _lbStatus.ForeColor = Color.FromArgb(252, 253, 255);
            _lbStatus.AutoEllipsis = true;
            Controls.Add(_lbStatus);

            _flow = new FlowLayoutPanel();
            _flow.AutoScroll = true;
            _flow.FlowDirection = FlowDirection.LeftToRight;
            _flow.WrapContents = true;
            _flow.BackColor = Color.FromArgb(4, 7, 16);
            _flow.Padding = new Padding(0, 0, 0, 8);
            Controls.Add(_flow);

            // ── 详情页 ──
            _detail = new Panel();
            _detail.BackColor = Color.FromArgb(6, 10, 20);
            _detail.Visible = false;
            Controls.Add(_detail);

            _btnBack = MkBtn(L.T("返回列表"), 110, 32);
            _btnBack.Click += delegate(object s, EventArgs e) { ShowDetail(false); };
            _detail.Controls.Add(_btnBack);

            _dCoverBox = new Panel();
            _dCoverBox.BackColor = Color.FromArgb(10, 18, 32);
            _detail.Controls.Add(_dCoverBox);

            _dName = new Label();
            _dName.Font = Fonts.B(13f);
            _dName.ForeColor = Pal.TextMain;
            _dName.AutoEllipsis = true;
            _detail.Controls.Add(_dName);

            _dBody = new Label();
            _dBody.Font = Fonts.F(9f);
            _dBody.ForeColor = Color.FromArgb(200, 224, 240);
            _detail.Controls.Add(_dBody);

            _btnQuark = MkBtn(L.T("夸克网盘下载"), 150, 38);
            _btnQuark.Click += delegate(object s, EventArgs e) { OpenLink(_current == null ? "" : _current.QuarkUrl, L.T("夸克网盘")); };
            _detail.Controls.Add(_btnQuark);

            _btnXunlei = MkBtn(L.T("迅雷云盘下载"), 150, 38);
            _btnXunlei.Click += delegate(object s, EventArgs e) { OpenLink(_current == null ? "" : _current.XunleiUrl, L.T("迅雷云盘")); };
            _detail.Controls.Add(_btnXunlei);

            _overlay = new LoadingOverlay();
            _overlay.Bounds = ClientRectangle;
            Controls.Add(_overlay);

            Resize += delegate(object s, EventArgs e) { LayoutControls(); };
            LayoutControls();

            // 内置默认地址，用户不用填；设置里填过就以设置为准
            string srcUrl = (st == null || string.IsNullOrEmpty(st.PackFeedUrl)) ? PackFeed.DefaultSource : st.PackFeedUrl;
            _txtSource.Text = srcUrl;
            if (st != null && string.IsNullOrEmpty(st.PackFeedUrl)) { st.PackFeedUrl = srcUrl; try { st.Save(); } catch { } }
            Load += delegate(object s, EventArgs e) { Reload(true); };
        }

        private void LayoutControls()
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            _lbTitle.Bounds = new Rectangle(22, 12, 420, 30);
            _btnClose.Bounds = new Rectangle(w - 112, 12, 90, 32);
            _btnRefresh.Bounds = new Rectangle(w - 212, 12, 92, 32);
            _btnImport.Bounds = new Rectangle(w - 330, 12, 110, 32);
            _btnSubmit.Bounds = new Rectangle(w - 492, 12, 150, 32);

            _lbSource.Bounds = new Rectangle(22, 50, 220, 20);
            _txtSource.Bounds = new Rectangle(22, 70, Math.Max(200, w - 44 - 100), 24);
            _btnSaveSource.Bounds = new Rectangle(w - 114, 68, 92, 28);

            _lbNotice.Bounds = new Rectangle(22, h - 62, w - 44, 24);
            _lbStatus.Bounds = new Rectangle(22, h - 30, w - 44, 22);

            int top = 102;
            int bottom = h - 70;
            _flow.Bounds = new Rectangle(22, top, w - 44, Math.Max(160, bottom - top));

            _detail.Bounds = _flow.Bounds;
            int cw = 240;
            _dCoverBox.Bounds = new Rectangle(16, 52, cw, 170);
            _dName.Bounds = new Rectangle(16, 14, w - 44 - 32, 30);
            _btnBack.Bounds = new Rectangle(_detail.Width - 126, 10, 110, 32);
            _dBody.Bounds = new Rectangle(cw + 36, 52, Math.Max(200, _detail.Width - cw - 60), 170);
            _btnQuark.Bounds = new Rectangle(cw + 36, 232, 150, 38);
            _btnXunlei.Bounds = new Rectangle(cw + 196, 232, 150, 38);

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
            b.Size = new Size(w, h);
            return b;
        }

        private void SetBusy(bool busy, string text)
        {
            _btnRefresh.Enabled = !busy;
            _btnImport.Enabled = !busy;
            _btnClose.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (_overlay != null)
            {
                if (busy) _overlay.ShowOverlay(L.T("正在加载中，请稍候…"), text);
                else _overlay.HideOverlay();
            }
        }

        private void SaveSource()
        {
            if (_st == null) return;
            _st.PackFeedUrl = _txtSource.Text == null ? "" : _txtSource.Text.Trim();
            _st.Save();
            _lbStatus.Text = L.T("数据源地址已保存。点「刷新」重新获取。");
        }

        // ── 载入 ──
        private void Reload(bool tryOnline)
        {
            string url = _txtSource.Text == null ? "" : _txtSource.Text.Trim();
            if (string.IsNullOrEmpty(url)) { url = PackFeed.DefaultSource; _txtSource.Text = url; }
            SetBusy(true, tryOnline ? L.T("正在获取整合包资源表…") : L.T("正在读取缓存…"));
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                List<PackItem> items = null;
                string err = null;
                string usedUrl = null;
                // 空 或 内置源之一 → 走多源降级；只有用户自己填了别的地址才死磕那一个
                bool isDefault = string.IsNullOrEmpty(url) || PackFeed.IsKnownSource(url);
                if (tryOnline)
                {
                    if (isDefault) items = PackFeed.FetchPublic(out err, out usedUrl);
                    else items = PackFeed.TryFetch(url, out err);
                }
                string at;
                if (items == null || items.Count == 0)
                {
                    List<PackItem> cached = PackFeed.LoadCache(out at);
                    e.Result = new object[] { cached, err, at, true, usedUrl };
                }
                else
                {
                    PackFeed.SaveCache(items, url);
                    e.Result = new object[] { items, null, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), false, usedUrl };
                }
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false, "");
                object[] arr = e.Result as object[];
                List<PackItem> items = arr == null ? null : arr[0] as List<PackItem>;
                string err = arr == null ? "" : Convert.ToString(arr[1]);
                string at = arr == null ? "" : Convert.ToString(arr[2]);
                bool fromCache = arr != null && Convert.ToBoolean(arr[3]);
                string usedUrl = (arr == null || arr.Length < 5) ? "" : Convert.ToString(arr[4]);
                _updatedAt = at;

                _items = items == null ? new List<PackItem>() : items;
                BuildCards();

                if (_items.Count == 0)
                {
                    _lbStatus.Text = L.T("还没有数据。")
                        + (string.IsNullOrEmpty(err) ? "" : "（" + err + "）")
                        + L.T("  请点「导入表格」粘贴或导入 CSV。");
                    _lbStatus.ForeColor = Color.FromArgb(255, 180, 60);
                }
                else if (fromCache)
                {
                    _lbStatus.Text = L.T("共 ") + _items.Count + L.T(" 个整合包 · 数据更新于 ") + (string.IsNullOrEmpty(at) ? L.T("未知") : at)
                        + (string.IsNullOrEmpty(err) ? "" : L.T(" · 在线获取失败：") + err);
                    _lbStatus.ForeColor = Color.FromArgb(255, 180, 60);
                }
                else
                {
                    _lbStatus.Text = L.T("共 ") + _items.Count + L.T(" 个整合包 · 已更新 ") + at
                        + (string.IsNullOrEmpty(usedUrl) ? "" : L.T(" · 来源 ") + SourceName(usedUrl));
                    _lbStatus.ForeColor = Color.FromArgb(0, 220, 170);
                }
            };
            bw.RunWorkerAsync();
        }

        private void BuildCards()
        {
            _flow.SuspendLayout();
            try
            {
                for (int i = _flow.Controls.Count - 1; i >= 0; i--)
                {
                    Control c = _flow.Controls[i];
                    _flow.Controls.RemoveAt(i);
                    c.Dispose();
                }
                for (int i = 0; i < _items.Count; i++)
                {
                    PackCard card = new PackCard();
                    card.Item = _items[i];
                    card.Margin = new Padding(0, 0, 12, 12);
                    PackItem captured = _items[i];
                    card.Click += delegate(object s, EventArgs e) { OpenDetail(captured); };
                    _flow.Controls.Add(card);
                    LoadCoverAsync(card, captured);
                }
            }
            finally { _flow.ResumeLayout(); }
            EnsureSharedCover();
        }

        private static bool _sharedLoading;

        /// <summary>下载并设置所有卡片共用的默认预览图，只下载一次。</summary>
        private void EnsureSharedCover()
        {
            if (PackCard.SharedCover != null || _sharedLoading) return;
            _sharedLoading = true;
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e) { e.Result = PackFeed.DownloadDefaultCover(); };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                _sharedLoading = false;
                string path = e.Result as string;
                if (string.IsNullOrEmpty(path)) { Log.Warn(L.T("默认预览图下载失败")); return; }
                try
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (Image tmp = Image.FromStream(fs))
                        PackCard.SetSharedCover(new Bitmap(tmp));
                    foreach (Control c in _flow.Controls) c.Invalidate();
                }
                catch { }
            };
            bw.RunWorkerAsync();
        }

        private void LoadCoverAsync(PackCard card, PackItem item)
        {
            if (card == null || item == null || !PackFeed.LooksLikeUrl(item.Cover)) return;
            string url = item.Cover;
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e) { e.Result = PackFeed.DownloadCover(url); };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                string path = e.Result as string;
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    if (card.IsDisposed) return;
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (Image tmp = Image.FromStream(fs))
                        card.Cover = new Bitmap(tmp);
                    card.Invalidate();
                }
                catch { }
            };
            bw.RunWorkerAsync();
        }

        // ── 详情 ──
        private void OpenDetail(PackItem item)
        {
            if (item == null) return;
            _current = item;
            _dName.Text = item.Name;
            _dBody.Text = BuildDetailText(item);
            _btnQuark.Enabled = item.QuarkOk;
            _btnQuark.Text = item.QuarkOk ? L.T("夸克网盘下载") : L.T("夸克网盘（维护中）");
            _btnQuark.Invalidate();
            _btnXunlei.Enabled = item.XunleiOk;
            _btnXunlei.Text = item.XunleiOk ? L.T("迅雷云盘下载") : L.T("迅雷云盘（维护中）");
            _btnXunlei.Invalidate();
            ShowDetail(true);
            LoadDetailCover(item);
        }

        private static string BuildDetailText(PackItem p)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("本体版本：") + Show(p.GameVersion));
            sb.AppendLine(L.T("MOD 数量：") + Show(p.ModCount));
            sb.AppendLine(L.T("副本数量：") + Show(p.CopyCount));
            sb.AppendLine(L.T("讲述者数：") + Show(p.Storytellers));
            sb.AppendLine(L.T("游戏加载：") + Show(p.LoadTime));
            sb.AppendLine(L.T("最高 FPS：") + Show(p.MaxFps));
            sb.AppendLine(L.T("RJW 内容：") + Show(p.Rjw));
            sb.AppendLine(L.T("压缩包：") + Show(p.ZipSize));
            sb.AppendLine(L.T("解压大小：") + Show(p.UnzippedSize));
            sb.AppendLine("");
            sb.Append(L.T("简介：") + (string.IsNullOrEmpty(p.Description) ? L.T("暂无") : p.Description));
            return sb.ToString();
        }

        private static string Show(string s) { return string.IsNullOrEmpty(s) ? "—" : s; }

        private static string SourceName(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            if (url.IndexOf("ghproxy", StringComparison.OrdinalIgnoreCase) >= 0) return L.T("ghproxy 加速");
            if (url.IndexOf("statically", StringComparison.OrdinalIgnoreCase) >= 0) return L.T("statically 镜像");
            if (url.IndexOf("jsdelivr", StringComparison.OrdinalIgnoreCase) >= 0) return "jsDelivr";
            if (url.IndexOf("raw.githubusercontent", StringComparison.OrdinalIgnoreCase) >= 0) return "GitHub raw";
            return L.T("自定义");
        }

        private void LoadDetailCover(PackItem item)
        {
            try
            {
                foreach (Control c in _dCoverBox.Controls) c.Dispose();
                _dCoverBox.Controls.Clear();
                if (!PackFeed.LooksLikeUrl(item.Cover))
                {
                    Label lb = new Label();
                    lb.Text = L.T("暂无预览图");
                    lb.Dock = DockStyle.Fill;
                    lb.TextAlign = ContentAlignment.MiddleCenter;
                    lb.ForeColor = Color.FromArgb(110, 150, 180);
                    _dCoverBox.Controls.Add(lb);
                    return;
                }
                BackgroundWorker bw = new BackgroundWorker();
                string url = item.Cover;
                bw.DoWork += delegate(object s, DoWorkEventArgs e) { e.Result = PackFeed.DownloadCover(url); };
                bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
                {
                    if (IsDisposed || Disposing) return;
                    string path = e.Result as string;
                    if (string.IsNullOrEmpty(path) || _dCoverBox.IsDisposed) return;
                    try
                    {
                        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (Image tmp = Image.FromStream(fs))
                        {
                            PictureBox pb = new PictureBox();
                            pb.Dock = DockStyle.Fill;
                            pb.SizeMode = PictureBoxSizeMode.Zoom;
                            pb.Image = new Bitmap(tmp);
                            _dCoverBox.Controls.Clear();
                            _dCoverBox.Controls.Add(pb);
                        }
                    }
                    catch { }
                };
                bw.RunWorkerAsync();
            }
            catch { }
        }

        private void ShowDetail(bool show)
        {
            _detail.Visible = show;
            _flow.Visible = !show;
            _lbSource.Visible = !show;
            _txtSource.Visible = !show;
            _btnSaveSource.Visible = !show;
            _btnRefresh.Visible = !show;
            _btnImport.Visible = !show;
            if (show) { _detail.BringToFront(); }
            else { _flow.BringToFront(); if (_overlay != null) _overlay.BringToFront(); }
        }

        private void OpenLink(string url, string what)
        {
            if (!PackFeed.LooksLikeUrl(url)) { MinMsg.Show(this, L.T("这一项没有填下载链接。"), what, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            try
            {
                ShellOpen.Url(url.Trim());
                _lbStatus.Text = L.T("已用浏览器打开") + what + L.T("链接。");
            }
            catch (Exception ex)
            {
                MinMsg.Show(this, L.T("打开浏览器失败，请手动访问：\r\n\r\n") + url, what, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ── 导入 ──
        // =================================================================
        //  提交整合包到仓库（生成 zip + 打开预填 Issue 页）
        // =================================================================
        private void SubmitPack()
        {
            string ctxErr = null;
            if (_ctx == null || string.IsNullOrEmpty(_ctx.Root) || !Directory.Exists(_ctx.Root))
                ctxErr = L.T("没有检测到整合包目录，无法生成提交包。");

            string defaultName = (_ctx == null || string.IsNullOrEmpty(_ctx.Root))
                ? L.T("我的整合包") : new DirectoryInfo(_ctx.Root).Name;

            using (Form dlg = new Form())
            {
                dlg.Text = L.T("提交我的整合包（生成提交包）");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(660, 420);
                dlg.MinimizeBox = false; MaximizeBox = false;
                dlg.BackColor = Color.FromArgb(8, 12, 22);
                dlg.ForeColor = Color.White;
                dlg.Font = Fonts.F(9f);

                Label tip = new Label();
                tip.Bounds = new Rectangle(16, 12, 628, 62);
                tip.ForeColor = Color.FromArgb(190, 214, 232);
                tip.Text = (ctxErr == null
                    ? L.T("会打包：Config 目录里的配置 + ModsConfig.xml（模组排序）+ 一份模组清单。\r\n不包含模组文件本身。生成后请在打开的网页里把 zip 拖进去提交。")
                    : ctxErr);
                dlg.Controls.Add(tip);

                Label l1 = new Label(); l1.Text = L.T("整合包名称"); l1.Bounds = new Rectangle(16, 84, 100, 20); dlg.Controls.Add(l1);
                TextBox tName = new TextBox(); tName.Bounds = new Rectangle(120, 82, 524, 24);
                tName.Text = defaultName; dlg.Controls.Add(tName);

                Label l2 = new Label(); l2.Text = L.T("联系方式"); l2.Bounds = new Rectangle(16, 120, 100, 20); dlg.Controls.Add(l2);
                TextBox tContact = new TextBox(); tContact.Bounds = new Rectangle(120, 118, 524, 24);
                tContact.Text = L.T("QQ / 群号 / 邮箱，方便审核联系你");
                dlg.Controls.Add(tContact);

                Label l3 = new Label(); l3.Text = L.T("补充说明"); l3.Bounds = new Rectangle(16, 156, 100, 20); dlg.Controls.Add(l3);
                TextBox tNote = new TextBox(); tNote.Multiline = true; tNote.ScrollBars = ScrollBars.Vertical;
                tNote.Bounds = new Rectangle(120, 154, 524, 150);
                tNote.Text = L.T("比如：这个包的玩法特色、必装的前置、已知问题等");
                dlg.Controls.Add(tNote);

                ZButton ok = MkBtn(L.T("生成提交包"), 130, 34);
                ok.Bounds = new Rectangle(16, 330, 130, 34);
                dlg.Controls.Add(ok);
                ZButton cancel = MkBtn(L.T("取消"), 90, 34);
                cancel.Bounds = new Rectangle(554, 330, 90, 34);
                dlg.Controls.Add(cancel);

                bool made = false;
                PackSubmit.Result madeResult = null;
                ok.Click += delegate(object s, EventArgs e)
                {
                    if (ctxErr != null) { MinMsg.Show(dlg, ctxErr, L.T("无法提交"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    string err;
                    PackSubmit.Result r = PackSubmit.Build(_ctx, _st, tName.Text.Trim(), tContact.Text.Trim(), tNote.Text.Trim(), out err);
                    if (r == null)
                    {
                        MinMsg.Show(dlg, L.T("生成失败：") + (err == null ? L.T("未知原因") : err), L.T("失败"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    made = true;
                    madeResult = r;
                    dlg.Close();
                };
                cancel.Click += delegate(object s, EventArgs e) { dlg.Close(); };
                dlg.ShowDialog(this);

                if (made && madeResult != null) ShowSubmitResult(madeResult);
            }
        }

        private void ShowSubmitResult(PackSubmit.Result r)
        {
            try
            {
                string msg = L.T("提交包已生成：\r\n\r\n") + r.ZipPath + "\r\n\r\n"
                    + L.T("包含 ") + r.Files.Count + L.T(" 个文件，模组 ") + r.ModCount + L.T(" 个。\r\n\r\n")
                    + L.T("接下来会打开 GitHub 的提交页面，请把上面这个 zip 拖进输入框再点 Submit。\r\n\r\n")
                    + L.T("如果打不开网页，可以点「复制提交内容」自己粘到 Issue 或群里。");
                DialogResult dr = MinMsg.Show(this, msg, L.T("提交包已生成"),
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + r.ZipPath + "\""); } catch { }

                if (dr == DialogResult.OK)
                {
                    try { ShellOpen.Url(r.IssueUrl); }
                    catch
                    {
                        try { Clipboard.SetText(r.Body); _lbStatus.Text = L.T("打不开浏览器，提交内容已复制到剪贴板。"); }
                        catch { }
                    }
                }
                _lbStatus.Text = L.T("提交包：") + r.ZipPath;
            }
            catch (Exception ex)
            {
                MinMsg.Show(this, L.T("生成后处理出错：") + ex.Message, L.T("提示"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ImportTable()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = L.T("导入整合包表格（粘贴或选择 CSV）");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(820, 520);
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.BackColor = Color.FromArgb(8, 12, 22);
                dlg.ForeColor = Color.White;
                dlg.Font = Fonts.F(9f);

                Label tip = new Label();
                tip.Text = L.T("① 在在线表格里全选数据行（含表头）复制粘贴到下面；或 ② 点「打开 CSV 文件」选导出的 .csv。")
                    + L.T("\r\n要求第一列是整合包名称，有表头更好（按表头自动认列）。");
                tip.Bounds = new Rectangle(14, 12, 792, 44);
                tip.ForeColor = Color.FromArgb(190, 214, 232);
                dlg.Controls.Add(tip);

                TextBox box = new TextBox();
                box.Multiline = true;
                box.ScrollBars = ScrollBars.Both;
                box.WordWrap = false;
                box.Bounds = new Rectangle(14, 60, 792, 380);
                box.BackColor = Color.FromArgb(6, 10, 20);
                box.ForeColor = Color.White;
                box.Font = Fonts.F(8.5f);
                dlg.Controls.Add(box);

                ZButton ok = MkBtn(L.T("用这段内容"), 130, 34);
                ok.Bounds = new Rectangle(14, 452, 130, 34);
                dlg.Controls.Add(ok);

                ZButton file = MkBtn(L.T("打开 CSV 文件"), 140, 34);
                file.Bounds = new Rectangle(156, 452, 140, 34);
                dlg.Controls.Add(file);

                ZButton cancel = MkBtn(L.T("取消"), 90, 34);
                cancel.Bounds = new Rectangle(716, 452, 90, 34);
                dlg.Controls.Add(cancel);

                bool applied = false;
                ok.Click += delegate(object s, EventArgs e)
                {
                    List<PackItem> items = PackFeed.ParseTable(box.Text);
                    if (items.Count == 0) { MinMsg.Show(dlg, L.T("没解析出数据。检查一下是不是粘到了表格内容。"), L.T("导入"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    _items = items;
                    PackFeed.SaveCache(items, L.T("手动导入"));
                    _updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    BuildCards();
                    ShowDetail(false);
                    _lbStatus.Text = L.T("已导入 ") + items.Count + L.T(" 个整合包（") + _updatedAt + "）。";
                    _lbStatus.ForeColor = Color.FromArgb(0, 220, 170);
                    applied = true;
                    dlg.Close();
                };
                file.Click += delegate(object s, EventArgs e)
                {
                    using (OpenFileDialog ofd = new OpenFileDialog())
                    {
                        ofd.Title = L.T("选择导出的 CSV 文件");
                        ofd.Filter = L.T("CSV 文件|*.csv;*.txt|所有文件|*.*");
                        if (ofd.ShowDialog(dlg) != DialogResult.OK) return;
                        try
                        {
                            string txt = File.ReadAllText(ofd.FileName, Encoding.UTF8);
                            if (txt.IndexOf(L.T("整合包"), StringComparison.Ordinal) < 0 && txt.IndexOf("MOD", StringComparison.Ordinal) < 0)
                            {
                                try { txt = File.ReadAllText(ofd.FileName, Encoding.Default); } catch { }
                            }
                            box.Text = txt;
                        }
                        catch (Exception ex) { MinMsg.Show(dlg, L.T("读取失败：") + ex.Message, L.T("导入"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
                    }
                };
                cancel.Click += delegate(object s, EventArgs e) { dlg.Close(); };
                dlg.ShowDialog(this);
                if (!applied) { }
            }
        }
    }
}
