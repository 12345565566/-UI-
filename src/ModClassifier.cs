using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Zogos
{
    public static class ModClassifier
    {
        public static Dictionary<string, string> Classify(PackContext ctx, SortScheme scheme, Settings st, out string err)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            err = null;
            if (scheme == null) { err = L.T("没有排序方案"); return map; }
            List<ModIssue> issues = new List<ModIssue>();
            Dictionary<string, InstalledMod> installed = ModAnalyzer.ScanInstalled(ctx, issues);
            StringBuilder list = new StringBuilder();
            List<string> active = new List<string>();
            for (int i = 0; i < scheme.Mods.Count; i++)
            {
                string id = scheme.Mods[i] == null ? "" : scheme.Mods[i].Trim().ToLowerInvariant();
                if (id.Length == 0 || active.Contains(id)) continue;
                active.Add(id);
                InstalledMod m;
                installed.TryGetValue(id, out m);
                list.Append(id).Append('|').Append(m == null ? id : m.Name).Append('\n');
            }
            if (st == null || string.IsNullOrEmpty(st.ApiBase) || string.IsNullOrEmpty(st.ApiModel))
            {
                err = L.T("未配置 AI，使用本地关键词分类");
                Fallback(installed, active, map);
                return map;
            }
            try
            {
                string system = L.T("你是 RimWorld 模组分类器。把每个模组分到以下类别之一：前置库、种族、派系、武器装备、玩法、界面、美化、汉化、兼容补丁、其他。")
                    + L.T("只输出每行一个结果，格式必须是：类别|packageId。不要解释，不要 Markdown，不要表头。");
                string user = L.T("请分类这些模组，每行分别是 packageId|名称：\n") + list.ToString();
                string res = AiClient.Chat(st.ApiBase, st.ApiKey, st.ApiModel, system, user, 120000, out err);
                if (!string.IsNullOrEmpty(res))
                {
                    string[] lines = res.Replace("\r", "\n").Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        int bar = line.IndexOf('|');
                        if (bar <= 0) continue;
                        string cat = line.Substring(0, bar).Trim();
                        string id = line.Substring(bar + 1).Trim().ToLowerInvariant();
                        if (ContainsCategory(cat) && active.Contains(id)) map[id] = cat;
                    }
                }
                if (map.Count < Math.Max(1, active.Count / 3))
                {
                    err = L.T("AI 返回的分类不完整，已用本地关键词补齐");
                    Fallback(installed, active, map);
                }
            }
            catch (Exception ex)
            {
                err = L.T("AI 分类失败：") + ex.Message + L.T("，已用本地关键词分类");
                Fallback(installed, active, map);
            }
            if (map.Count < active.Count) Fallback(installed, active, map);
            return map;
        }

        public static string TranslateDescription(Settings st, string text, out string err)
        {
            err = null;
            if (string.IsNullOrEmpty(text)) { err = L.T("没有可翻译的说明"); return null; }
            if (st == null || string.IsNullOrEmpty(st.ApiBase) || string.IsNullOrEmpty(st.ApiModel))
            {
                err = L.T("未配置 AI，无法翻译");
                return null;
            }
            try
            {
                if (text.Length > 8000) text = text.Substring(0, 8000);
                string system = L.T("你是 RimWorld 模组说明翻译器。把下面的英文模组说明翻译成简体中文，保留专有名词和模组名，不要解释，只输出中文译文。");
                string user = L.T("翻译这段模组说明：\n") + text;
                string res = AiClient.Chat(st.ApiBase, st.ApiKey, st.ApiModel, system, user, 90000, out err);
                if (string.IsNullOrEmpty(res)) { err = L.T("AI 没有返回翻译：") + err; return null; }
                return res.Trim();
            }
            catch (Exception ex) { err = ex.Message; return null; }
        }

        public static string AiRiskCheck(PackContext ctx, SortScheme scheme, ModAnalysisResult analysis, Settings st, out string err)
        {
            err = null;
            if (st == null || string.IsNullOrEmpty(st.ApiBase) || string.IsNullOrEmpty(st.ApiModel))
            {
                err = L.T("未配置 AI，跳过 AI 风险复核");
                return "UNKNOWN";
            }
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(L.T("方案：") + (scheme == null ? "-" : scheme.Name));
                sb.AppendLine(L.T("模组数：") + (scheme == null ? 0 : scheme.Mods.Count));
                if (analysis != null)
                {
                    sb.AppendLine(L.T("本地体检错误：") + analysis.ErrorCount + L.T("，警告：") + analysis.WarningCount + L.T("，疑似模组：") + analysis.Suspects.Count);
                    for (int i = 0; i < analysis.Issues.Count && i < 30; i++) sb.AppendLine(analysis.Issues[i].ToLine());
                }
                string system = L.T("你是 RimWorld 整合包风险审查员。根据给出的模组列表和体检结果判断风险。")
                    + L.T("只输出一行：SAFE 或 RISK: 一句话原因。不要输出其它内容。");
                string res = AiClient.Chat(st.ApiBase, st.ApiKey, st.ApiModel, system, sb.ToString(), 60000, out err);
                if (string.IsNullOrEmpty(res)) { err = L.T("AI 没有返回内容"); return "UNKNOWN"; }
                res = res.Trim();
                if (res.StartsWith("RISK", StringComparison.OrdinalIgnoreCase)) return res;
                if (res.StartsWith("SAFE", StringComparison.OrdinalIgnoreCase)) return "SAFE";
                err = L.T("AI 返回格式无法识别：") + res;
                return "UNKNOWN";
            }
            catch (Exception ex) { err = ex.Message; return "UNKNOWN"; }
        }

        private static void Fallback(Dictionary<string, InstalledMod> installed, List<string> active, Dictionary<string, string> map)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (map.ContainsKey(active[i])) continue;
                InstalledMod m;
                installed.TryGetValue(active[i], out m);
                map[active[i]] = ModGraphBuilder.HeuristicCategory(m, active[i]);
            }
        }

        private static bool ContainsCategory(string cat)
        {
            for (int i = 0; i < ModGraphBuilder.DefaultCategories.Length; i++)
                if (ModGraphBuilder.DefaultCategories[i] == cat) return true;
            return false;
        }
    }
}
