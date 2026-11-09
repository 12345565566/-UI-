using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Microsoft.Win32;

namespace Zogos
{
    // =====================================================================
    //  路径与文本工具
    // =====================================================================
    public static class PathHelp
    {
        public static bool HasNonAscii(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            for (int i = 0; i < p.Length; i++) if (p[i] > 127) return true;
            return false;
        }

        /// <summary>找出路径里第一个非 ASCII 字符所在的片段，用于给玩家看具体是哪一段有问题。</summary>
        public static string NonAsciiSample(string p)
        {
            if (!HasNonAscii(p)) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < p.Length; i++)
            {
                if (p[i] > 127)
                {
                    int s = i;
                    while (s > 0 && p[s - 1] != '\\' && p[s - 1] != '/') s--;
                    int e = i;
                    while (e < p.Length && p[e] != '\\' && p[e] != '/') e++;
                    string seg = p.Substring(s, e - s);
                    if (sb.Length > 0) sb.Append("、");
                    sb.Append(seg);
                    i = e;
                }
            }
            return sb.ToString();
        }

        public static string DefaultTargetConfigDir()
        {
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(Path.Combine(Path.Combine(Path.Combine(up, "AppData"), "LocalLow"),
                Path.Combine("Ludeon Studios", "RimWorld by Ludeon Studios")), "Config");
        }

        /// <summary>便携模式：整合包自己的游戏数据根目录（-savedatafolder 指向这里）。</summary>
        public static string PortableSaveDataDir(string root)
        {
            return Path.Combine(root, "UserData");
        }

        /// <summary>老版本默认写到 C 盘 LocalLow 的 Config，识别出来后迁移到便携目录。</summary>
        public static bool IsLegacyTargetConfigDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string a = Path.GetFullPath(path).TrimEnd('\\', '/');
                string b = Path.GetFullPath(DefaultTargetConfigDir()).TrimEnd('\\', '/');
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>设置里保存的是不是某个整合包的 UserData Config；搬家后要按当前根目录重新解析。</summary>
        public static bool EndsWithUserDataConfig(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string p = Path.GetFullPath(path).TrimEnd('\\', '/').Replace('/', '\\');
                return p.EndsWith("\\UserData\\Config", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'),
                    Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>给接近 260 上限的绝对路径加 \\?\ 前缀，绕开 MAX_PATH 限制。</summary>
        public static string Long(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            try
            {
                string full = Path.GetFullPath(path);
                if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) return full;
                if (full.Length >= 240 && full.Length > 3 && full[1] == ':')
                    return @"\\?" + full;
                return full;
            }
            catch { return path; }
        }

        /// <summary>目标配置目录是否可用：必须以 Config 结尾，且不能长到必然超限。</summary>
        public static bool IsUsableConfigDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\', '/');
                if (!string.Equals(Path.GetFileName(full), "Config", StringComparison.OrdinalIgnoreCase)) return false;
                if (full.Length > 210) return false;
                return true;
            }
            catch { return false; }
        }


        public static string LocalLowRimWorldDir()
        {
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(Path.Combine(Path.Combine(up, "AppData"), "LocalLow"),
                Path.Combine("Ludeon Studios", "RimWorld by Ludeon Studios"));
        }

        public static string Short(string path, int max)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (path.Length <= max) return path;
            int keep = max - 4;
            if (keep < 8) keep = 8;
            return "..." + path.Substring(path.Length - keep);
        }

        public static string ReadAllTextSmart(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return Encoding.UTF8.GetString(data, 3, data.Length - 3);
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
            // 无 BOM：先按 UTF-8 严格解码，失败再退回 GB2312
            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                return strict.GetString(data);
            }
            catch
            {
                try { return Encoding.GetEncoding(936).GetString(data); }
                catch { return Encoding.UTF8.GetString(data); }
            }
        }
    }

    // =====================================================================
    //  排序方案（整合包的 ModsConfig）
    // =====================================================================
    public class SortScheme
    {
        public string SourcePath;
        public string Name;
        public string Format = "XML";      // XML / JSON
        public string Version;
        public bool IsBuiltIn;
        public DateTime Modified = DateTime.MinValue;
        public string ParseError;

        public List<string> Mods = new List<string>();
        public List<string> Expansions = new List<string>();

        public bool HasRjw;
        /// <summary>命中 RJW 特征的模组条数（展示/诊断用）。</summary>
        public int RjwCount;
        public int Missing;
        public List<string> MissingList = new List<string>();

        public static readonly List<string> DefaultExpansions = new List<string>(new string[] {
            "ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.ideology",
            "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly", "ludeon.rimworld.odyssey"
        });

        public string FileName
        {
            get
            {
                try { return Path.GetFileName(SourcePath); }
                catch { return SourcePath; }
            }
        }

        public string ModCountText
        {
            get { return Mods.Count + L.T(" 个模组"); }
        }

        public string VersionText
        {
            get { return string.IsNullOrEmpty(Version) ? L.T("未知版本") : Version; }
        }

        public static SortScheme Parse(string path, string nameOverride)
        {
            SortScheme s = new SortScheme();
            s.SourcePath = path;
            s.Name = string.IsNullOrEmpty(nameOverride) ? Path.GetFileNameWithoutExtension(path) : nameOverride;
            try { s.Modified = File.GetLastWriteTime(path); }
            catch { }

            try
            {
                string raw = PathHelp.ReadAllTextSmart(path);
                if (raw.TrimStart().StartsWith("{"))
                {
                    s.Format = "JSON";
                    s.Version = JsonValue(raw, "version");
                    s.Mods = JsonArray(raw, "activeMods");
                    s.Expansions = JsonArray(raw, "knownExpansions");
                }
                else
                {
                    s.Format = "XML";
                    XmlDocument doc = new XmlDocument();
                    doc.Load(path);
                    XmlElement root = doc.DocumentElement;
                    s.Version = NodeText(root, "version");
                    s.Mods = NodeList(root, "activeMods");
                    s.Expansions = NodeList(root, "knownExpansions");
                }
            }
            catch (Exception ex)
            {
                s.ParseError = ex.Message;
            }

            if (s.ParseError == null && s.Mods.Count == 0) s.ParseError = L.T("文件中没有找到模组列表（activeMods）");
            s.DetectRjw();
            return s;
        }

        private static string NodeText(XmlElement root, string name)
        {
            if (root == null) return null;
            XmlNode n = root.SelectSingleNode(name);
            if (n == null) return null;
            return n.InnerText.Trim();
        }

        private static List<string> NodeList(XmlElement root, string name)
        {
            List<string> res = new List<string>();
            if (root == null) return res;
            XmlNode parent = root.SelectSingleNode(name);
            if (parent == null) return res;
            foreach (XmlNode c in parent.ChildNodes)
            {
                if (c.Name == "li")
                {
                    string v = c.InnerText;
                    if (v != null)
                    {
                        v = v.Trim();
                        if (v.Length > 0) res.Add(v);
                    }
                }
            }
            return res;
        }

        private static string JsonValue(string text, string key)
        {
            Match m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>从简易 JSON 中扫出指定键的字符串数组（RimSort 的排序文件格式）。</summary>
        private static List<string> JsonArray(string text, string key)
        {
            List<string> res = new List<string>();
            Match m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[");
            if (!m.Success) return res;

            int i = m.Index + m.Length;
            bool inStr = false, esc = false;
            StringBuilder cur = null;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr)
                {
                    if (esc) { cur.Append(c); esc = false; }
                    else if (c == '\\') esc = true;
                    else if (c == '"') { inStr = false; res.Add(cur.ToString()); }
                    else cur.Append(c);
                }
                else
                {
                    if (c == '"') { inStr = true; cur = new StringBuilder(); }
                    else if (c == ']') break;
                }
            }
            return res;
        }

        /// <summary>
        /// 判断这套排序是不是成人向（含 RJW）。
        /// 以前只要有一个模组 id 里带 rjw 字样就标记，结果几乎所有包都被标"含 RJW"。
        /// 现在要求：名字里明说，或者模组里真的出现多个 RJW 才算。
        /// </summary>
        public void DetectRjw()
        {
            // 强信号：packageId 里以整段出现 rjw / rimjobworld（RJW 本体和它的子模组）
            Regex strong = new Regex("(^|[._\\-])rjw([._\\-]|$)|rimjobworld", RegexOptions.IgnoreCase);
            // 弱信号：跟 RJW 生态沾边，但单独出现说明不了什么
            Regex weak = new Regex("(sexperience|sexperiments|licentia|brothel)", RegexOptions.IgnoreCase);

            int s = 0, w = 0;
            if (Mods != null)
            {
                for (int i = 0; i < Mods.Count; i++)
                {
                    string id = Mods[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (strong.IsMatch(id)) s++;
                    else if (weak.IsMatch(id)) w++;
                }
            }
            RjwCount = s + w;

            bool nameHint = Name != null
                && (Name.IndexOf(L.T("色色")) >= 0 || Name.IndexOf("RJW", StringComparison.OrdinalIgnoreCase) >= 0);

            HasRjw = nameHint || s >= 2 || (s + w) >= 3;
        }

        /// <summary>把方案写成 RimWorld 能读的 ModsConfig.xml。</summary>
        public void WriteModsConfig(string destPath)
        {
            List<string> exp = (Expansions != null && Expansions.Count > 0) ? Expansions : DefaultExpansions;

            string dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            XmlWriterSettings set = new XmlWriterSettings();
            set.Indent = true;
            set.IndentChars = "  ";
            set.Encoding = new UTF8Encoding(false);
            set.NewLineChars = "\r\n";
            set.OmitXmlDeclaration = false;

            using (XmlWriter w = XmlWriter.Create(destPath, set))
            {
                w.WriteStartDocument();
                w.WriteStartElement("ModsConfigData");
                w.WriteElementString("version", string.IsNullOrEmpty(Version) ? "1.7.0" : Version);

                w.WriteStartElement("activeMods");
                for (int i = 0; i < Mods.Count; i++) w.WriteElementString("li", Mods[i]);
                w.WriteEndElement();

                w.WriteStartElement("knownExpansions");
                for (int i = 0; i < exp.Count; i++) w.WriteElementString("li", exp[i]);
                w.WriteEndElement();

                w.WriteEndElement();
                w.WriteEndDocument();
            }
        }

        public string Describe()
        {
            string s = Format + L.T(" 格式 · ") + Mods.Count + L.T(" 个模组");
            if (!string.IsNullOrEmpty(Version)) s += " · " + Version;
            if (Missing > 0) s += L.T(" · 本地缺失 ") + Missing + L.T(" 个");
            if (Modified > DateTime.MinValue) s += " · " + Modified.ToString("yyyy-MM-dd HH:mm");
            return s;
        }
    }

    public static class SortSchemeLoader
    {
        public static List<SortScheme> Load(PackContext ctx)
        {
            List<SortScheme> list = new List<SortScheme>();

            if (ctx != null && !string.IsNullOrEmpty(ctx.PackConfigDir))
            {
                string def = Path.Combine(ctx.PackConfigDir, "ModsConfig.xml");
                if (File.Exists(def))
                {
                    SortScheme s = SortScheme.Parse(def, L.T("整合包默认排序"));
                    if (s != null)
                    {
                        s.IsBuiltIn = true;
                        if (s.ParseError == null) list.Add(s);
                    }
                }
            }

            if (ctx != null && !string.IsNullOrEmpty(ctx.SortDir) && Directory.Exists(ctx.SortDir))
            {
                string[] files;
                try { files = Directory.GetFiles(ctx.SortDir); }
                catch (Exception ex) { Log.Error(L.T("读取排序文件目录失败: ") + ctx.SortDir, ex); return list; }

                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                    if (ext != ".xml" && ext != ".json" && ext != ".txt") continue;
                    SortScheme s = SortScheme.Parse(files[i], null);
                    if (s.ParseError != null)
                    {
                        Log.Warn(L.T("排序文件无法解析，已跳过: ") + files[i] + "  → " + s.ParseError);
                        continue;
                    }
                    list.Add(s);
                }
            }
            return list;
        }
    }

    // =====================================================================
    //  整合包环境
    // =====================================================================
    public class PackContext
    {
        public string LauncherDir;
        public string Root;
        public string GameDir;
        public string GameExe;
        public string GameExeAlt;
        public string ModsDir;
        public string PackConfigDir;
        public string SortDir;
        public string TargetConfigDir;
        /// <summary>传给游戏 -savedatafolder 的目录；Config/Saves/ModLists 都在它下面。</summary>
        public string SaveDataDir;
        /// <summary>游戏数据是否落在整合包根目录内（便携模式）。</summary>
        public bool PortableData;
        public string BackupDir;
        public string SettingsDir;
        public string GameVersion = L.T("未知");
        public string RimSortExe;
        public string PackId = "";
        public bool HasMarker;
        public string DetectionLog = "";
        public List<PackCandidate> Candidates = new List<PackCandidate>();

        public bool NonAscii;
        public string NonAsciiSample = "";

        public List<string> Problems = new List<string>();
        public List<string> Notes = new List<string>();

        public bool Ok { get { return Problems.Count == 0; } }

        public string GameConfigDir { get { return string.IsNullOrEmpty(GameDir) ? "" : Path.Combine(GameDir, "Config"); } }
        public string GameLogsDir { get { return string.IsNullOrEmpty(GameDir) ? "" : Path.Combine(GameDir, "Logs"); } }
        public string GameCrashesDir { get { return string.IsNullOrEmpty(GameDir) ? "" : Path.Combine(GameDir, "Crashes"); } }
        public string LocalLowLogsDir { get { return Path.Combine(PathHelp.LocalLowRimWorldDir(), "Logs"); } }
        public string PlayerLog { get { return Path.Combine(LocalLowLogsDir, "Player.log"); } }

        public static bool LooksLikeGameFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            return File.Exists(Path.Combine(dir, "RimWorldWin64.exe")) || File.Exists(Path.Combine(dir, "RimWorld.exe"));
        }

        /// <summary>从某目录出发，找出整合包根目录（含 RimWorld\RimWorldWin64.exe 的那一层）。</summary>
        public static string FindRoot(string startDir)
        {
            if (string.IsNullOrEmpty(startDir)) return null;
            try
            {
                DirectoryInfo di = new DirectoryInfo(startDir);
                for (int i = 0; i < 5 && di != null; i++)
                {
                    if (LooksLikeGameFolder(Path.Combine(di.FullName, "RimWorld"))) return di.FullName;
                    if (LooksLikeGameFolder(di.FullName)) return di.Parent == null ? di.FullName : di.Parent.FullName;
                    di = di.Parent;
                }
                // 再往下找一层
                DirectoryInfo[] subs = new DirectoryInfo(startDir).GetDirectories();
                for (int i = 0; i < subs.Length; i++)
                {
                    if (LooksLikeGameFolder(Path.Combine(subs[i].FullName, "RimWorld"))) return subs[i].FullName;
                }
            }
            catch { }
            return null;
        }

        public static PackContext Detect(string launcherDir, Settings st, string rootOverride, string targetOverride, string appDataOverride)
        {
            PackContext c = new PackContext();
            c.LauncherDir = launcherDir;

            string root = PackDetector.ResolveRoot(launcherDir, st, rootOverride, c);
            if (string.IsNullOrEmpty(root)) root = launcherDir;

            c.Root = root;
            c.GameDir = Path.Combine(root, "RimWorld");
            c.PackConfigDir = Path.Combine(root, "Config");
            c.SortDir = Path.Combine(root, L.T("排序文件"));
            c.ModsDir = Path.Combine(c.GameDir, "Mods");

            if (st != null && !string.IsNullOrEmpty(st.SortDirOverride) && Directory.Exists(st.SortDirOverride))
                c.SortDir = st.SortDirOverride;

            string exe64 = Path.Combine(c.GameDir, "RimWorldWin64.exe");
            string exe32 = Path.Combine(c.GameDir, "RimWorld.exe");
            if (File.Exists(exe64)) { c.GameExe = exe64; c.GameExeAlt = exe32; }
            else if (File.Exists(exe32)) { c.GameExe = exe32; c.GameExeAlt = exe64; }
            else { c.GameExe = exe64; c.GameExeAlt = exe32; }

            // 目标配置目录：默认走整合包内 UserData\Config，不再碰 C 盘 LocalLow
            c.SaveDataDir = PathHelp.PortableSaveDataDir(root);
            string portableTarget = Path.Combine(c.SaveDataDir, "Config");
            string target = null;
            if (!string.IsNullOrEmpty(targetOverride)) target = targetOverride;
            else if (st != null && !string.IsNullOrEmpty(st.TargetConfigDir)
                && !PathHelp.IsLegacyTargetConfigDir(st.TargetConfigDir)
                && !PathHelp.EndsWithUserDataConfig(st.TargetConfigDir))
                target = st.TargetConfigDir;
            else target = portableTarget;

            // ★ 防护：设置里可能残留一个错误的、层层嵌套的目标配置目录
            //   （表现为 ...\RimWorld.vX-P2P\RimWorld.vX-P2P\...\Mods），
            //   往那里写会让配置复制大面积失败（路径超 260），这里直接回退到整合包内。
            if (!PathHelp.IsUsableConfigDir(target))
            {
                c.Notes.Add(L.T("目标配置目录不可用（必须以 Config 结尾，且路径不能过长），已自动改用：") + portableTarget);
                c.Notes.Add(L.T("原设置：") + PathHelp.Short(target, 160));
                target = portableTarget;
            }
            c.TargetConfigDir = target;

            // -savedatafolder 要指向 Config 的上一级；游戏会往那里写 Config/Saves/ModLists/HardwareProfile 等
            try
            {
                string tfull = Path.GetFullPath(target).TrimEnd('\\', '/');
                if (!string.Equals(Path.GetFileName(tfull), "Config", StringComparison.OrdinalIgnoreCase))
                    c.Notes.Add(L.T("注意：配置目录名不是 Config，游戏实际会读取 ") + Path.Combine(Path.GetDirectoryName(tfull), "Config") + L.T("。建议设为 ...\\UserData\\Config。"));
                string parent = Path.GetDirectoryName(tfull);
                if (!string.IsNullOrEmpty(parent)) c.SaveDataDir = parent;
            }
            catch { }
            try
            {
                string rf = Path.GetFullPath(root).TrimEnd('\\', '/') + "\\";
                string df = Path.GetFullPath(c.SaveDataDir).TrimEnd('\\', '/') + "\\";
                c.PortableData = df.StartsWith(rf, StringComparison.OrdinalIgnoreCase);
            }
            catch { c.PortableData = false; }
            if (c.PortableData)
            {
                c.Notes.Add(L.T("便携模式：游戏配置、存档、模组列表写入 ") + c.SaveDataDir + L.T("（启动参数 -savedatafolder）"));
                if (!FileOps.IsWritable(root))
                    c.Problems.Add(L.T("整合包目录不可写，便携模式无法创建 UserData。请把整合包放到可写目录，或用管理员权限运行启动器。"));
            }
            else
                c.Notes.Add(L.T("非便携模式：游戏数据目录在整合包外 ") + c.SaveDataDir);

            // 设置 / 备份目录
            string appData;
            if (!string.IsNullOrEmpty(appDataOverride)) appData = appDataOverride;
            else
            {
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器"));
            }
            c.SettingsDir = appData;

            string packBackup = Path.Combine(root, L.T("ZOGOS备份"));
            if (FileOps.IsWritable(root)) c.BackupDir = packBackup;
            else
            {
                c.BackupDir = Path.Combine(appData, L.T("备份"));
                c.Notes.Add(L.T("整合包目录不可写，备份将保存到: ") + c.BackupDir);
            }

            // 游戏版本
            try
            {
                string vf = Path.Combine(c.GameDir, "Version.txt");
                if (File.Exists(vf)) c.GameVersion = PathHelp.ReadAllTextSmart(vf).Trim();
            }
            catch { }

            // RimSort
            try
            {
                string[] exes = Directory.GetFiles(root, "*.exe", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < exes.Length; i++)
                {
                    string nm = Path.GetFileName(exes[i]);
                    if (nm.IndexOf("rimsort", StringComparison.OrdinalIgnoreCase) >= 0) { c.RimSortExe = exes[i]; break; }
                }
            }
            catch { }

            // 路径中文检测
            c.NonAscii = PathHelp.HasNonAscii(c.Root) || PathHelp.HasNonAscii(c.GameExe);
            if (c.NonAscii) c.NonAsciiSample = PathHelp.NonAsciiSample(c.Root);

            // 识别令牌：认出整合包后立刻写入/刷新，以后文件夹改名也能找回
            c.HasMarker = PackMarker.Exists(c.Root);
            c.PackId = c.HasMarker ? PackMarker.ReadPackId(c.Root) : "";
            bool hasExeNow = File.Exists(c.GameExe);
            bool hasCfgNow = File.Exists(Path.Combine(c.PackConfigDir, "ModsConfig.xml"));
            bool hasSortNow = false;
            try { if (Directory.Exists(c.SortDir)) hasSortNow = Directory.GetFiles(c.SortDir, "*.xml").Length > 0; } catch { }
            if (hasExeNow && (hasCfgNow || hasSortNow) && !c.HasMarker)
            {
                string mErr;
                if (PackMarker.Write(c.Root, null, out mErr))
                {
                    c.HasMarker = true;
                    c.PackId = PackMarker.ReadPackId(c.Root);
                    c.Notes.Add(L.T("已写入识别令牌（") + PackMarker.JsonName + L.T("），以后改文件夹名字或路径也能自动认出。"));
                }
                else c.Notes.Add(L.T("识别令牌写入失败：") + mErr + L.T("（不影响正常使用）"));
            }
            if (!hasCfgNow && !hasSortNow)
                c.Problems.Add(L.T("这个目录不像整合包目录（找不到 Config\\ModsConfig.xml 和 排序文件）：") + c.Root + L.T("　→ 请到【设置】点【绑定整合包文件夹…】"));

            // 体检
            if (!File.Exists(c.GameExe)) c.Problems.Add(L.T("没有找到游戏主程序：") + c.GameExe);
            else
            {
                try
                {
                    using (FileStream fsx = new FileStream(c.GameExe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        int h1 = fsx.ReadByte();
                        int h2 = fsx.ReadByte();
                        if (h1 != 0x4D || h2 != 0x5A)
                            c.Problems.Add(L.T("游戏主程序不是有效的 Windows 程序（可能是 Linux/安卓版，或者整合包没解压完整）：") + c.GameExe);
                    }
                }
                catch { }
            }
            if (!Directory.Exists(c.ModsDir)) c.Problems.Add(L.T("没有找到模组目录：") + c.ModsDir);
            else
            {
                try
                {
                    int n = Directory.GetDirectories(c.ModsDir).Length;
                    if (n == 0) c.Problems.Add(L.T("模组目录是空的：") + c.ModsDir);
                }
                catch { }
            }
            if (!Directory.Exists(c.PackConfigDir)) c.Problems.Add(L.T("没有找到整合包 Config 目录：") + c.PackConfigDir);
            if (!Directory.Exists(c.SortDir)) c.Notes.Add(L.T("没有找到「排序文件」文件夹：") + c.SortDir);
            if (!Directory.Exists(c.TargetConfigDir)) c.Notes.Add(L.T("便携配置目录还不存在，启动时会自动创建：") + c.TargetConfigDir);

            return c;
        }
    }

    // =====================================================================
    //  用 shell 打开网址 / 以普通权限拉起程序
    //
    //  踩过的坑：启动器如果是以「管理员身份」运行的，直接 ShellExecute 打开网址
    //  会跟普通权限的浏览器撞车 —— Edge 会弹「现有实例正在以提升的权限运行，
    //  是否要用普通权限重启」，看上去像程序卡了。
    //  所以提权时改成经 explorer 转交（explorer 是普通权限，由它去开浏览器）。
    // =====================================================================
    public static class ShellOpen
    {
        public static bool IsElevated()
        {
            try
            {
                using (System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    System.Security.Principal.WindowsPrincipal p = new System.Security.Principal.WindowsPrincipal(id);
                    return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        /// <summary>打开网址。提权运行时自动改走 explorer，避免浏览器权限冲突。</summary>
        public static void Url(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (IsElevated())
            {
                try
                {
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("explorer.exe");
                    psi.UseShellExecute = true;
                    psi.Arguments = "\"" + url + "\"";
                    System.Diagnostics.Process.Start(psi);
                    Log.Info(L.T("当前是管理员权限，已通过 explorer 转交打开：") + url);
                    return;
                }
                catch { }
            }
            System.Diagnostics.ProcessStartInfo d = new System.Diagnostics.ProcessStartInfo(url);
            d.UseShellExecute = true;
            System.Diagnostics.Process.Start(d);
        }

        /// <summary>以普通权限拉起一个程序（安装完成后调用，避免子进程继承管理员权限）。</summary>
        public static void LaunchNormal(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return;
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("explorer.exe");
                psi.UseShellExecute = true;
                psi.Arguments = "\"" + exePath + "\"";
                System.Diagnostics.Process.Start(psi);
            }
            catch
            {
                System.Diagnostics.ProcessStartInfo d = new System.Diagnostics.ProcessStartInfo(exePath);
                d.UseShellExecute = true;
                try { d.WorkingDirectory = Path.GetDirectoryName(exePath); } catch { }
                System.Diagnostics.Process.Start(d);
            }
        }
    }

    // =====================================================================
    //  启动器设置
    // =====================================================================
    public class Settings
    {
        public string Root = "";
        public string TargetConfigDir = "";
        public string SortDirOverride = "";
        public string LastScheme = "";
        public int ThemeIndex;
        public int BgMode;
        public string CustomBg = "";
        public string CustomVideo = "";
        public int VideoOverlayOpacity = 38;
        public int UiPanelOpacity = 62;
        public string ApiBase = "";
        public string ApiKey = "";
        public string ApiModel = "";
        public bool AutoBackup = true;
        public bool ClearConfig = true;
        public bool ReplaceEveryTime = false;
        public bool GuideSeen = false;
        public bool SyncMigrated = false;
        public bool SyncGameConfig = false;
        public bool StartSteam = true;
        public bool WarnNonAscii = true;
        public bool MinimizeAfterLaunch = true;
        public int KeepBackups = 10;
        public string PackId = "";
        /// <summary>极简 UI：深色模式开关（顶栏月亮/太阳一键切换，替代旧外观页主题色）。</summary>
        public bool UiDark = false;
        /// <summary>极简 UI：界面缩放百分比（100~200，设置页点按循环切换，立即生效；渲染层矢量放大，不影响布局坐标）。</summary>
        public int UiScalePct = 130;
        /// <summary>极简 UI：true=苏苏 AI 用旧版独立悬浮窗（粉色视觉小说风）；false=新版内嵌覆盖层。诊断页「呼叫苏苏」旁的勾选项。</summary>
        public bool SusuLegacyWindow = false;
        /// <summary>极简 UI：主页右栏是否折叠（只显示左卡片 + 收起柄）；用户点收起柄时记录，下次启动自动恢复。</summary>
        public bool RightCollapsed = false;
        /// <summary>steamcmd.exe 路径；留空则自动在常见位置找。</summary>
        public string SteamCmdPath = "";
        /// <summary>Steam Web API Key（免费）；配了就能不挂梯子搜索创意工坊。</summary>
        public string SteamApiKey = "";
        /// <summary>整合包资源表的数据源（静态 CSV / JSON 直链）。</summary>
        public string PackFeedUrl = "";

        private string _ini;

        public static Settings Load(string dir)
        {
            Settings s = new Settings();
            if (string.IsNullOrEmpty(dir))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器"));
            s._ini = Path.Combine(dir, "settings.ini");
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (!File.Exists(s._ini)) { s.Save(); return s; }
                string[] lines = File.ReadAllLines(s._ini, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i];
                    if (ln == null) continue;
                    ln = ln.Trim();
                    if (ln.Length == 0 || ln.StartsWith("#")) continue;
                    int eq = ln.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = ln.Substring(0, eq).Trim();
                    string v = ln.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "Root": s.Root = v; break;
                        case "TargetConfigDir": s.TargetConfigDir = v; break;
                        case "SortDirOverride": s.SortDirOverride = v; break;
                        case "LastScheme": s.LastScheme = v; break;
                        case "PackId": s.PackId = v; break;
                        case "ThemeIndex": { int ti; if (int.TryParse(v, out ti)) s.ThemeIndex = ti; break; }
                        case "BgMode": { int bm; if (int.TryParse(v, out bm)) s.BgMode = bm; break; }
                        case "CustomBg": s.CustomBg = v; break;
                        case "CustomVideo": s.CustomVideo = v; break;
                        case "VideoOverlayOpacity": { int n; if (int.TryParse(v, out n)) s.VideoOverlayOpacity = Math.Max(0, Math.Min(100, n)); break; }
                        case "UiPanelOpacity": { int n; if (int.TryParse(v, out n)) s.UiPanelOpacity = Math.Max(0, Math.Min(100, n)); break; }
                        case "AutoBackup": s.AutoBackup = v == "1"; break;
                        case "ClearConfig": s.ClearConfig = v == "1"; break;
                        case "ReplaceEveryTime": s.ReplaceEveryTime = v == "1"; break;
                        case "GuideSeen": s.GuideSeen = v == "1"; break;
                        case "ApiBase": s.ApiBase = v; break;
                        case "ApiKey": s.ApiKey = v; break;
                        case "ApiModel": s.ApiModel = v; break;
                        case "SyncMigrated": s.SyncMigrated = v == "1"; break;
                        case "SyncGameConfig": s.SyncGameConfig = v == "1"; break;
                        case "StartSteam": s.StartSteam = v == "1"; break;
                        case "WarnNonAscii": s.WarnNonAscii = v == "1"; break;
                        case "MinimizeAfterLaunch": s.MinimizeAfterLaunch = v == "1"; break;
                        case "SteamCmdPath": s.SteamCmdPath = v; break;
                        case "SteamApiKey": s.SteamApiKey = v; break;
                        case "PackFeedUrl": s.PackFeedUrl = v; break;
                        case "UiDark": s.UiDark = v == "1"; break;
                        case "SusuLegacyWindow": s.SusuLegacyWindow = v == "1"; break;
                        case "RightCollapsed": s.RightCollapsed = v == "1"; break;
                        case "UiScalePct":
                            {
                                int n;
                                if (int.TryParse(v, out n) && n >= 100 && n <= 200) s.UiScalePct = n;
                                break;
                            }
                        case "KeepBackups":
                            {
                                int n;
                                if (int.TryParse(v, out n) && n >= 1 && n <= 200) s.KeepBackups = n;
                                break;
                            }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(L.T("读取设置失败: ") + ex.Message); }

            // ★ 老版本把配置目标写成 C 盘 LocalLow；现在默认走整合包内 UserData，
            //   这里清掉旧值，避免设置文件里一直留着一个失效的 C 盘路径。
            if (!string.IsNullOrEmpty(s.TargetConfigDir)
                && (PathHelp.IsLegacyTargetConfigDir(s.TargetConfigDir) || !PathHelp.IsUsableConfigDir(s.TargetConfigDir)))
            {
                s.TargetConfigDir = "";
            }

            // ★ 一次性迁移：老版本默认开着「同步游戏目录内的 Config」，
            //   实测会动到游戏自己的配置、把玩家的画面设置冲掉 —— 给老用户强制关一次。
            if (!s.SyncMigrated)
            {
                s.SyncGameConfig = false;
                s.SyncMigrated = true;
                s.Save();
            }
            return s;
        }

        public void Save()
        {
            try
            {
                if (string.IsNullOrEmpty(_ini))
                    _ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器"), "settings.ini");
                string dir = Path.GetDirectoryName(_ini);
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(L.T("# Astra 创世神协议 设置文件"));
                sb.AppendLine("Root=" + Root);
                sb.AppendLine("TargetConfigDir=" + TargetConfigDir);
                sb.AppendLine("SortDirOverride=" + SortDirOverride);
                sb.AppendLine("LastScheme=" + LastScheme);
                sb.AppendLine("PackId=" + PackId);
                sb.AppendLine("ThemeIndex=" + ThemeIndex);
                sb.AppendLine("BgMode=" + BgMode);
                sb.AppendLine("CustomBg=" + CustomBg);
                sb.AppendLine("CustomVideo=" + CustomVideo);
                sb.AppendLine("VideoOverlayOpacity=" + VideoOverlayOpacity);
                sb.AppendLine("UiPanelOpacity=" + UiPanelOpacity);
                sb.AppendLine("AutoBackup=" + (AutoBackup ? "1" : "0"));
                sb.AppendLine("ClearConfig=" + (ClearConfig ? "1" : "0"));
                sb.AppendLine("ReplaceEveryTime=" + (ReplaceEveryTime ? "1" : "0"));
                sb.AppendLine("GuideSeen=" + (GuideSeen ? "1" : "0"));
                sb.AppendLine("ApiBase=" + ApiBase);
                sb.AppendLine("ApiKey=" + ApiKey);
                sb.AppendLine("ApiModel=" + ApiModel);
                sb.AppendLine("SyncMigrated=" + (SyncMigrated ? "1" : "0"));
                sb.AppendLine("SyncGameConfig=" + (SyncGameConfig ? "1" : "0"));
                sb.AppendLine("StartSteam=" + (StartSteam ? "1" : "0"));
                sb.AppendLine("WarnNonAscii=" + (WarnNonAscii ? "1" : "0"));
                sb.AppendLine("MinimizeAfterLaunch=" + (MinimizeAfterLaunch ? "1" : "0"));
                sb.AppendLine("KeepBackups=" + KeepBackups);
                sb.AppendLine("SteamCmdPath=" + SteamCmdPath);
                sb.AppendLine("SteamApiKey=" + SteamApiKey);
                sb.AppendLine("PackFeedUrl=" + PackFeedUrl);
                sb.AppendLine("UiDark=" + (UiDark ? "1" : "0"));
                sb.AppendLine("SusuLegacyWindow=" + (SusuLegacyWindow ? "1" : "0"));
                sb.AppendLine("RightCollapsed=" + (RightCollapsed ? "1" : "0"));
                sb.AppendLine("UiScalePct=" + UiScalePct);
                File.WriteAllText(_ini, sb.ToString(), new UTF8Encoding(true));
                return;
            }
            catch (Exception ex) { Log.Warn(L.T("保存设置失败: ") + ex.Message); }
        }

        public string IniPath { get { return _ini; } }
    }

    // =====================================================================
    //  文件操作
    // =====================================================================
    public static class FileOps
    {
        public static bool IsWritable(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            string probe = Path.Combine(dir, ".zogos_write_test_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(probe, "t");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>文件操作的统计：成功数 / 超长跳过数 / 其它失败数。</summary>
        private sealed class OpStat
        {
            public int Ok;
            public int TooLong;
            public int Other;
            public string FirstError;
        }

        private static bool IsTooLongError(Exception ex)
        {
            if (ex is PathTooLongException) return true;
            string m = ex.Message == null ? "" : ex.Message;
            return m.IndexOf("太长", StringComparison.Ordinal) >= 0
                || m.IndexOf("too long", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static int CopyTree(string src, string dst, List<string> errors, bool skipReadme)
        {
            OpStat st = new OpStat();
            CopyTreeCore(src, dst, skipReadme, st);
            if (errors != null)
            {
                if (st.TooLong > 0)
                    errors.Add(L.T("有 ") + st.TooLong + L.T(" 个文件因路径过长被跳过（Windows 上限 260 字符）。把整合包或目标配置目录换成更短的路径即可。"));
                if (st.Other > 0)
                    errors.Add(L.T("有 ") + st.Other + L.T(" 个文件复制失败")
                        + (string.IsNullOrEmpty(st.FirstError) ? "" : L.T("，首个错误：") + st.FirstError));
            }
            return st.Ok;
        }

        private static void CopyTreeCore(string src, string dst, bool skipReadme, OpStat st)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst)) return;
            string srcLong = PathHelp.Long(src);
            if (!Directory.Exists(srcLong)) return;

            try
            {
                string dstLong = PathHelp.Long(dst);
                if (!Directory.Exists(dstLong)) Directory.CreateDirectory(dstLong);
            }
            catch (Exception ex) { st.Other++; if (st.FirstError == null) st.FirstError = ex.Message; return; }

            string[] files;
            try { files = Directory.GetFiles(srcLong); }
            catch (Exception ex) { st.Other++; if (st.FirstError == null) st.FirstError = ex.Message; return; }

            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                if (skipReadme && name == L.T("备份说明.txt")) continue;
                try
                {
                    File.Copy(PathHelp.Long(files[i]), PathHelp.Long(Path.Combine(dst, name)), true);
                    st.Ok++;
                }
                catch (Exception ex)
                {
                    if (IsTooLongError(ex)) st.TooLong++;
                    else { st.Other++; if (st.FirstError == null) st.FirstError = name + "：" + ex.Message; }
                }
            }

            string[] dirs;
            try { dirs = Directory.GetDirectories(srcLong); }
            catch { return; }

            for (int i = 0; i < dirs.Length; i++)
                CopyTreeCore(dirs[i], Path.Combine(dst, Path.GetFileName(dirs[i])), skipReadme, st);
        }

        public static int ClearDir(string dir, List<string> errors)
        {
            OpStat st = new OpStat();
            ClearDirCore(dir, st);
            if (errors != null)
            {
                if (st.TooLong > 0)
                    errors.Add(L.T("有 ") + st.TooLong + L.T(" 项因路径过长没能删除（Windows 上限 260 字符）。"));
                if (st.Other > 0)
                    errors.Add(L.T("有 ") + st.Other + L.T(" 项删除失败")
                        + (string.IsNullOrEmpty(st.FirstError) ? "" : L.T("，首个错误：") + st.FirstError));
            }
            return st.Ok;
        }

        private static void ClearDirCore(string dir, OpStat st)
        {
            if (string.IsNullOrEmpty(dir)) return;
            string d = PathHelp.Long(dir);
            if (!Directory.Exists(d)) return;

            string[] files;
            try { files = Directory.GetFiles(d); }
            catch (Exception ex) { st.Other++; if (st.FirstError == null) st.FirstError = ex.Message; return; }

            for (int i = 0; i < files.Length; i++)
            {
                try { File.Delete(PathHelp.Long(files[i])); st.Ok++; }
                catch (Exception ex)
                {
                    if (IsTooLongError(ex)) st.TooLong++;
                    else { st.Other++; if (st.FirstError == null) st.FirstError = Path.GetFileName(files[i]) + "：" + ex.Message; }
                }
            }

            string[] dirs;
            try { dirs = Directory.GetDirectories(d); }
            catch { return; }

            for (int i = 0; i < dirs.Length; i++)
            {
                try { Directory.Delete(PathHelp.Long(dirs[i]), true); st.Ok++; }
                catch (Exception ex)
                {
                    if (IsTooLongError(ex)) st.TooLong++;
                    else { st.Other++; if (st.FirstError == null) st.FirstError = Path.GetFileName(dirs[i]) + "：" + ex.Message; }
                }
            }
        }

        public static void OpenInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (Directory.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
                else if (File.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else
                {
                    string d = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) System.Diagnostics.Process.Start("explorer.exe", "\"" + d + "\"");
                    else System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
                }
            }
            catch (Exception ex) { Log.Error(L.T("打开文件夹失败: ") + path, ex); }
        }
    }
}
