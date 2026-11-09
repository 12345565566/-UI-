using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 全窗口半透明遮罩（模态覆盖层的底板）：
    /// 盖住整个主窗口内容区，点击触发 Clicked（用于「点遮罩关闭」）。
    /// Alpha 供打开/关闭动画淡入淡出（0~1）。
    /// </summary>
    class ElDim : El
    {
        public float Alpha = 1f;
        public event EventHandler Clicked;

        public override void OnMouseUp(Point p)
        {
            if (Bounds.Contains(p) && Clicked != null) Clicked(this, EventArgs.Empty);
        }

        public override void Paint(Graphics g)
        {
            using (var b = new SolidBrush(Color.FromArgb((int)(150 * Alpha), T.Window)))
                g.FillRectangle(b, Bounds);
        }
    }

    /// <summary>
    /// 创意工坊搜索结果行：标题（加粗）+ 右侧订阅数徽章 + 第二行简介。
    /// 单击选中（AccentSoft 底 + Accent 描边）；450ms 内再次点击已选中行 → Opened（打开工坊页面）。
    /// </summary>
    class ElWsRow : El
    {
        public WorkshopItem Item;
        public bool Selected;
        public ElWsList ParentList; // 滚轮冒泡用
        public event EventHandler Picked;  // 单击选中
        public event EventHandler Opened;  // 双击已选中行 → 打开页面
        bool _hover, _down;
        DateTime _lastUp = DateTime.MinValue;

        public override void OnMouseMove(Point p) { if (!_hover) { _hover = true; Redraw(); } }
        public override void OnMouseLeave() { _hover = false; _down = false; Redraw(); }
        public override void OnMouseDown(Point p) { _down = true; Redraw(); }
        public override void OnMouseUp(Point p)
        {
            bool was = _down; _down = false; Redraw();
            if (!was || !Bounds.Contains(p)) return;
            bool again = Selected && (DateTime.Now - _lastUp).TotalMilliseconds < 450;
            _lastUp = DateTime.Now;
            if (again) { if (Opened != null) Opened(this, EventArgs.Empty); }
            else if (Picked != null) Picked(this, EventArgs.Empty);
        }
        public override bool OnMouseWheel(int delta)
        {
            return ParentList != null && ParentList.OnMouseWheel(delta); // 冒泡给列表
        }
        public override Cursor CursorOf(Point p) { return Cursors.Hand; }

        static string FormatSubs(int n)
        {
            if (n >= 10000) return L.T("订阅 ") + (n / 10000.0).ToString("0.#") + L.T("万");
            return L.T("订阅 ") + n;
        }

        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r, 8, Selected ? T.AccentSoft : _hover ? T.Line2 : T.Card);
            T.DrawRound(g, r, 8, Selected ? T.Accent : T.Line, Selected ? 1.4f : 1f);

            int cy = Bounds.Y + Bounds.Height / 2;
            int right = Bounds.Right - 12;

            // 订阅数徽章（右对齐，底色 Line2 / 字 Text2）
            if (Item != null)
            {
                string badge = FormatSubs(Item.Subscriptions);
                var bf = T.F(11);
                var bsz = TextRenderer.MeasureText(badge, bf);
                int bw2 = bsz.Width + 14, bh2 = 20;
                var br = new RectangleF(right - bw2, cy - bh2 / 2f - 8, bw2, bh2);
                T.FillRound(g, br, 8, T.Line2);
                using (var b = new SolidBrush(T.Text2))
                    g.DrawString(badge, bf, b, br, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                right -= bw2 + 10;
            }

            // 标题（加粗，左）+ 简介（第二行，超宽省略）
            var tf = T.B(12.5f);
            var tRect = new RectangleF(Bounds.X + 12, Bounds.Y + 6, Math.Max(40, right - Bounds.X - 12), 20);
            using (var b = new SolidBrush(T.Text))
                g.DrawString(Item == null ? "" : Item.Title, tf, b, tRect,
                    new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
            string desc = Item == null || Item.Description == null ? "" : Item.Description.Replace("\r", " ").Replace("\n", " ");
            var dRect = new RectangleF(Bounds.X + 12, Bounds.Y + 27, Math.Max(40, Bounds.Width - 24), 17);
            using (var b = new SolidBrush(T.Text3))
                g.DrawString(desc, T.F(11), b, dRect,
                    new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
        }
    }

    /// <summary>
    /// 创意工坊结果列表（结构照搬 ElSchemeList）：
    /// 行高 48 + 行距 6，内边距 8；滚轮滚动，右侧细滚动条；空列表居中灰字提示。
    /// </summary>
    class ElWsList : El
    {
        public readonly List<ElWsRow> Rows = new List<ElWsRow>();
        public string EmptyHint = "";
        public event EventHandler SelectionChanged;
        public event EventHandler ItemOpened;
        int _scroll;
        const int ROWH = 48, GAP = 6, PADX = 8, PADY = 8;

        /// <summary>重建全部行（新搜索结果到达时调用）</summary>
        public void SetItems(List<WorkshopItem> items)
        {
            Rows.Clear();
            _scroll = 0;
            if (items != null)
                foreach (var it in items)
                {
                    var row = new ElWsRow { Item = it, Host = Host, ParentList = this };
                    row.Picked += delegate(object s, EventArgs e)
                    {
                        var r = (ElWsRow)s;
                        foreach (var x in Rows) x.Selected = x == r;
                        if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                        Redraw();
                    };
                    row.Opened += delegate(object s, EventArgs e)
                    {
                        if (ItemOpened != null) ItemOpened(this, EventArgs.Empty);
                    };
                    Rows.Add(row);
                }
            Relayout();
            Redraw();
        }

        public WorkshopItem SelectedItem
        {
            get
            {
                foreach (var r in Rows) if (r.Selected) return r.Item;
                return null;
            }
        }

        int ContentHeight { get { return Rows.Count == 0 ? 0 : PADY * 2 + Rows.Count * ROWH + (Rows.Count - 1) * GAP; } }
        int MaxScroll { get { return Math.Max(0, ContentHeight - Bounds.Height); } }

        public void Relayout()
        {
            int w = Math.Max(120, Bounds.Width - PADX * 2 - 16);
            int y = Bounds.Y + PADY - _scroll;
            foreach (var r in Rows) { r.SetBounds(Bounds.X + PADX, y, w, ROWH); y += ROWH + GAP; }
        }
        protected override void OnBoundsChanged()
        {
            _scroll = Math.Min(_scroll, MaxScroll);
            Relayout();
        }

        /// <summary>命中行（调用方已保证 p 在 Bounds 内）</summary>
        public El PickChild(Point p)
        {
            for (int i = Rows.Count - 1; i >= 0; i--)
                if (Rows[i].Visible && Rows[i].HitTest(p)) return Rows[i];
            return null;
        }

        public override bool OnMouseWheel(int delta)
        {
            if (MaxScroll <= 0) return false;
            int next = Math.Max(0, Math.Min(MaxScroll, _scroll - Math.Sign(delta) * 44));
            if (next == _scroll) return false;
            _scroll = next;
            Relayout();
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
                    g.DrawString(EmptyHint, T.F(11.5f), b, r,
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }
            else
            {
                g.SetClip(new Rectangle(Bounds.X + 1, Bounds.Y + 1, Bounds.Width - 2, Bounds.Height - 2));
                foreach (var row in Rows) if (row.Visible) row.Paint(g);
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

    /// <summary>
    /// 简单字符串列表（AI 模型列表用）：单行 30px，单击选中（AccentSoft 底 + Accent 描边），
    /// 滚轮滚动 + 右侧细滚动条，空列表居中灰字提示。
    /// </summary>
    class ElStrList : El
    {
        public readonly List<string> Items = new List<string>();
        public string EmptyHint = "";
        public int SelectedIndex = -1;
        public event EventHandler SelectionChanged;
        int _scroll, _hoverIdx = -1;
        const int ROWH = 30, PADX = 8, PADY = 6;

        public void SetItems(List<string> items)
        {
            Items.Clear();
            if (items != null) Items.AddRange(items);
            _scroll = 0;
            SelectedIndex = -1;
            Redraw();
        }

        public string SelectedItem
        {
            get { return SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null; }
        }

        int ContentHeight { get { return Items.Count == 0 ? 0 : PADY * 2 + Items.Count * ROWH; } }
        int MaxScroll { get { return Math.Max(0, ContentHeight - Bounds.Height); } }

        int RowAt(Point p)
        {
            int i = (p.Y - Bounds.Y - PADY + _scroll) / ROWH;
            if (i < 0 || i >= Items.Count) return -1;
            int y = Bounds.Y + PADY - _scroll + i * ROWH;
            if (p.Y < y || p.Y >= y + ROWH) return -1;
            return i;
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
            SelectedIndex = i;
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            Redraw();
        }
        public override Cursor CursorOf(Point p) { return RowAt(p) >= 0 ? Cursors.Hand : Cursors.Default; }

        public override bool OnMouseWheel(int delta)
        {
            if (MaxScroll <= 0) return false;
            int next = Math.Max(0, Math.Min(MaxScroll, _scroll - Math.Sign(delta) * 44));
            if (next == _scroll) return false;
            _scroll = next;
            Redraw();
            return true;
        }

        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r, 8, T.LogBg);
            T.DrawRound(g, r, 8, T.Line2, 1f);
            if (Items.Count == 0)
            {
                using (var b = new SolidBrush(T.Text3))
                    g.DrawString(EmptyHint, T.F(11.5f), b, r,
                        new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                return;
            }
            g.SetClip(new Rectangle(Bounds.X + 1, Bounds.Y + 1, Bounds.Width - 2, Bounds.Height - 2));
            var f = T.F(12);
            for (int i = 0; i < Items.Count; i++)
            {
                int y = Bounds.Y + PADY - _scroll + i * ROWH;
                if (y + ROWH < Bounds.Y || y > Bounds.Bottom) continue;
                var rr = new RectangleF(Bounds.X + PADX, y + 1, Bounds.Width - PADX * 2 - 12, ROWH - 2);
                if (i == SelectedIndex) { T.FillRound(g, rr, 6, T.AccentSoft); T.DrawRound(g, rr, 6, T.Accent, 1.2f); }
                else if (i == _hoverIdx) T.FillRound(g, rr, 6, T.Line2);
                using (var b = new SolidBrush(i == SelectedIndex ? T.Accent : T.Text))
                    g.DrawString(Items[i], f, b, new RectangleF(rr.X + 8, rr.Y, rr.Width - 16, rr.Height),
                        new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter });
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
