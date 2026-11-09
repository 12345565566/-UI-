using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace Zogos
{
    /// <summary>
    /// OpenAI 兼容接口的极简客户端（/models 和 /chat/completions）。
    /// 只要是兼容 OpenAI 的服务（官方 / 中转 / 本地 ollama / LM Studio）都能用。
    /// </summary>
    public static class AiClient
    {
        public static string Normalize(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl)) return "";
            string b = baseUrl.Trim().TrimEnd('/');
            if (b.EndsWith("/chat/completions")) b = b.Substring(0, b.Length - "/chat/completions".Length).TrimEnd('/');
            return b;
        }

        private static string Post(string url, string key, string json, int timeoutMs, out string err)
        {
            err = null;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "ZOGOS-Launcher/" + Log.Version;
                if (!string.IsNullOrEmpty(key)) req.Headers["Authorization"] = "Bearer " + key;

                byte[] body = Encoding.UTF8.GetBytes(json);
                req.ContentLength = body.Length;
                using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream rs = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(rs, Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (WebException wex)
            {
                try
                {
                    if (wex.Response != null)
                    {
                        using (Stream rs = wex.Response.GetResponseStream())
                        using (StreamReader sr = new StreamReader(rs, Encoding.UTF8))
                            err = L.T("HTTP 错误：") + sr.ReadToEnd();
                    }
                    else err = wex.Message;
                }
                catch { err = wex.Message; }
                return null;
            }
            catch (Exception ex) { err = ex.Message; return null; }
        }

        private static string Get(string url, string key, int timeoutMs, out string err)
        {
            err = null;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.UserAgent = "ZOGOS-Launcher/" + Log.Version;
                if (!string.IsNullOrEmpty(key)) req.Headers["Authorization"] = "Bearer " + key;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream rs = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(rs, Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (WebException wex)
            {
                try
                {
                    if (wex.Response != null)
                    {
                        using (Stream rs = wex.Response.GetResponseStream())
                        using (StreamReader sr = new StreamReader(rs, Encoding.UTF8))
                            err = L.T("HTTP 错误：") + sr.ReadToEnd();
                    }
                    else err = wex.Message;
                }
                catch { err = wex.Message; }
                return null;
            }
            catch (Exception ex) { err = ex.Message; return null; }
        }

        /// <summary>拉取模型列表（GET {base}/models）。</summary>
        public static List<string> ListModels(string baseUrl, string key, out string err)
        {
            List<string> res = new List<string>();
            string b = Normalize(baseUrl);
            if (b.Length == 0) { err = L.T("API 地址是空的"); return res; }
            string txt = Get(b + "/models", key, 20000, out err);
            if (txt == null) return res;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 8 * 1024 * 1024;
                Dictionary<string, object> root = js.Deserialize<Dictionary<string, object>>(txt);
                object data;
                if (root != null && root.TryGetValue("data", out data))
                {
                    object[] arr = data as object[];
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Length; i++)
                        {
                            Dictionary<string, object> m = arr[i] as Dictionary<string, object>;
                            if (m == null) continue;
                            object id;
                            if (m.TryGetValue("id", out id) && id != null) res.Add(id.ToString());
                        }
                    }
                }
                res.Sort(StringComparer.OrdinalIgnoreCase);
                if (res.Count == 0) err = L.T("接口通了，但没返回任何模型");
            }
            catch (Exception ex) { err = L.T("解析模型列表失败：") + ex.Message; }
            return res;
        }

        /// <summary>发一次对话（POST {base}/chat/completions）。</summary>
        public static string Chat(string baseUrl, string key, string model, string system, string user, int timeoutMs, out string err)
        {
            err = null;
            string b = Normalize(baseUrl);
            if (b.Length == 0) { err = L.T("API 地址是空的"); return null; }
            if (string.IsNullOrEmpty(model)) { err = L.T("没有选择模型"); return null; }

            Dictionary<string, object> sys = new Dictionary<string, object>();
            sys["role"] = "system";
            sys["content"] = system;
            Dictionary<string, object> usr = new Dictionary<string, object>();
            usr["role"] = "user";
            usr["content"] = user;

            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = model;
            body["temperature"] = 0.3;
            body["stream"] = false;
            body["messages"] = new object[] { sys, usr };

            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 16 * 1024 * 1024;
            string json = js.Serialize(body);

            string txt = Post(b + "/chat/completions", key, json, timeoutMs, out err);
            if (txt == null) return null;
            try
            {
                string content = ExtractChatContent(txt, js);
                if (!string.IsNullOrEmpty(content)) return content;
                err = L.T("AI 返回里没有内容：") + (txt.Length > 600 ? txt.Substring(0, 600) : txt);
            }
            catch (Exception ex) { err = L.T("解析 AI 返回失败：") + ex.Message; }
            return null;
        }

        private static string ExtractChatContent(string txt, JavaScriptSerializer js)
        {
            if (string.IsNullOrEmpty(txt)) return null;
            if (txt.IndexOf("data:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                StringBuilder streamed = new StringBuilder();
                string[] lines = txt.Replace("\r", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                    string part = line.Substring(5).Trim();
                    if (part.Length == 0 || part == "[DONE]") continue;
                    try
                    {
                        string c = ExtractJsonChatContent(js.Deserialize<Dictionary<string, object>>(part), js);
                        if (!string.IsNullOrEmpty(c)) streamed.Append(c);
                    }
                    catch { }
                }
                if (streamed.Length > 0) return streamed.ToString();
            }
            try { return ExtractJsonChatContent(js.Deserialize<Dictionary<string, object>>(txt), js); }
            catch { return null; }
        }

        private static string ExtractJsonChatContent(Dictionary<string, object> root, JavaScriptSerializer js)
        {
            if (root == null) return null;
            object choices;
            if (!root.TryGetValue("choices", out choices)) return null;
            IList arr = choices as IList;
            if (arr == null)
            {
                object[] tmp = choices as object[];
                if (tmp != null) arr = tmp;
            }
            if (arr == null || arr.Count == 0) return null;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < arr.Count; i++)
            {
                Dictionary<string, object> c = arr[i] as Dictionary<string, object>;
                if (c == null) continue;
                object msg, delta, text;
                if (c.TryGetValue("message", out msg))
                {
                    Dictionary<string, object> m = msg as Dictionary<string, object>;
                    if (m != null)
                    {
                        object content;
                        if (m.TryGetValue("content", out content))
                        {
                            string s1 = ContentToString(content);
                            if (!string.IsNullOrEmpty(s1)) sb.Append(s1);
                        }
                    }
                }
                if (c.TryGetValue("delta", out delta))
                {
                    Dictionary<string, object> d = delta as Dictionary<string, object>;
                    if (d != null)
                    {
                        object content;
                        if (d.TryGetValue("content", out content))
                        {
                            string s2 = ContentToString(content);
                            if (!string.IsNullOrEmpty(s2)) sb.Append(s2);
                        }
                    }
                }
                if (c.TryGetValue("text", out text) && text != null) sb.Append(text.ToString());
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        private static string ContentToString(object content)
        {
            if (content == null) return null;
            string s = content as string;
            if (s != null) return s;
            IList arr = content as IList;
            if (arr == null)
            {
                object[] tmp = content as object[];
                if (tmp != null) arr = tmp;
            }
            if (arr != null)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < arr.Count; i++)
                {
                    Dictionary<string, object> p = arr[i] as Dictionary<string, object>;
                    if (p == null) continue;
                    object t;
                    if (p.TryGetValue("text", out t) && t != null) sb.Append(t.ToString());
                    else if (p.TryGetValue("content", out t) && t != null) sb.Append(t.ToString());
                }
                return sb.Length == 0 ? null : sb.ToString();
            }
            return content.ToString();
        }

        /// <summary>系统提示词：把已知结论喂给 AI，避免它瞎猜。</summary>
        public static string SystemPrompt()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("你是 RimWorld（边缘世界）整合包的技术支持助手，服务对象是不太懂电脑的普通玩家。"));
            sb.AppendLine(L.T("回答要求："));
            sb.AppendLine(L.T("1. 用简体中文，大白话，别堆术语；总长度控制在 300 字以内。"));
            sb.AppendLine(L.T("2. 先给一句「结论」，再给 2~4 条按优先级排序的处理步骤，每条不超过两行。"));
            sb.AppendLine(L.T("3. 不要重复用户已知的信息，不要编造日志里没有的东西。"));
            sb.AppendLine();
            sb.AppendLine(L.T("【这个整合包已知的坑（务必优先考虑）】"));
            sb.AppendLine(L.T("- RimWorld 1.7 换了新引擎，没有做显存不够的保护：核显或显存小于 4G 的电脑会爆显存，"));
            sb.AppendLine(L.T("  表现为黑屏闪退，退出码常见 0xC0000005（-1073741819）。解法是打 Steam 创意工坊的"));
            sb.AppendLine(L.T("  「1.7 显存分配补丁」；但大显存用户打这个补丁是负提升，不要推荐他们装。"));
            sb.AppendLine(L.T("- 整合包路径不能含中文，否则模组加载不出来。"));
            sb.AppendLine(L.T("- 启动器只在第一次或整合包更新时完整替换 Config，之后只更新排序；"));
            sb.AppendLine(L.T("  Prefs.xml（画面/音量）和 KeyPrefs.xml（按键）是玩家私有的，不会被覆盖。"));
            sb.AppendLine(L.T("- 本地模组索引为 0、或模组全缺失，说明整合包没解压完整（可能只解压了一层）。"));
            sb.AppendLine(L.T("- 退出码 0xC0000135/0xC000007B 多是缺运行库或位数不对；0xE0434352 是模组抛异常。"));
            sb.AppendLine();
            sb.AppendLine(L.T("如果日志里看不出明确原因，就说明「日志里没有直接证据」，然后给出最可能的 2 个怀疑方向和处理办法。"));
            return sb.ToString();
        }

        /// <summary>把诊断数据拼成给 AI 的提问（含日志摘录）。</summary>
        public static string BuildUserPrompt(PackContext ctx, Settings st, List<DiagItem> items)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("=== 玩家环境 ==="));
            if (ctx != null)
            {
                sb.AppendLine(L.T("整合包路径: ") + ctx.Root);
                sb.AppendLine(L.T("路径含中文: ") + (ctx.NonAscii ? L.T("是（") + ctx.NonAsciiSample + "）" : L.T("否")));
                sb.AppendLine(L.T("游戏主程序: ") + ctx.GameExe + L.T("  存在=") + File.Exists(ctx.GameExe));
                sb.AppendLine(L.T("游戏版本  : ") + ctx.GameVersion);
                sb.AppendLine(L.T("目标配置  : ") + ctx.TargetConfigDir);
                sb.AppendLine(L.T("游戏数据  : ") + ctx.SaveDataDir + L.T("（便携=") + (ctx.PortableData ? L.T("是") : L.T("否")) + "）");
                sb.AppendLine(L.T("启动参数  : ") + GameLauncher.BuildArguments(ctx));
            }
            sb.AppendLine(L.T("显卡      : ") + SusuDiag.GpuLine());
            sb.AppendLine(L.T("显存判定  : ") + (SusuDiag.RecommendVramPatch ? L.T("核显或小于 4G，属于危险区") : L.T("够用")));
            sb.AppendLine(L.T("本地模组  : ") + ModIndex.Count + L.T(" 个 packageId"));

            sb.AppendLine();
            sb.AppendLine(L.T("=== 启动器自动体检结果 ==="));
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++) sb.AppendLine(items[i].Mark + " " + items[i].Text.Replace("\n", " "));
            }

            sb.AppendLine();
            sb.AppendLine(L.T("=== 启动器日志（尾部 60 行）==="));
            sb.AppendLine(TailLines(Log.Snapshot(), 60));

            sb.AppendLine();
            sb.AppendLine(L.T("=== RimWorld Player.log 里的报错（最近 40 条）==="));
            sb.AppendLine(PlayerLogErrors(ctx, 40));

            sb.AppendLine();
            sb.AppendLine(L.T("请根据以上信息，告诉我这个玩家现在最可能是什么问题、该怎么一步步处理。"));
            return sb.ToString();
        }

        private static string TailLines(string[] all, int n)
        {
            if (all == null || all.Length == 0) return L.T("(空)");
            int s = Math.Max(0, all.Length - n);
            StringBuilder sb = new StringBuilder();
            for (int i = s; i < all.Length; i++) sb.AppendLine(all[i]);
            return sb.ToString();
        }

        public static string PlayerLogErrors(PackContext ctx, int max)
        {
            try
            {
                string log = ctx == null ? null : ctx.PlayerLog;
                if (log == null || !File.Exists(log))
                {
                    if (ctx != null)
                    {
                        string alt = Path.Combine(ctx.GameLogsDir, "Player.log");
                        if (File.Exists(alt)) log = alt;
                    }
                }
                if (log == null || !File.Exists(log)) return L.T("(没有找到 Player.log)");

                string tail = Diag.TailOfFile(log, 300 * 1024);
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
                        if (t.Length > 300) t = t.Substring(0, 300);
                        hits.Add(t);
                    }
                }
                if (hits.Count == 0) return L.T("(日志尾部没有明显报错行)");
                StringBuilder sb = new StringBuilder();
                int s = Math.Max(0, hits.Count - max);
                for (int i = s; i < hits.Count; i++) sb.AppendLine(hits[i]);
                return sb.ToString();
            }
            catch (Exception ex) { return L.T("(读取 Player.log 失败: ") + ex.Message + ")"; }
        }
    }
}
