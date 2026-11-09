using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;

namespace Zogos
{
    // =====================================================================
    //  本机模组索引：把 Mods / Data 下所有 About.xml 的 packageId 收集起来
    // =====================================================================
    public static class ModIndex
    {
        private static HashSet<string> _ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static volatile bool _ready;
        private static int _count;
        private static string _gameDir;

        public static bool Ready { get { return _ready; } }
        public static int Count { get { return _count; } }

        public static bool Contains(string id)
        {
            if (string.IsNullOrEmpty(id)) return true;
            return _ids.Contains(id);
        }

        public static void StartAsync(string gameDir)
        {
            _gameDir = gameDir;
            ThreadPool.QueueUserWorkItem(delegate(object o) { Build(); });
        }

        public static void Build()
        {
            Stopwatch sw = Stopwatch.StartNew();
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                ScanFolder(Path.Combine(_gameDir, "Mods"), set);
                ScanFolder(Path.Combine(_gameDir, "Data"), set);
            }
            catch (Exception ex) { Log.Error(L.T("建立模组索引失败"), ex); }

            _ids = set;
            _count = set.Count;
            _ready = true;
            Log.Info(L.T("模组索引完成：本地可用 packageId ") + _count + L.T(" 个（耗时 ") + sw.ElapsedMilliseconds + " ms）");
        }

        private static void ScanFolder(string folder, HashSet<string> set)
        {
            if (!Directory.Exists(folder)) return;
            List<string> abouts = new List<string>();
            Collect(folder, abouts, 0);
            for (int i = 0; i < abouts.Count; i++)
            {
                string id = ReadPackageId(abouts[i]);
                if (!string.IsNullOrEmpty(id)) set.Add(id);
            }
        }

        private static void Collect(string dir, List<string> outList, int depth)
        {
            if (depth > 6) return;
            try
            {
                string[] files = Directory.GetFiles(dir, "About.xml");
                for (int i = 0; i < files.Length; i++) outList.Add(files[i]);
            }
            catch { }

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { return; }
            for (int i = 0; i < subs.Length; i++) Collect(subs[i], outList, depth + 1);
        }

        private static string ReadPackageId(string aboutPath)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(aboutPath);
                XmlElement root = doc.DocumentElement;
                if (root == null) return null;
                XmlNode n = root.SelectSingleNode("packageId");
                if (n == null) return null;
                return n.InnerText.Trim();
            }
            catch { return null; }
        }

        /// <summary>算出方案里本地找不到的模组（创意工坊模组也会算进来，所以只作为提示）。</summary>
        public static void Evaluate(SortScheme s)
        {
            if (s == null) return;
            List<string> miss = new List<string>();
            for (int i = 0; i < s.Mods.Count; i++)
            {
                if (!_ids.Contains(s.Mods[i])) miss.Add(s.Mods[i]);
            }
            s.MissingList = miss;
            s.Missing = miss.Count;
        }
    }

    // =====================================================================
    //  配置部署：备份 → 清空 → 写入整合包 Config → 写入所选排序
    // =====================================================================
    public class DeployResult
    {
        public string BackupPath;
        public string GameConfigBackup;
        public int Copied;
        public int Removed;
        public int ModsWritten;
        public bool DidBackup;
        public bool FullDeploy;
        public List<string> Warnings = new List<string>();
        public List<string> Errors = new List<string>();
        public bool Success { get { return Errors.Count == 0; } }
    }

    public static class ConfigDeployer
    {
        public static string Stamp()
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        public static string Backup(string srcDir, string backupRoot, string label, List<string> warnings)
        {
            string dst = Path.Combine(backupRoot, label + "_" + Stamp());
            int i = 1;
            while (Directory.Exists(dst))
            {
                dst = Path.Combine(backupRoot, label + "_" + Stamp() + "_" + i);
                i++;
            }
            Directory.CreateDirectory(dst);

            List<string> errs = new List<string>();
            int n = FileOps.CopyTree(srcDir, dst, errs, false);
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(L.T("Astra 创世神协议 —— 配置备份"));
                sb.AppendLine(L.T("备份时间 : ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine(L.T("源目录   : ") + srcDir);
                sb.AppendLine(L.T("文件数量 : ") + n);
                sb.AppendLine();
                sb.AppendLine(L.T("还原方法 : 打开启动器「设置」页，点「从备份还原」，选择本文件夹。"));
                File.WriteAllText(Path.Combine(dst, L.T("备份说明.txt")), sb.ToString(), new UTF8Encoding(true));
            }
            catch { }

            if (warnings != null && errs.Count > 0) warnings.AddRange(errs);
            if (errs.Count > 0) Log.Warn(L.T("备份过程中有 ") + errs.Count + L.T(" 个文件没能复制（其余已成功）"));
            return dst;
        }

        public static DeployResult Deploy(PackContext ctx, SortScheme scheme, Settings st, Action<string> progress)
        {
            DeployResult r = new DeployResult();
            if (ctx == null) { r.Errors.Add(L.T("没有找到整合包目录")); return r; }
            if (scheme == null) { r.Errors.Add(L.T("没有选择排序方案")); return r; }

            if (ctx.PortableData)
                Log.Info(L.T("→ 便携模式：游戏数据目录 ") + ctx.SaveDataDir + L.T("（Config/Saves/ModLists 都在整合包内）"));
            else
                Log.Warn(L.T("→ 非便携模式：游戏数据目录在整合包外 ") + ctx.SaveDataDir);

            if (progress != null) progress(L.T("检查配置目录…"));
            try
            {
                if (!Directory.Exists(ctx.TargetConfigDir)) Directory.CreateDirectory(ctx.TargetConfigDir);
            }
            catch (Exception ex)
            {
                r.Errors.Add(L.T("无法创建游戏配置目录：") + ctx.TargetConfigDir + "（" + ex.Message + "）");
                return r;
            }

            // 关键：只有第一次（或玩家要求重置）才覆盖整合包 Config，
            // 否则每次启动都会把玩家的画面 / 按键 / 模组设置冲掉。
            bool needFull = (st == null) || st.ReplaceEveryTime || !DeployState.Matches(ctx);
            r.FullDeploy = needFull;
            if (needFull) Log.Info(L.T("→ 完整套用整合包配置（首次安装，或你勾了每次重置）"));
            else Log.Info(L.T("→ 之前已套用过：只更新排序方案，保留你自己的画面 / 按键 / 模组设置"));

            // 1) 备份玩家原有配置
            if (needFull && (st == null || st.AutoBackup))
            {
                if (progress != null) progress(L.T("正在备份玩家原有配置…"));
                try
                {
                    if (!Directory.Exists(ctx.BackupDir)) Directory.CreateDirectory(ctx.BackupDir);
                    r.BackupPath = Backup(ctx.TargetConfigDir, ctx.BackupDir, L.T("备份"), r.Warnings);
                    r.DidBackup = true;
                    Log.Info(L.T("已备份原配置 → ") + r.BackupPath);
                }
                catch (Exception ex)
                {
                    r.Errors.Add(L.T("备份失败，为了保护你的存档，已放弃替换配置：") + ex.Message);
                    return r;
                }
            }
            else Log.Warn(L.T("按设置跳过备份（AutoBackup 已关闭）"));

            // 2) 清空原有配置
            // ★ 完整替换前，先把玩家的「画面 / 音量 / 按键」设置留一份。
            //   这两项属于玩家个人偏好，任何情况下都不该被整合包的默认值覆盖。
            string keepPrefs = null, keepKeys = null;
            if (needFull)
            {
                try
                {
                    string p1 = Path.Combine(ctx.TargetConfigDir, "Prefs.xml");
                    if (File.Exists(p1)) { keepPrefs = Path.Combine(Path.GetTempPath(), "zogos_keep_prefs.xml"); File.Copy(p1, keepPrefs, true); }
                    string p2 = Path.Combine(ctx.TargetConfigDir, "KeyPrefs.xml");
                    if (File.Exists(p2)) { keepKeys = Path.Combine(Path.GetTempPath(), "zogos_keep_keyprefs.xml"); File.Copy(p2, keepKeys, true); }
                }
                catch { }
            }

            if (needFull && (st == null || st.ClearConfig))
            {
                if (progress != null) progress(L.T("正在清空原有配置…"));
                r.Removed = FileOps.ClearDir(ctx.TargetConfigDir, r.Errors);
                Log.Info(L.T("清空原有配置 ") + r.Removed + L.T(" 项"));
            }

            // 3) 写入整合包 Config
            if (needFull)
            {
                if (progress != null) progress(L.T("正在写入整合包配置…"));
                if (Directory.Exists(ctx.PackConfigDir))
                {
                    r.Copied = FileOps.CopyTree(ctx.PackConfigDir, ctx.TargetConfigDir, r.Errors, true);
                    // ★ 把玩家的画面 / 按键设置放回去（整合包只提供默认值）
                    if (keepPrefs != null)
                    {
                        try { File.Copy(keepPrefs, Path.Combine(ctx.TargetConfigDir, "Prefs.xml"), true); Log.Info(L.T("已保留你原来的画面设置（Prefs.xml）")); }
                        catch { }
                    }
                    if (keepKeys != null)
                    {
                        try { File.Copy(keepKeys, Path.Combine(ctx.TargetConfigDir, "KeyPrefs.xml"), true); Log.Info(L.T("已保留你原来的按键设置（KeyPrefs.xml）")); }
                        catch { }
                    }
                    Log.Info(L.T("写入整合包配置 ") + r.Copied + L.T(" 个文件"));
                }
                else r.Errors.Add(L.T("整合包 Config 目录不存在：") + ctx.PackConfigDir);
            }
            else
            {
                try
                {
                    string oldMc = Path.Combine(ctx.TargetConfigDir, "ModsConfig.xml");
                    if (File.Exists(oldMc)) File.Copy(oldMc, Path.Combine(ctx.TargetConfigDir, L.T("ModsConfig.xml.zogos旧版")), true);
                }
                catch { }
            }

            // 4) 写入玩家选择的排序方案
            if (progress != null) progress(L.T("正在写入排序方案：") + scheme.Name + " …");
            string modsConfig = Path.Combine(ctx.TargetConfigDir, "ModsConfig.xml");
            try
            {
                scheme.WriteModsConfig(modsConfig);
                r.ModsWritten = scheme.Mods.Count;
                Log.Info(L.T("排序方案已写入：") + modsConfig + "（" + scheme.Mods.Count + L.T(" 个模组）"));
            }
            catch (Exception ex)
            {
                r.Errors.Add(L.T("写入排序方案失败：") + ex.Message);
            }

            // 5) 顺带同步游戏目录内的 Config（少数启动方式会被这个目录影响）
            if (st == null || st.SyncGameConfig)
            {
                try
                {
                    string gcd = ctx.GameConfigDir;
                    if (!string.IsNullOrEmpty(gcd) && Directory.Exists(gcd))
                    {
                        string gm = Path.Combine(gcd, "ModsConfig.xml");
                        if (File.Exists(gm))
                        {
                            string bak = Path.Combine(gcd, L.T("ModsConfig.xml.zogos备份"));
                            if (!File.Exists(bak)) File.Copy(gm, bak, false);
                        }
                        scheme.WriteModsConfig(gm);
                        r.GameConfigBackup = gm;
                        Log.Info(L.T("已同步游戏目录内的 Config：") + gm);
                    }
                }
                catch (Exception ex)
                {
                    r.Warnings.Add(L.T("同步游戏目录内的 Config 失败（不影响启动）：") + ex.Message);
                    Log.Warn(L.T("同步游戏目录内的 Config 失败：") + ex.Message);
                }
            }

            // 6) 清理过旧的备份
            try { PruneBackups(ctx.BackupDir, st == null ? 10 : st.KeepBackups); }
            catch { }

            // 7) 校验写入结果
            try
            {
                if (File.Exists(modsConfig))
                {
                    SortScheme check = SortScheme.Parse(modsConfig, L.T("校验"));
                    if (check.ParseError == null && check.Mods.Count == scheme.Mods.Count)
                        Log.Info(L.T("校验通过：ModsConfig.xml 共 ") + check.Mods.Count + L.T(" 个模组"));
                    else
                        r.Warnings.Add(L.T("校验时读回的模组数量与预期不一致（预期 ") + scheme.Mods.Count + L.T(" 个，读到 ") + check.Mods.Count + L.T(" 个）"));
                }
                else r.Errors.Add(L.T("写入后没有找到 ModsConfig.xml"));
            }
            catch (Exception ex) { r.Warnings.Add(L.T("校验失败：") + ex.Message); }

            if (r.Success) DeployState.Save(ctx);

            return r;
        }

        public static void PruneBackups(string backupRoot, int keep)
        {
            if (keep < 1) keep = 1;
            if (!Directory.Exists(backupRoot)) return;
            DirectoryInfo[] subs = new DirectoryInfo(backupRoot).GetDirectories();
            List<DirectoryInfo> list = new List<DirectoryInfo>(subs);
            list.Sort(delegate(DirectoryInfo a, DirectoryInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
            for (int i = keep; i < list.Count; i++)
            {
                try { list[i].Delete(true); Log.Info(L.T("已清理旧备份：") + list[i].Name); }
                catch { }
            }
        }

        public static List<string> ListBackups(string backupRoot)
        {
            List<string> res = new List<string>();
            if (!Directory.Exists(backupRoot)) return res;
            DirectoryInfo[] subs = new DirectoryInfo(backupRoot).GetDirectories();
            List<DirectoryInfo> list = new List<DirectoryInfo>(subs);
            list.Sort(delegate(DirectoryInfo a, DirectoryInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
            for (int i = 0; i < list.Count; i++) res.Add(list[i].FullName);
            return res;
        }

        public static DeployResult Restore(string backupDir, string targetDir)
        {
            DeployResult r = new DeployResult();
            if (!Directory.Exists(backupDir)) { r.Errors.Add(L.T("备份文件夹不存在：") + backupDir); return r; }
            try
            {
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
            }
            catch (Exception ex) { r.Errors.Add(L.T("无法创建目标目录：") + ex.Message); return r; }

            try
            {
                string parent = Path.GetDirectoryName(targetDir);
                r.BackupPath = Backup(targetDir, Path.Combine(parent, L.T("ZOGOS还原前备份")), L.T("还原前备份"), r.Warnings);
            }
            catch { }

            r.Removed = FileOps.ClearDir(targetDir, r.Errors);
            r.Copied = FileOps.CopyTree(backupDir, targetDir, r.Errors, true);
            Log.Info(L.T("已从备份还原：") + backupDir + " → " + targetDir + "（" + r.Copied + L.T(" 个文件）"));
            return r;
        }
    }

    // =====================================================================
    //  Steam 辅助
    // =====================================================================
    public static class SteamHelp
    {
        public static bool IsRunning()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName("steam");
                return ps.Length > 0;
            }
            catch { return false; }
        }

        public static string SteamExe()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("SteamExe");
                        if (v != null)
                        {
                            string p = v.ToString().Replace('/', '\\');
                            if (File.Exists(p)) return p;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static void EnsureRunning()
        {
            if (IsRunning()) { Log.Info(L.T("Steam 已在运行")); return; }
            try
            {
                string exe = SteamExe();
                if (!string.IsNullOrEmpty(exe))
                {
                    Log.Info(L.T("正在启动 Steam：") + exe);
                    Process.Start(exe);
                    for (int i = 0; i < 30; i++)
                    {
                        Thread.Sleep(1000);
                        if (IsRunning()) { Log.Info(L.T("Steam 已就绪")); return; }
                    }
                }
                else Log.Warn(L.T("没有检测到 Steam，跳过（直接运行 RimWorld 主程序）"));
            }
            catch (Exception ex) { Log.Warn(L.T("启动 Steam 失败：") + ex.Message); }
        }
    }

    // =====================================================================
    //  诊断报告：一键生成给社区管理员看的txt
    // =====================================================================
    public static class Diag
    {
        public static string BuildReport(PackContext ctx, SortScheme scheme, Settings st)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine(L.T(" Astra 创世神协议 —— 诊断报告"));
            sb.AppendLine("==================================================");
            sb.AppendLine(L.T("生成时间  : ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(L.T("启动器版本: ") + Log.Version);
            sb.AppendLine(L.T("系统      : ") + Environment.OSVersion.VersionString + "  " + (Environment.Is64BitOperatingSystem ? L.T("64 位") : L.T("32 位")));
            sb.AppendLine("CLR       : " + Environment.Version);
            sb.AppendLine("");
            sb.AppendLine(L.T("---------- 系统资源 ----------"));
            sb.AppendLine(ResourceInfo());
            sb.AppendLine("");
            sb.AppendLine(L.T("---------- 最近一次游戏退出码 ----------"));
            sb.AppendLine(RecentExit());
            sb.AppendLine("");
            sb.AppendLine(L.T("---------- 路径 ----------"));
            sb.AppendLine(L.T("启动器目录    : ") + ctx.LauncherDir);
            sb.AppendLine(L.T("整合包根目录  : ") + ctx.Root);
            sb.AppendLine(L.T("路径含中文    : ") + (ctx.NonAscii ? L.T("是（") + ctx.NonAsciiSample + L.T("）← 可能导致游戏加载模组失败") : L.T("否")));
            sb.AppendLine(L.T("游戏主程序    : ") + ctx.GameExe + L.T("   存在=") + File.Exists(ctx.GameExe));
            sb.AppendLine(L.T("游戏版本      : ") + ctx.GameVersion);
            sb.AppendLine(L.T("模组目录      : ") + ctx.ModsDir);
            sb.AppendLine(L.T("目标配置目录  : ") + ctx.TargetConfigDir);
            sb.AppendLine(L.T("游戏数据目录  : ") + ctx.SaveDataDir + L.T("（便携=") + (ctx.PortableData ? L.T("是") : L.T("否")) + "）");
            sb.AppendLine(L.T("游戏启动参数  : ") + GameLauncher.BuildArguments(ctx));
            sb.AppendLine(L.T("整合包 Config  : ") + ctx.PackConfigDir);
            sb.AppendLine(L.T("排序文件夹    : ") + ctx.SortDir);
            sb.AppendLine(L.T("备份目录      : ") + ctx.BackupDir);
            sb.AppendLine(L.T("备份目录可写  : ") + FileOps.IsWritable(ctx.BackupDir));
            sb.AppendLine("");
            sb.AppendLine(L.T("---------- 模组 ----------"));
            if (ModIndex.Ready) ModIndex.Evaluate(scheme);
            sb.AppendLine(L.T("本地 packageId 数量 : ") + ModIndex.Count + (ModIndex.Ready ? "" : L.T("（索引尚未完成）")));
            if (scheme != null)
            {
                sb.AppendLine(L.T("当前排序方案 : ") + scheme.Name);
                sb.AppendLine(L.T("方案文件     : ") + scheme.SourcePath);
                sb.AppendLine(L.T("格式 / 版本  : ") + scheme.Format + " / " + scheme.VersionText);
                sb.AppendLine(L.T("模组数量     : ") + scheme.Mods.Count);
                sb.AppendLine(L.T("本地缺失数量 : ") + scheme.Missing + L.T("（若用创意工坊模组，属正常）"));
                int max = Math.Min(40, scheme.MissingList.Count);
                for (int i = 0; i < max; i++) sb.AppendLine("    - " + scheme.MissingList[i]);
                if (scheme.MissingList.Count > max) sb.AppendLine(L.T("    ... 其余 ") + (scheme.MissingList.Count - max) + L.T(" 个省略"));
            }
            else sb.AppendLine(L.T("当前排序方案 : 未选择"));
            sb.AppendLine("");
            sb.AppendLine(L.T("---------- 设置 ----------"));
            if (st != null)
            {
                sb.AppendLine(L.T("自动备份=") + st.AutoBackup + L.T("  清空原配置=") + st.ClearConfig + L.T("  同步游戏内Config=") + st.SyncGameConfig);
                sb.AppendLine(L.T("启动Steam=") + st.StartSteam + L.T("  保留备份数=") + st.KeepBackups + L.T("  中文路径警告=") + st.WarnNonAscii);
                sb.AppendLine(L.T("设置文件: ") + st.IniPath);
            }
            sb.AppendLine("");
            sb.AppendLine(L.T("---------- RimWorld 日志中的错误 ----------"));
            sb.AppendLine(RimLogErrors(ctx, 60));
            sb.AppendLine("");
            sb.AppendLine(L.T("---------- 启动器日志（尾部 100 行） ----------"));
            sb.AppendLine(Log.TailText(100));
            return sb.ToString();
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MEMSTAT
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MEMSTAT lpBuffer);

        /// <summary>磁盘剩余 + 物理内存（崩溃排查最需要的两个数）。</summary>
        public static string ResourceInfo()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;
                    double freeGB = d.AvailableFreeSpace / 1073741824.0;
                    double totGB = d.TotalSize / 1073741824.0;
                    string flag = "";
                    if (freeGB < 5) flag = L.T("   ← 空间严重不足，很可能导致崩溃！");
                    else if (freeGB < 20) flag = L.T("   ← 空间偏少");
                    sb.AppendLine("  " + d.Name + L.T("  剩余 ") + freeGB.ToString("0.0") + L.T(" GB / 共 ") + totGB.ToString("0.0") + " GB" + flag);
                }
            }
            catch { }
            try
            {
                MEMSTAT m = new MEMSTAT();
                m.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMSTAT));
                if (GlobalMemoryStatusEx(ref m))
                    sb.AppendLine(L.T("  物理内存  共 ") + (m.ullTotalPhys / 1073741824.0).ToString("0.0") + L.T(" GB，可用 ")
                        + (m.ullAvailPhys / 1073741824.0).ToString("0.0") + L.T(" GB（占用 ") + m.dwMemoryLoad + "%）");
            }
            catch { }
            return sb.ToString().TrimEnd();
        }

        /// <summary>从启动器日志里找出最近一次游戏退出码并翻译。</summary>
        public static string RecentExit()
        {
            try
            {
                string[] all = Log.Snapshot();
                for (int i = all.Length - 1; i >= 0; i--)
                {
                    System.Text.RegularExpressions.Match mm =
                        System.Text.RegularExpressions.Regex.Match(all[i], L.T("退出代码 = (-?\\d+)"));
                    if (mm.Success)
                    {
                        int code = 0;
                        int.TryParse(mm.Groups[1].Value, out code);
                        StringBuilder sb2 = new StringBuilder();
                        sb2.AppendLine("  " + all[i].Trim());
                        sb2.AppendLine(L.T("  含义：") + ExitCodes.Explain(code));
                        if (ExitCodes.IsCrash(code))
                        {
                            sb2.AppendLine(L.T("  这不是启动器的问题，是游戏进程本身崩了。"));
                            sb2.AppendLine(L.T("  建议排查顺序："));
                            sb2.AppendLine("    " + ExitCodes.Advice(code).Replace("\n", "\n    "));
                        }
                        return sb2.ToString().TrimEnd();
                    }
                }
            }
            catch { }
            return L.T("  （本次运行还没有游戏退出记录）");
        }

        public static string RimLogErrors(PackContext ctx, int maxLines)
        {
            try
            {
                string log = ctx.PlayerLog;
                if (!File.Exists(log))
                {
                    string alt = Path.Combine(ctx.GameLogsDir, "Player.log");
                    if (File.Exists(alt)) log = alt;
                }
                if (!File.Exists(log)) return L.T("（没有找到 Player.log，游戏可能还没运行过）");

                string tail = TailOfFile(log, 400 * 1024);
                string[] lines = tail.Replace("\r\n", "\n").Split('\n');
                List<string> hits = new List<string>();
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i];
                    if (l.Length == 0) continue;
                    if (l.IndexOf("[Error]", StringComparison.OrdinalIgnoreCase) >= 0
                        || l.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0
                        || l.IndexOf("Could not load", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string t = l.Trim();
                        if (t.Length > 400) t = t.Substring(0, 400) + " ...";
                        hits.Add(t);
                    }
                }
                if (hits.Count == 0) return L.T("（日志尾部没有发现明显错误行）");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(L.T("日志文件: ") + log);
                int start = Math.Max(0, hits.Count - maxLines);
                if (start > 0) sb.AppendLine(L.T("（共 ") + hits.Count + L.T(" 条，仅显示最后 ") + (hits.Count - start) + L.T(" 条）"));
                for (int i = start; i < hits.Count; i++) sb.AppendLine("  " + hits[i]);
                return sb.ToString();
            }
            catch (Exception ex) { return L.T("（读取日志失败：") + ex.Message + "）"; }
        }

        public static string TailOfFile(string path, int bytes)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long len = fs.Length;
                long start = len > bytes ? len - bytes : 0;
                fs.Seek(start, SeekOrigin.Begin);
                byte[] buf = new byte[len - start];
                int read = fs.Read(buf, 0, buf.Length);
                return Encoding.UTF8.GetString(buf, 0, read);
            }
        }

        public static string SaveReport(PackContext ctx, SortScheme scheme, Settings st)
        {
            string text = BuildReport(ctx, scheme, st);
            string dir = ctx.BackupDir;
            try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
            catch { dir = ctx.LauncherDir; }
            string file = Path.Combine(dir, L.T("ZOGOS诊断报告_") + ConfigDeployer.Stamp() + ".txt");
            File.WriteAllText(file, text, new UTF8Encoding(true));
            Log.Info(L.T("已生成诊断报告：") + file);
            return file;
        }
    }
}
