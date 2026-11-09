using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 排序方案卡片（对应原 ZSchemeCard，W:692-800）：
    /// 左侧单选圆环（选中时内点 8px）+ 名称（加粗）/描述 + 右侧徽章（本地缺失 N 个 / 含 RJW ×N）。
    /// 选中态：AccentSoft 底 + Accent 2px 描边。点击触发 Picked（契约 pickSchemeCard）。
    /// </summary>
    class ElSchemeCard : El
    {
        public SortScheme Scheme; // 对应的真实排序方案（并轨后由 BuildCards 填入）
        public string Title, Desc;
        public int Missing, Rjw; // 徽章数值，0 = 不显示
        public bool Selected;
        public ElSchemeList ParentList; // 滚轮冒泡用（悬停在卡片上也要能滚列表）
        public event EventHandler Picked;
        bool _hover, _down;

        public override void OnMouseMove(Point p) { if (!_hover) { _hover = true; Redraw(); } }
        public override void OnMouseLeave() { _hover = false; _down = false; Redraw(); }
        public override void OnMouseDown(Point p) { _down = true; Redraw(); }
        public override void OnMouseUp(Point p)
        {
            bool was = _down; _down = false; Redraw();
            if (was && Bounds.Contains(p) && Picked != null) Picked(this, EventArgs.Empty);
        }
        public override bool OnMouseWheel(int delta)
        {
            return ParentList != null && ParentList.OnMouseWheel(delta); // 冒泡给列表
        }
        public override Cursor CursorOf(Point p) { return Cursors.Hand; }

        public override void Paint(Graphics g)
        {
            var r = new RectangleF(Bounds.X + 0.5f, Bounds.Y + 0.5f, Bounds.Width - 1f, Bounds.Height - 1f);
            T.FillRound(g, r, 9, _down ? T.Line2 : _hover ? T.Line2 : T.Card); // 选中态不再铺浅青底，只留细青描边（减弱）
            T.DrawRound(g, r, 9, Selected ? T.Accent : T.Line, Selected ? 1.4f : 1f);

            // 单选圆环：(18, cy-9, 18, 18)，选中时内点 8px（与原实现一致）
            int cy = Bounds.Y + Bounds.Height / 2;
            using (var pen = new Pen(Selected ? T.Accent : T.Text3, 1.6f))
                g.DrawEllipse(pen, Bounds.X + 18, cy - 9, 18, 18);
            if (Selected)
                using (var b = new SolidBrush(T.Accent)) g.FillEllipse(b, Bounds.X + 23, cy - 4, 8, 8);

            int tx = Bounds.X + 48;
            using (var b = new SolidBrush(T.Text)) g.DrawString(Title, T.B(13), b, tx, Bounds.Y + 12);
            using (var b = new SolidBrush(T.Text3)) g.DrawString(Desc, T.F(11.5f), b, tx, Bounds.Y + 36);

            // 徽章从右往左排（原实现同款）
            int bx = Bounds.Right - 14;
            if (Rjw > 0) bx = DrawBadge(g, "含 RJW ×" + Rjw, bx, cy, T.AccentSoft, T.Accent);
            if (Missing > 0) DrawBadge(g, "本地缺失 " + Missing + " 个", bx, cy, T.WarnBg, T.WarnText);
        }

        int DrawBadge(Graphics g, string s, int right, int cy, Color bg, Color fg)
        {
            var f = T.F(11);
            var sz = TextRenderer.MeasureText(s, f);
            int w = sz.Width + 16, h = 22;
            var r = new RectangleF(right - w, cy - h / 2f, w, h);
            T.FillRound(g, r, 9, bg);
            using (var b = new SolidBrush(fg))
                g.DrawString(s, f, b, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            return right - w - 8;
        }
    }

    /// <summary>
    /// 可滚动的方案卡片列表（对应原 _sortList + LayoutCards，MF:910-920）：
    /// 行高 66 + 行距 8，左缩进 8，右侧留 16 给滚动条；滚轮滚动，右侧细滚动条。
    /// </summary>
    class ElSchemeList : El
    {
        public readonly List<ElSchemeCard> Cards = new List<ElSchemeCard>();
        int _scroll;
        const int ROWH = 66, GAP = 8, PADX = 8, PADY = 8;

        public void AddCard(ElSchemeCard c)
        {
            c.Host = Host;
            c.ParentList = this;
            Cards.Add(c);
            Relayout();
        }

        /// <summary>清空全部卡片（ReloadSchemes → BuildCards 重建前调用）</summary>
        public void ClearCards()
        {
            Cards.Clear();
            _scroll = 0;
            Relayout();
        }

        int ContentHeight { get { return Cards.Count == 0 ? 0 : PADY * 2 + Cards.Count * ROWH + (Cards.Count - 1) * GAP; } }
        int MaxScroll { get { return Math.Max(0, ContentHeight - Bounds.Height); } }

        public void Relayout()
        {
            int w = Math.Max(120, Bounds.Width - PADX * 2 - 16);
            int y = Bounds.Y + PADY - _scroll;
            foreach (var c in Cards) { c.SetBounds(Bounds.X + PADX, y, w, ROWH); y += ROWH + GAP; }
        }
        protected override void OnBoundsChanged()
        {
            _scroll = Math.Min(_scroll, MaxScroll);
            Relayout();
        }

        /// <summary>命中卡片（只认落在列表可视区内的，调用方已保证 p 在 Bounds 内）</summary>
        public El PickChild(Point p)
        {
            for (int i = Cards.Count - 1; i >= 0; i--)
                if (Cards[i].Visible && Cards[i].HitTest(p)) return Cards[i];
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
            g.SetClip(new Rectangle(Bounds.X + 1, Bounds.Y + 1, Bounds.Width - 2, Bounds.Height - 2));
            foreach (var c in Cards) if (c.Visible) c.Paint(g);
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
