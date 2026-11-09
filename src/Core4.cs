using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Zogos
{
    // =====================================================================
    //  整合包识别令牌
    //  在整合包根目录写一个小文件，以后无论文件夹改成什么名字 / 搬到哪，
    //  启动器都能凭这个令牌认出「这就是那个整合包」。
    // =====================================================================
    public static class PackMarker
    {
        public const string JsonName = "ZOGOS整合包标识.json";
        public const string IdName = "zogos.pack";

        public static string JsonPath(string root)
        {
            return Path.Combine(root, JsonName);
        }

        public static string IdPath(string root)
        {
            return Path.Combine(root, IdName);
        }

        public static bool Exists(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false;
            try { return File.Exists(JsonPath(root)) || File.Exists(IdPath(root)); }
            catch { return false; }
        }

        public static string ReadPackId(string root)
        {
            if (string.IsNullOrEmpty(root)) return "";
            try
            {
                string jp = JsonPath(root);
                if (File.Exists(jp))
                {
                    string t = File.ReadAllText(jp, Encoding.UTF8);
                    Match m = Regex.Match(t, "\"packId\"\\s*:\\s*\"([^\"]*)\"");
                    if (m.Success && m.Groups[1].Value.Length > 0) return m.Groups[1].Value;
                }
                string ip = IdPath(root);
                if (File.Exists(ip))
                {
                    string[] ls = File.ReadAllLines(ip, Encoding.UTF8);
                    for (int i = 0; i < ls.Length; i++)
                    {
                        string s = ls[i].Trim();
                        if (s.Length > 0) return s;
                    }
                }
            }
            catch { }
            return "";
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>读令牌里记的游戏版本（老令牌没有这一项时返回空）。</summary>
        public static string ReadVersion(string root)
        {
            try
            {
                string jp = JsonPath(root);
                if (!File.Exists(jp)) return "";
                string t = File.ReadAllText(jp, Encoding.UTF8);
                Match m = Regex.Match(t, "\"gameVersion\"\\s*:\\s*\"([^\"]*)\"");
                if (m.Success) return m.Groups[1].Value;
            }
            catch { }
            return "";
        }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        public static bool Write(string root, string packId, out string err)
        {
            return Write(root, packId, "", "", 0, out err);
        }

        /// <summary>写令牌，并带上版本 / 排序 / 模组数（1.6 与 1.7 共存时要靠它分辨）。</summary>
        public static bool Write(string root, string packId, string gameVersion, string sortScheme, int modCount, out string err)
        {
            err = null;
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    err = L.T("目录不存在");
                    return false;
                }
                if (string.IsNullOrEmpty(packId)) packId = ReadPackId(root);
                if (string.IsNullOrEmpty(packId)) packId = NewId();

                string name = "";
                try { name = Path.GetFileName(root.TrimEnd('\\', '/')); }
                catch { }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"zogosPack\": 1,");
                sb.AppendLine("  \"packId\": \"" + packId + "\",");
                sb.AppendLine("  \"packName\": \"" + name + "\",");
                sb.AppendLine("  \"updatedAt\": \"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\",");
                sb.AppendLine("  \"game\": \"RimWorld\",");
                sb.AppendLine("  \"gameExe\": \"RimWorld\\\\RimWorldWin64.exe\",");
                sb.AppendLine("  \"configDir\": \"Config\",");
                sb.AppendLine(L.T("  \"sortDir\": \"排序文件\","));
                if (!string.IsNullOrEmpty(gameVersion)) sb.AppendLine("  \"gameVersion\": \"" + Escape(gameVersion) + "\",");
                if (!string.IsNullOrEmpty(sortScheme)) sb.AppendLine("  \"sortScheme\": \"" + Escape(sortScheme) + "\",");
                sb.AppendLine("  \"modCount\": " + modCount + ",");
                sb.AppendLine(L.T("  \"launcher\": \"Astra 创世神协议 ") + Log.Version + "\"");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine(L.T("// 这是 Astra 创世神协议的整合包识别令牌。"));
                sb.AppendLine(L.T("// 启动器靠它认出整合包，改了文件夹名字或路径也照样认得。请不要删除。"));
                File.WriteAllText(JsonPath(root), sb.ToString(), new UTF8Encoding(true));

                File.WriteAllText(IdPath(root), packId + Environment.NewLine, new UTF8Encoding(true));

                Log.Info(L.T("识别令牌已写入：") + JsonPath(root) + "（packId " + packId + "）");
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }
    }

    // =====================================================================
    //  整合包定位：带打分的候选目录评估
    //  只看「有没有 RimWorld\RimWorldWin64.exe」会被别的 RimWorld 目录骗到，
    //  所以改成按特征打分：识别令牌 / 游戏主程序 / Config\ModsConfig.xml / 排序文件
    // =====================================================================
    public class PackCandidate
    {
        public string Dir = "";
        public int Score;
        public bool Strict;
        public string PackId = "";
        public List<string> Notes = new List<string>();

        public string Explain()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Score).Append(L.T(" 分"));
            if (Strict) sb.Append(L.T("（像整合包）"));
            if (Notes.Count > 0) sb.Append("：").Append(string.Join("、", Notes.ToArray()));
            return sb.ToString();
        }
    }

    public static class PackDetector
    {
        private const int MaxScanDirs = 600;

        public static int Score(string dir, out bool strict, out List<string> notes, out string packId)
        {
            strict = false;
            notes = new List<string>();
            packId = "";
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return -1;

            int s = 0;

            if (PackMarker.Exists(dir))
            {
                s += 100;
                packId = PackMarker.ReadPackId(dir);
                notes.Add(packId.Length > 0 ? (L.T("识别令牌 ") + packId) : L.T("识别令牌"));
            }

            bool hasExe = false;
            try
            {
                hasExe = File.Exists(Path.Combine(dir, "RimWorld\\RimWorldWin64.exe"))
                      || File.Exists(Path.Combine(dir, "RimWorld\\RimWorld.exe"));
                if (hasExe) { s += 40; notes.Add(L.T("游戏主程序")); }
            }
            catch { }

            bool hasCfg = false;
            try
            {
                hasCfg = File.Exists(Path.Combine(dir, "Config\\ModsConfig.xml"));
                if (hasCfg) { s += 30; notes.Add("Config\\ModsConfig.xml"); }
                else if (Directory.Exists(Path.Combine(dir, "Config"))) { s += 10; notes.Add(L.T("Config 目录")); }
            }
            catch { }

            int sortN = 0;
            try
            {
                string sd = Path.Combine(dir, L.T("排序文件"));
                if (Directory.Exists(sd))
                {
                    sortN = Directory.GetFiles(sd, "*.xml").Length + Directory.GetFiles(sd, "*.json").Length;
                    if (sortN > 0) { s += 25; notes.Add(L.T("排序文件 ") + sortN + L.T(" 个")); }
                    else { s += 8; notes.Add(L.T("排序文件夹(空)")); }
                }
            }
            catch { }

            try
            {
                if (Directory.Exists(Path.Combine(dir, "RimWorld\\Mods"))) { s += 15; notes.Add(L.T("Mods 目录")); }
                if (File.Exists(Path.Combine(dir, "RimWorld\\Version.txt"))) s += 5;
            }
            catch { }

            strict = hasExe && (hasCfg || sortN > 0);
            return s;
        }

        private static string Parent(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return null;
                DirectoryInfo d = Directory.GetParent(dir.TrimEnd('\\', '/'));
                return d == null ? null : d.FullName;
            }
            catch { return null; }
        }

        private static void TryAdd(List<PackCandidate> list, HashSet<string> seen, string dir)
        {
            if (string.IsNullOrEmpty(dir) || list.Count >= MaxScanDirs) return;
            string full;
            try { full = Path.GetFullPath(dir).TrimEnd('\\', '/'); }
            catch { return; }
            if (seen.Contains(full)) return;
            seen.Add(full);

            bool strict; List<string> notes; string pid;
            int sc = Score(full, out strict, out notes, out pid);
            if (sc <= 0) return;

            PackCandidate pc = new PackCandidate();
            pc.Dir = full;
            pc.Score = sc;
            pc.Strict = strict;
            pc.Notes = notes;
            pc.PackId = pid;
            list.Add(pc);
        }

        private static void AddWithChildren(List<PackCandidate> list, HashSet<string> seen, string dir)
        {
            TryAdd(list, seen, dir);
            if (string.IsNullOrEmpty(dir) || list.Count >= MaxScanDirs) return;
            string[] subs = null;
            try { subs = Directory.GetDirectories(dir); }
            catch { return; }
            if (subs == null || subs.Length > MaxScanDirs) return;
            for (int i = 0; i < subs.Length; i++) TryAdd(list, seen, subs[i]);
        }

        public static string ResolveRoot(string launcherDir, Settings st, string rootOverride, PackContext c)
        {
            List<PackCandidate> list = new List<PackCandidate>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            StringBuilder log = new StringBuilder();

            // 0) 手动指定的目录最优先
            if (!string.IsNullOrEmpty(rootOverride))
            {
                TryAdd(list, seen, rootOverride);
                TryAdd(list, seen, Path.Combine(rootOverride, "RimWorld"));
                TryAdd(list, seen, Parent(rootOverride));
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Strict)
                    {
                        c.DetectionLog = L.T("使用手动指定的整合包目录：") + list[i].Dir;
                        c.Candidates = list;
                        return list[i].Dir;
                    }
                }
                list.Clear();
                seen.Clear();
            }

            string p1 = Parent(launcherDir);
            string p2 = Parent(p1);

            // 1) 启动器自己所在目录 + 同级目录
            AddWithChildren(list, seen, launcherDir);
            // 2) 上一层（启动器被放在整合包外面时的常见情况）
            AddWithChildren(list, seen, p1);
            // 3) 再上一层
            TryAdd(list, seen, p2);
            // 4) 上次记住的位置
            if (st != null && !string.IsNullOrEmpty(st.Root)) TryAdd(list, seen, st.Root);

            if (list.Count == 0)
            {
                c.DetectionLog = L.T("没有找到任何候选目录，暂用启动器所在目录：") + launcherDir;
                return launcherDir;
            }

            // 先只留下「像整合包」的候选
            List<PackCandidate> strictPool = new List<PackCandidate>();
            for (int i = 0; i < list.Count; i++) if (list[i].Strict) strictPool.Add(list[i]);
            List<PackCandidate> pool = strictPool.Count > 0 ? strictPool : list;

            // 识别令牌里的 packId 和上次一致，直接采用
            if (st != null && !string.IsNullOrEmpty(st.PackId))
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    if (string.Equals(pool[i].PackId, st.PackId, StringComparison.OrdinalIgnoreCase))
                    {
                        c.DetectionLog = L.T("凭识别令牌匹配到上次的整合包：") + pool[i].Dir;
                        c.Candidates = list;
                        return pool[i].Dir;
                    }
                }
            }

            // 否则：优先「启动器就在整合包里」，其次分数高、路径短
            pool.Sort(delegate(PackCandidate a, PackCandidate b)
            {
                bool al = string.Equals(a.Dir, launcherDir, StringComparison.OrdinalIgnoreCase);
                bool bl = string.Equals(b.Dir, launcherDir, StringComparison.OrdinalIgnoreCase);
                if (al != bl) return al ? -1 : 1;
                if (a.Score != b.Score) return b.Score - a.Score;
                return a.Dir.Length - b.Dir.Length;
            });

            for (int i = 0; i < list.Count && i < 6; i++)
                log.AppendLine("    " + list[i].Dir + "  ->  " + list[i].Explain());

            if (strictPool.Count == 0)
            {
                c.DetectionLog = L.T("没有找到完整的整合包，只能选最像的一个：\r\n") + log.ToString();
                c.Notes.Add(L.T("没有识别出完整的整合包目录，请到【设置】点【绑定整合包文件夹…】手动指定。"));
            }
            else if (strictPool.Count > 1)
            {
                c.DetectionLog = L.T("找到多个像整合包的目录，已选最合适的一个：\r\n") + log.ToString();
                c.Notes.Add(L.T("发现了 ") + strictPool.Count + L.T(" 个像整合包的目录，如果选错了请到【设置】点【绑定整合包文件夹…】。"));
            }
            else
            {
                c.DetectionLog = L.T("找到整合包：") + pool[0].Dir;
            }

            c.Candidates = list;
            return pool[0].Dir;
        }
    }
}
