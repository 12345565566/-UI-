using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

namespace Zogos
{
    // =====================================================================
    //  创意工坊 ↔ 本地 Mods：目录扫描 / 同步 / 按排序补齐缺失
    //
    //  设计要点（对应需求）：
    //   1) 工坊下载完 → 自动落到本地 Mods（不是只躺在 steamapps\workshop）
    //   2) 下载后端可切：Steam 客户端协议 / SteamCMD（后者不需要开梯子、不需要 Steam 登录）
    //   3) 读排序（ModsConfig.xml 的 activeMods 顺序）→ 找出本地没有的 → 按 id 补下载
    // =====================================================================

    /// <summary>本地一个模组条目的元数据。</summary>
    public class ModEntry
    {
        public string PublishedId = "";
        public string PackageId = "";
        public string Name = "";
        public string Dir = "";
        public bool FromWorkshop;
    }

    /// <summary>同步计划中的一步。</summary>
    public class SyncAction
    {
        public string PublishedId = "";
        public string PackageId = "";
        public string Name = "";
        public string SourceDir = "";
        public string TargetDir = "";
        /// <summary>复制 / 覆盖 / 跳过</summary>
        public string Action = "";
        public string Reason = "";
    }

    public class SyncReport
    {
        public List<SyncAction> Actions = new List<SyncAction>();
        public List<string> Errors = new List<string>();
        public int Copied;
        public int Replaced;
        public int Skipped;
        public int Failed;

        public string Summary()
        {
            return L.T("复制 ") + Copied + L.T(" · 覆盖 ") + Replaced + L.T(" · 跳过 ") + Skipped + L.T(" · 失败 ") + Failed;
        }
    }

    public static class ModCatalog
    {
        private static readonly string[] VersionDirs = new string[] { "1.7", "1.6", "1.5", "1.4", "1.3", "Common", "Core" };

        /// <summary>扫描一个「装着很多模组」的根目录（Mods 或工坊 content 目录）。</summary>
        public static List<ModEntry> ScanRoot(string root, bool fromWorkshop)
        {
            List<ModEntry> list = new List<ModEntry>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return list;

            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { return list; }

            for (int i = 0; i < dirs.Length; i++)
            {
                string modDir = FindModRoot(dirs[i]);
                if (modDir == null) continue;
                ModEntry e = Read(modDir, fromWorkshop);
                if (e == null) continue;
                list.Add(e);
            }
            return list;
        }

        /// <summary>给定一个候选目录，找出真正含 About\About.xml 的那一层。</summary>
        public static string FindModRoot(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            if (File.Exists(Path.Combine(Path.Combine(dir, "About"), "About.xml")))
                return dir;

            // 有些工坊条目里套了一层版本目录，或者干脆只有 1.7\About
            for (int v = 0; v < VersionDirs.Length; v++)
            {
                string sub = Path.Combine(dir, VersionDirs[v]);
                if (File.Exists(Path.Combine(Path.Combine(sub, "About"), "About.xml")))
                    return sub;
            }
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { return null; }
            for (int i = 0; i < subs.Length && i < 8; i++)
            {
                if (File.Exists(Path.Combine(Path.Combine(subs[i], "About"), "About.xml")))
                    return subs[i];
            }
            return null;
        }

        public static ModEntry Read(string modDir, bool fromWorkshop)
        {
            try
            {
                string aboutPath = Path.Combine(Path.Combine(modDir, "About"), "About.xml");
                string pkg = ReadPackageId(aboutPath);
                if (string.IsNullOrEmpty(pkg)) return null;

                ModEntry e = new ModEntry();
                e.PackageId = pkg;
                e.Name = ReadModName(aboutPath);
                e.Dir = modDir;
                e.FromWorkshop = fromWorkshop;
                e.PublishedId = ReadPublishedId(modDir);
                return e;
            }
            catch { return null; }
        }

        /// <summary>
        /// 读 packageId。先按正规 XML 解析；不合法就正则兜底
        /// —— 实测工坊里有约 20% 的 About.xml 因为描述里有裸 &amp; 之类的东西导致整份 XML 解析失败，
        ///    但 packageId 元素本身是好的，不能因此把这个模组漏掉。
        /// </summary>
        public static string ReadPackageId(string aboutPath)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(aboutPath);
                XmlElement root = doc.DocumentElement;
                if (root != null)
                {
                    XmlNode n = root.SelectSingleNode("packageId");
                    if (n != null && n.InnerText.Trim().Length > 0) return n.InnerText.Trim();
                }
            }
            catch { }
            return RegexField(aboutPath, "packageId");
        }

        public static string ReadModName(string aboutPath)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(aboutPath);
                XmlElement root = doc.DocumentElement;
                if (root != null)
                {
                    XmlNode n = root.SelectSingleNode("name");
                    if (n != null && n.InnerText.Trim().Length > 0) return n.InnerText.Trim();
                }
            }
            catch { }
            return RegexField(aboutPath, "name");
        }

        /// <summary>XML 解析失败时的兜底：直接从文本里抠一个 <tag>…</tag>。</summary>
        private static string RegexField(string aboutPath, string tag)
        {
            try
            {
                string raw = File.ReadAllText(aboutPath, Encoding.UTF8);
                Match m = Regex.Match(raw, "<" + tag + ">\\s*([^<]+?)\\s*</" + tag + ">", RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            catch { }
            return "";
        }

        /// <summary>PublishedFileId.txt 优先；没有就看目录名是不是纯数字（工坊目录就是 id）。</summary>
        public static string ReadPublishedId(string modDir)
        {
            try
            {
                string f = Path.Combine(Path.Combine(modDir, "About"), "PublishedFileId.txt");
                if (File.Exists(f))
                {
                    string s = File.ReadAllText(f, Encoding.UTF8).Trim();
                    if (s.Length > 0 && IsDigits(s)) return s;
                }
                string leaf = Path.GetFileName(modDir.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(leaf) && IsDigits(leaf)) return leaf;
            }
            catch { }
            return "";
        }

        public static bool IsDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }
    }

    // =====================================================================
    //  本地 packageId ↔ publishedFileId 索引（可持久化，越用越全）
    // =====================================================================
    public static class ModIdIndex
    {
        private static Dictionary<string, string> _pkgToPub = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _pubToPkg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        public static int Count { get { lock (_lock) return _pkgToPub.Count; } }

        public static void Add(ModEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.PackageId) || string.IsNullOrEmpty(e.PublishedId)) return;
            lock (_lock)
            {
                _pkgToPub[e.PackageId] = e.PublishedId;
                _pubToPkg[e.PublishedId] = e.PackageId;
                if (!string.IsNullOrEmpty(e.Name)) _names[e.PackageId] = e.Name;
            }
        }

        public static void AddRange(List<ModEntry> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++) Add(list[i]);
        }

        public static string PublishedOf(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return "";
            lock (_lock)
            {
                string v;
                return _pkgToPub.TryGetValue(packageId, out v) ? v : "";
            }
        }

        public static string NameOf(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return "";
            lock (_lock)
            {
                string v;
                return _names.TryGetValue(packageId, out v) ? v : "";
            }
        }

        /// <summary>落盘：一行一条  packageId \t publishedId \t name</summary>
        public static void Save(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                lock (_lock)
                {
                    foreach (KeyValuePair<string, string> kv in _pkgToPub)
                    {
                        string nm = "";
                        _names.TryGetValue(kv.Key, out nm);
                        sb.AppendLine(kv.Key + "\t" + kv.Value + "\t" + (nm == null ? "" : nm.Replace('\t', ' ')));
                    }
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Warn(L.T("保存模组 id 索引失败：") + ex.Message); }
        }

        public static void Load(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                lock (_lock)
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string ln = lines[i];
                        if (string.IsNullOrEmpty(ln)) continue;
                        string[] p = ln.Split('\t');
                        if (p.Length < 2) continue;
                        string pkg = p[0].Trim();
                        string pub = p[1].Trim();
                        if (pkg.Length == 0 || pub.Length == 0) continue;
                        _pkgToPub[pkg] = pub;
                        _pubToPkg[pub] = pkg;
                        if (p.Length >= 3 && p[2].Trim().Length > 0) _names[pkg] = p[2].Trim();
                    }
                }
            }
            catch (Exception ex) { Log.Warn(L.T("读取模组 id 索引失败：") + ex.Message); }
        }
    }

    // =====================================================================
    //  工坊 → 本地 Mods 同步
    // =====================================================================
    public static class WorkshopSync
    {
        public static string FindWorkshopContentDir()
        {
            return WorkshopClient.FindWorkshopContentDir();
        }

        /// <summary>对比工坊目录和本地 Mods，生成同步计划（不落盘）。</summary>
        public static SyncReport Plan(string workshopDir, string modsDir, bool replaceExisting)
        {
            SyncReport rep = new SyncReport();
            if (string.IsNullOrEmpty(workshopDir) || !Directory.Exists(workshopDir))
            {
                rep.Errors.Add(L.T("没有找到 Steam 创意工坊下载目录（steamapps\\workshop\\content\\294100）。"));
                return rep;
            }
            if (string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir))
            {
                rep.Errors.Add(L.T("没有找到本地 Mods 目录：") + modsDir);
                return rep;
            }

            List<ModEntry> wsp = ModCatalog.ScanRoot(workshopDir, true);
            List<ModEntry> local = ModCatalog.ScanRoot(modsDir, false);
            ModIdIndex.AddRange(wsp);
            ModIdIndex.AddRange(local);

            // 本地已有的 packageId -> 条目
            Dictionary<string, ModEntry> localByPkg = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ModEntry> localByPub = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < local.Count; i++)
            {
                if (!localByPkg.ContainsKey(local[i].PackageId)) localByPkg[local[i].PackageId] = local[i];
                if (!string.IsNullOrEmpty(local[i].PublishedId) && !localByPub.ContainsKey(local[i].PublishedId))
                    localByPub[local[i].PublishedId] = local[i];
            }

            for (int i = 0; i < wsp.Count; i++)
            {
                ModEntry w = wsp[i];
                SyncAction a = new SyncAction();
                a.PublishedId = w.PublishedId;
                a.PackageId = w.PackageId;
                a.Name = w.Name;
                a.SourceDir = w.Dir;

                bool havePkg = localByPkg.ContainsKey(w.PackageId);
                bool havePub = !string.IsNullOrEmpty(w.PublishedId) && localByPub.ContainsKey(w.PublishedId);

                if (!havePkg && !havePub)
                {
                    a.Action = L.T("复制");
                    a.TargetDir = Path.Combine(modsDir, LeafName(w.Dir));
                    rep.Actions.Add(a);
                }
                else if (replaceExisting)
                {
                    a.Action = L.T("覆盖");
                    a.TargetDir = havePub ? localByPub[w.PublishedId].Dir : localByPkg[w.PackageId].Dir;
                    a.Reason = havePub ? L.T("本地已有同一 PublishedFileId，按源覆盖") : L.T("本地已有同一 packageId（folder 名不同），按源覆盖");
                    rep.Actions.Add(a);
                }
                else
                {
                    a.Action = L.T("跳过");
                    a.Reason = L.T("本地已有");
                    a.TargetDir = havePub ? localByPub[w.PublishedId].Dir : localByPkg[w.PackageId].Dir;
                    rep.Actions.Add(a);
                }
            }

            if (wsp.Count == 0) rep.Errors.Add(L.T("工坊目录里没有解析到任何带 About.xml 的模组。"));
            return rep;
        }

        /// <summary>执行同步计划。</summary>
        public static void Apply(SyncReport rep, string modsDir)
        {
            if (rep == null) return;
            for (int i = 0; i < rep.Actions.Count; i++)
            {
                SyncAction a = rep.Actions[i];
                if (a.Action == L.T("跳过")) { rep.Skipped++; continue; }
                try
                {
                    if (a.Action == L.T("覆盖") && Directory.Exists(a.TargetDir))
                    {
                        FileOps.ClearDir(a.TargetDir, rep.Errors);
                    }
                    if (!Directory.Exists(a.TargetDir)) Directory.CreateDirectory(a.TargetDir);
                    FileOps.CopyTree(a.SourceDir, a.TargetDir, rep.Errors, false);
                    // 保证 About\PublishedFileId.txt 存在，方便下次用 id 匹配
                    if (!string.IsNullOrEmpty(a.PublishedId))
                    {
                        string pf = Path.Combine(Path.Combine(a.TargetDir, "About"), "PublishedFileId.txt");
                        try
                        {
                            if (!File.Exists(pf))
                            {
                                string d = Path.GetDirectoryName(pf);
                                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                                File.WriteAllText(pf, a.PublishedId, new UTF8Encoding(false));
                            }
                        }
                        catch { }
                    }
                    if (a.Action == L.T("覆盖")) rep.Replaced++; else rep.Copied++;
                }
                catch (Exception ex)
                {
                    rep.Failed++;
                    rep.Errors.Add(a.Name + "（" + a.PublishedId + "）：" + ex.Message);
                }
            }
            Log.Info(L.T("工坊同步完成：") + rep.Summary());
        }

        /// <summary>把工坊下载下来的某个条目复制进本地 Mods。id 用于命名目标目录。</summary>
        public static bool CopyItemToMods(string itemDir, string publishedId, string modsDir, out string dest, out string err)
        {
            dest = null;
            err = null;
            if (string.IsNullOrEmpty(itemDir) || !Directory.Exists(itemDir)) { err = L.T("源目录不存在：") + itemDir; return false; }
            string root = ModCatalog.FindModRoot(itemDir);
            if (root == null) { err = L.T("这个目录里没有 About\\About.xml，不像是模组：") + itemDir; return false; }
            if (string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir)) { err = L.T("本地 Mods 目录不存在：") + modsDir; return false; }
            try
            {
                string leaf = !string.IsNullOrEmpty(publishedId) ? publishedId : LeafName(root);
                string target = Path.Combine(modsDir, leaf);
                if (Directory.Exists(target)) FileOps.ClearDir(target, null);
                if (!Directory.Exists(target)) Directory.CreateDirectory(target);
                List<string> errs = new List<string>();
                FileOps.CopyTree(root, target, errs, false);
                if (errs.Count > 0) { err = string.Join("；", errs.ToArray()); return false; }
                dest = target;
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        private static string LeafName(string dir)
        {
            string leaf = Path.GetFileName(dir.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(leaf) ? "mod_" + Guid.NewGuid().ToString("N").Substring(0, 8) : leaf;
        }

        /// <summary>
        /// 从用户粘贴的任意文本里抠出工坊 id：
        /// 支持 "123456"，也支持
        /// https://steamcommunity.com/sharedfiles/filedetails/?id=123456
        /// https://steamcommunity.com/workshop/filedetails/?id=123456
        /// 以及多行混排。这里只取长度 >= 6 的纯数字，并去重。
        /// </summary>
        public static List<string> ExtractWorkshopIds(string text)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            MatchCollection ms = Regex.Matches(text, "[0-9]{6,}");
            for (int i = 0; i < ms.Count; i++)
            {
                string id = ms[i].Value;
                if (seen.Add(id)) list.Add(id);
            }
            return list;
        }

        /// <summary>读 ModsConfig.xml 的 activeMods，按原顺序返回 packageId。</summary>
        public static List<string> ReadActiveMods(string modsConfigPath)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(modsConfigPath) || !File.Exists(modsConfigPath)) return list;
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(modsConfigPath);
                XmlNode root = doc.DocumentElement;
                if (root == null) return list;
                XmlNode act = root.SelectSingleNode("activeMods");
                if (act == null) return list;
                XmlNodeList lis = act.SelectNodes("li");
                if (lis == null) return list;
                for (int i = 0; i < lis.Count; i++)
                {
                    string s = lis[i].InnerText.Trim();
                    if (s.Length > 0) list.Add(s);
                }
            }
            catch (Exception ex) { Log.Warn(L.T("读取排序文件失败：") + ex.Message); }
            return list;
        }

        /// <summary>按排序找出本地（Mods + 工坊缓存都算）没有的模组。</summary>
        public static List<string> FindMissing(string modsConfigPath, string modsDir, string workshopDir)
        {
            List<string> order = ReadActiveMods(modsConfigPath);
            HashSet<string> have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<ModEntry> local = ModCatalog.ScanRoot(modsDir, false);
            List<ModEntry> wsp = ModCatalog.ScanRoot(workshopDir, true);
            ModIdIndex.AddRange(local);
            ModIdIndex.AddRange(wsp);

            for (int i = 0; i < local.Count; i++) have.Add(local[i].PackageId);
            for (int i = 0; i < wsp.Count; i++) have.Add(wsp[i].PackageId);

            // 游戏本体/官方 DLC 不该算缺失
            have.Add("ludeon.rimworld");
            have.Add("ludeon.rimworld.royalty");
            have.Add("ludeon.rimworld.ideology");
            have.Add("ludeon.rimworld.biotech");
            have.Add("ludeon.rimworld.anomaly");
            have.Add("ludeon.rimworld.odyssey");

            List<string> miss = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < order.Count; i++)
            {
                if (!have.Contains(order[i]) && seen.Add(order[i])) miss.Add(order[i]);
            }
            return miss;
        }
    }

    // =====================================================================
    //  SteamCMD 后端：不需要 Steam 客户端、不需要开梯子（走内容 CDN）
    // =====================================================================
    public static class SteamCmd
    {
        public const string AppId = "294100";

        public static string Find(Settings st)
        {
            if (st != null && !string.IsNullOrEmpty(st.SteamCmdPath) && File.Exists(st.SteamCmdPath))
                return st.SteamCmdPath;

            List<string> cands = new List<string>();
            cands.Add(Path.Combine("C:\\steamcmd", "steamcmd.exe"));
            cands.Add(Path.Combine("D:\\steamcmd", "steamcmd.exe"));
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamcmd.exe"));
            cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器"), "steamcmd", "steamcmd.exe"));
            for (int i = 0; i < cands.Count; i++)
                if (File.Exists(cands[i])) return cands[i];
            return "";
        }

        /// <summary>用 steamcmd 匿名下载一个工坊条目，成功时返回内容目录。</summary>
        public static string DownloadItem(string steamCmdExe, string itemId, int timeoutSeconds, out string log, out string err)
        {
            log = "";
            err = null;
            if (string.IsNullOrEmpty(steamCmdExe) || !File.Exists(steamCmdExe))
            {
                err = L.T("没有找到 steamcmd.exe（可在设置里指定路径）");
                return null;
            }
            if (!ModCatalog.IsDigits(itemId))
            {
                err = L.T("工坊 id 不合法：") + itemId;
                return null;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(steamCmdExe);
                psi.Arguments = "+login anonymous +workshop_download_item " + AppId + " " + itemId + " validate +quit";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = Path.GetDirectoryName(steamCmdExe);

                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    StringBuilder sb = new StringBuilder();
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (sb) sb.AppendLine(e.Data); }
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (sb) sb.AppendLine(e.Data); }
                    };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(timeoutSeconds * 1000))
                    {
                        try { p.Kill(); } catch { }
                        lock (sb) log = sb.ToString();
                        err = L.T("steamcmd 超时（") + timeoutSeconds + L.T(" 秒）");
                        return null;
                    }
                    lock (sb) log = sb.ToString();
                }

                if (log.IndexOf("Success. Downloaded item", StringComparison.OrdinalIgnoreCase) < 0
                    && log.IndexOf("already up to date", StringComparison.OrdinalIgnoreCase) < 0
                    && log.IndexOf("Success", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    err = L.T("steamcmd 报告失败，详见日志。");
                    return null;
                }

                string content = Path.Combine(Path.Combine(Path.Combine(Path.Combine(
                    Path.GetDirectoryName(steamCmdExe), "steamapps"), "workshop"), "content"), AppId);
                string target = Path.Combine(content, itemId);
                if (Directory.Exists(target)) return target;

                err = L.T("steamcmd 说成功了，但没找到目录：") + target;
                return null;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return null;
            }
        }
    }

    // =====================================================================
    //  Steam Web API：不需要 key 的 GetPublishedFileDetails（查元数据/校验 id）
    // =====================================================================
    public static class WorkshopApi
    {
        public static string GetDetailsJson(List<string> ids, int timeoutMs, out string err)
        {
            err = null;
            if (ids == null || ids.Count == 0) { err = L.T("id 列表是空的"); return null; }
            try
            {
                StringBuilder body = new StringBuilder();
                body.Append("itemcount=").Append(ids.Count);
                for (int i = 0; i < ids.Count; i++)
                    body.Append("&publishedfileids[").Append(i).Append("]=").Append(ids[i]);

                bool plain;
                return WorkshopWebApi.ApiRequest("ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                    body.ToString(), timeoutMs, out err, out plain);
            }
            catch (Exception ex) { err = ex.Message; return null; }
        }

        /// <summary>启动器内自检：逐个端点测通不通，返回人类可读结果。必须在 UI 线程之外跑。</summary>
        public static List<string> SelfTest()
        {
            List<string> lines = new List<string>();
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

            lines.Add(L.T("── 连通性自检 ──"));

            // 1) api.steampowered.com（无 key，查元数据用）
            string err;
            Stopwatch sw = Stopwatch.StartNew();
            string json = GetDetailsJson(new List<string>(new string[] { "2009463077" }), 10000, out err);
            sw.Stop();
            if (json != null && json.Length > 0 && json.IndexOf("\"result\":1", StringComparison.Ordinal) >= 0)
                lines.Add(L.T("[通] api.steampowered.com   ") + sw.ElapsedMilliseconds + L.T(" ms   → 元数据查询可用（不需要梯子）"));
            else if (json != null)
                lines.Add(L.T("[通] api.steampowered.com   ") + sw.ElapsedMilliseconds + L.T(" ms   但返回内容异常"));
            else
                lines.Add(L.T("[不通] api.steampowered.com  ") + sw.ElapsedMilliseconds + " ms   : " + (err == null ? L.T("无响应") : err));

            // 2) steamcommunity.com（搜索页，通常要梯子）
            sw = Stopwatch.StartNew();
            string e2;
            bool ok2 = ProbeHead("https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077", 8000, out e2);
            sw.Stop();
            lines.Add((ok2 ? L.T("[通] ") : L.T("[不通] ")) + "steamcommunity.com   " + sw.ElapsedMilliseconds + " ms"
                + (ok2 ? L.T("   → 网页搜索可用") : L.T("   → 网页搜索需要梯子（这是正常的）")));

            // 3) steamcmd / 内容 CDN
            string sc = SteamCmd.Find(null);
            lines.Add(string.IsNullOrEmpty(sc)
                ? L.T("[提示] 没有找到 steamcmd.exe：装上它就能「不开梯子」直接下载工坊内容")
                : L.T("[通] steamcmd.exe 已就位：") + sc);

            lines.Add(L.T("── 说明 ──"));
            lines.Add(L.T("· 下载走 Steam 内容 CDN，一般不需要梯子；只有「网页搜索」走 steamcommunity.com，那才需要。"));
            lines.Add(L.T("· 装了 steamcmd 后，即使不启动 Steam 客户端也能下载工坊模组。"));
            return lines;
        }

        private static bool ProbeHead(string url, int timeoutMs, out string err)
        {
            err = null;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "HEAD";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "Mozilla/5.0 ZOGOS-Launcher/" + Log.Version;
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    return (int)resp.StatusCode < 500;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }
    }
}
