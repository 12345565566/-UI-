using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using Microsoft.Win32;
using System.Text;
using System.Text.RegularExpressions;

namespace Zogos
{
    public class WorkshopItem
    {
        public string Id;
        public string Title;
        public string Description;
        public string PreviewUrl;
        public int Subscriptions;
        public string ItemType = "mod";
        public List<string> Tags = new List<string>();
        public string Url { get { return "https://steamcommunity.com/sharedfiles/filedetails/?id=" + Id; } }
    }

    public static class WorkshopClient
    {
        public const string RimWorldAppId = "294100";

        public static List<WorkshopItem> Search(string query, out string err)
        {
            return Search(query, "", false, out err);
        }

        public static List<WorkshopItem> Search(string query, string version, bool collections, out string err)
        {
            List<WorkshopItem> list = new List<WorkshopItem>();
            err = null;
            if (string.IsNullOrEmpty(query)) { err = L.T("搜索词是空的"); return list; }
            try
            {
                string section = collections ? "collections" : "readytouseitems";
                string url = "https://steamcommunity.com/workshop/browse/?appid=" + RimWorldAppId
                    + "&searchtext=" + Uri.EscapeDataString(query)
                    + "&browsesort=textsearch&section=" + section + "&actualsort=textsearch&p=1";
                if (!string.IsNullOrEmpty(version)) url += "&requiredtags%5B%5D=" + Uri.EscapeDataString(version);
                string html = Get(url, out err);
                if (html == null) return list;
                list = ParseHtml(html, out err);
                for (int i = 0; i < list.Count; i++) list[i].ItemType = collections ? "collection" : "mod";
                return list;
            }
            catch (Exception ex) { err = ex.Message; return list; }
        }

        public static List<WorkshopItem> ParseHtml(string html, out string err)
        {
            List<WorkshopItem> list = new List<WorkshopItem>();
            err = null;
            if (string.IsNullOrEmpty(html)) { err = L.T("页面是空的"); return list; }
            try
            {
                // Steam 把结果 JSON 塞在 JS 字符串里，先还原转义引号
                html = html.Replace("\\\"", "\"").Replace("\\\\", "\\");
                MatchCollection ids = Regex.Matches(html, "\"publishedfileid\":\"(\\d+)\"");
                HashSet<string> seen = new HashSet<string>();
                for (int i = 0; i < ids.Count; i++)
                {
                    int start = ids[i].Index;
                    int end = (i + 1 < ids.Count) ? ids[i + 1].Index : Math.Min(html.Length, start + 20000);
                    string chunk = html.Substring(start, Math.Max(0, end - start));
                    string id = ids[i].Groups[1].Value;
                    if (!seen.Add(id)) continue;
                    string title = ExtractJsonString(chunk, "title");
                    if (string.IsNullOrEmpty(title)) continue;
                    WorkshopItem item = new WorkshopItem();
                    item.Id = id;
                    item.Title = WebUtility.HtmlDecode(Unescape(title));
                    item.Description = WebUtility.HtmlDecode(Unescape(ExtractJsonString(chunk, "short_description")));
                    item.PreviewUrl = ExtractJsonString(chunk, "preview_url");
                    Match tags = Regex.Match(chunk, "\"tags\":\\[(.*?)\\]", RegexOptions.Singleline);
                    if (tags.Success)
                    {
                        MatchCollection tagMatches = Regex.Matches(tags.Groups[1].Value, "\"tag\":\"((?:\\.|[^\"])*)\"");
                        for (int ti = 0; ti < tagMatches.Count; ti++) item.Tags.Add(Unescape(tagMatches[ti].Groups[1].Value));
                    }
                    Match subs = Regex.Match(chunk, "\"subscriptions\":(\\d+)");
                    if (subs.Success) { int n; int.TryParse(subs.Groups[1].Value, out n); item.Subscriptions = n; }
                    list.Add(item);
                    if (list.Count >= 30) break;
                }
                if (list.Count == 0) err = L.T("搜索页打开了，但没有解析到模组。Steam 页面结构可能变了。");
            }
            catch (Exception ex) { err = ex.Message; }
            return list;
        }

        public static string TranslateQuery(Settings st, string text, out bool usedAi, out string err)
        {
            usedAi = false;
            err = null;
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Trim();
            if (!HasChinese(text)) return text;
            if (st == null || string.IsNullOrEmpty(st.ApiBase) || string.IsNullOrEmpty(st.ApiModel))
            {
                err = L.T("未配置苏苏 AI，已用本地词典翻译");
                return LocalTranslate(text);
            }
            try
            {
                string system = L.T("你是 RimWorld 创意工坊搜索关键词翻译器。把用户的中文需求翻译成适合 Steam 创意工坊搜索的英文关键词。")
                    + L.T("只输出英文关键词，不要解释，不要加引号，不要输出中文。保留模组专有名词。");
                string user = L.T("翻译成英文搜索关键词：") + text;
                string res = AiClient.Chat(st.ApiBase, st.ApiKey, st.ApiModel, system, user, 30000, out err);
                if (string.IsNullOrEmpty(res))
                {
                    err = L.T("AI 翻译失败：") + (err == null ? L.T("没有返回内容") : err) + L.T("；已用本地词典");
                    return LocalTranslate(text);
                }
                res = CleanTranslation(res);
                if (res.Length == 0) { err = L.T("AI 翻译结果为空，已用本地词典"); return LocalTranslate(text); }
                usedAi = true;
                return res;
            }
            catch (Exception ex)
            {
                err = L.T("AI 翻译失败：") + ex.Message + L.T("；已用本地词典");
                return LocalTranslate(text);
            }
        }

        public static string LocalTranslate(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = text;
            string[,] map = new string[,] {
                {L.T("鼠族"), "ratkin"}, {L.T("老鼠"), "rat"}, {L.T("鼠鼠"), "rat"}, {L.T("鼠"), "rat"}, {L.T("脸部动画"), "facial animation"}, {L.T("面部动画"), "facial animation"}, {L.T("动画"), "animation"},
                {L.T("种族"), "race"}, {L.T("派系"), "faction"}, {L.T("武器"), "weapon"}, {L.T("装备"), "equipment"},
                {L.T("服装"), "apparel"}, {L.T("医疗"), "medical"}, {L.T("动物"), "animal"}, {L.T("基因"), "gene"},
                {L.T("机械族"), "mech"}, {L.T("机械"), "mech"}, {L.T("家具"), "furniture"}, {L.T("存储"), "storage"},
                {L.T("电力"), "power"}, {L.T("农业"), "farming"}, {L.T("食物"), "food"}, {L.T("烹饪"), "cooking"},
                {L.T("战斗"), "combat"}, {L.T("界面"), "UI"}, {L.T("汉化"), "Chinese translation"}, {L.T("中文"), "Chinese"},
                {L.T("兼容"), "compatibility"}, {L.T("补丁"), "patch"}, {L.T("框架"), "framework"}, {L.T("前置"), "library"},
                {L.T("美化"), "retexture"}, {L.T("头发"), "hair"}, {L.T("建筑"), "building"}, {L.T("地形"), "terrain"},
                {L.T("载具"), "vehicle"}, {L.T("太空"), "space"}, {L.T("飞船"), "ship"}, {L.T("吸血鬼"), "vampire"},
                {L.T("魔法"), "magic"}, {L.T("赛博"), "cyber"}, {L.T("中世纪"), "medieval"}, {L.T("边缘世界"), "RimWorld"}
            };
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < map.GetLength(0); i++)
            {
                if (s.IndexOf(map[i, 0], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string term = map[i, 1];
                    if (sb.ToString().IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        if (sb.Length > 0) sb.Append(" ");
                        sb.Append(term);
                    }
                }
            }
            if (sb.Length > 0)
            {
                MatchCollection en = Regex.Matches(s, "[A-Za-z][A-Za-z0-9_\\-]{2,}");
                for (int i = 0; i < en.Count; i++)
                {
                    string w = en[i].Value;
                    if (sb.ToString().IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0) sb.Append(" ").Append(w);
                }
                return sb.ToString().Trim();
            }
            return s;
        }

        public static string FindWorkshopContentDir()
        {
            List<string> roots = new List<string>();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("SteamPath");
                        if (v != null) roots.Add(Path.Combine(v.ToString().Replace('/', '\\'), "steamapps"));
                    }
                }
            }
            catch { }
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Valve\\Steam"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("InstallPath");
                        if (v != null) roots.Add(Path.Combine(v.ToString().Replace('/', '\\'), "steamapps"));
                    }
                }
            }
            catch { }
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps"));
            roots.Add(Path.Combine("D:\\Steam", "steamapps"));
            roots.Add(Path.Combine("D:\\steam", "steamapps"));
            for (int i = 0; i < roots.Count; i++)
            {
                string p = Path.Combine(Path.Combine(Path.Combine(roots[i], "workshop"), "content"), RimWorldAppId);
                if (Directory.Exists(p)) return p;
            }
            return null;
        }

        public static bool DownloadToPack(PackContext ctx, string id, bool collection, out string dests, out string err)
        {
            dests = null;
            err = null;
            if (ctx == null || string.IsNullOrEmpty(ctx.ModsDir)) { err = L.T("没有找到整合包 Mods 目录"); return false; }
            string root = FindWorkshopContentDir();
            if (string.IsNullOrEmpty(root)) { err = L.T("没有找到 Steam 创意工坊下载目录"); return false; }
            HashSet<string> before = SnapshotDirs(root);
            string dummy;
            if (!OpenSteamDownload(id, out dummy)) { err = dummy; return false; }
            DateTime end = DateTime.Now.AddMinutes(4);
            List<string> copied = new List<string>();
            while (DateTime.Now < end)
            {
                Thread.Sleep(2000);
                List<string> candidates = new List<string>();
                if (!collection)
                {
                    string one = Path.Combine(root, id);
                    if (Directory.Exists(one)) candidates.Add(one);
                }
                else
                {
                    string[] dirs = Directory.GetDirectories(root);
                    for (int i = 0; i < dirs.Length; i++) if (!before.Contains(Path.GetFileName(dirs[i]))) candidates.Add(dirs[i]);
                }
                for (int i = 0; i < candidates.Count; i++)
                {
                    string src = candidates[i];
                    if (!File.Exists(Path.Combine(Path.Combine(src, "About"), "About.xml"))) continue;
                    string dest = Path.Combine(ctx.ModsDir, Path.GetFileName(src));
                    if (Directory.Exists(dest)) { try { FileOps.ClearDir(dest, null); } catch { } }
                    List<string> errs = new List<string>();
                    FileOps.CopyTree(src, dest, errs, false);
                    copied.Add(dest);
                }
                if (copied.Count > 0)
                {
                    if (collection) Thread.Sleep(4000);
                    dests = string.Join("\r\n", copied.ToArray());
                    Log.Info(L.T("创意工坊下载完成并复制到 Mods：") + dests);
                    return true;
                }
            }
            err = L.T("Steam 下载超时，或该合集没有可复制的模组。请确认 Steam 已登录并完成订阅。");
            return false;
        }

        private static HashSet<string> SnapshotDirs(string root)
        {
            HashSet<string> set = new HashSet<string>();
            try { string[] dirs = Directory.GetDirectories(root); for (int i = 0; i < dirs.Length; i++) set.Add(Path.GetFileName(dirs[i])); } catch { }
            return set;
        }

        public static bool OpenSteamDownload(string id, out string err)
        {
            err = null;
            if (string.IsNullOrEmpty(id)) { err = L.T("模组 id 是空的"); return false; }
            try
            {
                try { SteamHelp.EnsureRunning(); } catch { }
                Process.Start("steam://workshop_download_item/" + RimWorldAppId + "/" + id);
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        public static bool OpenPage(string id, out string err)
        {
            err = null;
            if (string.IsNullOrEmpty(id)) { err = L.T("模组 id 是空的"); return false; }
            try
            {
                Process.Start("https://steamcommunity.com/sharedfiles/filedetails/?id=" + id);
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 自动选后端：配了 API Key 就走 api.steampowered.com（不需要梯子）；
        /// 没配或没结果，再退回网页抓取（需要梯子）。
        /// </summary>
        /// <summary>
        /// 完全离线的本地检索：只查本机已装的 Mods + 工坊缓存，不需要任何网络。
        /// 注意：搜索词可能是翻译出来的一串（例如 "Ratkin rat mouse"），
        /// 必须按词切分后「任一词命中即算」，整串当子串匹配会一条都搜不到。
        /// </summary>
        public static List<WorkshopItem> LocalSearch(string query, string modsDir, int max)
        {
            List<WorkshopItem> list = new List<WorkshopItem>();
            if (string.IsNullOrEmpty(query)) return list;

            char[] seps = new char[] { ' ', '\t', ',', ';', '|', '/', '\\', '，', '、', '+' };
            string[] raw = query.ToLowerInvariant().Split(seps, StringSplitOptions.RemoveEmptyEntries);
            List<string> tokens = new List<string>();
            for (int i = 0; i < raw.Length && tokens.Count < 8; i++)
            {
                string t = raw[i].Trim();
                if (t.Length >= 2 && !tokens.Contains(t)) tokens.Add(t);
            }
            if (tokens.Count == 0) return list;

            List<ModEntry> all = new List<ModEntry>();
            try { all.AddRange(ModCatalog.ScanRoot(modsDir, false)); } catch { }
            try { all.AddRange(ModCatalog.ScanRoot(FindWorkshopContentDir(), true)); } catch { }

            List<int> scores = new List<int>();
            List<ModEntry> hits = new List<ModEntry>();
            for (int i = 0; i < all.Count; i++)
            {
                ModEntry e = all[i];
                string nm = e.Name == null ? "" : e.Name.ToLowerInvariant();
                string pk = e.PackageId == null ? "" : e.PackageId.ToLowerInvariant();
                int score = 0;
                for (int k = 0; k < tokens.Count; k++)
                {
                    if (nm.IndexOf(tokens[k], StringComparison.Ordinal) >= 0) score += 3;
                    else if (pk.IndexOf(tokens[k], StringComparison.Ordinal) >= 0) score += 1;
                }
                if (score <= 0) continue;

                int pos = scores.Count;
                for (int k = 0; k < scores.Count; k++)
                {
                    if (score > scores[k]) { pos = k; break; }
                }
                scores.Insert(pos, score);
                hits.Insert(pos, e);
                if (hits.Count > max * 3)
                {
                    scores.RemoveAt(scores.Count - 1);
                    hits.RemoveAt(hits.Count - 1);
                }
            }

            for (int i = 0; i < hits.Count && list.Count < max; i++)
            {
                ModEntry e = hits[i];
                WorkshopItem it = new WorkshopItem();
                it.Id = e.PublishedId;
                it.Title = e.Name;
                string pub = string.IsNullOrEmpty(e.PublishedId) ? L.T("（无工坊id）") : e.PublishedId;
                it.Description = L.T("本地已装  id=") + pub + "   " + e.PackageId + "     " + e.Dir;
                it.ItemType = "local";
                list.Add(it);
            }
            return list;
        }

        public static List<WorkshopItem> SearchAuto(Settings st, string query, string version, bool collections,
            string modsDir, string originalQuery, out string err, out string backend, out int total)
        {
            err = null;
            backend = "";
            total = 0;
            Log.Info(L.T("创意工坊搜索：<") + query + L.T(">  版本=") + (string.IsNullOrEmpty(version) ? L.T("全部") : version)
                + L.T("  合集=") + (collections ? L.T("是") : L.T("否")) + "  APIKey=" + (WorkshopWebApi.HasKey(st) ? L.T("有") : L.T("无")));

            if (!collections && WorkshopWebApi.HasKey(st))
            {
                string e1;
                int t1;
                List<WorkshopItem> r1 = WorkshopWebApi.QueryFiles(st, query, version, 1, 30, out e1, out t1);
                Log.Info(L.T("  └ 官方接口：") + r1.Count + L.T(" 条") + (e1 == null ? "" : L.T("，错误：") + e1));
                if (r1.Count > 0) { backend = L.T("官方接口（不需要梯子）"); total = t1; return r1; }
                err = L.T("官方接口：") + (e1 == null ? L.T("没有结果") : e1);
            }

            string e2;
            List<WorkshopItem> r2 = Search(query, version, collections, out e2);
            Log.Info(L.T("  └ 网页抓取：") + r2.Count + L.T(" 条") + (e2 == null ? "" : L.T("，错误：") + e2));
            if (r2.Count > 0) { backend = L.T("网页抓取（需要梯子）"); total = r2.Count; return r2; }
            if (string.IsNullOrEmpty(err)) err = e2;
            else err = err + L.T("；网页抓取：") + e2;

            // 注意：这里【不再】把「本地已装模组」混进搜索结果。
            // 用户要的是创意工坊上的模组，拿本地已装的去凑数会误导。
            // 本地检索能力保留在 LocalSearch()，供别的入口显式使用。
            Log.Warn(L.T("创意工坊搜索失败（官方接口无 key / 网页被屏蔽）：") + err);
            return r2;
        }

        private static string Get(string url, out string err)
        {
            err = null;
            try
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 30000;
                req.ReadWriteTimeout = 30000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ZOGOS-Launcher/" + Log.Version;
                req.Accept = "text/html,application/xhtml+xml";
                req.Headers["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8";
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream rs = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(rs, Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (WebException wex)
            {
                err = wex.Message;
                return null;
            }
            catch (Exception ex) { err = ex.Message; return null; }
        }

        private static string ExtractJsonString(string chunk, string key)
        {
            Match m = Regex.Match(chunk, "\"" + Regex.Escape(key) + "\":\"((?:\\\\.|[^\"])*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string uPrefix = "\\" + "u";
            s = Regex.Replace(s, Regex.Escape(uPrefix) + "([0-9a-fA-F]{4})", delegate(Match m)
            {
                return ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString();
            });
            s = s.Replace("\\\"", "\"");
            s = s.Replace("\\n", "\n");
            s = s.Replace("\\r", "\r");
            s = s.Replace("\\t", "\t");
            s = s.Replace("\\\\", "\\");
            return s;
        }

        private static bool HasChinese(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] >= 0x4E00 && s[i] <= 0x9FFF) return true;
            return false;
        }

        private static string CleanTranslation(string res)
        {
            if (res == null) return "";
            string[] lines = res.Replace("\r", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim().Trim('"').Trim();
                line = line.Replace("'", "").Trim();
                if (line.Length == 0) continue;
                int colon = line.LastIndexOf(':');
                if (colon >= 0 && colon + 1 < line.Length) line = line.Substring(colon + 1).Trim();
                if (Regex.IsMatch(line, "[A-Za-z]")) return line;
            }
            return res.Trim();
        }
    }
}
