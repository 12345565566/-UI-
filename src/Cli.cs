using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Zogos
{
    /// <summary>命令行入口：自动化测试与排错用（图形界面之外的第二套外壳）。</summary>
    public static class CliMain
    {
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
            string root = null, target = null, appdata = null, schemeArg = null;
            int waitSec = 20;
            bool fullFlag = false;
            string outPath = null, destDir = null;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--root" && i + 1 < args.Length) root = args[++i];
                else if (a == "--target" && i + 1 < args.Length) target = args[++i];
                else if (a == "--appdata" && i + 1 < args.Length) appdata = args[++i];
                else if (a == "--scheme" && i + 1 < args.Length) schemeArg = args[++i];
                else if (a == "--wait" && i + 1 < args.Length) { int.TryParse(args[++i], out waitSec); }
                else if (a == "--full") fullFlag = true;
                else if (a == "--out" && i + 1 < args.Length) outPath = args[++i];
                else if (a == "--dest" && i + 1 < args.Length) destDir = args[++i];
                else if (a.Length > 0 && a[0] != '-' && schemeArg == null) schemeArg = a;
            }

            if (mode == "help" || mode == "-h" || mode == "--help")
            {
                Console.WriteLine("Astra 创世神协议 命令行模式");
                Console.WriteLine("  diag    [--root 目录] [--target 配置目录] [--appdata 目录]");
                Console.WriteLine("  schemes [--root 目录]");
                Console.WriteLine("  deploy  [方案名或序号] [--root 目录] [--target 配置目录]");
                Console.WriteLine("  run     [方案名或序号] [--wait 秒] [--root 目录] [--target 配置目录]");
                Console.WriteLine("  analyze [方案名或序号] [--root 目录]   启动前模组体检（依赖/顺序/兼容性/日志归因）");
                Console.WriteLine("  order   [方案名或序号] [--root 目录]   生成智能排序方案");
                Console.WriteLine("  safe    [方案名或序号] [--root 目录]   生成排除疑似问题模组的安全排序");
                Console.WriteLine("  export  [方案名或序号] [--full] [--out 输出.zip]");
                Console.WriteLine("  import  <zip文件> --dest 解压目录");
                return 0;
            }

            string launcherDir = AppDomain.CurrentDomain.BaseDirectory;
            Settings st = Settings.Load(appdata != null ? appdata : AppDataDir());
            PackContext ctx = PackContext.Detect(launcherDir, st, root, target, appdata);
            Log.Init(ctx.SettingsDir);

            try
            {
                if (mode == "diag") return CmdDiag(ctx, st);
                if (mode == "schemes") return CmdSchemes(ctx);
                if (mode == "deploy") return CmdDeploy(ctx, st, schemeArg);
                if (mode == "run") return CmdRun(ctx, st, schemeArg, waitSec);
                if (mode == "analyze") return CmdAnalyze(ctx, st, schemeArg);
                if (mode == "order") return CmdOrder(ctx, st, schemeArg);
                if (mode == "safe") return CmdSafe(ctx, st, schemeArg);
                if (mode == "export") return CmdExport(ctx, schemeArg, fullFlag, outPath);
                if (mode == "import") return CmdImport(schemeArg, destDir);
                if (mode == "report")
                {
                    string f = Diag.SaveReport(ctx, PickScheme(ctx, schemeArg), st);
                    Console.WriteLine("诊断报告: " + f);
                    Console.WriteLine();
                    Console.WriteLine(PathHelp.ReadAllTextSmart(f));
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("异常: " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                return 2;
            }

            Console.WriteLine("未知命令: " + mode);
            return 2;
        }

        public static string AppDataDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZOGOS创世神启动器");
        }

        private static int CmdDiag(PackContext ctx, Settings st)
        {
            Console.WriteLine("=== 环境检测 ===");
            Console.WriteLine("启动器目录     : " + ctx.LauncherDir);
            Console.WriteLine("整合包根目录   : " + ctx.Root);
            Console.WriteLine("游戏目录       : " + ctx.GameDir);
            Console.WriteLine("游戏主程序     : " + ctx.GameExe + "   存在=" + File.Exists(ctx.GameExe));
            Console.WriteLine("游戏版本       : " + ctx.GameVersion);
            Console.WriteLine("模组目录       : " + ctx.ModsDir + "   存在=" + Directory.Exists(ctx.ModsDir));
            Console.WriteLine("整合包 Config  : " + ctx.PackConfigDir + "   存在=" + Directory.Exists(ctx.PackConfigDir));
            Console.WriteLine("排序文件夹     : " + ctx.SortDir + "   存在=" + Directory.Exists(ctx.SortDir));
            Console.WriteLine("配置目标目录   : " + ctx.TargetConfigDir + "   存在=" + Directory.Exists(ctx.TargetConfigDir));
            Console.WriteLine("游戏数据目录   : " + ctx.SaveDataDir + "   存在=" + Directory.Exists(ctx.SaveDataDir));
            Console.WriteLine("游戏启动参数   : " + GameLauncher.BuildArguments(ctx));
            Console.WriteLine("便携模式       : " + (ctx.PortableData ? "是（数据在整合包内）" : "否（数据在整合包外）"));
            Console.WriteLine("备份目录       : " + ctx.BackupDir);
            Console.WriteLine("设置目录       : " + ctx.SettingsDir);
            Console.WriteLine("RimSort        : " + (ctx.RimSortExe == null ? "(未找到)" : ctx.RimSortExe));
            Console.WriteLine("路径含中文     : " + ctx.NonAscii + "  " + ctx.NonAsciiSample);
            Console.WriteLine("问题           : " + (ctx.Problems.Count == 0 ? "无" : string.Join(" | ", ctx.Problems.ToArray())));
            Console.WriteLine("提示           : " + (ctx.Notes.Count == 0 ? "无" : string.Join(" | ", ctx.Notes.ToArray())));

            Console.WriteLine();
            Console.WriteLine("=== 本地模组索引 ===");
            ModIndex.StartAsync(ctx.GameDir);
            for (int i = 0; i < 120 && !ModIndex.Ready; i++) System.Threading.Thread.Sleep(250);
            Console.WriteLine("packageId 数量 : " + ModIndex.Count + "   完成=" + ModIndex.Ready);
            Console.WriteLine("设置文件       : " + (st == null ? "-" : st.IniPath));
            return 0;
        }

        private static int CmdSchemes(PackContext ctx)
        {
            List<SortScheme> list = SortSchemeLoader.Load(ctx);
            Console.WriteLine("共 " + list.Count + " 个排序方案：");
            for (int i = 0; i < list.Count; i++)
            {
                SortScheme s = list[i];
                Console.WriteLine("  [" + i + "] " + s.Name
                    + "  格式=" + s.Format
                    + "  模组=" + s.Mods.Count
                    + "  版本=" + s.VersionText
                    + "  RJW=" + (s.HasRjw ? "是" : "否")
                    + (s.IsBuiltIn ? "  (整合包默认)" : "")
                    + "  文件=" + s.SourcePath);
            }
            return 0;
        }

        private static SortScheme PickScheme(PackContext ctx, string schemeArg)
        {
            List<SortScheme> list = SortSchemeLoader.Load(ctx);
            if (list.Count == 0) return null;

            if (!string.IsNullOrEmpty(schemeArg))
            {
                int idx;
                if (int.TryParse(schemeArg, out idx) && idx >= 0 && idx < list.Count) return list[idx];
                for (int i = 0; i < list.Count; i++)
                {
                    SortScheme s = list[i];
                    if (string.Equals(s.SourcePath, schemeArg, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Path.GetFileNameWithoutExtension(s.SourcePath), schemeArg, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s.Name, schemeArg, StringComparison.OrdinalIgnoreCase))
                        return s;
                }
                return null;
            }
            for (int i = 0; i < list.Count; i++) if (list[i].IsBuiltIn) return list[i];
            return list[0];
        }

        private static int CmdDeploy(PackContext ctx, Settings st, string schemeArg)
        {
            SortScheme pick = PickScheme(ctx, schemeArg);
            if (pick == null) { Console.WriteLine("没有找到匹配的排序方案: " + schemeArg); return 1; }

            Console.WriteLine("使用排序方案: " + pick.Name + "（" + pick.Mods.Count + " 个模组，格式 " + pick.Format + "）");
            Console.WriteLine("配置文件将写入: " + Path.Combine(ctx.TargetConfigDir, "ModsConfig.xml"));
            Console.WriteLine("游戏数据目录  : " + ctx.SaveDataDir);
            Console.WriteLine("游戏启动参数  : " + GameLauncher.BuildArguments(ctx));
            Console.WriteLine("备份目录: " + ctx.BackupDir);
            Console.WriteLine();

            DeployResult r = ConfigDeployer.Deploy(ctx, pick, st, delegate(string msg) { Console.WriteLine("  · " + msg); });
            Console.WriteLine();
            Console.WriteLine("备份到        : " + (r.BackupPath == null ? "(未备份)" : r.BackupPath));
            Console.WriteLine("写入文件数    : " + r.Copied);
            Console.WriteLine("清空条目数    : " + r.Removed);
            Console.WriteLine("排序模组数    : " + r.ModsWritten);
            for (int i = 0; i < r.Warnings.Count; i++) Console.WriteLine("警告: " + r.Warnings[i]);
            for (int i = 0; i < r.Errors.Count; i++) Console.WriteLine("错误: " + r.Errors[i]);
            Console.WriteLine("结果: " + (r.Success ? "成功" : "失败"));

            string mc = Path.Combine(ctx.TargetConfigDir, "ModsConfig.xml");
            if (File.Exists(mc))
            {
                SortScheme check = SortScheme.Parse(mc, "verify");
                Console.WriteLine("复查:     " + mc);
                Console.WriteLine("          XML 头 = " + PathHelp.ReadAllTextSmart(mc).Split('\n')[0].Trim());
                Console.WriteLine("          模组数 = " + check.Mods.Count + "（期望 " + pick.Mods.Count + "）");
                Console.WriteLine("          版本 = " + check.Version + "   扩展 = " + check.Expansions.Count);
            }
            return r.Success ? 0 : 1;
        }

        private static int CmdAnalyze(PackContext ctx, Settings st, string schemeArg)
        {
            SortScheme pick = PickScheme(ctx, schemeArg);
            if (pick == null) { Console.WriteLine("没有找到匹配的排序方案: " + schemeArg); return 1; }
            Console.WriteLine("正在体检方案: " + pick.Name + "（" + pick.Mods.Count + " 个模组）");
            ModAnalysisResult r = ModAnalyzer.Analyze(ctx, pick);
            ModAnalyzer.AnalyzePlayerLog(ctx, pick, r);
            Console.WriteLine();
            Console.WriteLine(r.SummaryText());
            string report = null;
            try
            {
                if (!Directory.Exists(ctx.SortDir)) Directory.CreateDirectory(ctx.SortDir);
                report = Path.Combine(ctx.SortDir, "ZOGOS体检_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(report, r.BuildReport(pick.SourcePath, null, null), new UTF8Encoding(true));
            }
            catch
            {
                try
                {
                    if (!Directory.Exists(ctx.SettingsDir)) Directory.CreateDirectory(ctx.SettingsDir);
                    report = Path.Combine(ctx.SettingsDir, "ZOGOS体检_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                    File.WriteAllText(report, r.BuildReport(pick.SourcePath, null, null), new UTF8Encoding(true));
                }
                catch (Exception ex2) { Console.WriteLine("写体检报告失败: " + ex2.Message); }
            }
            if (!string.IsNullOrEmpty(report)) Console.WriteLine("体检报告: " + report);
            return r.HasErrors ? 1 : 0;
        }

        private static int CmdOrder(PackContext ctx, Settings st, string schemeArg)
        {
            SortScheme pick = PickScheme(ctx, schemeArg);
            if (pick == null) { Console.WriteLine("没有找到匹配的排序方案: " + schemeArg); return 1; }
            ModAnalysisResult r = ModAnalyzer.Analyze(ctx, pick);
            ModAnalyzer.AnalyzePlayerLog(ctx, pick, r);
            Console.WriteLine(r.SummaryText());
            string path = ModAnalyzer.WriteSuggestedScheme(ctx, pick, r);
            if (string.IsNullOrEmpty(path)) { Console.WriteLine("生成智能排序失败"); return 1; }
            Console.WriteLine("已生成智能排序: " + path);
            Console.WriteLine("调整位置: " + r.MovedCount + "  错误: " + r.ErrorCount + "  警告: " + r.WarningCount);
            return 0;
        }

        private static int CmdSafe(PackContext ctx, Settings st, string schemeArg)
        {
            SortScheme pick = PickScheme(ctx, schemeArg);
            if (pick == null) { Console.WriteLine("没有找到匹配的排序方案: " + schemeArg); return 1; }
            ModAnalysisResult r = ModAnalyzer.Analyze(ctx, pick);
            ModAnalyzer.AnalyzePlayerLog(ctx, pick, r);
            Console.WriteLine(r.SummaryText());
            string path = ModAnalyzer.WriteSafeScheme(ctx, pick, r);
            if (string.IsNullOrEmpty(path)) { Console.WriteLine("生成安全排序失败"); return 1; }
            Console.WriteLine("已生成安全排序: " + path);
            Console.WriteLine("疑似问题模组: " + string.Join(", ", r.Suspects.ToArray()));
            return 0;
        }

        private static int CmdExport(PackContext ctx, string schemeArg, bool full, string outPath)
        {
            SortScheme scheme = PickScheme(ctx, schemeArg);
            if (string.IsNullOrEmpty(outPath))
            {
                string parent = Path.GetDirectoryName(ctx.Root.TrimEnd((char)92));
                outPath = Path.Combine(parent, PackSpec.SuggestName(ctx, scheme, full));
            }
            Console.WriteLine("导出" + (full ? "完整包" : "轻量包") + "  ->  " + outPath);
            ExportResult r = PackExporter.Export(ctx, scheme, full, outPath,
                delegate(string m) { Console.WriteLine("  " + m); },
                delegate(int i, int n, string nm) { if (i == n || i % 200 == 0) Console.WriteLine("    " + i + "/" + n); });
            for (int i = 0; i < r.Warnings.Count; i++) Console.WriteLine("  警告: " + r.Warnings[i]);
            for (int i = 0; i < r.Errors.Count; i++) Console.WriteLine("  错误: " + r.Errors[i]);
            Console.WriteLine("结果: " + (r.Success ? "成功" : "失败") + "  文件数=" + r.Files + "  原始=" + PackExporter.Human(r.RawBytes) + "  压缩后=" + PackExporter.Human(r.ZipBytes));
            return r.Success ? 0 : 1;
        }

        private static int CmdImport(string zip, string destDir)
        {
            if (string.IsNullOrEmpty(zip) || !File.Exists(zip)) { Console.WriteLine("找不到压缩包: " + zip); return 1; }
            ZipPackInfo info = PackImporter.Inspect(zip);
            Console.WriteLine(info.Describe());
            if (string.IsNullOrEmpty(destDir)) { Console.WriteLine("需要 --dest 指定解压目录"); return 1; }
            List<string> errs = new List<string>();
            int n = PackImporter.Extract(zip, destDir, delegate(int i, int all, string nm)
            {
                if (i == all || i % 500 == 0) Console.WriteLine("    解压 " + i + "/" + all);
            }, errs);
            Console.WriteLine("解压文件数 = " + n + "   问题 = " + errs.Count);
            for (int i = 0; i < errs.Count && i < 10; i++) Console.WriteLine("    " + errs[i]);
            string found = PackImporter.FindPackRoot(destDir);
            Console.WriteLine("找到整合包根目录 = " + (found == null ? "(没有找到)" : found));
            if (found != null)
            {
                Console.WriteLine("  识别令牌 packId = " + PackMarker.ReadPackId(found));
                bool strict; List<string> notes; string pid;
                int sc = PackDetector.Score(found, out strict, out notes, out pid);
                Console.WriteLine("  打分 = " + sc + "  像整合包 = " + strict);
            }
            return found != null ? 0 : 1;
        }

        private static int CmdRun(PackContext ctx, Settings st, string schemeArg, int waitSec)
        {
            int rc = CmdDeploy(ctx, st, schemeArg);
            if (rc != 0) { Console.WriteLine("配置写入失败，已取消启动"); return rc; }

            Console.WriteLine();
            if (st.StartSteam) SteamHelp.EnsureRunning();

            Process p = GameLauncher.Start(ctx);
            bool exited;
            int code = GameLauncher.WaitForExit(p, Math.Max(3, waitSec) * 1000, out exited);
            if (!exited)
            {
                Console.WriteLine("结果: 游戏在 " + waitSec + " 秒内仍在运行 → 判定为启动成功");
                try { p.Kill(); Console.WriteLine("（测试环境已结束假游戏进程）"); } catch { }
                return 0;
            }
            Console.WriteLine("游戏已退出，退出码 = " + code);
            if (GameLauncher.LooksLikeFailure(code, exited))
            {
                Console.WriteLine("结果: 判定为启动失败（图形界面下会弹出错误日志提示，并生成诊断报告）");
                return 1;
            }
            Console.WriteLine("结果: 正常退出");
            return 0;
        }
    }
}
