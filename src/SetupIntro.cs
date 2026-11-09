using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>安装程序里的引导动画：三步循环高亮，讲清启动器是干什么的。</summary>
    public class ZIntroPanel : Control
    {
        private int _step;
        private Timer _tm;
        private Font _fNum, _fTitle, _fCap;

        private static readonly string[] Titles = new string[] { L.T("① 整合包"), L.T("② 选排序"), L.T("③ 点启动") };
        private static readonly string[] Caps = new string[] {
            L.T("整合包就是「游戏本体 + 一堆模组 + 一份排序表」。"),
            L.T("不同的排序 = 不同的玩法（比如带不带 RJW），启动器帮你挑。"),
            L.T("点一下【启动游戏】：备份原配置 → 写入整合包配置 → 写好排序 → 开玩。")
        };

        public ZIntroPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Pal.White;
            _fNum = Fonts.B(13f);
            _fTitle = Fonts.B(10f);
            _fCap = Fonts.F(9f);
            _tm = new Timer();
            _tm.Interval = 1800;
            _tm.Tick += delegate(object s, EventArgs e)
            {
                _step = (_step + 1) % 3;
                Invalidate();
            };
            _tm.Start();
        }

        protected override void Dispose(bool disposing)
        {
            try { if (_tm != null) { _tm.Stop(); _tm.Dispose(); _tm = null; } } catch { }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle cr = ClientRectangle;
                if (cr.Width < 120 || cr.Height < 80) return;
                using (SolidBrush b = new SolidBrush(Pal.White)) g.FillRectangle(b, cr);

                int gap = 14;
                int capH = 26;
                int cardH = cr.Height - capH - 12;
                if (cardH < 50) cardH = 50;
                int cardW = (cr.Width - gap * 2) / 3;
                int y = 6;

                for (int i = 0; i < 3; i++)
                {
                    Rectangle r = new Rectangle(i * (cardW + gap), y, cardW, cardH);
                    bool act = (i == _step);
                    Color fill = act ? Color.FromArgb(232, 243, 255) : Color.FromArgb(248, 250, 253);
                    Color border = act ? Pal.Accent : Color.FromArgb(226, 234, 244);
                    Draw.FillRound(g, r, 10, fill);
                    Draw.StrokeRound(g, r, 10, border, act ? 2f : 1f);

                    // 圆形编号
                    Rectangle num = new Rectangle(r.X + 12, r.Y + 10, 26, 26);
                    using (SolidBrush b = new SolidBrush(act ? Pal.Accent : Color.FromArgb(190, 202, 216)))
                        g.FillEllipse(b, num);
                    Draw.T1(g, (i + 1).ToString(), _fNum, Color.White, num, ContentAlignment.MiddleCenter);

                    // 标题
                    Draw.T1(g, Titles[i].Substring(2), _fTitle, act ? Pal.AccentDeep : Pal.TextSub,
                        new Rectangle(r.X + 44, r.Y + 12, r.Width - 54, 22), ContentAlignment.MiddleLeft);

                    // 图标
                    Rectangle ic = new Rectangle(r.X + r.Width / 2 - 26, r.Y + 40, 52, 42);
                    DrawIcon(g, i, ic, act);

                    if (act)
                    {
                        // 活动卡片下面加一条高亮
                        Rectangle bar = new Rectangle(r.X + 18, r.Bottom - 8, r.Width - 36, 4);
                        Draw.FillRound(g, bar, 2, Pal.Accent);
                    }
                }

                // 说明文字
                Rectangle cap = new Rectangle(0, cardH + 14, cr.Width, capH);
                Draw.T1(g, Caps[_step], _fCap, Pal.TextMain, cap, ContentAlignment.MiddleLeft);

                // 小圆点指示
                int dw = 8, dg = 6;
                int totalW = 3 * dw + 2 * dg;
                int dx = cr.Width - totalW;
                int dy = cap.Y + cap.Height / 2 - dw / 2;
                for (int i = 0; i < 3; i++)
                {
                    Rectangle d = new Rectangle(dx + i * (dw + dg), dy, dw, dw);
                    using (SolidBrush b = new SolidBrush(i == _step ? Pal.Accent : Color.FromArgb(210, 220, 232)))
                        g.FillEllipse(b, d);
                }
            }
            catch { }
        }

        private void DrawIcon(Graphics g, int kind, Rectangle r, bool act)
        {
            Color c = act ? Pal.Accent : Color.FromArgb(178, 192, 208);
            if (kind == 0)
            {
                // 文件夹
                Rectangle body = new Rectangle(r.X + 4, r.Y + 10, r.Width - 8, r.Height - 14);
                Draw.FillRound(g, body, 5, c);
                Rectangle tab = new Rectangle(r.X + 10, r.Y + 4, 18, 12);
                Draw.FillRound(g, tab, 3, c);
            }
            else if (kind == 1)
            {
                // 列表
                for (int i = 0; i < 3; i++)
                {
                    int yy = r.Y + 8 + i * 12;
                    Rectangle dot = new Rectangle(r.X + 6, yy + 2, 6, 6);
                    using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, dot);
                    Rectangle line = new Rectangle(r.X + 18, yy + 3, r.Width - 26, 5);
                    Draw.FillRound(g, line, 2, c);
                }
            }
            else
            {
                // 播放三角
                Rectangle cir = new Rectangle(r.X + r.Width / 2 - 20, r.Y + 2, 40, 40);
                using (Pen p = new Pen(c, 3)) g.DrawEllipse(p, cir);
                PointF[] tri = new PointF[3];
                tri[0] = new PointF(cir.X + 15, cir.Y + 11);
                tri[1] = new PointF(cir.X + 15, cir.Y + 29);
                tri[2] = new PointF(cir.X + 30, cir.Y + 20);
                using (SolidBrush b = new SolidBrush(c)) g.FillPolygon(b, tri);
            }
        }
    }
}
