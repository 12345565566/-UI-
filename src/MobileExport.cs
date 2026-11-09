using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Zogos
{
    /// <summary>导出选项。</summary>
    public class MobileExportOptions
    {
        public string SourcePath = "";      // 整合包目录 或 整合包 zip
        public string OutputZip = "";       // 输出 zip
        public bool IncludeGame = true;     // 游戏本体文件
        public bool IncludeMods = true;     // Mods 目录
        public bool IncludeConfig = true;   // Config（含 ModsConfig.xml）
        public bool WrapInFolder = true;    // 是否外包一层顶层文件夹
        public string TopFolder = "";       // 顶层文件夹名（空则自动）

        /// <summary>是否自动注入中文字体 mod（RimDroid 上中文必需）。</summary>
        public bool IncludeCjk = true;
        /// <summary>RimDroidCJK 字体 mod 的 zip 路径。</summary>
        public string CjkModZip = "";
    }

    public class MobileExportResult
    {
        public bool Ok;
        public string Err;
        public string Note;
        public int FileCount;
        public long Bytes;
        public int ModCount;
        public double Seconds;
        public string TopFolderUsed;
        public bool CjkInjected;
    }

    /// <summary>
    /// 端游整合包 → 手机版整合包。
    ///
    /// 目标格式参照 RimDroid 官方配发的「RimWorld v1.7.4976b.zip」：
    ///   <顶层文件夹>/
    ///       Assembly-CSharp.dll / Assets.zip / Brutal.*.dll ...
    ///       Mods/            ← 整合包的模组
    ///       Config/ModsConfig.xml  ← 模组排序
    /// RimDroid 的 Import ZIP 是整包解压成一个实例，所以这两个目录带进去就自带 mod 与排序。
    /// </summary>
    public static class MobileExporter
    {
        // 不导出：存档、日志、备份、版本控制
        private static readonly string[] SkipDirs = new string[] {
            "saves", "logs", "log", L.T("zogos备份"), "userdata", ".git", ".svn", ".vs",
            "__pycache__", "$recycle.bin", "system volume information", "crashdumps",
            "playerdata", "backup", "backups"
        };
        private static readonly string[] SkipExt = new string[] { ".log", ".dmp", ".tmp", ".bak" };

        private static bool SkipDirName(string name)
        {
            string n = name.ToLowerInvariant();
            for (int i = 0; i < SkipDirs.Length; i++) if (n == SkipDirs[i]) return true;
            return false;
        }

        private static bool SkipFileName(string name)
        {
            string n = name.ToLowerInvariant();
            if (n == "player.log" || n == "output_log.txt") return true;
            // Prefs 存的是 PC 的分辨率/窗口/键位，带到手机上会出画面问题，必须排除
            if (n == "prefs.xml" || n == "keyprefs.xml") return true;
            string ext = Path.GetExtension(n);
            for (int i = 0; i < SkipExt.Length; i++) if (ext == SkipExt[i]) return true;
            return false;
        }

        /// <summary>分析整合包：返回 mod 数量、是否有排序文件等。</summary>
        public static void Analyze(string path, out int modCount, out string version, out string err)
        {
            modCount = 0; version = null; err = null;
            try
            {
                string modsDir = null;
                if (Directory.Exists(path))
                {
                    modsDir = Path.Combine(path, "Mods");
                    string verFile = Path.Combine(path, "Version.txt");
                    if (File.Exists(verFile)) version = File.ReadAllText(verFile).Trim();
                }
                else if (File.Exists(path))
                {
                    using (ZipArchive z = ZipFile.OpenRead(path))
                    {
                        foreach (ZipArchiveEntry e in z.Entries)
                        {
                            string[] seg = e.FullName.Split('/');
                            for (int i = 0; i < seg.Length - 1; i++)
                            {
                                if (string.Equals(seg[i], "Mods", StringComparison.OrdinalIgnoreCase) && i + 1 < seg.Length)
                                {
                                    modCount++;
                                    break;
                                }
                            }
                            if (e.FullName.EndsWith("/Mods/", StringComparison.OrdinalIgnoreCase))
                                modCount++;
                        }
                        // 去重：按第二级目录名统计
                        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (ZipArchiveEntry e in z.Entries)
                        {
                            string[] seg = e.FullName.Split('/');
                            for (int i = 0; i < seg.Length - 1; i++)
                            {
                                if (string.Equals(seg[i], "Mods", StringComparison.OrdinalIgnoreCase) && i + 1 < seg.Length && seg[i + 1].Length > 0)
                                {
                                    names.Add(seg[i + 1]);
                                    break;
                                }
                            }
                        }
                        modCount = names.Count;
                    }
                }
                if (Directory.Exists(path) && modsDir != null && Directory.Exists(modsDir))
                    modCount = Directory.GetDirectories(modsDir).Length;
            }
            catch (Exception ex) { err = ex.Message; }
        }

        /// <summary>执行导出。progress(说明, 已完成文件数)。cancelled 返回 true 则中断。</summary>
        public static MobileExportResult Run(MobileExportOptions o, Action<string, int> progress, Func<bool> cancelled)
        {
            MobileExportResult r = new MobileExportResult();
            DateTime t0 = DateTime.Now;
            try
            {
                if (string.IsNullOrEmpty(o.SourcePath)) { r.Err = L.T("没有选择源整合包"); return r; }
                if (string.IsNullOrEmpty(o.OutputZip)) { r.Err = L.T("没有选择输出文件"); return r; }
                string srcDir = null;
                ZipArchive srcZip = null;
                if (Directory.Exists(o.SourcePath)) srcDir = o.SourcePath;
                else if (File.Exists(o.SourcePath)) srcZip = ZipFile.OpenRead(o.SourcePath);
                else { r.Err = L.T("源路径不存在"); return r; }

                // 顶层文件夹名
                string top = o.TopFolder;
                if (string.IsNullOrEmpty(top))
                {
                    if (srcDir != null) top = new DirectoryInfo(srcDir).Name;
                    else top = Path.GetFileNameWithoutExtension(o.SourcePath);
                }
                top = Safe(top);
                r.TopFolderUsed = o.WrapInFolder ? top : L.T("(无)");

                // 源 zip 的公共前缀（去掉原本的顶层目录，避免双层）
                string prefix = "";
                if (srcZip != null && o.WrapInFolder && srcZip.Entries.Count > 0)
                {
                    string first = srcZip.Entries[0].FullName.Replace('\\', '/');
                    int slash = first.IndexOf('/');
                    if (slash > 0) prefix = first.Substring(0, slash + 1);
                }

                string outDir = Path.GetDirectoryName(o.OutputZip);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                if (File.Exists(o.OutputZip)) File.Delete(o.OutputZip);

                int n = 0;
                long bytes = 0;
                int mods = 0;
                HashSet<string> modNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (FileStream fs = new FileStream(o.OutputZip, FileMode.Create))
                using (ZipArchive dst = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    if (srcDir != null)
                    {
                        List<string> files = new List<string>();
                        Collect(srcDir, srcDir, o, files, modNames, cancelled);
                        mods = modNames.Count;
                        for (int i = 0; i < files.Count; i++)
                        {
                            if (cancelled != null && cancelled()) { r.Err = L.T("已取消"); return r; }
                            string full = files[i];
                            string rel = full.Substring(srcDir.Length).TrimStart('\\', '/').Replace('\\', '/');
                            string entryName = o.WrapInFolder ? (top + "/" + rel) : rel;
                            try
                            {
                                // 注意：CreateEntryFromFile 返回的 entry 在写入模式下读 Length 会抛异常，
                                // 所以文件大小必须另取，否则计数会是 0。
                                dst.CreateEntryFromFile(full, entryName, CompressionLevel.Optimal);
                                try { bytes += new FileInfo(full).Length; } catch { }
                                n++;
                            }
                            catch { }
                            if ((n % 40) == 0 && progress != null) progress(rel, n);
                        }
                    }
                    else
                    {
                        foreach (ZipArchiveEntry se in srcZip.Entries)
                        {
                            if (cancelled != null && cancelled()) { r.Err = L.T("已取消"); return r; }
                            // Windows 打的 zip 用反斜杠做分隔符，必须统一成正斜杠，
                            // 否则过滤规则失效（Saves/Prefs 会被打包进来），安卓侧也解不开。
                            string name = se.FullName.Replace('\\', '/');
                            if (name.EndsWith("/")) continue;
                            string rel = name;
                            if (prefix.Length > 0 && rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                rel = rel.Substring(prefix.Length);
                            if (!Want(rel, o)) continue;
                            CountMod(rel, modNames);
                            string entryName = o.WrapInFolder ? (top + "/" + rel) : rel;
                            try
                            {
                                ZipArchiveEntry de = dst.CreateEntry(entryName, CompressionLevel.Optimal);
                                using (Stream si = se.Open())
                                using (Stream so = de.Open())
                                    si.CopyTo(so, 81920);
                                bytes += se.Length;
                                n++;
                            }
                            catch { }
                            if ((n % 40) == 0 && progress != null) progress(rel, n);
                        }
                        mods = modNames.Count;
                    }
                }
                if (srcZip != null) srcZip.Dispose();

                // 注入中文字体 mod（RimDroid 上没它中文全是方框）
                if (o.IncludeCjk)
                {
                    r.CjkInjected = InjectCjk(o.OutputZip, o.CjkModZip, o.WrapInFolder ? top : null, progress);
                }

                r.Ok = true;
                r.FileCount = n;
                r.Bytes = bytes;
                r.ModCount = mods;
                r.Seconds = (DateTime.Now - t0).TotalSeconds;
                r.Note = BuildNote(r);
                return r;
            }
            catch (Exception ex)
            {
                r.Err = ex.Message;
                return r;
            }
        }

        // ===== 中文字体 mod 注入 =====

        /// <summary>
        /// 把 RimDroidCJK 字体 mod 塞进导出包，并把 rimdroid.cjkfont 写进 ModsConfig 的启用列表。
        /// 不做这一步，手机上中文全是方框（RimDroid 不会自己找系统字体）。
        /// </summary>
        private static bool InjectCjk(string zipPath, string cjkZip, string topFolder, Action<string, int> progress)
        {
            if (string.IsNullOrEmpty(cjkZip) || !File.Exists(cjkZip)) return false;
            try
            {
                string prefix = string.IsNullOrEmpty(topFolder) ? "" : topFolder + "/";
                using (ZipArchive dst = ZipFile.Open(zipPath, ZipArchiveMode.Update))
                {
                    ZipArchiveEntry mc = null;
                    for (int i = 0; i < dst.Entries.Count; i++)
                    {
                        if (dst.Entries[i].FullName.EndsWith("Config/ModsConfig.xml", StringComparison.OrdinalIgnoreCase))
                        {
                            mc = dst.Entries[i];
                            break;
                        }
                    }

                    string cfg;
                    string cfgName;
                    if (mc != null)
                    {
                        cfgName = mc.FullName;
                        using (Stream s = mc.Open())
                        using (StreamReader rd = new StreamReader(s, Encoding.UTF8))
                            cfg = rd.ReadToEnd();
                        mc.Delete();
                    }
                    else
                    {
                        cfgName = prefix + "Config/ModsConfig.xml";
                        cfg = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ModsConfigData>\r\n  <activeMods>\r\n  </activeMods>\r\n</ModsConfigData>";
                    }

                    cfg = EnableMod(cfg, "rimdroid.cjkfont");
                    WriteTextEntry(dst, cfgName, cfg);

                    int n = 0;
                    using (ZipArchive src = ZipFile.OpenRead(cjkZip))
                    {
                        foreach (ZipArchiveEntry se in src.Entries)
                        {
                            if (se.FullName.EndsWith("/")) continue;
                            string en = prefix + "Mods/" + se.FullName.Replace('\\', '/');
                            ZipArchiveEntry de = dst.CreateEntry(en, CompressionLevel.Optimal);
                            using (Stream si = se.Open())
                            using (Stream so = de.Open())
                                si.CopyTo(so, 81920);
                            n++;
                        }
                    }
                    if (progress != null) progress(L.T("已注入中文字体 mod（") + n + L.T(" 个文件）"), n);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>把 packageId 插到 activeMods 的最前面（已经存在就不动）。</summary>
        private static string EnableMod(string cfg, string packageId)
        {
            if (string.IsNullOrEmpty(cfg)) return cfg;
            if (cfg.IndexOf(packageId, StringComparison.OrdinalIgnoreCase) >= 0) return cfg;
            int idx = cfg.IndexOf("<activeMods>", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return cfg;
            int at = idx + "<activeMods>".Length;
            string line = "\r\n    <li>" + packageId + "</li>";
            return cfg.Substring(0, at) + line + cfg.Substring(at);
        }

        private static void WriteTextEntry(ZipArchive zip, string name, string text)
        {
            ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream s = e.Open())
            using (StreamWriter w = new StreamWriter(s, new UTF8Encoding(false)))
                w.Write(text);
        }

        private static string BuildNote(MobileExportResult r)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("手机版整合包导入说明"));
            sb.AppendLine("====================");
            sb.AppendLine("");
            sb.AppendLine(L.T("这个 zip 是给 Android 上的 RimDroid 用的，内含 ") + r.ModCount + L.T(" 个模组和模组排序。"));
            sb.AppendLine("");
            sb.AppendLine(L.T("导入步骤："));
            sb.AppendLine(L.T("  1. 把本 zip 拷到手机（数据线 / 微信文件传输 / 网盘都行）"));
            sb.AppendLine(L.T("  2. 手机打开 RimDroid"));
            sb.AppendLine(L.T("  3. 点 Import ZIP（导入 ZIP），选这个文件"));
            sb.AppendLine(L.T("  4. 等它解压完，会生成一个实例，直接点开始"));
            sb.AppendLine(L.T("  5. 中文界面还要再装 RimDroidCJK 模组（单独一个 zip，导入方式一样）"));
            sb.AppendLine("");
            sb.AppendLine(L.T("出问题怎么办："));
            sb.AppendLine(L.T("  · 黑屏 / 闪退：RimDroid 设置 → 高级 → 打开「兼容模式」"));
            sb.AppendLine(L.T("  · 卡顿：装 RocketMan 之类的性能模组；模组越多越吃 CPU"));
            sb.AppendLine(L.T("  · 模组没生效：RimDroid 的 Mods 界面里确认已勾选，并用它自带的排序功能重排一次"));
            sb.AppendLine("");
            sb.AppendLine(L.T("注意：模组数量超过 50 在手机上属于未测试区间，卡顿是正常的。"));
            return sb.ToString();
        }

        private static void CountMod(string rel, HashSet<string> names)
        {
            string[] seg = rel.Split('/');
            for (int i = 0; i < seg.Length - 1; i++)
            {
                if (string.Equals(seg[i], "Mods", StringComparison.OrdinalIgnoreCase) && i + 1 < seg.Length && seg[i + 1].Length > 0)
                {
                    names.Add(seg[i + 1]);
                    return;
                }
            }
        }

        /// <summary>这个相对路径要不要收进手机包。</summary>
        private static bool Want(string rel, MobileExportOptions o)
        {
            string[] seg = rel.Split('/');
            // 先看目录段
            for (int i = 0; i < seg.Length - 1; i++)
            {
                if (SkipDirName(seg[i])) return false;
                if (!o.IncludeMods && string.Equals(seg[i], "Mods", StringComparison.OrdinalIgnoreCase)) return false;
                if (!o.IncludeConfig && string.Equals(seg[i], "Config", StringComparison.OrdinalIgnoreCase)) return false;
            }
            string file = seg[seg.Length - 1];
            if (SkipFileName(file)) return false;
            if (!o.IncludeMods && seg.Length > 1 && string.Equals(seg[0], "Mods", StringComparison.OrdinalIgnoreCase)) return false;
            if (!o.IncludeConfig && string.Equals(file, "ModsConfig.xml", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>递归收集文件。</summary>
        private static void Collect(string root, string dir, MobileExportOptions o, List<string> files,
            HashSet<string> modNames, Func<bool> cancelled)
        {
            if (cancelled != null && cancelled()) return;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { subs = new string[0]; }
            for (int i = 0; i < subs.Length; i++)
            {
                string nm = Path.GetFileName(subs[i]);
                if (SkipDirName(nm)) continue;
                string rel = subs[i].Substring(root.Length).TrimStart('\\', '/');
                if (!o.IncludeMods && rel.Equals("Mods", StringComparison.OrdinalIgnoreCase)) continue;
                if (!o.IncludeConfig && rel.Equals("Config", StringComparison.OrdinalIgnoreCase)) continue;
                // 统计模组
                string[] parts = rel.Replace('\\', '/').Split('/');
                if (parts.Length == 2 && string.Equals(parts[0], "Mods", StringComparison.OrdinalIgnoreCase))
                    modNames.Add(parts[1]);
                Collect(root, subs[i], o, files, modNames, cancelled);
            }
            string[] fsr;
            try { fsr = Directory.GetFiles(dir); } catch { fsr = new string[0]; }
            for (int i = 0; i < fsr.Length; i++)
            {
                if (SkipFileName(Path.GetFileName(fsr[i]))) continue;
                files.Add(fsr[i]);
            }
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "RimWorld";
            StringBuilder sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < s.Length && sb.Length < 60; i++)
            {
                char c = s[i];
                bool ok = true;
                for (int k = 0; k < bad.Length; k++) if (c == bad[k]) { ok = false; break; }
                sb.Append(ok ? c : '_');
            }
            return sb.Length == 0 ? "RimWorld" : sb.ToString();
        }
    }
}
