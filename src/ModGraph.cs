using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Zogos
{
    public class ModGraphNode
    {
        public string Id;
        public string Name;
        /// <summary>中文译名（AI 翻译或本地词表兜底），空表示还没翻。</summary>
        public string NameZh;
        public string Category;
        public bool Enabled = true;
        public bool Warning;
        public int X;
        public int Y;
        public int Index;
        public List<string> Dependencies = new List<string>();
        public InstalledMod Mod;
    }

    public class ModGraphEdge
    {
        public int From;
        public int To;
        public bool Hard;
    }

    public class ModGraphData
    {
        public List<ModGraphNode> Nodes = new List<ModGraphNode>();
        public List<ModGraphEdge> Edges = new List<ModGraphEdge>();
        public List<string> Categories = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    public static class ModGraphBuilder
    {
        public static readonly string[] DefaultCategories = new string[] {
            L.T("前置库"), L.T("种族"), L.T("派系"), L.T("武器装备"), L.T("玩法"), L.T("界面"), L.T("美化"), L.T("汉化"), L.T("兼容补丁"), L.T("其他")
        };

        public static ModGraphData Build(PackContext ctx, SortScheme scheme, Dictionary<string, string> categories)
        {
            ModGraphData data = new ModGraphData();
            if (scheme == null) return data;
            List<ModIssue> issues = new List<ModIssue>();
            Dictionary<string, InstalledMod> installed = ModAnalyzer.ScanInstalled(ctx, issues);
            Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < scheme.Mods.Count; i++)
            {
                string id = scheme.Mods[i] == null ? "" : scheme.Mods[i].Trim().ToLowerInvariant();
                if (id.Length == 0 || index.ContainsKey(id)) continue;
                InstalledMod m;
                installed.TryGetValue(id, out m);
                ModGraphNode node = new ModGraphNode();
                node.Id = id;
                node.Name = m == null ? id : (string.IsNullOrEmpty(m.Name) ? id : m.Name);
                node.Index = data.Nodes.Count;
                node.Category = ClassifierCategory(categories, id, m);
                node.Mod = m;
                if (m != null)
                {
                    for (int d = 0; d < m.Dependencies.Count; d++)
                    {
                        string dep = m.Dependencies[d];
                        if (!string.IsNullOrEmpty(dep)) node.Dependencies.Add(dep);
                    }
                }
                index[id] = node.Index;
                data.Nodes.Add(node);
                if (!data.Categories.Contains(node.Category)) data.Categories.Add(node.Category);
            }

            // dependency edges + loadAfter edges
            for (int i = 0; i < data.Nodes.Count; i++)
            {
                ModGraphNode node = data.Nodes[i];
                InstalledMod m;
                installed.TryGetValue(node.Id, out m);
                if (m == null) continue;
                for (int d = 0; d < node.Dependencies.Count; d++)
                {
                    int from;
                    if (index.TryGetValue(node.Dependencies[d], out from))
                    {
                        ModGraphEdge e = new ModGraphEdge(); e.From = from; e.To = i; e.Hard = true; data.Edges.Add(e);
                    }
                }
                for (int d = 0; d < m.LoadAfter.Count; d++)
                {
                    int from;
                    if (index.TryGetValue(m.LoadAfter[d], out from))
                    {
                        ModGraphEdge e = new ModGraphEdge(); e.From = from; e.To = i; e.Hard = false; data.Edges.Add(e);
                    }
                }
            }

            Layout(data);
            RecomputeWarnings(data);
            return data;
        }

        public static void RecomputeWarnings(ModGraphData data)
        {
            if (data == null) return;
            Dictionary<string, ModGraphNode> map = new Dictionary<string, ModGraphNode>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < data.Nodes.Count; i++) { data.Nodes[i].Warning = false; if (!map.ContainsKey(data.Nodes[i].Id)) map.Add(data.Nodes[i].Id, data.Nodes[i]); }
            data.Warnings.Clear();
            for (int i = 0; i < data.Nodes.Count; i++)
            {
                ModGraphNode n = data.Nodes[i];
                if (!n.Enabled) continue;
                for (int d = 0; d < n.Dependencies.Count; d++)
                {
                    ModGraphNode dep;
                    if (map.TryGetValue(n.Dependencies[d], out dep) && !dep.Enabled)
                    {
                        n.Warning = true;
                        data.Warnings.Add(n.Name + L.T(" 依赖 ") + dep.Name + L.T("，但前置已被关闭"));
                        break;
                    }
                }
            }
        }

        private static string ClassifierCategory(Dictionary<string, string> categories, string id, InstalledMod m)
        {
            if (categories != null && categories.ContainsKey(id) && !string.IsNullOrEmpty(categories[id])) return categories[id];
            return HeuristicCategory(m, id);
        }

        public static string HeuristicCategory(InstalledMod m, string id)
        {
            string text = ((m == null ? "" : m.Name) + " " + (id == null ? "" : id)).ToLowerInvariant();
            if (text.Contains("harmony") || text.Contains("hugs") || text.Contains("framework") || text.Contains("library") || text.Contains("lib") || text.Contains("core")) return L.T("前置库");
            if (text.Contains("translation") || text.Contains("chinese") || text.Contains(L.T("汉化")) || text.Contains(L.T("中文"))) return L.T("汉化");
            if (text.Contains("race") || text.Contains("xeno") || text.Contains("alien") || text.Contains("milira") || text.Contains("kiiro") || text.Contains(L.T("种族"))) return L.T("种族");
            if (text.Contains("faction") || text.Contains("empire") || text.Contains(L.T("派系"))) return L.T("派系");
            if (text.Contains("weapon") || text.Contains("gun") || text.Contains("armor") || text.Contains("apparel") || text.Contains("equipment") || text.Contains(L.T("武器")) || text.Contains(L.T("装备"))) return L.T("武器装备");
            if (text.Contains("ui") || text.Contains("hud") || text.Contains("interface") || text.Contains("menu") || text.Contains("icon") || text.Contains(L.T("界面"))) return L.T("界面");
            if (text.Contains("texture") || text.Contains("graphic") || text.Contains("hair") || text.Contains("face") || text.Contains("animation") || text.Contains("beauty") || text.Contains(L.T("美化")) || text.Contains(L.T("动画"))) return L.T("美化");
            if (text.Contains("patch") || text.Contains("compat") || text.Contains(L.T("兼容")) || text.Contains(L.T("补丁"))) return L.T("兼容补丁");
            return L.T("玩法");
        }

        private static void Layout(ModGraphData data)
        {
            if (data.Nodes.Count == 0) return;
            int nCat = Math.Max(1, data.Categories.Count);
            int cx = 560, cy = 540;
            int ring = 360;
            Dictionary<string, PointF> centers = new Dictionary<string, PointF>();
            for (int i = 0; i < data.Categories.Count; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI * 2 / nCat;
                centers[data.Categories[i]] = new PointF((float)(cx + Math.Cos(a) * ring), (float)(cy + Math.Sin(a) * ring));
            }
            Dictionary<string, List<ModGraphNode>> groups = new Dictionary<string, List<ModGraphNode>>();
            for (int i = 0; i < data.Nodes.Count; i++)
            {
                string c = data.Nodes[i].Category;
                if (!groups.ContainsKey(c)) groups[c] = new List<ModGraphNode>();
                groups[c].Add(data.Nodes[i]);
            }
            foreach (KeyValuePair<string, List<ModGraphNode>> kv in groups)
            {
                PointF center = centers[kv.Key];
                int count = kv.Value.Count;
                float rad = 48f + (float)Math.Sqrt(count) * 24f;
                for (int i = 0; i < count; i++)
                {
                    double a = i * Math.PI * 2 / Math.Max(1, count);
                    kv.Value[i].X = (int)(center.X + Math.Cos(a) * rad);
                    kv.Value[i].Y = (int)(center.Y + Math.Sin(a) * rad);
                }
            }
        }
    }
}
