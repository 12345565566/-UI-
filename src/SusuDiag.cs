using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace Zogos
{
    public class DiagItem
    {
        public string Level;
        public string Text;
        public DiagItem(string level, string text) { Level = level; Text = text; }
        public string Mark
        {
            get
            {
                if (Level == "bad") return "[!]";
                if (Level == "warn") return "[?]";
                if (Level == "ok") return "[OK]";
                return "·";
            }
        }
    }

    /// <summary>
    /// 苏苏的体检。按「状态」给处方：
    ///   没认出整合包  → 去社区下载 / 教你怎么导入
    ///   整合包不完整  → 重新解压或重新下载
    ///   路径有中文    → 改成英文路径
    ///   核显/小显存   → 打 1.7 显存分配补丁
    /// </summary>
    public static class SusuDiag
    {
        public const string CommunityUrl = "https://docs.qq.com/sheet/DZkt5bFF3cHdaY1ZM?tab=BB08J2";

        public static bool RecommendVramPatch;
        public static string GpuName = "";
        public static long VramMB;
        public static bool Integrated;
        public static string Summary = "";

        public static bool NoPack;         // 完全没认出整合包
        public static bool NoMods;         // 认出了但 Mods 是空的
        public static int BadCount, WarnCount;

        public static string GpuLine()
        {
            if (string.IsNullOrEmpty(GpuName)) return L.T("显卡：读不到（可能没装独显驱动）");
            string vram = VramMB > 0 ? (VramMB + " MB") : L.T("未知");
            return L.T("显卡：") + GpuName + L.T("，显存约 ") + vram + (Integrated ? L.T("（核显）") : "");
        }

        private static void ProbeGpu()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string name = Convert.ToString(mo["Name"]);
                        if (string.IsNullOrEmpty(name)) continue;
                        long ram = 0;
                        try { ram = Convert.ToInt64(mo["AdapterRAM"]); } catch { }
                        if (ram < 0) ram = 0;
                        long mb = ram / (1024L * 1024L);
                        if (GpuName.Length == 0 || mb > VramMB) { GpuName = name; VramMB = mb; }
                    }
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(GpuName))
            {
                string n = GpuName.ToLowerInvariant();
                if (n.Contains("intel") || n.Contains("uhd") || n.Contains("iris") || n.Contains("vega")
                    || n.Contains("radeon(tm) graphics") || n.Contains("radeon graphics") || n.Contains("microsoft basic"))
                    Integrated = true;
            }
            RecommendVramPatch = Integrated || (VramMB > 0 && VramMB < 4096);
        }

        private static string LastExit()
        {
            try
            {
                string[] all = Log.Snapshot();
                for (int i = all.Length - 1; i >= 0; i--)
                {
                    Match m = Regex.Match(all[i], L.T("退出代码 = (-?\\d+)"));
                    if (m.Success)
                    {
                        int code = 0;
                        int.TryParse(m.Groups[1].Value, out code);
                        return code + "（" + ExitCodes.Explain(code) + "）";
                    }
                }
            }
            catch { }
            return "";
        }

        public static List<DiagItem> Run(PackContext ctx, Settings st)
        {
            List<DiagItem> r = new List<DiagItem>();
            ProbeGpu();
            NoPack = false; NoMods = false;

            if (ctx == null) { NoPack = true; r.Add(new DiagItem("bad", L.T("启动器还没有任何整合包信息"))); return r; }

            // ---- 判定最基本的三件事 ----
            bool hasExe = File.Exists(ctx.GameExe);
            bool hasCfg = Directory.Exists(ctx.PackConfigDir);
            if (!hasExe && !hasCfg) NoPack = true;

            if (NoPack)
            {
                r.Add(new DiagItem("bad", L.T("还没检测到整合包\n当前指向的位置是：\n") + ctx.Root + L.T("\n那里既没有 RimWorld 本体，也没有 Mods 文件夹")));
            }
            else
            {
                // 游戏主程序
                if (!hasExe) r.Add(new DiagItem("bad", L.T("没找到游戏主程序：") + ctx.GameExe + L.T("\n→ 点【设置】的【绑定整合包文件夹…】重新指一次")));
                else
                {
                    string reason;
                    if (!GameLauncher.Validate(ctx, out reason)) r.Add(new DiagItem("bad", L.T("游戏主程序不正常：") + reason));
                    else r.Add(new DiagItem("ok", L.T("游戏主程序正常")));
                }

                // 模组
                if (ModIndex.Ready)
                {
                    if (ModIndex.Count == 0)
                    {
                        NoMods = true;
                        r.Add(new DiagItem("bad", L.T("Mods 文件夹里一个模组都没认出来（packageId = 0）\n→ 整合包不完整：多半是解压中断，或者只解压了一层（里面还是 zip）")));
                    }
                    else r.Add(new DiagItem("ok", L.T("本地认出 ") + ModIndex.Count + L.T(" 个模组")));
                }

                // 排序
                List<SortScheme> list = SortSchemeLoader.Load(ctx);
                if (list.Count == 0)
                    r.Add(new DiagItem("bad", L.T("「排序文件」文件夹里没有可用方案\n→ 检查整合包里的 排序文件 文件夹")));
                else
                {
                    SortScheme first = list[0];
                    if (ModIndex.Ready && first.Missing > 0 && first.Missing == first.Mods.Count)
                        r.Add(new DiagItem("bad", L.T("这个排序里的 ") + first.Mods.Count + L.T(" 个模组，本地一个都没有\n→ 整合包不完整")));
                    else r.Add(new DiagItem("ok", L.T("有 ") + list.Count + L.T(" 个排序方案可选")));
                }
            }

            // ---- 路径中文 ----
            if (ctx.NonAscii)
                r.Add(new DiagItem("warn", L.T("路径里有中文：") + ctx.NonAsciiSample + L.T("\n→ RimWorld 在中文路径下经常加载不了模组，得换成英文路径")));
            else
                r.Add(new DiagItem("ok", L.T("路径没有中文")));

            // ---- 磁盘 ----
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;
                    double free = d.AvailableFreeSpace / 1073741824.0;
                    if (free < 5) r.Add(new DiagItem("bad", d.Name + L.T(" 只剩 ") + free.ToString("0.0") + L.T(" GB\n→ 空间不足会崩溃，先清理或把整合包换到别的盘")));
                    else if (free < 20) r.Add(new DiagItem("warn", d.Name + L.T(" 剩余 ") + free.ToString("0.0") + L.T(" GB，偏少")));
                }
            }
            catch { }

            // ---- 显卡 / 显存 ----
            if (RecommendVramPatch)
                r.Add(new DiagItem("bad", GpuLine() + L.T("\n→ 核显 / 小显存，很可能中「RimWorld 1.7 显存爆炸」\n→ 建议装「1.7 显存分配补丁」（点下面按钮我教你）")));
            else if (VramMB > 0)
                r.Add(new DiagItem("ok", GpuLine() + L.T("\n→ 显存够用，别装那个显存补丁，对你反而是负提升")));

            // ---- 上次退出 ----
            string exitTxt = LastExit();
            if (exitTxt.Length > 0)
            {
                if (exitTxt.StartsWith("-1073741819"))
                {
                    if (RecommendVramPatch) r.Add(new DiagItem("bad", L.T("上次游戏崩了：") + exitTxt + L.T("\n→ 结合你的显卡，基本就是显存爆了导致的黑屏闪退")));
                    else r.Add(new DiagItem("warn", L.T("上次游戏崩了：") + exitTxt + L.T("\n→ 不是显存问题（你显存够），看【日志】页找找模组报错")));
                }
                else r.Add(new DiagItem("info", L.T("上次游戏退出：") + exitTxt));
            }

            // ---- 备份 ----
            try
            {
                List<string> bks = ConfigDeployer.ListBackups(ctx.BackupDir);
                if (bks.Count == 0) r.Add(new DiagItem("warn", L.T("还没有任何配置备份")));
                else r.Add(new DiagItem("ok", L.T("有 ") + bks.Count + L.T(" 份配置备份，能还原")));
            }
            catch { }

            BadCount = 0; WarnCount = 0;
            for (int i = 0; i < r.Count; i++) { if (r[i].Level == "bad") BadCount++; else if (r[i].Level == "warn") WarnCount++; }

            if (NoPack) Summary = L.T("先解决整合包这一步吧 —— 有了包我才能帮你看别的。");
            else if (NoMods) Summary = L.T("这个包是残缺的，先弄一份完整的整合包。");
            else if (BadCount > 0) Summary = L.T("有 ") + BadCount + L.T(" 个地方要处理，最要紧的是带 [!] 的那条。");
            else if (WarnCount > 0) Summary = L.T("有 ") + WarnCount + L.T(" 个小提醒，不影响玩，有空再管。");
            else Summary = L.T("看完了，一切正常，去玩吧。");
            return r;
        }

        private static void AddChoice(GuideNode n, string label, string next, string action)
        {
            GuideChoice c = new GuideChoice();
            c.Label = label;
            c.Next = next;
            c.Action = action;
            n.Choices.Add(c);
        }

        /// <summary>把诊断结果做成苏苏的对话节点（分状态给处方）。</summary>
        public static GuideNode BuildNode(PackContext ctx, Settings st)
        {
            List<DiagItem> items = Run(ctx, st);
            GuideNode n = new GuideNode();
            n.Id = "diag";
            n.Title = L.T("苏苏体检");

            // ========== 情况一：根本没检测到整合包 ==========
            if (NoPack)
            {
                n.Lines.Add(new GuideLine(L.T("苏苏"), L.T("等等……我还没认到整合包。"), 12));
                n.Lines.Add(new GuideLine(L.T("苏苏"), L.T("你现在指向的这个位置，里面既没有 RimWorld 本体，也没有 Mods 文件夹。"), 12));
                n.Lines.Add(new GuideLine("", L.T("[!] 当前指向：") + (ctx == null ? L.T("(无)") : ctx.Root)
                    + L.T("\n\n如果你刚装好启动器，那很正常 —— 整合包得单独去社区拿。"), 12));
                n.Lines.Add(new GuideLine(L.T("苏苏"), L.T("两种办法，你挑一个：\n① 去社区下载整合包\n② 手里已经有 zip，就点主页那颗【导入整合包】"), 3));
                AddChoice(n, L.T("【去社区下载整合包】"), "", "community");
                AddChoice(n, L.T("【我有 zip，教我导入】"), "b1", "");
                AddChoice(n, L.T("【AI】直接问苏苏（即将上线）"), "", "");
                n.Choices[2].Disabled = true;
                n.Choices[2].Note = L.T("还在路上");
                n.Next = "";
                return n;
            }

            // ========== 情况二：认到了但模组是空的 ==========
            if (NoMods)
            {
                n.Lines.Add(new GuideLine(L.T("苏苏"), L.T("这个整合包我认得，但它不完整。"), 12));
                n.Lines.Add(new GuideLine("", L.T("[!] Mods 文件夹里一个模组都没认出来（packageId = 0）\n→ 多半是解压中断，或者只解压了一层（里面还是 zip）"), 12));
                n.Lines.Add(new GuideLine(L.T("苏苏"), L.T("重新完整解压一遍就行；不想折腾就去社区重新下一份。"), 12));
                AddChoice(n, L.T("【去社区重新下载】"), "", "community");
                AddChoice(n, L.T("【我知道怎么解压，我自己弄】"), "end", "");
                AddChoice(n, L.T("【AI】直接问苏苏（即将上线）"), "", "");
                n.Choices[2].Disabled = true;
                n.Choices[2].Note = L.T("还在路上");
                n.Next = "";
                return n;
            }

            // ========== 情况三：路径有中文 ==========
            if (ctx != null && ctx.NonAscii)
            {
                n.Lines.Add(new GuideLine(L.T("苏苏"), L.T("我先说最要紧的那条 —— 你的路径里有中文。"), 12));
                n.Lines.Add(new GuideLine(L.T("苏苏"), "「" + ctx.NonAsciiSample + L.T("」这段就是。"), 12));
                n.Lines.Add(new GuideLine(L.T("苏苏"), L.T("RimWorld 在中文路径下经常加载不了模组，这是游戏本身的毛病，不是整合包的锅。"), 3));
                n.Lines.Add(new GuideLine("", L.T("改法（三步）：\n① 把整合包文件夹改名成纯英文，比如 RimWorldPack\n② 整个文件夹挪到盘符根目录，比如 D:\\RimWorldPack\n③ 回【设置】→【绑定整合包文件夹…】指到新位置"), 12));
                AddChoice(n, L.T("【改完了，再体检一次】"), "diag", "");
                AddChoice(n, L.T("【先不改，继续看看别的】"), "diag2", "");
                n.Next = "";
                // diag2 = 继续看体检结果
                GuideNode cont = new GuideNode();
                cont.Id = "diag2";
                cont.Title = L.T("继续体检");
                cont.Lines.Add(new GuideLine(L.T("苏苏"), L.T("行，那我把其他情况也念给你。"), 11));
                cont.Lines.Add(new GuideLine("", BuildListText(items), 11));
                cont.Lines.Add(new GuideLine(L.T("苏苏"), Summary, 11));
                if (RecommendVramPatch) AddChoice(cont, L.T("【补丁】1.7 显存分配补丁怎么装"), "vram", "");
                AddChoice(cont, L.T("【目录】我想看别的"), GuideScript.TocId, "");
                cont.Next = "end";
                return cont;   // 直接把L.T("继续")节点返回（苏苏先讲中文路径）
            }

            // ========== 情况四：正常体检 ==========
            n.Lines.Add(new GuideLine(L.T("苏苏"), L.T("我看看你现在什么情况……"), 12));
            n.Lines.Add(new GuideLine("", BuildListText(items), 12));
            n.Lines.Add(new GuideLine(L.T("苏苏"), Summary, RecommendVramPatch ? 12 : 11));
            if (RecommendVramPatch) AddChoice(n, L.T("【补丁】1.7 显存分配补丁怎么装"), "vram", "");
            AddChoice(n, L.T("【AI 诊断】让 AI 看日志给建议（需先配接口）"), "", "ai");
            AddChoice(n, L.T("【二维码】还是不行？去社区发帖求助"), "", "qr");
            AddChoice(n, L.T("【目录】我想看别的"), GuideScript.TocId, "");
            AddChoice(n, L.T("【AI】直接问苏苏（即将上线）"), "", "");
            n.Choices[n.Choices.Count - 1].Disabled = true;
            n.Choices[n.Choices.Count - 1].Note = L.T("还在路上");
            n.Next = "end";
            return n;
        }

        /// <summary>「还是不行」时给玩家看的求助节点：社区频道二维码。</summary>
        public static GuideNode BuildHelpNode()
        {
            GuideNode n = new GuideNode();
            n.Id = "help";
            n.Title = L.T("去社区求助");
            n.Lines.Add(new GuideLine(L.T("苏苏"), L.T("照这个试试。"), 3));
            n.Lines.Add(new GuideLine(L.T("苏苏"), L.T("不过我得先说实话：AI 看日志也不是万能的，有些问题它也只能猜。"), 3));
            n.Lines.Add(new GuideLine(L.T("苏苏"), L.T("如果照做完还是不行，别一个人硬扛 —— 去社区频道发帖，扫这个码就能进。"), 12));
            n.Lines.Add(new GuideLine("", L.T("发帖记得带三样：\n① 出问题时的截图\n② 启动器【日志】页生成的诊断报告\n③ AI 诊断建议（如果用过 AI 的话）"), 12));
            AddChoice(n, L.T("【二维码】社区频道 · 扫码加入"), "", "qr");
            AddChoice(n, L.T("【再体检一次】"), "diag", "");
            AddChoice(n, L.T("【关闭】"), "end", "");
            n.Next = "end";
            return n;
        }

        private static string BuildListText(List<DiagItem> items)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append(items[i].Mark).Append(' ').Append(items[i].Text);
                if (i < items.Count - 1) sb.Append("\n\n");
            }
            return sb.ToString();
        }
    }
}
