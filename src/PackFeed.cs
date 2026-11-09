using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Zogos
{
    /// <summary>表格里的一行：一个整合包。</summary>
    public class PackItem
    {
        public string Name = "";
        public string ModCount = "";
        public string GameVersion = "";
        public string CopyCount = "";
        public string Storytellers = "";
        public string LoadTime = "";
        public string MaxFps = "";
        public string Rjw = "";
        public string ZipSize = "";
        public string UnzippedSize = "";
        public string QuarkUrl = "";
        public string XunleiUrl = "";
        public string Description = "";
        public string Cover = "";

        /// <summary>夸克链接是不是有效地址（不是"链接维护中"这种文字）。</summary>
        public bool QuarkOk { get { return PackFeed.LooksLikeUrl(QuarkUrl); } }
        public bool XunleiOk { get { return PackFeed.LooksLikeUrl(XunleiUrl); } }

        /// <summary>两个网盘都没有有效链接 = 这个包暂时下不了，卡片上打"维护中"。</summary>
        public bool Dead { get { return !QuarkOk && !XunleiOk; } }
    }

    /// <summary>
    /// 整合包资源表：负责「取数据」和「解析」。
    ///
    /// 取数策略（每次打开窗口都跑一遍）：
    ///   1. 数据源 URL 直接 GET（要求响应体里就有 CSV / TSV / JSON，静态直链最佳）
    ///   2. 失败则用上次缓存
    /// 另外支持本地导入（CSV / Excel 粘贴），作为永远可用的保底。
    ///
    /// 注意：腾讯文档 / 飞书这类在线表格是 SPA，HTML 里没有数据，
    /// 它们的内部接口需要登录态，匿名 GET 只会拿到 401。所以不要指望直接粘它们的分享链接。
    /// </summary>
    public static class PackFeed
    {
        /// <summary>默认预览图（用户没填封面时统一用这张）。走 jsDelivr，国内快。</summary>
        public static readonly string[] DefaultCoverUrls = new string[] {
            "https://cdn.jsdelivr.net/gh/shitaitai/zogos-packs@main/covers/default.jpg",
            "https://raw.githubusercontent.com/shitaitai/zogos-packs/main/covers/default.jpg"
        };

        /// <summary>下载默认预览图，返回本地缓存路径；全失败返回 null。</summary>
        public static string DownloadDefaultCover()
        {
            for (int i = 0; i < DefaultCoverUrls.Length; i++)
            {
                string p = DownloadCover(DefaultCoverUrls[i]);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            return null;
        }

        /// <summary>主数据源：GitHub raw，延迟约 1-2 分钟，速度最快。</summary>
        public const string DefaultSource = "https://raw.githubusercontent.com/shitaitai/zogos-packs/main/packs.json";

        /// <summary>
        /// 备用源，按顺序自动降级：
        ///  - raw.githubusercontent 在国内常被 DNS 污染，连不上就走下面的加速镜像
        ///  - ghproxy / statically 都是国内可访问的 GitHub 加速
        ///  - jsDelivr 最快但分支缓存最长 12 小时，所以放最后，只在前几个全挂时才用
        /// </summary>
        public static readonly string[] Sources = new string[] {
            "https://raw.githubusercontent.com/shitaitai/zogos-packs/main/packs.json",
            "https://ghproxy.net/https://raw.githubusercontent.com/shitaitai/zogos-packs/main/packs.json",
            "https://cdn.statically.io/gh/shitaitai/zogos-packs/main/packs.json",
            "https://cdn.jsdelivr.net/gh/shitaitai/zogos-packs@main/packs.json"
        };

        public static string BaseDir()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器"));
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public static string CachePath() { return Path.Combine(BaseDir(), "packfeed_cache.txt"); }
        public static string MetaPath() { return Path.Combine(BaseDir(), "packfeed_meta.txt"); }
        public static string CoverDir()
        {
            string d = Path.Combine(BaseDir(), "packcovers");
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            return d;
        }

        // ── 缓存 ────────────────────────────────────────────────
        public static void SaveCache(List<PackItem> items, string source)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < items.Count; i++)
                {
                    PackItem p = items[i];
                    sb.AppendLine(string.Join("\t", new string[] {
                        Esc(p.Name), Esc(p.ModCount), Esc(p.GameVersion), Esc(p.CopyCount),
                        Esc(p.Storytellers), Esc(p.LoadTime), Esc(p.MaxFps), Esc(p.Rjw),
                        Esc(p.ZipSize), Esc(p.UnzippedSize), Esc(p.QuarkUrl), Esc(p.XunleiUrl),
                        Esc(p.Description), Esc(p.Cover) }));
                }
                File.WriteAllText(CachePath(), sb.ToString(), new UTF8Encoding(false));
                File.WriteAllText(MetaPath(), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + (source == null ? "" : source),
                    new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Warn(L.T("保存整合包表缓存失败：") + ex.Message); }
        }

        public static List<PackItem> LoadCache(out string updatedAt)
        {
            updatedAt = "";
            List<PackItem> list = new List<PackItem>();
            try
            {
                if (File.Exists(MetaPath()))
                {
                    string[] meta = File.ReadAllLines(MetaPath(), Encoding.UTF8);
                    if (meta.Length > 0) updatedAt = meta[0];
                }
                if (!File.Exists(CachePath())) return list;
                string[] lines = File.ReadAllLines(CachePath(), Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (string.IsNullOrEmpty(lines[i])) continue;
                    string[] c = lines[i].Split('\t');
                    if (c.Length < 14) continue;
                    PackItem p = new PackItem();
                    p.Name = Unesc(c[0]); p.ModCount = Unesc(c[1]); p.GameVersion = Unesc(c[2]);
                    p.CopyCount = Unesc(c[3]); p.Storytellers = Unesc(c[4]); p.LoadTime = Unesc(c[5]);
                    p.MaxFps = Unesc(c[6]); p.Rjw = Unesc(c[7]); p.ZipSize = Unesc(c[8]);
                    p.UnzippedSize = Unesc(c[9]); p.QuarkUrl = Unesc(c[10]); p.XunleiUrl = Unesc(c[11]);
                    p.Description = Unesc(c[12]); p.Cover = Unesc(c[13]);
                    if (!string.IsNullOrEmpty(p.Name)) list.Add(p);
                }
            }
            catch (Exception ex) { Log.Warn(L.T("读取整合包表缓存失败：") + ex.Message); }
            return list;
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n");
        }
        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 't') { sb.Append('\t'); i++; continue; }
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // ── 在线获取 ────────────────────────────────────────────
        /// <summary>这个地址是不是内置源之一（是的话就走多源自动降级，而不是死磕它一个）。</summary>
        public static bool IsKnownSource(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            string u = url.Trim();
            int q = u.IndexOf('?');
            if (q > 0) u = u.Substring(0, q);
            for (int i = 0; i < Sources.Length; i++)
                if (string.Equals(Sources[i], u, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>依次尝试内置的多个源，第一个能解析出数据的就是它。</summary>
        public static List<PackItem> FetchPublic(out string err, out string usedUrl)
        {
            err = null;
            usedUrl = null;
            List<string> errs = new List<string>();
            for (int i = 0; i < Sources.Length; i++)
            {
                string e;
                List<PackItem> items = TryFetch(Sources[i], out e);
                if (items != null && items.Count > 0)
                {
                    usedUrl = Sources[i];
                    if (i > 0) Log.Info(L.T("主数据源不可用，已切换到备用源 ") + (i + 1) + "：" + Sources[i]);
                    return items;
                }
                errs.Add(L.T("源") + (i + 1) + "：" + (e == null ? L.T("无数据") : e));
            }
            err = string.Join("；", errs.ToArray());
            return null;
        }

        /// <summary>
        /// 直接 GET 数据源。只有响应体本身是 CSV/TSV/JSON 时才可能成功。
        /// 成功返回解析出的列表，失败返回 null 并给出原因。
        /// </summary>
        public static List<PackItem> TryFetch(string url, out string err)
        {
            err = null;
            if (string.IsNullOrEmpty(url)) { err = L.T("没有配置数据源"); return null; }
            try
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 20000;
                req.ReadWriteTimeout = 20000;
                req.UserAgent = "Mozilla/5.0 ZOGOS-Launcher/" + Log.Version;
                req.Accept = "text/csv,text/plain,application/json,*/*";
                string body;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                    body = sr.ReadToEnd();

                if (string.IsNullOrEmpty(body)) { err = L.T("响应是空的"); return null; }

                // 明显的「空壳页面」判定：HTML 但没有任何表格痕迹
                string head = body.Length > 400 ? body.Substring(0, 400) : body;
                bool looksHtml = head.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0
                    || head.IndexOf("<!doctype", StringComparison.OrdinalIgnoreCase) >= 0;
                List<PackItem> items = ParseTable(body);
                if (items.Count == 0)
                {
                    err = looksHtml
                        ? L.T("数据源返回的是网页（表格由 JS 加载），程序拿不到内容。请改用静态 CSV/JSON 直链，或点「导入表格」。")
                        : L.T("没解析到任何整合包数据");
                    return null;
                }
                return items;
            }
            catch (Exception ex) { err = ex.Message; return null; }
        }

        // ── 解析 ────────────────────────────────────────────────
        /// <summary>
        /// 解析 CSV / TSV / JSON。按表头关键字定位列，表头认不出来时退回固定列序
        /// （表头：名称,MOD数量,游戏版,副本数量,讲述者数,加载时间,最高fps,RJW,压缩包,解压大小,夸克,迅雷,简介,封面）
        /// </summary>
        public static List<PackItem> ParseTable(string text)
        {
            List<PackItem> list = new List<PackItem>();
            if (string.IsNullOrEmpty(text)) return list;

            string t = text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            if (t.StartsWith("[") || t.StartsWith("{")) return ParseJson(t);

            char sep = '\t';
            // 第一行里逗号明显多于制表符 → 按 CSV 解析
            int firstLineEnd = t.IndexOf('\n');
            string firstLine = firstLineEnd > 0 ? t.Substring(0, firstLineEnd) : t;
            int commas = 0, tabs = 0;
            for (int i = 0; i < firstLine.Length; i++)
            {
                if (firstLine[i] == ',') commas++;
                else if (firstLine[i] == '\t') tabs++;
            }
            sep = commas > tabs ? ',' : '\t';

            List<List<string>> rows = SplitRows(t, sep);
            if (rows.Count == 0) return list;

            int headerRow = -1;
            for (int i = 0; i < rows.Count && i < 5; i++)
            {
                for (int k = 0; k < rows[i].Count; k++)
                {
                    if (rows[i][k].IndexOf(L.T("整合包"), StringComparison.Ordinal) >= 0
                        || rows[i][k].IndexOf(L.T("名称"), StringComparison.Ordinal) >= 0)
                    { headerRow = i; break; }
                }
                if (headerRow >= 0) break;
            }

            int iName = 0, iMods = 1, iVer = 2, iCopy = 3, iStory = 4, iLoad = 5, iFps = 6,
                iRjw = 7, iZip = 8, iUnzip = 9, iQuark = 10, iXunlei = 11, iDesc = 12, iCover = 18;

            int start = 0;
            if (headerRow >= 0)
            {
                List<string> h = rows[headerRow];
                for (int k = 0; k < h.Count; k++)
                {
                    string s = h[k];
                    if (s.Contains(L.T("名称"))) iName = k;
                    else if (s.Contains("MOD") || s.Contains(L.T("模组数"))) iMods = k;
                    else if (s.Contains(L.T("游戏版")) || s.Contains(L.T("本体版本")) || s.Contains(L.T("版本"))) iVer = k;
                    else if (s.Contains(L.T("副本"))) iCopy = k;
                    else if (s.Contains(L.T("讲述"))) iStory = k;
                    else if (s.Contains(L.T("加载"))) iLoad = k;
                    else if (s.Contains("fps") || s.Contains("FPS")) iFps = k;
                    else if (s.Contains("RJW")) iRjw = k;
                    else if (s.Contains(L.T("压缩包")) || s.Contains(L.T("压缩"))) iZip = k;
                    else if (s.Contains(L.T("解压"))) iUnzip = k;
                    else if (s.Contains(L.T("夸克"))) iQuark = k;
                    else if (s.Contains(L.T("迅雷"))) iXunlei = k;
                    else if (s.Contains(L.T("简介")) || s.Contains(L.T("介绍"))) iDesc = k;
                    else if (s.Contains(L.T("封面")) || s.Contains(L.T("预览"))) iCover = k;
                }
                start = headerRow + 1;
            }

            for (int r = start; r < rows.Count; r++)
            {
                List<string> row = rows[r];
                string name = Cell(row, iName);
                if (string.IsNullOrEmpty(name)) continue;
                // 只跳过"表头样"的行；不要把名字里带"整合包"的正常条目误杀
                if (name.IndexOf(L.T("整合包名称"), StringComparison.Ordinal) >= 0) continue;
                PackItem p = new PackItem();
                p.Name = name;
                p.ModCount = Cell(row, iMods);
                p.GameVersion = Cell(row, iVer);
                p.CopyCount = Cell(row, iCopy);
                p.Storytellers = Cell(row, iStory);
                p.LoadTime = Cell(row, iLoad);
                p.MaxFps = Cell(row, iFps);
                p.Rjw = Cell(row, iRjw);
                p.ZipSize = Cell(row, iZip);
                p.UnzippedSize = Cell(row, iUnzip);
                p.QuarkUrl = Cell(row, iQuark);
                p.XunleiUrl = Cell(row, iXunlei);
                p.Description = Cell(row, iDesc);
                p.Cover = Cell(row, iCover);
                list.Add(p);
            }
            return list;
        }

        private static string Cell(List<string> row, int idx)
        {
            if (row == null || idx < 0 || idx >= row.Count) return "";
            return (row[idx] == null ? "" : row[idx].Trim());
        }

        /// <summary>按分隔符切行，支持双引号包裹（含逗号、换行）。</summary>
        private static List<List<string>> SplitRows(string text, char sep)
        {
            List<List<string>> rows = new List<List<string>>();
            List<string> cur = new List<string>();
            StringBuilder field = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else quoted = false;
                    }
                    else field.Append(c);
                    continue;
                }
                if (c == '"') { quoted = true; continue; }
                if (c == sep) { cur.Add(field.ToString()); field.Length = 0; continue; }
                if (c == '\r') continue;
                if (c == '\n')
                {
                    cur.Add(field.ToString()); field.Length = 0;
                    if (cur.Count > 1 || (cur.Count == 1 && cur[0].Trim().Length > 0)) rows.Add(cur);
                    cur = new List<string>();
                    continue;
                }
                field.Append(c);
            }
            cur.Add(field.ToString());
            if (cur.Count > 1 || (cur.Count == 1 && cur[0].Trim().Length > 0)) rows.Add(cur);
            return rows;
        }

        /// <summary>兼容 JSON 数组：[{Name:..., GameVersion:..., QuarkUrl:...}]</summary>
        private static List<PackItem> ParseJson(string json)
        {
            List<PackItem> list = new List<PackItem>();
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 32 * 1024 * 1024;
                object[] arr = js.DeserializeObject(json) as object[];
                if (arr == null) return list;
                for (int i = 0; i < arr.Length; i++)
                {
                    Dictionary<string, object> d = arr[i] as Dictionary<string, object>;
                    if (d == null) continue;
                    PackItem p = new PackItem();
                    p.Name = J(d, "Name", "name", L.T("名称"));
                    p.ModCount = J(d, "ModCount", "modCount", L.T("MOD数量"));
                    p.GameVersion = J(d, "GameVersion", "gameVersion", L.T("游戏版"), L.T("版本"));
                    p.CopyCount = J(d, "CopyCount", L.T("副本数量"));
                    p.Storytellers = J(d, "Storytellers", L.T("讲述者数"));
                    p.LoadTime = J(d, "LoadTime", L.T("游戏加载时间"), L.T("加载时间"));
                    p.MaxFps = J(d, "MaxFps", L.T("最高fps"));
                    p.Rjw = J(d, "Rjw", L.T("RJW内容"));
                    p.ZipSize = J(d, "ZipSize", L.T("压缩包"));
                    p.UnzippedSize = J(d, "UnzippedSize", L.T("解压大小"));
                    p.QuarkUrl = J(d, "QuarkUrl", L.T("夸克"), L.T("夸克网盘下载链接"));
                    p.XunleiUrl = J(d, "XunleiUrl", L.T("迅雷"), L.T("迅雷云盘"));
                    p.Description = J(d, "Description", L.T("内容简介"), L.T("简介"));
                    p.Cover = J(d, "Cover", L.T("封面"), L.T("封面预览图"));
                    if (!string.IsNullOrEmpty(p.Name)) list.Add(p);
                }
            }
            catch (Exception ex) { Log.Warn(L.T("解析整合包 JSON 失败：") + ex.Message); }
            return list;
        }

        private static string J(Dictionary<string, object> d, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                object v;
                if (d.TryGetValue(keys[i], out v) && v != null)
                {
                    string s = Convert.ToString(v);
                    if (!string.IsNullOrEmpty(s)) return s.Trim();
                }
            }
            return "";
        }

        /// <summary>封面列可能是一句「暂无预览图」，只有 http 开头才当图片用。</summary>
        public static bool LooksLikeUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string t = s.Trim();
            return t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>把封面下载到本地缓存；返回本地文件路径，失败返回 null。</summary>
        public static string DownloadCover(string url)
        {
            if (!LooksLikeUrl(url)) return null;
            try
            {
                string dir = CoverDir();
                string name = Math.Abs(url.GetHashCode()).ToString("X8") + ".img";
                string path = Path.Combine(dir, name);
                if (File.Exists(path) && new FileInfo(path).Length > 0) return path;

                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url.Trim());
                req.Method = "GET";
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                req.UserAgent = "Mozilla/5.0 ZOGOS-Launcher/" + Log.Version;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[8192];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
                }
                return new FileInfo(path).Length > 0 ? path : null;
            }
            catch { return null; }
        }
    }
}
