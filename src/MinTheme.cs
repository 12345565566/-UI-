using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Zogos
{
    /// <summary>
    /// 极简主题令牌 —— 支持浅色/深色一键切换（T.Dark）。
    /// 浅色板与网页原型（工作区A1\新前端-极简主页\index.html 的 :root）一一对应；
    /// 深色板为同一结构的镜像映射。后续并入主工程时，这份色板可以替换/映射到 Widgets.cs 的 Pal。
    /// </summary>
    static class T
    {
        /// <summary>深色模式开关（顶栏月亮/太阳按钮切换；自绘控件绘制时读这里，立即生效）</summary>
        public static bool Dark;

        // —— 色板：深色 ? 浅色 ——
        public static Color Page    { get { return Dark ? Color.FromArgb(10, 10, 12)     : Color.FromArgb(244, 244, 240); } } // 桌面衬底（兜底）
        public static Color Window  { get { return Dark ? Color.FromArgb(30, 30, 33)     : Color.FromArgb(252, 252, 250); } } // 窗口底
        public static Color Card    { get { return Dark ? Color.FromArgb(38, 38, 42)     : Color.White; } }
        public static Color Line    { get { return Dark ? Color.FromArgb(60, 60, 66)     : Color.FromArgb(231, 230, 224); } } // 1px 细线
        public static Color Line2   { get { return Dark ? Color.FromArgb(48, 48, 53)     : Color.FromArgb(239, 238, 233); } }
        public static Color Text    { get { return Dark ? Color.FromArgb(242, 242, 238)  : Color.FromArgb(28, 27, 23); } }
        public static Color Text2   { get { return Dark ? Color.FromArgb(170, 170, 165)  : Color.FromArgb(74, 73, 68); } }  // 次级文字：偏深，保证浅色底可读性
        public static Color Text3   { get { return Dark ? Color.FromArgb(120, 120, 116)  : Color.FromArgb(118, 117, 110); } } // 提示文字：同向加深一档
        public static Color Accent  { get { return Dark ? Color.FromArgb(45, 160, 172)   : Color.FromArgb(14, 124, 134); } } // 唯一强调色：低饱和青
        public static Color AccentSoft { get { return Dark ? Color.FromArgb(26, 58, 64)  : Color.FromArgb(230, 242, 243); } }
        public static Color WarnBg   { get { return Dark ? Color.FromArgb(62, 50, 24)    : Color.FromArgb(253, 247, 233); } }
        public static Color WarnLine { get { return Dark ? Color.FromArgb(110, 88, 40)   : Color.FromArgb(237, 220, 176); } }
        public static Color WarnText { get { return Dark ? Color.FromArgb(232, 200, 130) : Color.FromArgb(107, 84, 16); } }
        public static Color LogBg    { get { return Dark ? Color.FromArgb(26, 26, 29)    : Color.FromArgb(247, 247, 244); } }

        const string FAMILY = "Microsoft YaHei UI";
        static readonly Dictionary<string, Font> _cache = new Dictionary<string, Font>();

        /// <summary>常规字体，单位为像素（与网页 px 对齐；DPI 缩放下不随系统放大，保证布局不错位）</summary>
        public static Font F(float px)
        {
            string k = "r" + px;
            if (!_cache.ContainsKey(k)) _cache[k] = new Font(FAMILY, px, FontStyle.Regular, GraphicsUnit.Pixel);
            return _cache[k];
        }
        /// <summary>加粗字体，单位为像素</summary>
        public static Font B(float px)
        {
            string k = "b" + px;
            if (!_cache.ContainsKey(k)) _cache[k] = new Font(FAMILY, px, FontStyle.Bold, GraphicsUnit.Pixel);
            return _cache[k];
        }

        /// <summary>
        /// 圆角矩形路径：角部用超椭圆（squircle，n=4.5，苹果式连续曲率），
        /// 每角 20 段逼近，比四段 90° 圆弧更圆润规整，直边保持不变。
        /// </summary>
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            float rr = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f);
            const double EXP = 2.0 / 4.5; // 超椭圆指数：n=4.5
            const int SEG = 20;           // 每角分段数
            var pts = new List<PointF>((SEG + 1) * 4);

            // 四角圆心：左上 / 右上 / 右下 / 左下，扫描角 180→270→360→90→180（顺时针，y 向下）
            float l = r.X + rr, t = r.Y + rr, rt = r.Right - rr, rb = r.Bottom - rr;
            double[,] corners = { { l, t, 180 }, { rt, t, 270 }, { rt, rb, 0 }, { l, rb, 90 } };
            for (int c = 0; c < 4; c++)
            {
                double cx = corners[c, 0], cy = corners[c, 1], a0 = corners[c, 2];
                for (int i = 0; i <= SEG; i++)
                {
                    double a = (a0 + 90.0 * i / SEG) * Math.PI / 180.0;
                    double cos = Math.Cos(a), sin = Math.Sin(a);
                    float x = (float)(cx + rr * Math.Sign(cos) * Math.Pow(Math.Abs(cos), EXP));
                    float y = (float)(cy + rr * Math.Sign(sin) * Math.Pow(Math.Abs(sin), EXP));
                    pts.Add(new PointF(x, y));
                }
            }
            var p = new GraphicsPath();
            p.AddPolygon(pts.ToArray());
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float rad, Color c)
        {
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) { g.FillPath(b, p); }
        }
        public static void DrawRound(Graphics g, RectangleF r, float rad, Color c, float w)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, w)) { g.DrawPath(pen, p); }
        }
    }
}
