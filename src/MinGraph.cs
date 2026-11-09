using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 模组依赖网图画布（原 ModGraphControl 的自绘元素版，分层窗口里放不了真控件）：
    /// 滚轮以鼠标为中心缩放、拖拽平移、左键节点=详情、右键节点=启停、悬停高亮相关依赖。
    /// 与原版差异：底色/网格/标签颜色走极简主题 T；缩放/平移/命中公式原样照搬（虚拟画布 1120×1080）。
    /// </summary>
    class ElModGraph : El
    {
        public ModGraphData Data;
        public string SelectedId;
        public event EventHandler NodeToggled;   // 右键启停后
        public event EventHandler NodeSelected;  // 左键点节点
        public float Zoom = 1f;
        public PointF Pan = new PointF(0f, 0f);

        /// <summary>放大到这个倍数以上才显示模组缩略图。</summary>
        public const float ThumbZoom = 1.7f;
        readonly Dictionary<string, Image> _thumbs = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        Point _lastMouse;
        bool _dragging, _moved;
        MouseButtons _downBtn;
        string _hoverId;

        /// <summary>外部触发重绘（基类 Redraw 是 protected）</summary>
        public void Redraw2() { Redraw(); }

        static readonly Color[] Palette = new Color[] {
            Color.FromArgb(0,229,255), Color.FromArgb(255,80,210), Color.FromArgb(120,255,170),
            Color.FromArgb(255,190,60), Color.FromArgb(140,120,255), Color.FromArgb(80,180,255),
            Color.FromArgb(255,110,110), Color.FromArgb(200,255,80), Color.FromArgb(90,230,210), Color.FromArgb(180,180,180)
        };

        public void SetData(ModGraphData data)
        {
            Data = data;
            SelectedId = null;
            _hoverId = null;
            Zoom = FitZoom();
            Pan = new PointF(0f, 0f);
            Redraw();
        }

        float FitZoom()
        {
            if (Data == null || Data.Nodes.Count == 0 || Bounds.Width < 50 || Bounds.Height < 50) return 1f;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < Data.Nodes.Count; i++)
            {
                ModGraphNode n = Data.Nodes[i];
                if (n.X < minX) minX = n.X; if (n.X > maxX) maxX = n.X;
                if (n.Y < minY) minY = n.Y; if (n.Y > maxY) maxY = n.Y;
            }
            float sx = (Bounds.Width - 80f) / Math.Max(1f, maxX - minX);
            float sy = (Bounds.Height - 80f) / Math.Max(1f, maxY - minY);
            return Math.Max(0.2f, Math.Min(1.2f, Math.Min(sx, sy)));
        }

        public ModGraphNode FindNode(string id)
        {
            if (Data == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Data.Nodes.Count; i++)
                if (string.Equals(Data.Nodes[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Data.Nodes[i];
            return null;
        }

        /// <summary>选中并把节点平移到画布中心（搜索跳转用）</summary>
        public void FocusNode(string id, bool center)
        {
            ModGraphNode n = FindNode(id);
            if (n == null) return;
            SelectedId = n.Id;
            if (center)
            {
                float sx = Pan.X + (Bounds.Width - 1120 * Zoom) / 2f + n.X * Zoom;
                float sy = Pan.Y + (Bounds.Height - 1080 * Zoom) / 2f + n.Y * Zoom;
                Pan = new PointF(Pan.X + Bounds.Width / 2f - sx, Pan.Y + Bounds.Height / 2f - sy);
            }
            Redraw();
        }

        // —— 坐标换算（世界 ⇄ 画布）——
        float ToGraphX(int px) { return (px - Bounds.X - Pan.X - (Bounds.Width - 1120 * Zoom) / 2f) / Zoom; }
        float ToGraphY(int py) { return (py - Bounds.Y - Pan.Y - (Bounds.Height - 1080 * Zoom) / 2f) / Zoom; }

        ModGraphNode HitNode(Point p)
        {
            if (Data == null) return null;
            float gx = ToGraphX(p.X), gy = ToGraphY(p.Y);
            for (int i = Data.Nodes.Count - 1; i >= 0; i--)
            {
                ModGraphNode n = Data.Nodes[i];
                float dx = gx - n.X, dy = gy - n.Y;
                if (dx * dx + dy * dy <= 12f * 12f) return n;
            }
            return null;
        }

        // —— 鼠标（El 没有按钮参数，按键在按下时从 Control.MouseButtons 快照）——
        public override void OnMouseDown(Point p)
        {
            _lastMouse = p;
            _downBtn = Control.MouseButtons;
            _dragging = true;
            _moved = false;
        }

        public override void OnMouseMove(Point p)
        {
            if (_dragging && Control.MouseButtons != MouseButtons.None)
            {
                int dx = p.X - _lastMouse.X, dy = p.Y - _lastMouse.Y;
                if (Math.Abs(dx) + Math.Abs(dy) > 2) _moved = true;
                Pan = new PointF(Pan.X + dx, Pan.Y + dy);
                _lastMouse = p;
                Redraw();
            }
            else
            {
                ModGraphNode n = HitNode(p);
                string id = n == null ? null : n.Id;
                if (id != _hoverId) { _hoverId = id; Redraw(); }
            }
        }

        public override void OnMouseUp(Point p)
        {
            bool moved = _moved;
            MouseButtons btn = _downBtn;
            _dragging = false;
            if (moved) return;
            ModGraphNode n = HitNode(p);
            if (n == null) return;
            SelectedId = n.Id;
            if (btn == MouseButtons.Right)
            {
                n.Enabled = !n.Enabled;
                ModGraphBuilder.RecomputeWarnings(Data);
                if (NodeToggled != null) NodeToggled(this, EventArgs.Empty);
            }
            else if (NodeSelected != null) NodeSelected(this, EventArgs.Empty);
            Redraw();
        }

        public override void OnMouseLeave()
        {
            if (_hoverId != null) { _hoverId = null; Redraw(); }
        }

        public override Cursor CursorOf(Point p)
        {
            return HitNode(p) != null ? Cursors.Hand : Cursors.Default;
        }

        public override bool OnMouseWheel(int delta)
        {
            if (Data == null) return false;
            float old = Zoom;
            Zoom = Math.Max(0.25f, Math.Min(4f, Zoom * (delta > 0 ? 1.12f : 0.89f)));
            // 以鼠标位置为中心缩放（鼠标坐标 = Host.MousePos，MainForm 已转逻辑坐标）
            Point mp = Host is MainForm ? ((MainForm)Host).MousePos : Point.Empty;
            float mx = mp.X - Bounds.X, my = mp.Y - Bounds.Y;
            Pan = new PointF(mx - (mx - Pan.X) * (Zoom / old), my - (my - Pan.Y) * (Zoom / old));
            Redraw();
            return true;
        }

        static Color CategoryColor(string cat)
        {
            if (string.IsNullOrEmpty(cat)) cat = "?";
            int h = 0;
            for (int i = 0; i < cat.Length; i++) h = h * 31 + cat[i];
            return Palette[Math.Abs(h) % Palette.Length];
        }

        /// <summary>模组缩略图（About\Preview.png 等），加载一次缩到 64×64 缓存</summary>
        Image GetThumb(ModGraphNode n)
        {
            if (n == null || n.Mod == null || string.IsNullOrEmpty(n.Mod.RootDir)) return null;
            string key = n.Id == null ? n.Mod.RootDir : n.Id;
            Image cached;
            if (_thumbs.TryGetValue(key, out cached)) return cached;
            Image result = null;
            try
            {
                string about = Path.Combine(n.Mod.RootDir, "About");
                string[] names = new string[] { "Preview.png", "preview.png", "Preview.jpg", "preview.jpg", "Icon.png", "icon.png" };
                for (int i = 0; i < names.Length; i++)
                {
                    string p = Path.Combine(about, names[i]);
                    if (!File.Exists(p)) continue;
                    using (Image raw = Image.FromFile(p))
                    {
                        Bitmap b = new Bitmap(64, 64);
                        using (Graphics g = Graphics.FromImage(b))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            float sc = Math.Max(64f / raw.Width, 64f / raw.Height);
                            int dw = Math.Max(1, (int)(raw.Width * sc));
                            int dh = Math.Max(1, (int)(raw.Height * sc));
                            g.DrawImage(raw, (64 - dw) / 2, (64 - dh) / 2, dw, dh);
                        }
                        result = b;
                    }
                    break;
                }
            }
            catch { result = null; }
            _thumbs[key] = result;
            return result;
        }

        public override void Paint(Graphics g)
        {
            try
            {
                var bg = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
                T.FillRound(g, bg, 8, T.LogBg);
                T.DrawRound(g, bg, 8, T.Line2, 1f);

                if (Data == null || Data.Nodes.Count == 0)
                {
                    using (var b = new SolidBrush(T.Text3))
                        g.DrawString(L.T("还没有生成网状图。先点【AI 分类并生成网状图】。"), T.B(12), b, bg,
                            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                    return;
                }

                g.SetClip(new Rectangle(Bounds.X + 1, Bounds.Y + 1, Bounds.Width - 2, Bounds.Height - 2));

                // 网格（很淡，只给平移时的参照感）
                using (Pen grid = new Pen(Color.FromArgb(T.Dark ? 14 : 255, T.Line), 1f))
                {
                    for (int x = Bounds.X; x < Bounds.Right; x += 40) g.DrawLine(grid, x, Bounds.Y, x, Bounds.Bottom);
                    for (int y = Bounds.Y; y < Bounds.Bottom; y += 40) g.DrawLine(grid, Bounds.X, y, Bounds.Right, y);
                }

                GraphicsState state = g.Save();
                g.TranslateTransform(Bounds.X + Pan.X + (Bounds.Width - 1120 * Zoom) / 2f,
                                     Bounds.Y + Pan.Y + (Bounds.Height - 1080 * Zoom) / 2f);
                g.ScaleTransform(Zoom, Zoom);

                string focusId = _hoverId != null ? _hoverId : SelectedId;
                ModGraphNode focusNode = FindNode(focusId);
                int focusIndex = focusNode == null ? -1 : focusNode.Index;
                HashSet<int> relatedIndex = new HashSet<int>();
                if (focusNode != null)
                {
                    relatedIndex.Add(focusIndex);
                    for (int i = 0; i < Data.Edges.Count; i++)
                    {
                        ModGraphEdge e2 = Data.Edges[i];
                        if (e2.From == focusIndex) relatedIndex.Add(e2.To);
                        else if (e2.To == focusIndex) relatedIndex.Add(e2.From);
                    }
                }

                // 依赖边（贝塞尔；硬依赖青色、软依赖紫色，无关边压到很淡）
                for (int i = 0; i < Data.Edges.Count; i++)
                {
                    ModGraphEdge edge = Data.Edges[i];
                    if (edge.From < 0 || edge.To < 0 || edge.From >= Data.Nodes.Count || edge.To >= Data.Nodes.Count) continue;
                    ModGraphNode a = Data.Nodes[edge.From], b = Data.Nodes[edge.To];
                    bool related = focusNode == null || edge.From == focusIndex || edge.To == focusIndex;
                    bool bad = !a.Enabled || !b.Enabled;
                    Color c = edge.Hard ? Color.FromArgb(related ? (bad ? 150 : 210) : 16, 0, 229, 255)
                                        : Color.FromArgb(related ? (bad ? 60 : 95) : 10, 255, 80, 210);
                    using (Pen p = new Pen(c, related ? (edge.Hard ? 1.8f : 1.1f) : 0.6f))
                    {
                        int mx = (a.X + b.X) / 2;
                        int my = (a.Y + b.Y) / 2 - 20;
                        g.DrawBezier(p, a.X, a.Y, mx, my, mx, my, b.X, b.Y);
                    }
                }

                // 分类标签（取该分类节点质心）
                Dictionary<string, PointF> catPos = new Dictionary<string, PointF>();
                Dictionary<string, int> catCount = new Dictionary<string, int>();
                for (int i = 0; i < Data.Nodes.Count; i++)
                {
                    ModGraphNode n = Data.Nodes[i];
                    string cat = n.Category == null ? "" : n.Category;
                    PointF p;
                    if (!catPos.ContainsKey(cat)) { catPos[cat] = new PointF(n.X, n.Y); catCount[cat] = 1; }
                    else { catPos[cat] = new PointF(catPos[cat].X + n.X, catPos[cat].Y + n.Y); catCount[cat]++; }
                }
                foreach (KeyValuePair<string, PointF> kv in catPos)
                {
                    if (kv.Key.Length == 0) continue;
                    float x = kv.Value.X / catCount[kv.Key];
                    float y = kv.Value.Y / catCount[kv.Key];
                    Font f = T.B(13f / Math.Max(0.7f, Zoom)); // T 的字体是缓存共享的，不能 Dispose
                    using (SolidBrush tb = new SolidBrush(T.Accent))
                        g.DrawString(kv.Key, f, tb, x - 40, y - 8);
                }

                // 节点
                for (int i = 0; i < Data.Nodes.Count; i++)
                {
                    ModGraphNode n = Data.Nodes[i];
                    bool related = focusNode == null || relatedIndex.Contains(i);
                    int r = (n.Id == SelectedId || n.Id == _hoverId) ? 10 : 7;
                    if (!related) r = 5;
                    Image thumb = Zoom >= ThumbZoom ? GetThumb(n) : null;
                    if (thumb != null)
                    {
                        float tr = Math.Max(9f, r + 4f);
                        RectangleF dst = new RectangleF(n.X - tr, n.Y - tr, tr * 2f, tr * 2f);
                        GraphicsState inner = g.Save();
                        try
                        {
                            using (GraphicsPath clip = new GraphicsPath())
                            {
                                clip.AddEllipse(dst);
                                g.SetClip(clip, CombineMode.Intersect);
                                g.DrawImage(thumb, dst);
                            }
                        }
                        finally { g.Restore(inner); }
                        if (!n.Enabled)
                            using (SolidBrush dim = new SolidBrush(Color.FromArgb(155, T.LogBg)))
                                g.FillEllipse(dim, dst);
                        using (Pen bp = new Pen(n.Warning ? Color.Red
                            : (n.Id == SelectedId ? T.Text : Color.FromArgb(230, T.Accent)),
                            n.Id == SelectedId ? 2.6f : 1.4f))
                            g.DrawEllipse(bp, dst);
                    }
                    else
                    {
                        Color c = CategoryColor(n.Category);
                        if (!n.Enabled) c = Color.FromArgb(55, c);
                        if (n.Warning) c = Color.FromArgb(255, 40, 70);
                        if (!related) c = Color.FromArgb(30, c);
                        using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, n.X - r, n.Y - r, r * 2, r * 2);
                        using (Pen p = new Pen(n.Warning ? Color.Red : (related ? (n.Enabled ? T.Text : T.Text3) : Color.FromArgb(60, T.Text3)),
                            n.Id == SelectedId ? 2.4f : 1f))
                            g.DrawEllipse(p, n.X - r, n.Y - r, r * 2, r * 2);
                    }
                    if (related && (Zoom > 1.1f || n.Id == SelectedId || n.Id == _hoverId || n.Warning))
                    {
                        string label = !string.IsNullOrEmpty(n.NameZh) ? n.NameZh
                            : (n.Name == null ? n.Id : n.Name);
                        if (label.Length > 18) label = label.Substring(0, 18) + "…";
                        float labelX = (thumb != null ? Math.Max(9f, r + 4f) + 5f : r + 3f);
                        Font f = T.F(11f / Math.Max(0.8f, Zoom)); // 缓存共享字体，不 Dispose
                        using (SolidBrush tb = new SolidBrush(n.Enabled ? T.Text : T.Text3))
                            g.DrawString(label, f, tb, n.X + labelX, n.Y - 7);
                    }
                }
                g.Restore(state);
                g.ResetClip();
            }
            catch { }
        }
    }

    /// <summary>
    /// 排序列表（原 ModGraphForm 右侧 ListView 的元素版）：
    /// 每行 = 勾选框 + 序号 + 名称（有中文对照显示「中文 | 原名」）+ 分类；
    /// 点勾选框启停，点行聚焦到网图节点；警告行红字、停用行灰字；滚轮滚动 + 细滚动条。
    /// </summary>
    class ElModList : El
    {
        public readonly List<ModGraphNode> Rows = new List<ModGraphNode>(); // 已按方案顺序排好
        public event EventHandler RowChosen;   // 点行 → 聚焦节点（Tag=当前行）
        public event EventHandler NodeToggled; // 勾选启停后
        public string SelectedId;
        int _scroll, _hoverIdx = -1;
        const int ROWH = 26, PADX = 8, PADY = 6;

        /// <summary>外部触发重绘（基类 Redraw 是 protected）</summary>
        public void Redraw2() { Redraw(); }

        public ModGraphNode RowAtNode { get; private set; } // 最近一次点击的行节点

        public void SetRows(List<ModGraphNode> rows)
        {
            Rows.Clear();
            if (rows != null) Rows.AddRange(rows);
            _scroll = 0;
            Redraw();
        }

        int ContentHeight { get { return Rows.Count == 0 ? 0 : PADY * 2 + Rows.Count * ROWH; } }
        int MaxScroll { get { return Math.Max(0, ContentHeight - Bounds.Height); } }

        int RowAt(Point p)
        {
            int i = (p.Y - Bounds.Y - PADY + _scroll) / ROWH;
            if (i < 0 || i >= Rows.Count) return -1;
            int y = Bounds.Y + PADY - _scroll + i * ROWH;
            return (p.Y >= y && p.Y < y + ROWH) ? i : -1;
        }

        public override void OnMouseMove(Point p)
        {
            int i = RowAt(p);
            if (i != _hoverIdx) { _hoverIdx = i; Redraw(); }
        }
        public override void OnMouseLeave() { if (_hoverIdx != -1) { _hoverIdx = -1; Redraw(); } }

        public override void OnMouseUp(Point p)
        {
            int i = RowAt(p);
            if (i < 0) return;
            ModGraphNode n = Rows[i];
            RowAtNode = n;
            if (p.X < Bounds.X + 34) // 勾选框区域：启停
            {
                n.Enabled = !n.Enabled;
                if (NodeToggled != null) NodeToggled(this, EventArgs.Empty);
            }
            else
            {
                SelectedId = n.Id;
                if (RowChosen != null) RowChosen(this, EventArgs.Empty);
            }
            Redraw();
        }

        public override Cursor CursorOf(Point p) { return RowAt(p) >= 0 ? Cursors.Hand : Cursors.Default; }

        public override bool OnMouseWheel(int delta)
        {
            if (MaxScroll <= 0) return false;
            int next = Math.Max(0, Math.Min(MaxScroll, _scroll - Math.Sign(delta) * 44));
            if (next == _scroll) return false;
            _scroll = next;
            _hoverIdx = -1;
            Redraw();
            return true;
        }

        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r, 8, T.LogBg);
            T.DrawRound(g, r, 8, T.Line2, 1f);
            if (Rows.Count == 0)
            {
                using (var b = new SolidBrush(T.Text3))
                    g.DrawString(L.T("生成网状图后这里会列出全部模组。"), T.F(11.5f), b, r,
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                return;
            }
            g.SetClip(new Rectangle(Bounds.X + 1, Bounds.Y + 1, Bounds.Width - 2, Bounds.Height - 2));
            for (int i = 0; i < Rows.Count; i++)
            {
                int y = Bounds.Y + PADY - _scroll + i * ROWH;
                if (y + ROWH < Bounds.Y || y > Bounds.Bottom) continue;
                ModGraphNode n = Rows[i];
                var rr = new RectangleF(Bounds.X + 3, y + 1, Bounds.Width - 14, ROWH - 2);
                if (n.Id == SelectedId) { T.FillRound(g, rr, 5, T.AccentSoft); T.DrawRound(g, rr, 5, T.Accent, 1.2f); }
                else if (i == _hoverIdx) T.FillRound(g, rr, 5, T.Line2);

                // 勾选框
                int cy = y + ROWH / 2;
                var box = new RectangleF(Bounds.X + 10.5f, cy - 6.5f, 13, 13);
                T.FillRound(g, box, 3.5f, n.Enabled ? T.AccentSoft : T.Card);
                T.DrawRound(g, box, 3.5f, n.Enabled ? T.Accent : T.Line, 1f);
                if (n.Enabled)
                    using (var pen = new Pen(T.Accent, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(pen, Bounds.X + 13.5f, cy + 0.5f, Bounds.X + 16.5f, cy + 3.5f);
                        g.DrawLine(pen, Bounds.X + 16.5f, cy + 3.5f, Bounds.X + 21.5f, cy - 3.5f);
                    }

                string en = n.Name == null ? n.Id : n.Name;
                string shown = string.IsNullOrEmpty(n.NameZh) ? en : (n.NameZh + "  |  " + en);
                Color fg = n.Warning ? Color.FromArgb(214, 64, 84) : n.Enabled ? T.Text : T.Text3;
                using (var b = new SolidBrush(fg))
                    g.DrawString((i + 1) + ". " + shown, T.F(11.5f), b,
                        new RectangleF(Bounds.X + 32, y, Bounds.Width - 110, ROWH),
                        new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
                using (var b = new SolidBrush(T.Text3))
                    g.DrawString(n.Category ?? "", T.F(10.5f), b,
                        new RectangleF(Bounds.Right - 86, y, 70, ROWH),
                        new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
            }
            g.ResetClip();
            if (MaxScroll > 0)
            {
                float th = Math.Max(24f, Bounds.Height * Bounds.Height / (float)ContentHeight);
                float ty = Bounds.Y + (Bounds.Height - th) * (_scroll / (float)MaxScroll);
                T.FillRound(g, new RectangleF(Bounds.Right - 5, ty, 3, th), 1.5f, T.Line);
            }
        }
    }
}
