using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Zogos
{
    /// <summary>
    /// 窗口内侧装饰框 + 轻量后处理（不改任何布局坐标，只在整窗位图上叠一层）。
    /// 组成：呼吸描边（双线） / 四角角标 / 扫描线 / 暗角。（鼠标跟随光斑已按用户要求移除）
    /// 全部画在已有 squircle 之内并裁剪，不会在桌面上留下方形外发光。
    /// </summary>
    static class FrameFx
    {
        /// <summary>总开关：呼吸特效层是卡顿主源（每帧全窗重合成），默认关闭；想恢复改回 true 即可（TODO：以后接到设置页做成选项）</summary>
        public static bool Enabled = false;
        /// <summary>只画静态部分（扫描线/暗角/角标），用于低配或自检</summary>
        public static bool Animate = true;

        static float _phase;
        static Bitmap _scan;
        static Color _scanKey;

        public static void Advance(float dt)
        {
            _phase += dt;
            if (_phase > 6283.185f) _phase -= 6283.185f; // 2π 的整数倍后复位，避免 float 精度漂移
        }

        /// <summary>在整窗位图上叠效果。w/h 为逻辑尺寸，mouse 为逻辑鼠标坐标。</summary>
        public static void Paint(Graphics g, float w, float h, Point mouse)
        {
            if (!Enabled || w < 80 || h < 60) return;

            float pulse = Animate ? (float)((Math.Sin(_phase) + 1) * 0.5) : 0.5f; // 0..1 呼吸

            GraphicsState st = g.Save();
            using (var clip = T.Round(new RectangleF(0, 0, w, h), 12))
                g.SetClip(clip);

            // 1) 外圈描边：贴窗口边缘，1.2px，透明度呼吸
            using (var p = T.Round(new RectangleF(0.6f, 0.6f, w - 1.2f, h - 1.2f), 11.5f))
            using (var pen = new Pen(Color.FromArgb(16 + (int)(26 * pulse), T.Accent), 1.2f))
                g.DrawPath(pen, p);

            // 2) 内圈第二道线：内缩 5px，更淡，做出双层边框的层次
            using (var p2 = T.Round(new RectangleF(5.2f, 5.2f, w - 10.4f, h - 10.4f), 8.6f))
            using (var pen2 = new Pen(Color.FromArgb(8 + (int)(10 * pulse), T.Accent), 1f))
                g.DrawPath(pen2, p2);

            // 3) 四角角标
            DrawCorners(g, w, h, 70 + (int)(60 * pulse));

            // 4) 扫描线 + 暗角（静态，最轻的两层）
            DrawScanlines(g, w, h);
            DrawVignette(g, w, h);

            g.Restore(st);
        }

        static void DrawCorners(Graphics g, float w, float h, int alpha)
        {
            const float L = 22f;   // 角标长度
            const float I = 7f;    // 距边缘内缩
            const float G = 14f;   // 起点：跳过圆角曲率，贴着弧线末端
            using (var pen = new Pen(Color.FromArgb(alpha, T.Accent), 1.8f))
            {
                // 左上
                g.DrawLine(pen, I + G, I, I + G + L, I);
                g.DrawLine(pen, I, I + G, I, I + G + L);
                // 右上
                g.DrawLine(pen, w - I - G - L, I, w - I - G, I);
                g.DrawLine(pen, w - I, I + G, w - I, I + G + L);
                // 左下
                g.DrawLine(pen, I + G, h - I, I + G + L, h - I);
                g.DrawLine(pen, I, h - I - G - L, I, h - I - G);
                // 右下
                g.DrawLine(pen, w - I - G - L, h - I, w - I - G, h - I);
                g.DrawLine(pen, w - I, h - I - G - L, w - I, h - I - G);
            }
        }

        static void DrawScanlines(Graphics g, float w, float h)
        {
            // 4px 一个周期：1px 细线 + 3px 空白，缓存成小图平铺
            Color key = T.Dark ? Color.FromArgb(9, 255, 255, 255) : Color.FromArgb(4, 0, 0, 0);
            if (_scan == null || _scanKey != key)
            {
                if (_scan != null) _scan.Dispose();
                _scanKey = key;
                _scan = new Bitmap(4, 4, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var sg = Graphics.FromImage(_scan))
                {
                    sg.Clear(Color.Transparent);
                    using (var b = new SolidBrush(key)) sg.FillRectangle(b, 0, 0, 4, 1);
                }
            }
            using (var tb = new TextureBrush(_scan, WrapMode.Tile))
                g.FillRectangle(tb, 0, 0, w, h);
        }

        static void DrawVignette(Graphics g, float w, float h)
        {
            const float D = 60f;       // 边缘渐隐宽度
            int a = T.Dark ? 22 : 9;
            using (var b = new LinearGradientBrush(new RectangleF(0, 0, D, h),
                Color.FromArgb(a, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Horizontal))
                g.FillRectangle(b, 0, 0, D, h);
            using (var b = new LinearGradientBrush(new RectangleF(w - D, 0, D, h),
                Color.FromArgb(0, 0, 0, 0), Color.FromArgb(a, 0, 0, 0), LinearGradientMode.Horizontal))
                g.FillRectangle(b, w - D, 0, D, h);
            using (var b = new LinearGradientBrush(new RectangleF(0, 0, w, D),
                Color.FromArgb(a, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Vertical))
                g.FillRectangle(b, 0, 0, w, D);
            using (var b = new LinearGradientBrush(new RectangleF(0, h - D, w, D),
                Color.FromArgb(0, 0, 0, 0), Color.FromArgb(a, 0, 0, 0), LinearGradientMode.Vertical))
                g.FillRectangle(b, 0, h - D, w, D);
        }
    }
}
