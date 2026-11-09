using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Zogos
{
    public class ModGraphControl : Control
    {
        public ModGraphData Data;
        public string SelectedId;
        public event EventHandler NodeToggled;
        public event EventHandler NodeSelected;
        public float Zoom = 1f;
        public PointF Pan = new PointF(0f, 0f);

        /// <summary>放大到这个倍数以上才显示模组缩略图。</summary>
        public const float ThumbZoom = 1.7f;
        private readonly Dictionary<string, Image> _thumbs = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        private Point _lastMouse;
        private bool _dragging;
        private bool _moved;
        private string _hoverId;

        private static readonly Color[] Palette = new Color[] {
            Color.FromArgb(0,229,255), Color.FromArgb(255,80,210), Color.FromArgb(120,255,170),
            Color.FromArgb(255,190,60), Color.FromArgb(140,120,255), Color.FromArgb(80,180,255),
            Color.FromArgb(255,110,110), Color.FromArgb(200,255,80), Color.FromArgb(90,230,210), Color.FromArgb(180,180,180)
        };

        public ModGraphControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(4, 7, 16);
            Cursor = Cursors.Default;
        }

        public void SetData(ModGraphData data)
        {
            Data = data;
            SelectedId = null;
            Zoom = FitZoom();
            Pan = new PointF(0f, 0f);
            Invalidate();
        }

        private float FitZoom()
        {
            if (Data == null || Data.Nodes.Count == 0 || Width < 50 || Height < 50) return 1f;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < Data.Nodes.Count; i++)
            {
                ModGraphNode n = Data.Nodes[i];
                if (n.X < minX) minX = n.X; if (n.X > maxX) maxX = n.X;
                if (n.Y < minY) minY = n.Y; if (n.Y > maxY) maxY = n.Y;
            }
            float sx = (Width - 80f) / Math.Max(1f, maxX - minX);
            float sy = (Height - 80f) / Math.Max(1f, maxY - minY);
            return Math.Max(0.2f, Math.Min(1.2f, Math.Min(sx, sy)));
        }

        public ModGraphNode FindNode(string id)
        {
            if (Data == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Data.Nodes.Count; i++) if (string.Equals(Data.Nodes[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Data.Nodes[i];
            return null;
        }

        public void FocusNode(string id, bool center)
        {
            ModGraphNode n = FindNode(id);
            if (n == null) return;
            SelectedId = n.Id;
            if (center)
            {
                float sx = Pan.X + (Width - 1120 * Zoom) / 2f + n.X * Zoom;
                float sy = Pan.Y + (Height - 1080 * Zoom) / 2f + n.Y * Zoom;
                Pan = new PointF(Pan.X + Width / 2f - sx, Pan.Y + Height / 2f - sy);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                try { g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; } catch { }
                using (LinearGradientBrush bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(5,8,18), Color.FromArgb(12,18,38), 30f))
                    g.FillRectangle(bg, ClientRectangle);
                using (Pen grid = new Pen(Color.FromArgb(18, 0, 229, 255), 1f))
                {
                    for (int x = 0; x < Width; x += 40) g.DrawLine(grid, x, 0, x, Height);
                    for (int y = 0; y < Height; y += 40) g.DrawLine(grid, 0, y, Width, y);
                }
                if (Data == null || Data.Nodes.Count == 0)
                {
                    TextRenderer.DrawText(g, L.T("还没有生成网状图。先点【AI 分类并生成网状图】。"), Fonts.B(11f), ClientRectangle, Pal.TextSub, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    return;
                }

                GraphicsState state = g.Save();
                g.TranslateTransform(Pan.X + (Width - 1120 * Zoom) / 2f, Pan.Y + (Height - 1080 * Zoom) / 2f);
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

                // edges
                for (int i = 0; i < Data.Edges.Count; i++)
                {
                    ModGraphEdge edge = Data.Edges[i];
                    if (edge.From < 0 || edge.To < 0 || edge.From >= Data.Nodes.Count || edge.To >= Data.Nodes.Count) continue;
                    ModGraphNode a = Data.Nodes[edge.From], b = Data.Nodes[edge.To];
                    bool related = focusNode == null || edge.From == focusIndex || edge.To == focusIndex;
                    bool bad = !a.Enabled || !b.Enabled;
                    Color c = edge.Hard ? Color.FromArgb(related ? (bad ? 150 : 210) : 16, 0, 229, 255) : Color.FromArgb(related ? (bad ? 60 : 95) : 10, 255, 80, 210);
                    using (Pen p = new Pen(c, related ? (edge.Hard ? 1.8f : 1.1f) : 0.6f))
                    {
                        int mx = (a.X + b.X) / 2;
                        int my = (a.Y + b.Y) / 2 - 20;
                        g.DrawBezier(p, a.X, a.Y, mx, my, mx, my, b.X, b.Y);
                    }
                }

                // category labels
                Dictionary<string, PointF> catPos = new Dictionary<string, PointF>();
                Dictionary<string, int> catCount = new Dictionary<string, int>();
                for (int i = 0; i < Data.Nodes.Count; i++)
                {
                    ModGraphNode n = Data.Nodes[i];
                    PointF p;
                    if (!catPos.ContainsKey(n.Category)) { catPos[n.Category] = new PointF(n.X, n.Y); catCount[n.Category] = 1; }
                    else { catPos[n.Category] = new PointF(catPos[n.Category].X + n.X, catPos[n.Category].Y + n.Y); catCount[n.Category]++; }
                }
                foreach (KeyValuePair<string, PointF> kv in catPos)
                {
                    float x = kv.Value.X / catCount[kv.Key];
                    float y = kv.Value.Y / catCount[kv.Key];
                    using (Font f = Fonts.B(13f / Math.Max(0.7f, Zoom)))
                    using (SolidBrush tb = new SolidBrush(Pal.Accent))
                        g.DrawString(kv.Key, f, tb, x - 40, y - 8);
                }

                // nodes
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
                        {
                            using (SolidBrush dim = new SolidBrush(Color.FromArgb(155, 4, 7, 16)))
                                g.FillEllipse(dim, dst);
                        }
                        using (Pen bp = new Pen(n.Warning ? Color.Red
                            : (n.Id == SelectedId ? Color.White : Color.FromArgb(230, Pal.Accent)),
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
                        using (Pen p = new Pen(n.Warning ? Color.Red : (related ? (n.Enabled ? Color.White : Color.FromArgb(80, 180, 200)) : Color.FromArgb(25, 80, 180, 200)), n.Id == SelectedId ? 2.4f : 1f))
                            g.DrawEllipse(p, n.X - r, n.Y - r, r * 2, r * 2);
                    }
                    if (related && (Zoom > 1.1f || n.Id == SelectedId || n.Id == _hoverId || n.Warning))
                    {
                        string label = !string.IsNullOrEmpty(n.NameZh) ? n.NameZh
                            : (n.Name == null ? n.Id : n.Name);
                        if (label.Length > 18) label = label.Substring(0, 18) + "…";
                        // 显示缩略图时节点变大了，标签要往外挪，否则会压在图上
                        float labelX = (thumb != null ? Math.Max(9f, r + 4f) + 5f : r + 3f);
                        using (Font f = Fonts.F(8f / Math.Max(0.8f, Zoom)))
                        using (SolidBrush tb = new SolidBrush(n.Enabled ? Pal.TextMain : Pal.TextFaint))
                            g.DrawString(label, f, tb, n.X + labelX, n.Y - 7);
                    }
                }
                g.Restore(state);
            }
            catch { }
        }

        /// <summary>
        /// 取某个模组的缩略图（About\Preview.png 之类）。
        /// 一次加载就缩成 64x64 缓存起来：600 多个模组全用原图会吃掉上百 MB。
        /// </summary>
        private Image GetThumb(ModGraphNode n)
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    foreach (KeyValuePair<string, Image> kv in _thumbs)
                        if (kv.Value != null) kv.Value.Dispose();
                    _thumbs.Clear();
                }
                catch { }
            }
            base.Dispose(disposing);
        }

        private static Color CategoryColor(string cat)
        {
            int h = 0;
            for (int i = 0; i < cat.Length; i++) h = h * 31 + cat[i];
            return Palette[Math.Abs(h) % Palette.Length];
        }

        private ModGraphNode HitNode(int x, int y)
        {
            if (Data == null) return null;
            float gx = (x - Pan.X - (Width - 1120 * Zoom) / 2f) / Zoom;
            float gy = (y - Pan.Y - (Height - 1080 * Zoom) / 2f) / Zoom;
            for (int i = Data.Nodes.Count - 1; i >= 0; i--)
            {
                ModGraphNode n = Data.Nodes[i];
                float dx = gx - n.X, dy = gy - n.Y;
                float rr = 12f;
                if (dx * dx + dy * dy <= rr * rr) return n;
            }
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _lastMouse = e.Location;
            _dragging = true;
            _moved = false;
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging && e.Button == MouseButtons.Left)
            {
                int dx = e.X - _lastMouse.X, dy = e.Y - _lastMouse.Y;
                if (Math.Abs(dx) + Math.Abs(dy) > 2) _moved = true;
                Pan = new PointF(Pan.X + dx, Pan.Y + dy);
                _lastMouse = e.Location;
                Invalidate();
            }
            else
            {
                ModGraphNode n = HitNode(e.X, e.Y);
                string id = n == null ? null : n.Id;
                Cursor = n == null ? Cursors.Default : Cursors.Hand;
                if (id != _hoverId) { _hoverId = id; Invalidate(); }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool moved = _moved;
            _dragging = false;
            if (!moved)
            {
                ModGraphNode n = HitNode(e.X, e.Y);
                if (n != null)
                {
                    SelectedId = n.Id;
                    if (e.Button == MouseButtons.Right)
                    {
                        n.Enabled = !n.Enabled;
                        ModGraphBuilder.RecomputeWarnings(Data);
                        if (NodeToggled != null) NodeToggled(this, EventArgs.Empty);
                    }
                    else if (NodeSelected != null) NodeSelected(this, EventArgs.Empty);
                    Invalidate();
                }
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float old = Zoom;
            Zoom = Math.Max(0.25f, Math.Min(4f, Zoom * (e.Delta > 0 ? 1.12f : 0.89f)));
            // 以鼠标位置为中心缩放
            float mx = e.X, my = e.Y;
            Pan = new PointF(mx - (mx - Pan.X) * (Zoom / old), my - (my - Pan.Y) * (Zoom / old));
            Invalidate();
            base.OnMouseWheel(e);
        }
    }
}
