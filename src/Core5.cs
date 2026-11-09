using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Zogos
{
    // =====================================================================
    //  整合包格式规范（v1）
    //  命名规则 + 目录结构 + 校验规则，导出/导入都按这一套来
    // =====================================================================
    public static class PackSpec
    {
        public const string SpecVersion = "1";
        public const string MarkerJson = "ZOGOS整合包标识.json";
        public const string MarkerId = "zogos.pack";
        public const string ReadmeName = "ZOGOS整合包说明.txt";
        public const string ConfigName = "Config";
        public const string GameName = "RimWorld";
        public const string SortName = "排序文件";

        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return L.T("整合包");
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = true;
                for (int j = 0; j < bad.Length; j++) { if (c == bad[j]) { ok = false; break; } }
                if (ok) sb.Append(c); else sb.Append('_');
            }
            string s = sb.ToString().Trim();
            s = s.Trim('.', ' ');
            if (s.Length == 0) s = L.T("整合包");
            if (s.Length > 40) s = s.Substring(0, 40);
            return s;
        }

        public static string PackNameOf(PackContext ctx)
        {
            try { return Sanitize(Path.GetFileName(ctx.Root.TrimEnd('\\', '/'))); }
            catch { return L.T("整合包"); }
        }

        /// <summary>压缩包建议文件名：ZOGOS_包名_模组数mods_RimWorld版本_类型_日期.zip</summary>
        public static string SuggestName(PackContext ctx, SortScheme scheme, bool full)
        {
            int mods = (scheme != null && scheme.Mods != null) ? scheme.Mods.Count : 0;
            string ver = (ctx != null && !string.IsNullOrEmpty(ctx.GameVersion)) ? Sanitize(ctx.GameVersion) : L.T("未知版本");
            string kind = full ? L.T("完整") : L.T("轻量");
            return "ZOGOS_" + PackNameOf(ctx) + "_" + mods + "mods_RimWorld" + ver + "_" + kind + "_" + DateTime.Now.ToString("yyyyMMdd") + ".zip";
        }

        public static string Text()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("================ ZOGOS 整合包格式规范 v") + SpecVersion + " ================");
            sb.AppendLine();
            sb.AppendLine(L.T("【一、压缩包命名规则】"));
            sb.AppendLine(L.T("    ZOGOS_<整合包名>_<模组数>mods_RimWorld<游戏版本>_<完整|轻量>_<日期>.zip"));
            sb.AppendLine(L.T("    例：ZOGOS_花不语的整合包_612mods_RimWorld1.7.4980_完整_20261017.zip"));
            sb.AppendLine(L.T("    说明："));
            sb.AppendLine(L.T("      · 必须以 ZOGOS_ 开头，这样在网盘和群里一眼就能认出来"));
            sb.AppendLine(L.T("      · <整合包名> 不能包含 \\ / : * ? \" < > | 这些字符"));
            sb.AppendLine(L.T("      · 完整包 = 连游戏本体和模组一起打包，拿到就能玩"));
            sb.AppendLine(L.T("        轻量包 = 只打 Config + 排序文件 + 令牌，体积很小，"));
            sb.AppendLine(L.T("                 适合发给已经装了同一套整合包的人"));
            sb.AppendLine();
            sb.AppendLine(L.T("【二、压缩包内部结构】"));
            sb.AppendLine(L.T("    解压后必须是「一个文件夹」，文件夹名就是整合包名："));
            sb.AppendLine();
            sb.AppendLine(L.T("    <整合包名>/"));
            sb.AppendLine(L.T("      ├─ ZOGOS整合包标识.json    识别令牌（必须）"));
            sb.AppendLine(L.T("      ├─ zogos.pack              识别令牌副本（必须）"));
            sb.AppendLine(L.T("      ├─ ZOGOS整合包说明.txt      包信息（导出时自动生成）"));
            sb.AppendLine(L.T("      ├─ Config/                 游戏配置（必须）"));
            sb.AppendLine(L.T("      │    └─ ModsConfig.xml     排序文件本体（必须）"));
            sb.AppendLine(L.T("      ├─ rimsort创世神1.7社区版.exe  模组管理器（必须，配套工具）"));
            sb.AppendLine(L.T("      ├─ _internal/                RimSort 的运行库（有 RimSort 就必须一起带）"));
            sb.AppendLine(L.T("      ├─ 排序文件/                可切换的排序方案（必须，至少 1 个 .xml）"));
            sb.AppendLine(L.T("      ├─ UserData/              首次启动后自动生成（存档/配置/模组列表）"));
            sb.AppendLine(L.T("      └─ RimWorld/               游戏本体 + 模组（完整包必须）"));
            sb.AppendLine(L.T("           ├─ RimWorldWin64.exe  游戏主程序（必须）"));
            sb.AppendLine(L.T("           └─ Mods/              模组文件夹（必须）"));
            sb.AppendLine();
            sb.AppendLine(L.T("【三、必需项 / 可选项】"));
            sb.AppendLine(L.T("    必需："));
            sb.AppendLine(L.T("      · 识别令牌 ZOGOS整合包标识.json（含 packId）"));
            sb.AppendLine(L.T("      · Config 文件夹（里面有 ModsConfig.xml）"));
            sb.AppendLine(L.T("      · 排序文件 文件夹（至少 1 个 .xml 或 .json）"));
            sb.AppendLine(L.T("      · RimSort 模组管理器 rimsort*.exe（以及同目录的 _internal 运行库）"));
            sb.AppendLine(L.T("    完整包还必须要有："));
            sb.AppendLine(L.T("      · RimWorld 文件夹，里面有 RimWorldWin64.exe"));
            sb.AppendLine(L.T("      · RimWorld\\Mods 文件夹"));
            sb.AppendLine(L.T("    可选项（有更好，没有也能用）："));
            sb.AppendLine(L.T("      · 教程.txt、发布页图片等"));
            sb.AppendLine();
            sb.AppendLine(L.T("【四、打包时会被自动排除的东西】"));
            sb.AppendLine(L.T("    · ZOGOS备份（启动器的配置备份，属于本机数据）"));
            sb.AppendLine(L.T("    · UserData（便携模式的存档/配置/模组列表，属于本机数据）"));
            sb.AppendLine(L.T("    · RimWorld\\Logs、RimWorld\\Crashes（运行日志，属于本机数据）"));
            sb.AppendLine(L.T("    · 正在导出的那个 zip 本身"));
            sb.AppendLine();
            sb.AppendLine(L.T("【五、启动器是怎么认包的】"));
            sb.AppendLine(L.T("    1. 先看识别令牌里的 packId，认得出来就直接用"));
            sb.AppendLine(L.T("    2. 认不出来就按特征打分："));
            sb.AppendLine(L.T("         识别令牌 +100 / 游戏主程序 +40 / Config\\ModsConfig.xml +30"));
            sb.AppendLine(L.T("         / 排序文件 +25 / Mods 目录 +15"));
            sb.AppendLine(L.T("    3. 只接受「同时有游戏主程序，并且有 Config 或 排序文件」的目录"));
            sb.AppendLine(L.T("       所以隔壁单独一个 RimWorld 游戏本体不会被误认成整合包"));
            sb.AppendLine();
            sb.AppendLine(L.T("【六、导入方式】"));
            sb.AppendLine(L.T("    启动器 →【分享】页 →【导入整合包…】→ 选这个 zip"));
            sb.AppendLine(L.T("    启动器会先检查令牌，再把内容解压到你指定的位置，"));
            sb.AppendLine(L.T("    解压完自动绑定成当前整合包，直接就能启动。"));
            sb.AppendLine();
            sb.AppendLine(L.T("============================== 规范结束 =============================="));
            return sb.ToString();
        }
    }
}

namespace Zogos
{
    // =====================================================================
    //  导出整合包
    // =====================================================================
    public class ExportResult
    {
        public string ZipPath = "";
        public int Files;
        public long RawBytes;
        public long ZipBytes;
        public List<string> Warnings = new List<string>();
        public List<string> Errors = new List<string>();
        public bool Success { get { return Errors.Count == 0; } }
    }

    public static class PackExporter
    {
        private static readonly string[] NoCompressExt = new string[] {
            ".zip", ".7z", ".rar", ".png", ".jpg", ".jpeg", ".gif", ".webp",
            ".mp4", ".mkv", ".mp3", ".ogg", ".wav", ".gz", ".xz", ".zst", ".br" };

        private static readonly string[] SkipTopDirs = new string[] {
            L.T("ZOGOS备份"), "UserData", ".git", ".svn", "__pycache__", "$RECYCLE.BIN", "System Volume Information" };

        public static string Human(long bytes)
        {
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("0.00") + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("0.0") + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0.0") + " KB";
            return bytes + " B";
        }

        private static string Rel(string root, string full)
        {
            try
            {
                string r = Path.GetFullPath(root).TrimEnd('\\') + "\\";
                string f = Path.GetFullPath(full);
                if (f.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return f.Substring(r.Length);
            }
            catch { }
            return Path.GetFileName(full);
        }

        private static string FirstSeg(string rel)
        {
            int k = rel.IndexOf('\\');
            return k > 0 ? rel.Substring(0, k) : rel;
        }

        private static bool SkipRel(string rel, bool full)
        {
            if (string.IsNullOrEmpty(rel)) return false;
            string first = FirstSeg(rel);
            for (int i = 0; i < SkipTopDirs.Length; i++)
                if (string.Equals(first, SkipTopDirs[i], StringComparison.OrdinalIgnoreCase)) return true;
            if (!full && string.Equals(first, PackSpec.GameName, StringComparison.OrdinalIgnoreCase)) return true;
            if (rel.StartsWith(PackSpec.GameName + "\\Logs", StringComparison.OrdinalIgnoreCase)) return true;
            if (rel.StartsWith(PackSpec.GameName + "\\Crashes", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static List<string> CollectFiles(PackContext ctx, bool full, string outZip, List<string> warnings)
        {
            List<string> res = new List<string>();
            string root = ctx.Root;
            string outFull = null;
            try { if (!string.IsNullOrEmpty(outZip)) outFull = Path.GetFullPath(outZip); }
            catch { }

            Stack<string> st = new Stack<string>();
            st.Push(root);
            while (st.Count > 0)
            {
                string d = st.Pop();

                string[] subs = null;
                try { subs = Directory.GetDirectories(d); }
                catch (Exception ex) { if (warnings != null) warnings.Add(L.T("读不到目录：") + d + "（" + ex.Message + "）"); }
                if (subs != null)
                {
                    for (int i = 0; i < subs.Length; i++)
                    {
                        string rel = Rel(root, subs[i]);
                        if (SkipRel(rel, full)) continue;
                        st.Push(subs[i]);
                    }
                }

                string[] fs = null;
                try { fs = Directory.GetFiles(d); }
                catch { }
                if (fs == null) continue;
                for (int i = 0; i < fs.Length; i++)
                {
                    string rel = Rel(root, fs[i]);
                    if (SkipRel(rel, full)) continue;
                    if (outFull != null)
                    {
                        string cur = null;
                        try { cur = Path.GetFullPath(fs[i]); } catch { }
                        if (cur != null && string.Equals(cur, outFull, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    res.Add(fs[i]);
                }
            }
            return res;
        }

        private static CompressionLevel LevelFor(string file)
        {
            string ext = "";
            try { ext = Path.GetExtension(file).ToLowerInvariant(); } catch { }
            for (int i = 0; i < NoCompressExt.Length; i++)
                if (ext == NoCompressExt[i]) return CompressionLevel.NoCompression;
            return CompressionLevel.Optimal;
        }

        public static string BuildReadme(PackContext ctx, SortScheme scheme, bool full)
        {
            StringBuilder sb = new StringBuilder();
            string packName = PackSpec.PackNameOf(ctx);
            sb.AppendLine(L.T("================ ZOGOS 整合包 ================"));
            sb.AppendLine(L.T("整合包名  : ") + packName);
            sb.AppendLine(L.T("打包类型  : ") + (full ? L.T("完整包（含游戏本体 + 模组）") : L.T("轻量包（只含配置 + 排序）")));
            sb.AppendLine(L.T("导出时间  : ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(L.T("导出工具  : Astra 创世神协议 v") + Log.Version);
            sb.AppendLine(L.T("游戏      : RimWorld ") + ctx.GameVersion);
            sb.AppendLine(L.T("识别令牌  : ") + (PackMarker.ReadPackId(ctx.Root) == "" ? L.T("(未写入)") : PackMarker.ReadPackId(ctx.Root)));
            sb.AppendLine(L.T("格式规范  : v") + PackSpec.SpecVersion + L.T("（见启动器【分享】页）"));
            bool hasRS = false;
            try { hasRS = Directory.GetFiles(ctx.Root, "*rimsort*.exe").Length > 0; } catch { }
            sb.AppendLine(L.T("模组管理器: ") + (hasRS ? L.T("已包含 RimSort（含 _internal 运行库）") : L.T("未包含（建议补上）")));
            sb.AppendLine();
            sb.AppendLine(L.T("---------------- 排序方案 ----------------"));
            if (scheme != null)
            {
                sb.AppendLine(L.T("默认方案  : ") + scheme.Name);
                sb.AppendLine(L.T("模组数量  : ") + scheme.Mods.Count);
                sb.AppendLine(L.T("游戏版本  : ") + scheme.VersionText);
                if (scheme.HasRjw) sb.AppendLine(L.T("包含内容  : 含 RJW"));
            }
            else sb.AppendLine(L.T("默认方案  : (导出时未选择)"));
            try
            {
                List<SortScheme> all = SortSchemeLoader.Load(ctx);
                sb.AppendLine(L.T("方案总数  : ") + all.Count);
                for (int i = 0; i < all.Count && i < 20; i++)
                    sb.AppendLine("    · " + all[i].Name + "（" + all[i].Mods.Count + L.T(" 个模组）"));
            }
            catch { }
            sb.AppendLine();
            sb.AppendLine(L.T("---------------- 怎么用 ----------------"));
            sb.AppendLine(L.T("1. 解压整个文件夹（不要只解压里面的文件）"));
            sb.AppendLine(L.T("2. 打开 Astra 创世神协议，到【分享】页点【导入整合包…】，"));
            sb.AppendLine(L.T("   选这个压缩包（或者直接解压后用【绑定整合包文件夹…】指过去）"));
            sb.AppendLine(L.T("3. 在【排序】页选一个排序方案，回主页点【启动游戏】"));
            sb.AppendLine();
            if (!full)
            {
                sb.AppendLine(L.T("注意：这是轻量包，不含游戏本体和模组。"));
                sb.AppendLine(L.T("     请确保你已经有同一套整合包，再把本包解压覆盖或单独使用。"));
                sb.AppendLine();
            }
            sb.AppendLine("=============================================");
            return sb.ToString();
        }

        public static ExportResult Export(PackContext ctx, SortScheme scheme, bool full, string outZip, Action<string> log, Action<int, int, string> prog)
        {
            ExportResult r = new ExportResult();
            r.ZipPath = outZip;
            try
            {
                // 1) 先确保令牌存在并刷新（目录只读时，只要有旧令牌就继续导出）
                string err;
                if (!PackMarker.Write(ctx.Root, null, out err))
                {
                    if (PackMarker.Exists(ctx.Root))
                    {
                        r.Warnings.Add(L.T("识别令牌无法刷新（目录只读？），改用已有令牌继续导出：") + err);
                        if (log != null) log(L.T("提示：识别令牌无法刷新，改用已有的继续导出。"));
                    }
                    else
                    {
                        r.Errors.Add(L.T("识别令牌写入失败（目录可能只读）：") + err);
                        return r;
                    }
                }

                string packName = PackSpec.PackNameOf(ctx);

                // 2) 收集文件
                if (log != null) log(L.T("正在统计文件…"));
                List<string> files = CollectFiles(ctx, full, outZip, r.Warnings);
                long total = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    try { total += new FileInfo(files[i]).Length; }
                    catch { }
                }
                r.RawBytes = total;
                if (log != null) log(L.T("待打包 ") + files.Count + L.T(" 个文件，原始大小 ") + Human(total));
                bool hasRimSort = false;
                for (int i = 0; i < files.Count; i++)
                {
                    string fn = "";
                    try { fn = Path.GetFileName(files[i]); } catch { }
                    if (fn.IndexOf("rimsort", StringComparison.OrdinalIgnoreCase) >= 0 && fn.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { hasRimSort = true; break; }
                }
                if (!hasRimSort)
                {
                    r.Warnings.Add(L.T("这个整合包目录里没有找到 RimSort 模组管理器（rimsort*.exe），导出的包会缺少配套工具。"));
                    if (log != null) log(L.T("警告：没找到 RimSort，导出的包会缺配套工具。"));
                }
                if (files.Count == 0) { r.Errors.Add(L.T("没有找到可以打包的文件")); return r; }

                string dir = Path.GetDirectoryName(outZip);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 3) 打包
                using (FileStream fs = new FileStream(outZip, FileMode.Create, FileAccess.Write))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    // 说明文件
                    string readme = BuildReadme(ctx, scheme, full);
                    ZipArchiveEntry re = zip.CreateEntry(packName + "/" + PackSpec.ReadmeName, CompressionLevel.Optimal);
                    using (Stream s = re.Open())
                    using (StreamWriter w = new StreamWriter(s, new UTF8Encoding(false)))
                        w.Write(readme);

                    for (int i = 0; i < files.Count; i++)
                    {
                        string rel = Rel(ctx.Root, files[i]);
                        string entry = packName + "/" + rel.Replace('\\', '/');
                        try
                        {
                            zip.CreateEntryFromFile(files[i], entry, LevelFor(files[i]));
                        }
                        catch (Exception ex)
                        {
                            r.Warnings.Add(L.T("跳过 ") + rel + "：" + ex.Message);
                        }
                        if (prog != null) prog(i + 1, files.Count, rel);
                    }
                }

                r.Files = files.Count + 1;
                try { r.ZipBytes = new FileInfo(outZip).Length; } catch { }
                if (log != null) log(L.T("打包完成：") + outZip + "（" + Human(r.ZipBytes) + "）");
            }
            catch (Exception ex)
            {
                r.Errors.Add(ex.Message);
                Log.Error(L.T("导出整合包失败"), ex);
            }
            return r;
        }
    }
}

namespace Zogos
{
    // =====================================================================
    //  导入整合包（读取 + 解压）
    // =====================================================================
    public class ZipPackInfo
    {
        public string ZipPath = "";
        public bool IsZogosPack;
        public string PackId = "";
        public string RootEntry = "";
        public int Entries;
        public long RawBytes;
        public long ZipBytes;
        public bool HasConfig;
        public bool HasGameExe;
        public bool HasSortDir;
        public bool HasMods;
        public int ModFolders;
        public bool HasRimSort;
        public List<string> Warnings = new List<string>();

        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("文件        : ") + Path.GetFileName(ZipPath));
            sb.AppendLine(L.T("是不是 ZOGOS 包 : ") + (IsZogosPack ? L.T("是") : L.T("否（没找到识别令牌）")));
            if (IsZogosPack)
            {
                sb.AppendLine(L.T("识别令牌    : ") + (PackId.Length > 0 ? PackId : L.T("(无编号)")));
                sb.AppendLine(L.T("包内根目录  : ") + (RootEntry.Length > 0 ? RootEntry : L.T("(压缩包根目录)")));
            }
            sb.AppendLine(L.T("条目数      : ") + Entries);
            sb.AppendLine(L.T("解压后大小  : ") + PackExporter.Human(RawBytes));
            sb.AppendLine(L.T("压缩包大小  : ") + PackExporter.Human(ZipBytes));
            sb.AppendLine();
            sb.AppendLine(L.T("内容检查："));
            sb.AppendLine(L.T("    Config 文件夹      : ") + (HasConfig ? L.T("有") : L.T("没有")));
            sb.AppendLine(L.T("    游戏主程序          : ") + (HasGameExe ? L.T("有") : L.T("没有（轻量包属正常）")));
            sb.AppendLine(L.T("    排序文件 文件夹     : ") + (HasSortDir ? L.T("有") : L.T("没有")));
            sb.AppendLine(L.T("    模组文件夹          : ") + (HasMods ? (L.T("有，约 ") + ModFolders + L.T(" 个")) : L.T("没有（轻量包属正常）")));
            sb.AppendLine(L.T("    模组管理器 RimSort  : ") + (HasRimSort ? L.T("有") : L.T("没有（规范化要求带上）")));
            for (int i = 0; i < Warnings.Count; i++) sb.AppendLine(L.T("    注意：") + Warnings[i]);
            return sb.ToString();
        }
    }

    public static class PackImporter
    {
        public static ZipPackInfo Inspect(string zipPath)
        {
            ZipPackInfo info = new ZipPackInfo();
            info.ZipPath = zipPath;
            try
            {
                FileInfo fi = new FileInfo(zipPath);
                info.ZipBytes = fi.Length;

                using (FileStream fs = File.OpenRead(zipPath))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    info.Entries = zip.Entries.Count;
                    Dictionary<string, int> modDirs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    for (int i = 0; i < zip.Entries.Count; i++)
                    {
                        ZipArchiveEntry e = zip.Entries[i];
                        info.RawBytes += e.Length;
                        string n = e.FullName.Replace('\\', '/');

                        if (n.EndsWith("/" + PackSpec.MarkerJson, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(n, PackSpec.MarkerJson, StringComparison.OrdinalIgnoreCase))
                        {
                            info.IsZogosPack = true;
                            info.RootEntry = n.Substring(0, n.Length - PackSpec.MarkerJson.Length).TrimEnd('/');
                            try
                            {
                                using (Stream s = e.Open())
                                using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                                {
                                    string txt = sr.ReadToEnd();
                                    System.Text.RegularExpressions.Match m =
                                        System.Text.RegularExpressions.Regex.Match(txt, "\"packId\"\\s*:\\s*\"([^\"]*)\"");
                                    if (m.Success) info.PackId = m.Groups[1].Value;
                                }
                            }
                            catch { }
                        }
                        else if (n.EndsWith("/" + PackSpec.MarkerId, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(n, PackSpec.MarkerId, StringComparison.OrdinalIgnoreCase))
                        {
                            info.IsZogosPack = true;
                            if (info.RootEntry.Length == 0)
                                info.RootEntry = n.Substring(0, n.Length - PackSpec.MarkerId.Length).TrimEnd('/');
                        }

                        string low = n.ToLowerInvariant();
                        if (low.EndsWith("/config/modsconfig.xml")) info.HasConfig = true;
                        if (low.EndsWith("/rimworld/rimworldwin64.exe")) info.HasGameExe = true;
                        if (low.Contains(L.T("排序文件/"))) info.HasSortDir = true;
                        if (low.Contains("rimsort") && low.EndsWith(".exe")) info.HasRimSort = true;
                        if (low.EndsWith("/rimworld/mods/rimworldwin64.exe")) info.HasGameExe = true;

                        int mi = low.IndexOf("/rimworld/mods/");
                        if (mi >= 0)
                        {
                            info.HasMods = true;
                            string rest = n.Substring(mi + "/rimworld/mods/".Length);
                            int sl = rest.IndexOf('/');
                            if (sl > 0)
                            {
                                string seg = rest.Substring(0, sl);
                                if (!modDirs.ContainsKey(seg)) modDirs[seg] = 0;
                            }
                        }
                    }
                    info.ModFolders = modDirs.Count;
                    if (info.RootEntry.Length == 0 && info.IsZogosPack) info.Warnings.Add(L.T("压缩包把整合包文件放在了最外层（没有外层文件夹），导入时会自动套一层。"));
                }
            }
            catch (Exception ex)
            {
                info.Warnings.Add(L.T("读取压缩包失败：") + ex.Message);
            }
            return info;
        }

        /// <summary>安全解压（防 ../ 逃逸）。返回解压的文件数。</summary>
        public static int Extract(string zipPath, string destDir, Action<int, int, string> prog, List<string> errors)
        {
            int n = 0;
            string destFull = Path.GetFullPath(destDir).TrimEnd('\\') + "\\";
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

            using (FileStream fs = File.OpenRead(zipPath))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                int count = zip.Entries.Count;
                for (int i = 0; i < count; i++)
                {
                    ZipArchiveEntry e = zip.Entries[i];
                    string rel = e.FullName.Replace('\\', '/');
                    if (prog != null) prog(i + 1, count, e.Name);

                    if (rel.EndsWith("/") || e.Name.Length == 0) continue;

                    string target;
                    try { target = Path.GetFullPath(Path.Combine(destDir, rel.Replace('/', '\\'))); }
                    catch (Exception ex) { if (errors != null) errors.Add(L.T("路径无效：") + rel + "（" + ex.Message + "）"); continue; }

                    if (!target.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                    {
                        if (errors != null) errors.Add(L.T("跳过不安全的路径：") + rel);
                        continue;
                    }

                    try
                    {
                        string d = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                        e.ExtractToFile(target, true);
                        n++;
                    }
                    catch (Exception ex)
                    {
                        if (errors != null) errors.Add(L.T("解压失败 ") + rel + "：" + ex.Message);
                    }
                }
            }
            return n;
        }

        /// <summary>解压完成后，在目标目录里找整合包根目录（含令牌或 Config\ModsConfig.xml）。</summary>
        public static string FindPackRoot(string destDir)
        {
            try
            {
                if (IsPackRoot(destDir)) return destDir;
                DirectoryInfo[] subs = new DirectoryInfo(destDir).GetDirectories();
                for (int i = 0; i < subs.Length; i++)
                    if (IsPackRoot(subs[i].FullName)) return subs[i].FullName;

                for (int i = 0; i < subs.Length; i++)
                {
                    try
                    {
                        DirectoryInfo[] sub2 = subs[i].GetDirectories();
                        for (int j = 0; j < sub2.Length; j++)
                            if (IsPackRoot(sub2[j].FullName)) return sub2[j].FullName;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        private static bool IsPackRoot(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                if (PackMarker.Exists(dir)) return true;
                if (File.Exists(Path.Combine(dir, PackSpec.ConfigName + "\\ModsConfig.xml"))) return true;
            }
            catch { }
            return false;
        }
    }
}
