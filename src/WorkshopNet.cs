using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace Zogos
{
    // =====================================================================
    //  内置 steamcmd：一键下载安装，之后下载工坊内容不需要梯子、不需要 Steam 客户端
    //
    //  说明：steamcmd 只能「按 id 下载」，它没有搜索能力。
    //        搜索走 WorkshopWebApi（api.steampowered.com，不是被墙的 steamcommunity.com）。
    // =====================================================================
    public static class SteamCmdInstall
    {
        private static readonly string[] ZipUrls = new string[] {
            "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip",
            "https://media.steampowered.com/client/installer/steamcmd.zip"
        };

        public static string RootDir()
        {
            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器"));
            return Path.Combine(baseDir, "steamcmd");
        }

        public static string ExePath()
        {
            return Path.Combine(RootDir(), "steamcmd.exe");
        }

        public static bool IsInstalled()
        {
            string p = ExePath();
            return File.Exists(p) && new FileInfo(p).Length > 100000;
        }

        /// <summary>
        /// 确保 steamcmd 可用：没有就下载 zip、解压、跑一次自更新。
        /// progress 用于把进度回显到界面（可能为 null）。
        /// </summary>
        public static bool Ensure(Action<string> progress, int timeoutSeconds, out string err)
        {
            err = null;
            try
            {
                if (IsInstalled())
                {
                    if (progress != null) progress(L.T("steamcmd 已就位：") + ExePath());
                    return true;
                }

                string root = RootDir();
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);

                string zip = Path.Combine(root, "steamcmd.zip");
                string lastErr = null;
                bool got = false;
                for (int i = 0; i < ZipUrls.Length && !got; i++)
                {
                    try
                    {
                        if (progress != null) progress(L.T("正在下载 steamcmd.zip（源 ") + (i + 1) + "/" + ZipUrls.Length + "）…");
                        DownloadFile(ZipUrls[i], zip, 300000);
                        got = File.Exists(zip) && new FileInfo(zip).Length > 500000;
                        if (!got) lastErr = L.T("下载回来的文件不正常");
                    }
                    catch (Exception ex)
                    {
                        lastErr = ex.Message;
                        if (progress != null) progress(L.T("  该源失败：") + ex.Message);
                    }
                }
                if (!got)
                {
                    err = L.T("下载 steamcmd.zip 失败：") + (lastErr == null ? L.T("所有源都不可用") : lastErr)
                        + L.T("。可以手动下载后放到 ") + root;
                    return false;
                }

                if (progress != null) progress(L.T("正在解压…"));
                ExtractZip(zip, root);
                try { File.Delete(zip); } catch { }

                if (!IsInstalled())
                {
                    err = L.T("解压完了但没找到 steamcmd.exe（") + ExePath() + "）";
                    return false;
                }

                if (progress != null) progress(L.T("正在首次初始化 steamcmd（会自更新，稍等）…"));
                Bootstrap(timeoutSeconds, progress);

                if (progress != null) progress(L.T("steamcmd 安装完成：") + ExePath());
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        private static void DownloadFile(string url, string dest, int timeoutMs)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "Mozilla/5.0 ZOGOS-Launcher/" + Log.Version;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream rs = resp.GetResponseStream())
            using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
            {
                byte[] buf = new byte[81920];
                int n;
                while ((n = rs.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
            }
        }

        /// <summary>解压 zip；优先用 .NET 的 ZipFile，不行就退回 PowerShell。</summary>
        private static void ExtractZip(string zip, string dest)
        {
            try
            {
                ZipFile.ExtractToDirectory(zip, dest);
                return;
            }
            catch (Exception ex)
            {
                Log.Warn(L.T("ZipFile 解压失败，改用 PowerShell：") + ex.Message);
            }
            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe");
            psi.Arguments = "-NoProfile -NonInteractive -Command \"Expand-Archive -LiteralPath '" + zip.Replace("'", "''")
                + "' -DestinationPath '" + dest.Replace("'", "''") + "' -Force\"";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit(180000);
            }
        }

        private static void Bootstrap(int timeoutSeconds, Action<string> progress)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(ExePath());
                psi.Arguments = "+quit";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardInput = true;
                psi.WorkingDirectory = RootDir();
                using (Process p = Process.Start(psi))
                {
                    try { p.StandardInput.WriteLine("quit"); p.StandardInput.Flush(); } catch { }
                    p.WaitForExit(timeoutSeconds * 1000);
                }
            }
            catch (Exception ex)
            {
                if (progress != null) progress(L.T("  首次初始化没跑完（不影响后续使用）：") + ex.Message);
            }
        }
    }

    // =====================================================================
    //  免梯子搜索：Steam Web API
    //
    //  · GetPublishedFileDetails —— 不需要 key，按 id 查详情
    //  · QueryFiles               —— 需要免费 API key，可按关键词/标签搜索
    //  两者都在 api.steampowered.com，不是被墙的 steamcommunity.com
    // =====================================================================
    public static class WorkshopWebApi
    {
        private const string BaseApi = "https://api.steampowered.com/";
        private const string BaseApiHttp = "http://api.steampowered.com/";

        /// <summary>
        /// 实测结论（2026-11-01 本机验证）：
        ///   https://api.steampowered.com  → 被墙（TLS/SNI 层拦截，连接直接被重置）
        ///   http ://api.steampowered.com  → 通（明文 HTTP 不在拦截名单里）
        /// 所以这里先试 https（有梯子时走加密），失败自动降级 http。
        /// 注意：走 http 时 API Key 会明文出现在 URL 里，这是无梯子访问的代价。
        /// </summary>
        public static string ApiRequest(string pathAndQuery, string postBody, int timeoutMs,
            out string err, out bool usedPlainHttp)
        {
            usedPlainHttp = false;
            string e1;
            string r1 = postBody == null
                ? HttpGet(BaseApi + pathAndQuery, timeoutMs, out e1)
                : HttpPost(BaseApi + pathAndQuery, postBody, timeoutMs, out e1);
            if (r1 != null && r1.Length > 0)
            {
                err = null;
                return r1;
            }

            string e2;
            string r2 = postBody == null
                ? HttpGet(BaseApiHttp + pathAndQuery, timeoutMs, out e2)
                : HttpPost(BaseApiHttp + pathAndQuery, postBody, timeoutMs, out e2);
            if (r2 != null && r2.Length > 0)
            {
                err = null;
                usedPlainHttp = true;
                Log.Info(L.T("Steam Web API：HTTPS 不通，已降级为明文 HTTP（可用）"));
                return r2;
            }

            err = "HTTPS: " + (e1 == null ? L.T("无响应") : e1) + " / HTTP: " + (e2 == null ? L.T("无响应") : e2);
            return null;
        }

        public static bool HasKey(Settings st)
        {
            return st != null && !string.IsNullOrEmpty(st.SteamApiKey) && st.SteamApiKey.Trim().Length >= 16;
        }

        /// <summary>按关键词搜索工坊（不需要梯子，但需要一个免费 API key）。</summary>
        public static List<WorkshopItem> QueryFiles(Settings st, string searchText, string version,
            int page, int perPage, out string err, out int total)
        {
            List<WorkshopItem> list = new List<WorkshopItem>();
            err = null;
            total = 0;
            if (!HasKey(st)) { err = L.T("没有配置 Steam API Key"); return list; }

            try
            {
                StringBuilder url = new StringBuilder();
                url.Append("IPublishedFileService/QueryFiles/v1/?key=").Append(Uri.EscapeDataString(st.SteamApiKey.Trim()));
                url.Append("&query_type=0");
                url.Append("&page=").Append(page < 1 ? 1 : page);
                url.Append("&numperpage=").Append(perPage <= 0 ? 20 : Math.Min(perPage, 100));
                url.Append("&creator_appid=294100");
                url.Append("&appid=294100");
                url.Append("&filetype=0");
                if (!string.IsNullOrEmpty(searchText)) url.Append("&search_text=").Append(Uri.EscapeDataString(searchText));
                url.Append("&return_short_description=true");
                url.Append("&return_tags=true");
                url.Append("&return_vote_data=false");
                if (!string.IsNullOrEmpty(version)) url.Append("&requiredtags%5B%5D=").Append(Uri.EscapeDataString(version));

                bool plain;
                string json = ApiRequest(url.ToString(), null, 25000, out err, out plain);
                if (json == null) return list;

                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 32 * 1024 * 1024;
                Dictionary<string, object> root = js.Deserialize<Dictionary<string, object>>(json);
                object respObj;
                if (root == null || !root.TryGetValue("response", out respObj)) { err = L.T("返回内容不是预期格式"); return list; }
                Dictionary<string, object> resp = respObj as Dictionary<string, object>;
                if (resp == null) { err = L.T("返回内容不是预期格式"); return list; }

                object t;
                if (resp.TryGetValue("total", out t)) total = ToInt(t);

                object arrObj;
                if (!resp.TryGetValue("publishedfiledetails", out arrObj)) return list;
                object[] arr = arrObj as object[];
                if (arr == null) return list;

                for (int i = 0; i < arr.Length; i++)
                {
                    Dictionary<string, object> d = arr[i] as Dictionary<string, object>;
                    if (d == null) continue;
                    WorkshopItem it = new WorkshopItem();
                    it.Id = Str(d, "publishedfileid");
                    it.Title = Str(d, "title");
                    it.Description = Str(d, "short_description");
                    it.PreviewUrl = Str(d, "preview_url");
                    it.Subscriptions = ToInt_(d, "subscriptions");
                    it.ItemType = "mod";
                    object tagsObj;
                    if (d.TryGetValue("tags", out tagsObj))
                    {
                        object[] ta = tagsObj as object[];
                        if (ta != null)
                        {
                            for (int k = 0; k < ta.Length; k++)
                            {
                                Dictionary<string, object> td = ta[k] as Dictionary<string, object>;
                                if (td == null) continue;
                                string tag = Str(td, "tag");
                                if (!string.IsNullOrEmpty(tag)) it.Tags.Add(tag);
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(it.Id)) list.Add(it);
                }
                if (list.Count == 0) err = L.T("接口通了，但这个关键词没有结果（total=") + total + "）";
                return list;
            }
            catch (Exception ex) { err = ex.Message; return list; }
        }

        /// <summary>
        /// 展开一个工坊「合集」：返回里面的所有物品 id。不需要 key。
        /// 如果这个 id 不是合集，返回空列表（不是错误）。
        /// </summary>
        public static List<string> GetCollectionChildren(string collectionId, out string err)
        {
            err = null;
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(collectionId)) { err = L.T("id 是空的"); return list; }
            try
            {
                StringBuilder body = new StringBuilder();
                body.Append("collectioncount=1&publishedfileids[0]=").Append(collectionId);

                bool plain;
                string json = ApiRequest("ISteamRemoteStorage/GetCollectionDetails/v1/",
                    body.ToString(), 20000, out err, out plain);
                if (json == null) { err = L.T("接口没有返回内容"); return list; }

                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 32 * 1024 * 1024;
                Dictionary<string, object> root = js.Deserialize<Dictionary<string, object>>(json);
                object respObj;
                if (root == null || !root.TryGetValue("response", out respObj)) { err = L.T("返回格式异常"); return list; }
                Dictionary<string, object> resp = respObj as Dictionary<string, object>;
                object arrObj;
                if (resp == null || !resp.TryGetValue("collectiondetails", out arrObj)) return list;
                object[] arr = arrObj as object[];
                if (arr == null || arr.Length == 0) return list;

                Dictionary<string, object> d0 = arr[0] as Dictionary<string, object>;
                if (d0 == null) return list;
                object childrenObj;
                if (!d0.TryGetValue("children", out childrenObj)) return list;   // 没有 children = 不是合集
                object[] children = childrenObj as object[];
                if (children == null) return list;

                for (int i = 0; i < children.Length; i++)
                {
                    Dictionary<string, object> c = children[i] as Dictionary<string, object>;
                    if (c == null) continue;
                    string cid = Str(c, "publishedfileid");
                    if (!string.IsNullOrEmpty(cid)) list.Add(cid);
                }
                err = null;
                return list;
            }
            catch (Exception ex) { err = ex.Message; return list; }
        }

        /// <summary>把一批 id 换成标题等详情（不需要 key）。</summary>
        public static Dictionary<string, string> GetTitles(List<string> ids, out string err)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            err = null;
            if (ids == null || ids.Count == 0) return map;
            try
            {
                StringBuilder body = new StringBuilder();
                body.Append("itemcount=").Append(ids.Count);
                for (int i = 0; i < ids.Count; i++)
                    body.Append("&publishedfileids[").Append(i).Append("]=").Append(ids[i]);

                bool plain;
                string json = ApiRequest("ISteamRemoteStorage/GetPublishedFileDetails/v1/", body.ToString(), 25000, out err, out plain);
                if (json == null) return map;

                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 32 * 1024 * 1024;
                Dictionary<string, object> root = js.Deserialize<Dictionary<string, object>>(json);
                object respObj;
                if (root == null || !root.TryGetValue("response", out respObj)) return map;
                Dictionary<string, object> resp = respObj as Dictionary<string, object>;
                object arrObj;
                if (resp == null || !resp.TryGetValue("publishedfiledetails", out arrObj)) return map;
                object[] arr = arrObj as object[];
                if (arr == null) return map;
                for (int i = 0; i < arr.Length; i++)
                {
                    Dictionary<string, object> d = arr[i] as Dictionary<string, object>;
                    if (d == null) continue;
                    string id = Str(d, "publishedfileid");
                    string title = Str(d, "title");
                    if (!string.IsNullOrEmpty(id)) map[id] = title;
                }
            }
            catch (Exception ex) { err = ex.Message; }
            return map;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v);
        }

        private static int ToInt_(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            return ToInt(v);
        }

        private static int ToInt(object v)
        {
            if (v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        public static string HttpGet(string url, int timeoutMs, out string err)
        {
            err = null;
            try
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "ZOGOS-Launcher/" + Log.Version;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (Exception ex) { err = ex.Message; return null; }
        }

        public static string HttpPost(string url, string body, int timeoutMs, out string err)
        {
            err = null;
            try
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                byte[] data = Encoding.UTF8.GetBytes(body);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/x-www-form-urlencoded";
                req.ContentLength = data.Length;
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "ZOGOS-Launcher/" + Log.Version;
                using (Stream rs = req.GetRequestStream()) rs.Write(data, 0, data.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (Exception ex) { err = ex.Message; return null; }
        }
    }
}
