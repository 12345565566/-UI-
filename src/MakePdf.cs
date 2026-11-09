using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;

namespace Zogos
{
    public static class MakePdf
    {
        const int W = 1654, H = 2339;      // A4 @ 200dpi
        const float M = 110;
        static float CW { get { return W - M * 2; } }

        static Graphics NewG(Bitmap b)
        {
            Graphics g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            return g;
        }

        static void Bg(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(P.Bg)) g.FillRectangle(b, 0, 0, W, H);
        }

        static void PageHead(Graphics g, string title, string sub, Color accent)
        {
            // 顶部色条
            using (SolidBrush b = new SolidBrush(accent)) g.FillRectangle(b, 0, 0, W, 14);
            P.Txt(g, title, P.F(58, true), P.Ink, new RectangleF(M, 60, CW, 80), StringAlignment.Near, StringAlignment.Center);
            P.Txt(g, sub, P.F(30, false), P.Sub, new RectangleF(M, 140, CW, 46), StringAlignment.Near, StringAlignment.Center);
            using (Pen pen = new Pen(P.Line, 2))
                g.DrawLine(pen, M, 210, W - M, 210);
        }

        static void PageFoot(Graphics g, int page)
        {
            P.Txt(g, L.T("Astra 创世神协议  ·  整合包报错解决方案"), P.F(24, false), Color.FromArgb(160, 172, 186), new RectangleF(M, H - 70, CW - 120, 40), StringAlignment.Near, StringAlignment.Center);
            P.Txt(g, L.T("第 ") + page + L.T(" 页"), P.F(24, true), Color.FromArgb(150, 164, 180), new RectangleF(W - M - 120, H - 70, 120, 40), StringAlignment.Far, StringAlignment.Center);
        }

        // 带编号的标题卡
        static RectangleF Section(Graphics g, float y, float h, string num, string title, Color accent, Color bg)
        {
            RectangleF card = new RectangleF(M, y, CW, h);
            P.CardBox(g, card, Color.White, P.Line, 22, 2);
            P.Fill(g, new RectangleF(M + 28, y + 26, 60, 60), 30, accent);
            P.Txt(g, num, P.F(34, true), Color.White, new RectangleF(M + 28, y + 26, 60, 60), StringAlignment.Center, StringAlignment.Center);
            P.Txt(g, title, P.F(42, true), P.Ink, new RectangleF(M + 104, y + 30, CW - 140, 52), StringAlignment.Near, StringAlignment.Center);
            return new RectangleF(M + 40, y + 100, CW - 80, h - 120);
        }

        static void Bullet(Graphics g, float x, float y, string text, Font f, Color c, Color dot)
        {
            using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, x, y + 14, 12, 12);
            P.Wrap(g, text, f, c, new RectangleF(x + 28, y, CW - 60, 90));
        }

        // ---------------- 第 1 页：封面 + 结论 ----------------
        static void Page1(PdfDoc doc)
        {
            using (Bitmap b = new Bitmap(W, H))
            using (Graphics g = NewG(b))
            {
                Bg(g);
                RectangleF band = new RectangleF(0, 0, W, 780);
                using (LinearGradientBrush lg = new LinearGradientBrush(band, Color.FromArgb(22, 36, 64), Color.FromArgb(46, 92, 158), 12f))
                    g.FillRectangle(lg, band);

                P.Txt(g, L.T("RIMWORLD 整合包  ·  报错分析报告"), P.F(30, true), Color.FromArgb(186, 208, 234), new RectangleF(M, 96, CW, 46), StringAlignment.Near, StringAlignment.Center);
                P.Txt(g, L.T("游戏没崩，"), P.F(96, true), Color.White, new RectangleF(M, 176, CW, 116), StringAlignment.Near, StringAlignment.Center);
                P.Txt(g, L.T("但「生成小人」坏了"), P.F(96, true), Color.FromArgb(255, 222, 110), new RectangleF(M, 286, CW, 116), StringAlignment.Near, StringAlignment.Center);
                P.Txt(g, L.T("花不语的整合包   ·   v1.7.4981.136   ·   日志 2026-10-17 21:29 ~ 21:34"), P.F(30, false), Color.FromArgb(198, 214, 236), new RectangleF(M, 424, CW, 44), StringAlignment.Near, StringAlignment.Center);

                // 结论卡
                RectangleF cc = new RectangleF(M, 600, CW, 250);
                P.CardBox(g, cc, Color.FromArgb(255, 250, 235), Color.FromArgb(240, 200, 90), 24, 3);
                P.Txt(g, L.T("一句话结论"), P.F(32, true), Color.FromArgb(146, 104, 0), new RectangleF(M + 40, 626, CW - 80, 44), StringAlignment.Near, StringAlignment.Center);
                P.Wrap(g, L.T("12 条错误里，有 6 条来自同一个模组 —— MultiFloors（多楼层）。\n先把它禁用，主要问题就没了。另外两个模组也要处理，但没那么急。"),
                    P.F(34, false), P.Ink, new RectangleF(M + 40, 676, CW - 80, 150));

                // 三个问题卡
                float cw3 = (CW - 40) / 3f;
                string[] t1 = new string[] { "MultiFloors", "MiliraExpandedXY", "GD3" };
                string[] t2 = new string[] { L.T("多楼层"), L.T("Milira 扩展"), L.T("补丁模组") };
                string[] t3 = new string[] { L.T("必须禁用"), L.T("不兼容 · 等更新"), L.T("补丁全失效") };
                Color[] ac = new Color[] { P.Red, P.Orange, P.Yellow };
                Color[] ab = new Color[] { P.RedBg, P.OrangeBg, P.YellowBg };
                Image[] ic = new Image[] { null, null, null };
                for (int i = 0; i < 3; i++)
                {
                    RectangleF r = new RectangleF(M + i * (cw3 + 20), 900, cw3, 300);
                    P.CardBox(g, r, Color.White, P.Line, 22, 2);
                    P.Fill(g, new RectangleF(r.X + 26, r.Y + 26, 62, 62), 31, ac[i]);
                    P.Txt(g, (i + 1).ToString(), P.F(34, true), Color.White, new RectangleF(r.X + 26, r.Y + 26, 62, 62), StringAlignment.Center, StringAlignment.Center);
                    P.Txt(g, t1[i], P.F(33, true), P.Ink, new RectangleF(r.X + 26, r.Y + 106, cw3 - 52, 44), StringAlignment.Near, StringAlignment.Center);
                    P.Txt(g, t2[i], P.F(26, false), P.Sub, new RectangleF(r.X + 26, r.Y + 152, cw3 - 52, 40), StringAlignment.Near, StringAlignment.Center);
                    P.Badge(g, r.X + 26, r.Y + 210, t3[i], P.F(24, true), ab[i], ac[i], 20, 48);
                    P.Txt(g, L.T("见第 ") + (i == 0 ? "2" : "3") + L.T(" 页"), P.F(24, false), P.Sub, new RectangleF(r.X + 26, r.Y + 258, cw3 - 52, 36), StringAlignment.Near, StringAlignment.Center);
                }

                // 数据条
                RectangleF st = new RectangleF(M, 1250, CW, 150);
                P.CardBox(g, st, Color.White, P.Line, 22, 2);
                string[] kn = new string[] { L.T("错误总数"), L.T("警告总数"), L.T("问题模组"), L.T("运行时长") };
                string[] kv = new string[] { "12", "52", "3", L.T("5 分钟") };
                float cw4 = CW / 4f;
                for (int i = 0; i < 4; i++)
                {
                    if (i > 0)
                    {
                        using (Pen pen = new Pen(P.Line, 2))
                            g.DrawLine(pen, M + i * cw4, 1282, M + i * cw4, 1368);
                    }
                    P.Txt(g, kv[i], P.F(46, true), i == 0 ? P.Red : P.Ink, new RectangleF(M + i * cw4, 1268, cw4, 66), StringAlignment.Center, StringAlignment.Center);
                    P.Txt(g, kn[i], P.F(24, false), P.Sub, new RectangleF(M + i * cw4, 1332, cw4, 40), StringAlignment.Center, StringAlignment.Center);
                }

                // 阅读指引
                RectangleF gd = new RectangleF(M, 1450, CW, 210);
                P.CardBox(g, gd, P.BlueBg, Color.FromArgb(180, 205, 245), 22, 2);
                P.Txt(g, L.T("这份报告怎么看"), P.F(32, true), P.Blue, new RectangleF(M + 36, 1476, CW - 72, 44), StringAlignment.Near, StringAlignment.Center);
                P.Wrap(g, L.T("· 只想快点修好 → 直接翻到第 4 页，照着 5 步点就行\n· 想知道为什么 → 第 2、3 页有每个模组的报错原文和白话解释\n· 还是不行 → 第 5 页有「二分法」和反馈方法"), P.F(28, false), P.Ink, new RectangleF(M + 36, 1524, CW - 72, 130));

                P.Txt(g, L.T("生成时间 2026-10-17   ·   Astra 创世神协议 出品"), P.F(24, false), Color.FromArgb(160, 172, 186), new RectangleF(M, H - 100, CW, 40), StringAlignment.Center, StringAlignment.Center);
                PageFoot(g, 1);
                doc.AddPage(b, 92);
            }
        }

        // ---------------- 第 2 页：问题 ① ----------------
        static void Page2(PdfDoc doc)
        {
            using (Bitmap b = new Bitmap(W, H))
            using (Graphics g = NewG(b))
            {
                Bg(g);
                PageHead(g, L.T("问题 ①  MultiFloors（多楼层）"), L.T("这一条必须处理：它被挂进了「生成小人」的流程里"), P.Red);

                float y = 250;
                RectangleF a = Section(g, y, 300, "1", L.T("现象：日志里长这样"), P.Red, P.RedBg);
                P.Fill(g, new RectangleF(a.X, a.Y + 8, a.Width, 170), 12, Color.FromArgb(38, 44, 56));
                string[] code = new string[] {
                    "ERROR  Error while generating pawn. Rethrowing.",
                    L.T("  System.BadImageFormatException: 试图加载格式不正确的程序。(0x8007000B)"),
                    "     at MultiFloors.PrepatcherFields.LevelSettings(Pawn pawn)",
                    "     at Verse.PawnGenerator.TryGenerateNewPawnInternal(...)"
                };
                Font fm = P.Mono(23);
                for (int i = 0; i < code.Length; i++)
                    P.Txt(g, code[i], fm, i == 1 ? Color.FromArgb(255, 160, 150) : Color.FromArgb(210, 220, 232),
                        new RectangleF(a.X + 26, a.Y + 20 + i * 40, a.Width - 52, 38), StringAlignment.Near, StringAlignment.Center);

                y += 320;
                RectangleF b1 = Section(g, y, 400, "2", L.T("为什么会这样（大白话）"), P.Red, P.RedBg);
                P.Wrap(g, L.T("MultiFloors 这个模组，给自己挂了一个「每次生成小人就顺便加个楼层组件」的钩子。\n\n") +
                          L.T("但游戏一调用它，就要去加载它内部的一段程序，而那段程序**跟这个版本的游戏不匹配**，直接加载失败 —— 这就是 BadImageFormatException（格式不正确的程序）。\n\n") +
                          L.T("结果就是：**每到生成小人这一步就报错中断**，后面的代码全部跳过。"),
                    P.F(30, false), P.Ink, new RectangleF(b1.X, b1.Y + 6, b1.Width, 280));

                y += 420;
                RectangleF c1 = Section(g, y, 380, "3", L.T("会有什么后果"), P.Red, P.RedBg);
                Bullet(g, c1.X, c1.Y + 6, L.T("袭击来的小人可能不生成 / 生成不完整"), P.F(30, false), P.Ink, P.Red);
                Bullet(g, c1.X, c1.Y + 66, L.T("商队、流浪者、访客都可能出问题"), P.F(30, false), P.Ink, P.Red);
                Bullet(g, c1.X, c1.Y + 126, L.T("世界生成（开新图）时也会报同样的错"), P.F(30, false), P.Ink, P.Red);
                Bullet(g, c1.X, c1.Y + 186, L.T("日志被刷满，真问题会被淹掉"), P.F(30, false), P.Ink, P.Red);

                y += 400;
                RectangleF d1 = new RectangleF(M, y, CW, 300);
                P.CardBox(g, d1, P.GreenBg, Color.FromArgb(150, 210, 180), 22, 3);
                P.Txt(g, L.T("现在就做"), P.F(34, true), P.Green, new RectangleF(M + 36, y + 22, CW - 72, 46), StringAlignment.Near, StringAlignment.Center);
                P.Wrap(g, L.T("① 打开游戏 → 选项 → 模组 → 在搜索框输入 MultiFloors\n② 把它的勾去掉 → 点「应用」→ 重启游戏\n③ 重新读档，然后看日志里还有没有这个报错"),
                    P.F(30, true), P.Ink, new RectangleF(M + 36, y + 76, CW - 72, 200));

                PageFoot(g, 2);
                doc.AddPage(b, 92);
            }
        }

        // ---------------- 第 3 页：问题 ② ③ ----------------
        static void Page3(PdfDoc doc)
        {
            using (Bitmap b = new Bitmap(W, H))
            using (Graphics g = NewG(b))
            {
                Bg(g);
                PageHead(g, L.T("问题 ②③  另外两个也要处理"), L.T("这两个不急，但留着会让模组功能不正常"), P.Orange);

                // ②
                float y = 250;
                RectangleF c2 = new RectangleF(M, y, CW, 780);
                P.CardBox(g, c2, Color.White, P.Line, 22, 2);
                using (SolidBrush sb = new SolidBrush(P.OrangeBg)) g.FillRectangle(sb, M, y, 14, 780);
                P.Txt(g, L.T("②  MiliraExpandedXY（Milira 扩展）"), P.F(40, true), P.Ink, new RectangleF(M + 40, y + 28, CW - 80, 56), StringAlignment.Near, StringAlignment.Center);
                P.Badge(g, M + 40, y + 92, L.T("和这个版本不兼容 · 只能等更新"), P.F(25, true), P.OrangeBg, P.Orange, 20, 50);

                P.Fill(g, new RectangleF(M + 40, y + 168, CW - 80, 150), 12, Color.FromArgb(38, 44, 56));
                string[] code2 = new string[] {
                    "ERROR  System.MissingMethodException: Method not found:",
                    "       'Boolean UnityEngine.MaterialPropertyBlock.HasVector(Int32)'",
                    "         at MiliraExpandedXY.MEXY_ModSetting.BuildSchemaIndex()"
                };
                Font fm = P.Mono(23);
                for (int i = 0; i < code2.Length; i++)
                    P.Txt(g, code2[i], fm, i == 1 ? Color.FromArgb(255, 190, 130) : Color.FromArgb(210, 220, 232),
                        new RectangleF(M + 64, y + 182 + i * 40, CW - 128, 38), StringAlignment.Near, StringAlignment.Center);

                P.Wrap(g, L.T("它调用了一个「这个游戏版本里根本没有」的 Unity 接口，所以一加载设置就报错。\n\n") +
                          L.T("这不是排序问题、也不是你的错 —— 是这个模组按别的引擎版本编译的，**只能等作者更新**，或者先不用它。"),
                    P.F(30, false), P.Ink, new RectangleF(M + 40, y + 340, CW - 80, 220));

                P.Txt(g, L.T("建议"), P.F(28, true), P.Orange, new RectangleF(M + 40, y + 580, CW - 80, 40), StringAlignment.Near, StringAlignment.Center);
                P.Wrap(g, L.T("先在模组列表里禁用；如果整合包没有它就不能玩，那就只能等整合包作者出新版。"),
                    P.F(28, false), P.Sub, new RectangleF(M + 40, y + 626, CW - 80, 110));

                // ③
                y += 810;
                RectangleF c3 = new RectangleF(M, y, CW, 780);
                P.CardBox(g, c3, Color.White, P.Line, 22, 2);
                using (SolidBrush sb = new SolidBrush(P.YellowBg)) g.FillRectangle(sb, M, y, 14, 780);
                P.Txt(g, L.T("③  GD3（补丁类模组）"), P.F(40, true), P.Ink, new RectangleF(M + 40, y + 28, CW - 80, 56), StringAlignment.Near, StringAlignment.Center);
                P.Badge(g, M + 40, y + 92, L.T("4 个补丁全部失效 · 功能等于没装"), P.F(25, true), P.YellowBg, P.Yellow, 20, 50);

                P.Txt(g, L.T("失败的是这 4 个补丁："), P.F(28, true), P.Ink, new RectangleF(M + 40, y + 170, CW - 80, 40), StringAlignment.Near, StringAlignment.Center);
                string[] pat = new string[] { "Annihilator_CanMove_Patch", "Annihilator_RoofCollapse_Patch", "LogBlocker_FlyerWaitCombat_Patch", "ThreatDisabled_ForBlackApocriton_Patch" };
                for (int i = 0; i < pat.Length; i++)
                {
                    P.Fill(g, new RectangleF(M + 40, y + 216 + i * 52, CW - 80, 44), 10, Color.FromArgb(250, 248, 232));
                    P.Txt(g, "· " + pat[i], P.Mono(23), Color.FromArgb(120, 96, 20), new RectangleF(M + 60, y + 216 + i * 52, CW - 120, 44), StringAlignment.Near, StringAlignment.Center);
                }

                P.Wrap(g, L.T("它在启动时给游戏打补丁，但补丁代码和现在的游戏对不上，全部失败。\n补丁没打上 = 这个模组**什么也没做**（不会崩，但也没效果）。"),
                    P.F(30, false), P.Ink, new RectangleF(M + 40, y + 446, CW - 80, 180));

                P.Txt(g, L.T("建议"), P.F(28, true), P.Yellow, new RectangleF(M + 40, y + 620, CW - 80, 40), StringAlignment.Near, StringAlignment.Center);
                P.Wrap(g, L.T("去模组工坊看看有没有 1.7.4981 的新版；没有就先禁用，反正它现在也没生效。"),
                    P.F(28, false), P.Sub, new RectangleF(M + 40, y + 666, CW - 80, 100));

                PageFoot(g, 3);
                doc.AddPage(b, 92);
            }
        }

        // ---------------- 第 4 页：保姆级操作 ----------------
        static void Page4(PdfDoc doc)
        {
            using (Bitmap b = new Bitmap(W, H))
            using (Graphics g = NewG(b))
            {
                Bg(g);
                PageHead(g, L.T("手把手：3 分钟禁用模组"), L.T("照着下面 5 步点，不用懂原理也行"), P.Blue);

                string[] st = new string[] {
                    L.T("打开游戏，在主菜单点【选项】"),
                    L.T("左边一栏选【模组】"),
                    L.T("上方搜索框里，输入要禁用的模组名"),
                    L.T("找到它，把左边的勾「点掉」，然后点右下角【应用】"),
                    L.T("游戏会提示重启 → 点【是】→ 重新读档")
                };
                string[] tip = new string[] {
                    L.T("就是这个齿轮图标的按钮"),
                    L.T("英文叫 Mods，中文是「模组」"),
                    L.T("建议每次只搜一个：MultiFloors / MiliraExpandedXY / GD3"),
                    L.T("勾去掉 = 这个模组不加载了，存档不会丢"),
                    L.T("重启后模组才真正不加载")
                };

                float y = 250;
                for (int i = 0; i < 5; i++)
                {
                    RectangleF r = new RectangleF(M, y, CW, 250);
                    P.CardBox(g, r, Color.White, P.Line, 22, 2);
                    P.Fill(g, new RectangleF(M + 30, y + 34, 86, 86), 43, P.Blue);
                    P.Txt(g, (i + 1).ToString(), P.F(46, true), Color.White, new RectangleF(M + 30, y + 34, 86, 86), StringAlignment.Center, StringAlignment.Center);

                    P.Txt(g, st[i], P.F(36, true), P.Ink, new RectangleF(M + 146, y + 40, CW - 200, 60), StringAlignment.Near, StringAlignment.Center);
                    P.Txt(g, tip[i], P.F(27, false), P.Sub, new RectangleF(M + 146, y + 104, CW - 200, 46), StringAlignment.Near, StringAlignment.Center);

                    // 右侧小示意
                    RectangleF mini = new RectangleF(W - M - 260, y + 30, 230, 190);
                    P.Fill(g, mini, 16, Color.FromArgb(244, 247, 252));
                    P.Stroke(g, mini, 16, P.Line, 2);
                    if (i == 2)
                    {
                        // 搜索框
                        P.Fill(g, new RectangleF(mini.X + 20, mini.Y + 30, 190, 44), 10, Color.White);
                        P.Stroke(g, new RectangleF(mini.X + 20, mini.Y + 30, 190, 44), 10, P.Blue, 2);
                        P.Txt(g, "MultiFloors", P.F(22, false), P.Sub, new RectangleF(mini.X + 34, mini.Y + 30, 170, 44), StringAlignment.Near, StringAlignment.Center);
                        // 结果行
                        P.Fill(g, new RectangleF(mini.X + 20, mini.Y + 90, 190, 70), 10, Color.FromArgb(252, 253, 255));
                        P.Txt(g, L.T("MultiFloors (多楼层)"), P.F(20, false), P.Ink, new RectangleF(mini.X + 30, mini.Y + 90, 170, 70), StringAlignment.Near, StringAlignment.Center);
                    }
                    else if (i == 3)
                    {
                        // 勾选框
                        RectangleF cb = new RectangleF(mini.X + 30, mini.Y + 40, 46, 46);
                        P.CardBox(g, cb, Color.White, P.Sub, 8, 3);
                        using (Pen pen = new Pen(P.Green, 5)) { g.DrawLine(pen, cb.X + 8, cb.Y + 24, cb.X + 18, cb.Y + 34); g.DrawLine(pen, cb.X + 18, cb.Y + 34, cb.X + 38, cb.Y + 10); }
                        P.Txt(g, L.T("已禁用"), P.F(24, true), P.Green, new RectangleF(mini.X + 90, mini.Y + 40, 130, 46), StringAlignment.Near, StringAlignment.Center);
                        P.Fill(g, new RectangleF(mini.X + 30, mini.Y + 110, 150, 50), 10, P.Blue);
                        P.Txt(g, L.T("应用"), P.F(24, true), Color.White, new RectangleF(mini.X + 30, mini.Y + 110, 150, 50), StringAlignment.Center, StringAlignment.Center);
                    }
                    else
                    {
                        // 通用小图标
                        using (SolidBrush sb = new SolidBrush(i == 0 ? P.Blue : (i == 1 ? P.Orange : P.Green)))
                        {
                            g.FillEllipse(sb, mini.X + mini.Width / 2 - 40, mini.Y + mini.Height / 2 - 40, 80, 80);
                        }
                        P.Txt(g, (i == 0 ? "⚙" : (i == 1 ? "≡" : "↻")), P.F(52, true), Color.White,
                            new RectangleF(mini.X, mini.Y, mini.Width, mini.Height), StringAlignment.Center, StringAlignment.Center);
                    }
                    y += 270;
                }

                RectangleF warn = new RectangleF(M, y + 10, CW, 200);
                P.CardBox(g, warn, Color.FromArgb(255, 247, 230), Color.FromArgb(238, 196, 120), 22, 3);
                P.Txt(g, L.T("⚠  两个小提醒"), P.F(32, true), Color.FromArgb(150, 96, 0), new RectangleF(M + 36, y + 34, CW - 72, 46), StringAlignment.Near, StringAlignment.Center);
                P.Wrap(g, L.T("· 三个模组都要禁（MultiFloors、MiliraExpandedXY、GD3），只禁一个可能还有别的报错\n· 别一次删一堆模组：一次改一个，看日志有没有变好，才知道是谁的问题"),
                    P.F(28, false), P.Ink, new RectangleF(M + 36, y + 86, CW - 72, 110));

                PageFoot(g, 4);
                doc.AddPage(b, 92);
            }
        }

        // ---------------- 第 5 页：还不行怎么办 + 附录 ----------------
        static void Page5(PdfDoc doc)
        {
            using (Bitmap b = new Bitmap(W, H))
            using (Graphics g = NewG(b))
            {
                Bg(g);
                PageHead(g, L.T("还是不行？还有这几招"), L.T("按顺序试，基本都能定位到"), P.Green);

                float y = 250;
                // 二分法
                RectangleF s1 = Section(g, y, 420, "A", L.T("二分法：3 步找出捣乱的模组"), P.Green, P.GreenBg);
                P.Wrap(g, L.T("模组太多（几百个）不知道谁坏了？用「砍一半」的办法：\n\n") +
                          L.T("第 1 轮：禁用一半 → 进游戏看还报不报错\n") +
                          L.T("第 2 轮：还有错 → 说明在留下的那一半里，再砍一半\n") +
                          L.T("第 3 轮：没错 → 说明在禁用的那一半里，换那一半再试\n\n") +
                          L.T("几百个模组，最多 8~9 轮就能锁定到 1 个。"),
                    P.F(29, false), P.Ink, new RectangleF(s1.X, s1.Y + 6, s1.Width, 300));

                // 日志位置
                y += 440;
                RectangleF s2 = Section(g, y, 320, "B", L.T("日志文件在哪"), P.Blue, P.BlueBg);
                P.Txt(g, L.T("游戏日志（就是这份报告看的那个文件）："), P.F(27, true), P.Ink, new RectangleF(s2.X, s2.Y + 4, s2.Width, 40), StringAlignment.Near, StringAlignment.Center);
                P.Fill(g, new RectangleF(s2.X, s2.Y + 54, s2.Width, 62), 10, Color.FromArgb(38, 44, 56));
                P.Txt(g, L.T("C:\\Users\\你的用户名\\AppData\\LocalLow\\Ludeon Studios\\RimWorld by Ludeon Studios\\Logs\\Player.log"),
                    P.Mono(21), Color.FromArgb(220, 230, 240), new RectangleF(s2.X + 20, s2.Y + 54, s2.Width - 40, 62), StringAlignment.Near, StringAlignment.Center);
                P.Wrap(g, L.T("小技巧：Win + R 输入 %appdata%..\\LocalLow 回车，就能直接打开这个文件夹。\n用记事本打开 Player.log，搜 ERROR 就能看到所有报错。"),
                    P.F(27, false), P.Sub, new RectangleF(s2.X, s2.Y + 130, s2.Width, 130));

                // 反馈
                y += 340;
                RectangleF s3 = Section(g, y, 330, "C", L.T("还是搞不定？这样反馈最有效"), P.Orange, P.OrangeBg);
                P.Wrap(g, L.T("发这三种东西给社区管理员，基本都能帮你解决：\n\n") +
                          L.T("① Player.log 日志文件（不要截图，直接发文件）\n") +
                          L.T("② ModsConfig.xml（模组排序文件，在整合包的 Config 文件夹里）\n") +
                          L.T("③ 说明一下：什么时候报错（开新档 / 读档 / 来了袭击）"),
                    P.F(29, false), P.Ink, new RectangleF(s3.X, s3.Y + 6, s3.Width, 250));

                // 附录表
                y += 350;
                P.Txt(g, L.T("附：日志里其余的小问题（可以不管）"), P.F(32, true), P.Ink, new RectangleF(M, y, CW, 50), StringAlignment.Near, StringAlignment.Center);
                y += 60;
                string[][] rows = new string[][] {
                    new string[] { L.T("HugsLib 找不到日志文件"), L.T("无影响"), L.T("上面错误的副产品，忽略") },
                    new string[] { L.T("gravitywell 着色器无 Vulkan 变体"), L.T("部分特效发白/不显示"), L.T("画面问题，不影响玩法") },
                    new string[] { L.T("缺少音效 Pawn_Melee_Punch_HitBuilding"), L.T("无影响"), L.T("某个模组引用了旧音效") },
                    new string[] { L.T("中文翻译有 1542 处错误"), L.T("个别文本显示异常"), L.T("汉化包问题，可忽略") }
                };
                float rh = 86;
                for (int i = 0; i <= rows.Length; i++)
                {
                    RectangleF rr = new RectangleF(M, y + i * rh, CW, rh);
                    P.Fill(g, rr, 0, i == 0 ? Color.FromArgb(238, 242, 248) : (i % 2 == 0 ? Color.White : Color.FromArgb(250, 251, 253)));
                    if (i == 0)
                    {
                        P.Txt(g, L.T("问题"), P.F(26, true), P.Sub, new RectangleF(M + 24, rr.Y, 560, rh), StringAlignment.Near, StringAlignment.Center);
                        P.Txt(g, L.T("影响"), P.F(26, true), P.Sub, new RectangleF(M + 600, rr.Y, 300, rh), StringAlignment.Near, StringAlignment.Center);
                        P.Txt(g, L.T("要不要管"), P.F(26, true), P.Sub, new RectangleF(M + 920, rr.Y, CW - 940, rh), StringAlignment.Near, StringAlignment.Center);
                    }
                    else
                    {
                        string[] row = rows[i - 1];
                        P.Txt(g, row[0], P.F(25, false), P.Ink, new RectangleF(M + 24, rr.Y, 560, rh), StringAlignment.Near, StringAlignment.Center);
                        P.Txt(g, row[1], P.F(25, false), P.Orange, new RectangleF(M + 600, rr.Y, 300, rh), StringAlignment.Near, StringAlignment.Center);
                        P.Txt(g, row[2], P.F(25, false), P.Green, new RectangleF(M + 920, rr.Y, CW - 940, rh), StringAlignment.Near, StringAlignment.Center);
                    }
                }
                using (Pen pen = new Pen(P.Line, 2)) g.DrawRectangle(pen, M, y, CW, rh * (rows.Length + 1));

                P.Txt(g, L.T("祝早日修好，玩得开心～喵~ (=^･ω･^=)"), P.F(28, true), P.Sub, new RectangleF(M, H - 150, CW, 50), StringAlignment.Center, StringAlignment.Center);
                PageFoot(g, 5);
                doc.AddPage(b, 92);
            }
        }

        public static void Build(string outPath)
        {
            PdfDoc doc = new PdfDoc();
            Page1(doc);
            Page2(doc);
            Page3(doc);
            Page4(doc);
            Page5(doc);
            doc.Save(outPath);
            Console.WriteLine(L.T("PDF 已生成: ") + outPath + L.T("  页数=") + doc.PageCount);
        }
    }

    public static class PdfMain
    {
        [STAThread]
        public static void Main(string[] args)
        {
            string outPath = args.Length > 0 ? args[0] : L.T("ZOGOS整合包报错解决方案.pdf");
            MakePdf.Build(outPath);
        }
    }
}
