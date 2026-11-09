using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Zogos
{
    public enum ModIssueLevel
    {
        Info,
        Warning,
        Error
    }

    public class ModIssue
    {
        public ModIssueLevel Level;
        public string Code;
        public string PackageId;
        public string ModName;
        public string Message;
        public string Hint;

        public string ToLine()
        {
            string lv = Level == ModIssueLevel.Error ? L.T("错误") : (Level == ModIssueLevel.Warning ? L.T("警告") : L.T("提示"));
            string who = string.IsNullOrEmpty(ModName) ? PackageId : (ModName + " (" + PackageId + ")");
            string s = "[" + lv + "] " + (string.IsNullOrEmpty(who) ? "" : who + "：") + Message;
            if (!string.IsNullOrEmpty(Hint)) s += "  → " + Hint;
            return s;
        }
    }

    public class InstalledMod
    {
        public string PackageId;
        public string Name;
        public string Author;
        public string Description;
        public string RootDir;
        public string FolderName;
        public List<string> LoadBefore = new List<string>();
        public List<string> LoadAfter = new List<string>();
        public List<string> ForceLoadBefore = new List<string>();
        public List<string> ForceLoadAfter = new List<string>();
        public List<string> Dependencies = new List<string>();
        public List<string> IncompatibleWith = new List<string>();
        public List<string> SupportedVersions = new List<string>();
        public bool HasDefs;
        public bool HasAssemblies;
        public bool HasPatches;
        public bool Duplicate;
    }

    public class ModAnalysisResult
    {
        public string SchemeName;
        public int ActiveCount;
        public List<ModIssue> Issues = new List<ModIssue>();
        public List<string> SuggestedOrder = new List<string>();
        public List<string> Suspects = new List<string>();
        public int MovedCount;

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++) if (Issues[i].Level == ModIssueLevel.Error) return true;
                return false;
            }
        }

        public int WarningCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Issues.Count; i++) if (Issues[i].Level == ModIssueLevel.Warning) n++;
                return n;
            }
        }

        public int ErrorCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Issues.Count; i++) if (Issues[i].Level == ModIssueLevel.Error) n++;
                return n;
            }
        }

        public string SummaryText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("启动前模组体检结果"));
            sb.AppendLine(L.T("方案：") + (SchemeName == null ? "-" : SchemeName));
            sb.AppendLine(L.T("激活模组：") + ActiveCount + L.T(" 个"));
            sb.AppendLine(L.T("错误：") + ErrorCount + L.T("  警告：") + WarningCount + L.T("  疑似问题模组：") + Suspects.Count + L.T(" 个"));
            int versionWarn = 0;
            for (int i = 0; i < Issues.Count; i++) if (Issues[i].Code == "VERSION_MISMATCH") versionWarn++;
            if (versionWarn > 0) sb.AppendLine(L.T("其中版本元数据提醒：") + versionWarn + L.T(" 条（通常是 About.xml 没更新，不一定影响加载）"));
            if (MovedCount > 0) sb.AppendLine(L.T("建议排序会调整 ") + MovedCount + L.T(" 个模组的位置。"));
            sb.AppendLine();
            int shown = 0;
            for (int i = 0; i < Issues.Count && shown < 10; i++)
            {
                if (Issues[i].Level == ModIssueLevel.Info) continue;
                if (Issues[i].Code == "VERSION_MISMATCH") continue;
                sb.AppendLine(Issues[i].ToLine());
                shown++;
            }
            if (shown == 0) sb.AppendLine(L.T("没有发现结构性问题。"));
            if (Issues.Count > shown + CountInfos() + versionWarn) sb.AppendLine(L.T("……其余问题见体检报告。"));
            return sb.ToString();
        }

        private int CountInfos()
        {
            int n = 0;
            for (int i = 0; i < Issues.Count; i++) if (Issues[i].Level == ModIssueLevel.Info) n++;
            return n;
        }

        public string BuildReport(string schemePath, string suggestedPath, string safePath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine(L.T("Astra 创世神协议 - 模组体检报告"));
            sb.AppendLine(L.T("时间：") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine(L.T("方案：") + (SchemeName == null ? "-" : SchemeName));
            sb.AppendLine(L.T("方案文件：") + (schemePath == null ? "-" : schemePath));
            sb.AppendLine(L.T("激活模组：") + ActiveCount);
            sb.AppendLine(L.T("错误：") + ErrorCount + L.T("  警告：") + WarningCount + L.T("  疑似问题模组：") + Suspects.Count);
            sb.AppendLine(L.T("建议排序调整：") + MovedCount + L.T(" 个位置"));
            if (!string.IsNullOrEmpty(suggestedPath)) sb.AppendLine(L.T("智能排序文件：") + suggestedPath);
            if (!string.IsNullOrEmpty(safePath)) sb.AppendLine(L.T("安全排序文件：") + safePath);
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine(L.T("问题列表："));
            for (int i = 0; i < Issues.Count; i++) sb.AppendLine(Issues[i].ToLine());
            if (Issues.Count == 0) sb.AppendLine(L.T("（无）"));
            sb.AppendLine();
            sb.AppendLine(L.T("疑似问题模组："));
            if (Suspects.Count == 0) sb.AppendLine(L.T("（无）"));
            for (int i = 0; i < Suspects.Count; i++) sb.AppendLine("  - " + Suspects[i]);
            sb.AppendLine();
            sb.AppendLine(L.T("建议加载顺序："));
            for (int i = 0; i < SuggestedOrder.Count; i++) sb.AppendLine((i + 1) + ". " + SuggestedOrder[i]);
            sb.AppendLine("==================================================");
            return sb.ToString();
        }
    }

    public static class ModAnalyzer
    {
        public static Dictionary<string, InstalledMod> ScanInstalled(PackContext ctx, List<ModIssue> issues)
        {
            Dictionary<string, InstalledMod> map = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
            List<string> roots = new List<string>();
            if (ctx != null)
            {
                // 官方 Data 先扫，避免 Mods 里的官方副本被误报成重复 packageId
                if (!string.IsNullOrEmpty(ctx.GameDir)) roots.Add(Path.Combine(ctx.GameDir, "Data"));
                if (!string.IsNullOrEmpty(ctx.ModsDir)) roots.Add(ctx.ModsDir);
            }

            // 如果整合包放在 steamapps/common/RimWorld 下，顺手扫描创意工坊目录
            if (ctx != null && !string.IsNullOrEmpty(ctx.GameDir))
            {
                try
                {
                    DirectoryInfo game = new DirectoryInfo(ctx.GameDir);
                    if (game.Parent != null && game.Parent.Parent != null)
                    {
                        string workshop = Path.Combine(Path.Combine(Path.Combine(game.Parent.Parent.FullName, "workshop"), "content"), "294100");
                        if (Directory.Exists(workshop)) roots.Add(workshop);
                    }
                }
                catch { }
            }

            for (int r = 0; r < roots.Count; r++)
            {
                string root = roots[r];
                if (!Directory.Exists(root)) continue;
                string[] dirs;
                try { dirs = Directory.GetDirectories(root); }
                catch { continue; }
                for (int i = 0; i < dirs.Length; i++) ScanOneMod(dirs[i], map, issues);
            }
            return map;
        }

        private static void ScanOneMod(string dir, Dictionary<string, InstalledMod> map, List<ModIssue> issues)
        {
            string about = FindAbout(dir);
            if (string.IsNullOrEmpty(about)) return;
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(about);
                XmlElement root = doc.DocumentElement;
                if (root == null) return;
                string pid = Text(root, "packageId");
                if (string.IsNullOrEmpty(pid)) return;
                pid = NormalizeId(pid);

                InstalledMod m = new InstalledMod();
                m.PackageId = pid;
                m.Name = Text(root, "name");
                m.Author = Text(root, "author");
                if (string.IsNullOrEmpty(m.Author)) m.Author = string.Join(", ", List(root, "authors").ToArray());
                m.Description = Text(root, "description");
                m.RootDir = dir;
                m.FolderName = new DirectoryInfo(dir).Name;
                m.LoadBefore = List(root, "loadBefore");
                m.LoadAfter = List(root, "loadAfter");
                m.ForceLoadBefore = List(root, "forceLoadBefore");
                m.ForceLoadAfter = List(root, "forceLoadAfter");
                m.IncompatibleWith = List(root, "incompatibleWith");
                m.SupportedVersions = List(root, "supportedVersions");
                m.Dependencies = DependencyList(root);
                m.HasDefs = Directory.Exists(Path.Combine(dir, "Defs"));
                m.HasAssemblies = Directory.Exists(Path.Combine(dir, "Assemblies"));
                m.HasPatches = Directory.Exists(Path.Combine(dir, "Patches"));

                if (map.ContainsKey(pid))
                {
                    m.Duplicate = true;
                    if (issues != null)
                    {
                        bool officialA = IsOfficialPath(map[pid].RootDir);
                        bool officialB = IsOfficialPath(dir);
                        ModIssue issue = new ModIssue();
                        issue.PackageId = pid;
                        issue.ModName = m.Name;
                        issue.Hint = L.T("两个目录：") + map[pid].RootDir + L.T(" 与 ") + dir;
                        if (officialA || officialB)
                        {
                            issue.Level = ModIssueLevel.Info;
                            issue.Code = "DUPLICATE_OFFICIAL";
                            issue.Message = L.T("官方 Data 与 Mods 中存在同名副本，通常以官方 Data 为准，不必删除。");
                        }
                        else
                        {
                            issue.Level = ModIssueLevel.Error;
                            issue.Code = "DUPLICATE_PACKAGE_ID";
                            issue.Message = L.T("发现重复 packageId，后一个会被游戏忽略或产生冲突。");
                        }
                        issues.Add(issue);
                    }
                    return;
                }
                map[pid] = m;
            }
            catch (Exception ex)
            {
                if (issues != null)
                {
                    ModIssue issue = new ModIssue();
                    issue.Level = ModIssueLevel.Warning;
                    issue.Code = "BAD_ABOUT";
                    issue.PackageId = "";
                    issue.ModName = new DirectoryInfo(dir).Name;
                    issue.Message = L.T("About.xml 解析失败：") + ex.Message;
                    issue.Hint = about;
                    issues.Add(issue);
                }
            }
        }

        private static string FindAbout(string dir)
        {
            string direct = Path.Combine(Path.Combine(dir, "About"), "About.xml");
            if (File.Exists(direct)) return direct;
            try
            {
                string[] found = Directory.GetFiles(dir, "About.xml", SearchOption.AllDirectories);
                if (found.Length > 0) return found[0];
            }
            catch { }
            return null;
        }

        private static string NormalizeId(string id)
        {
            return (id == null ? "" : id.Trim().ToLowerInvariant());
        }

        private static bool IsOfficialPath(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try
            {
                string p = Path.GetFullPath(dir).TrimEnd('\\', '/').Replace('/', '\\');
                return p.IndexOf("\\RimWorld\\Data\\", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static string Text(XmlElement root, string name)
        {
            XmlNode n = root.SelectSingleNode(name);
            return n == null ? "" : n.InnerText.Trim();
        }

        private static List<string> List(XmlElement root, string name)
        {
            List<string> res = new List<string>();
            XmlNode parent = root.SelectSingleNode(name);
            if (parent == null) return res;
            foreach (XmlNode c in parent.ChildNodes)
            {
                if (c.Name != "li") continue;
                string v = c.InnerText;
                if (v == null) continue;
                v = v.Trim();
                if (v.Length > 0) res.Add(NormalizeId(v));
            }
            return res;
        }

        private static List<string> DependencyList(XmlElement root)
        {
            List<string> res = new List<string>();
            XmlNode parent = root.SelectSingleNode("modDependencies");
            if (parent == null) return res;
            foreach (XmlNode li in parent.ChildNodes)
            {
                if (li.Name != "li") continue;
                XmlNode pid = li.SelectSingleNode("packageId");
                if (pid != null)
                {
                    string v = pid.InnerText.Trim();
                    if (v.Length > 0) res.Add(NormalizeId(v));
                }
                XmlNode alts = li.SelectSingleNode("alternativePackageIds");
                if (alts != null)
                {
                    foreach (XmlNode a in alts.ChildNodes)
                    {
                        if (a.Name != "li") continue;
                        string v = a.InnerText.Trim();
                        if (v.Length > 0) res.Add(NormalizeId(v));
                    }
                }
            }
            return res;
        }

        public static ModAnalysisResult Analyze(PackContext ctx, SortScheme scheme)
        {
            ModAnalysisResult result = new ModAnalysisResult();
            if (scheme == null)
            {
                result.Issues.Add(NewIssue(ModIssueLevel.Error, "NO_SCHEME", "", "", L.T("没有选择排序方案。"), L.T("请先在排序页选择一个方案。")));
                return result;
            }
            result.SchemeName = scheme.Name;
            List<ModIssue> scanIssues = new List<ModIssue>();
            Dictionary<string, InstalledMod> installed = ScanInstalled(ctx, scanIssues);
            result.Issues.AddRange(scanIssues);
            for (int i = 0; i < scanIssues.Count; i++)
            {
                if (scanIssues[i].Level == ModIssueLevel.Error && scanIssues[i].Code == "DUPLICATE_PACKAGE_ID")
                    MarkSuspect(result, scanIssues[i].PackageId);
            }

            List<string> active = new List<string>();
            for (int i = 0; i < scheme.Mods.Count; i++)
            {
                string id = NormalizeId(scheme.Mods[i]);
                if (id.Length > 0 && !active.Contains(id)) active.Add(id);
            }
            result.ActiveCount = active.Count;
            if (active.Count == 0)
            {
                result.Issues.Add(NewIssue(ModIssueLevel.Error, "EMPTY_SCHEME", "", "", L.T("排序方案里没有激活模组。"), L.T("请检查 ModsConfig.xml。")));
                return result;
            }

            Dictionary<string, int> pos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < active.Count; i++) pos[active[i]] = i;

            List<List<int>> adj = new List<List<int>>();
            int[] indeg = new int[active.Count];
            HashSet<string> edges = new HashSet<string>();
            for (int i = 0; i < active.Count; i++) adj.Add(new List<int>());

            int dependencyEdges = 0;
            int dependencyWithoutDeclaredOrder = 0;
            for (int i = 0; i < active.Count; i++)
            {
                InstalledMod m;
                if (!installed.TryGetValue(active[i], out m))
                {
                    result.Issues.Add(NewIssue(ModIssueLevel.Warning, "NOT_FOUND_LOCAL", active[i], active[i], L.T("本地没有找到这个模组的 About.xml，可能是创意工坊模组或安装不完整。"), L.T("如果游戏能正常加载，可忽略。")));
                    continue;
                }

                AddOrderEdges(m.LoadBefore, false, i, pos, adj, indeg, edges, result);
                AddOrderEdges(m.LoadAfter, true, i, pos, adj, indeg, edges, result);
                AddOrderEdges(m.ForceLoadBefore, false, i, pos, adj, indeg, edges, result);
                AddOrderEdges(m.ForceLoadAfter, true, i, pos, adj, indeg, edges, result);

                for (int d = 0; d < m.Dependencies.Count; d++)
                {
                    string dep = m.Dependencies[d];
                    int depPos;
                    if (pos.TryGetValue(dep, out depPos))
                    {
                        dependencyEdges++;
                        AddEdge(depPos, i, adj, indeg, edges);
                        if (!m.LoadAfter.Contains(dep) && !m.ForceLoadAfter.Contains(dep)) dependencyWithoutDeclaredOrder++;
                    }
                    else if (!string.IsNullOrEmpty(dep))
                    {
                        result.Issues.Add(NewIssue(ModIssueLevel.Error, "MISSING_DEP", m.PackageId, m.Name, L.T("依赖的模组没有激活：") + dep, L.T("启用它，或选择替代依赖；安全排序会先禁用这个模组。")));
                        MarkSuspect(result, m.PackageId);
                    }
                }

                for (int x = 0; x < m.IncompatibleWith.Count; x++)
                {
                    string other = m.IncompatibleWith[x];
                    int otherPos;
                    if (pos.TryGetValue(other, out otherPos))
                    {
                        InstalledMod om = installed.ContainsKey(other) ? installed[other] : null;
                        result.Issues.Add(NewIssue(ModIssueLevel.Error, "INCOMPATIBLE", m.PackageId, m.Name, L.T("与已激活模组不兼容：") + (om == null ? other : om.Name + " (" + other + ")"), L.T("二者只能选一个；安全排序会把双方都列为疑似。")));
                        MarkSuspect(result, m.PackageId);
                        MarkSuspect(result, other);
                    }
                }

                CheckVersion(ctx, m, result);
            }

            AddOfficialOrder(active, pos, adj, indeg, edges);
            if (dependencyWithoutDeclaredOrder > 0)
            {
                result.Issues.Add(NewIssue(ModIssueLevel.Info, "DEP_ORDER_INFERRED", "", "", L.T("有 ") + dependencyWithoutDeclaredOrder + L.T(" 条依赖边没有 loadAfter 声明，已按依赖关系自动补边。"), L.T("这是 MC 式加载器补足的部分，但仍建议模组作者补全声明。")));
            }

            List<string> order = StableTopo(active, adj, indeg, result);
            if (order == null)
            {
                result.SuggestedOrder = new List<string>(active);
                result.Issues.Add(NewIssue(ModIssueLevel.Error, "CYCLE", "", "", L.T("排序图里存在循环依赖，无法自动求出安全顺序。"), L.T("请检查相关模组的 loadAfter/loadBefore 与依赖声明。")));
            }
            else
            {
                result.SuggestedOrder = order;
                int moved = 0;
                for (int i = 0; i < order.Count && i < active.Count; i++) if (!string.Equals(order[i], active[i], StringComparison.OrdinalIgnoreCase)) moved++;
                result.MovedCount = moved;
                if (moved > 0)
                    result.Issues.Add(NewIssue(ModIssueLevel.Info, "ORDER_ADJUST", "", "", L.T("按声明和依赖关系，建议调整 ") + moved + L.T(" 个模组的位置。"), L.T("可生成智能排序后启动。")));
            }

            return result;
        }

        private static void AddOrderEdges(List<string> list, bool after, int modIndex, Dictionary<string, int> pos,
            List<List<int>> adj, int[] indeg, HashSet<string> edges, ModAnalysisResult result)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                string target = NormalizeId(list[i]);
                int targetPos;
                if (!pos.TryGetValue(target, out targetPos)) continue;
                if (after) AddEdge(targetPos, modIndex, adj, indeg, edges);
                else AddEdge(modIndex, targetPos, adj, indeg, edges);
            }
        }

        private static void AddEdge(int from, int to, List<List<int>> adj, int[] indeg, HashSet<string> edges)
        {
            if (from == to || from < 0 || to < 0) return;
            string key = from + ">" + to;
            if (!edges.Add(key)) return;
            adj[from].Add(to);
            indeg[to]++;
        }

        private static void AddOfficialOrder(List<string> active, Dictionary<string, int> pos,
            List<List<int>> adj, int[] indeg, HashSet<string> edges)
        {
            string[] order = new string[] { "ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.ideology", "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly", "ludeon.rimworld.odyssey" };
            int last = -1;
            for (int i = 0; i < order.Length; i++)
            {
                int p;
                if (!pos.TryGetValue(order[i], out p)) continue;
                if (last >= 0) AddEdge(last, p, adj, indeg, edges);
                last = p;
            }
        }

        private static List<string> StableTopo(List<string> active, List<List<int>> adj, int[] indeg, ModAnalysisResult result)
        {
            int[] deg = new int[indeg.Length];
            Array.Copy(indeg, deg, indeg.Length);
            List<int> ready = new List<int>();
            for (int i = 0; i < deg.Length; i++) if (deg[i] == 0) ready.Add(i);
            List<int> outIdx = new List<int>();
            while (ready.Count > 0)
            {
                ready.Sort();
                int v = ready[0];
                ready.RemoveAt(0);
                outIdx.Add(v);
                for (int i = 0; i < adj[v].Count; i++)
                {
                    int w = adj[v][i];
                    deg[w]--;
                    if (deg[w] == 0) ready.Add(w);
                }
            }
            if (outIdx.Count != active.Count)
            {
                List<string> cycle = new List<string>();
                for (int i = 0; i < deg.Length; i++) if (deg[i] > 0) cycle.Add(active[i]);
                if (cycle.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < cycle.Count && i < 12; i++) { if (i > 0) sb.Append(" -> "); sb.Append(cycle[i]); }
                    if (cycle.Count > 12) sb.Append(" ...");
                    result.Issues.Add(NewIssue(ModIssueLevel.Error, "CYCLE_NODES", "", "", L.T("循环候选：") + sb.ToString(), L.T("这些模组之间的顺序声明可能互相矛盾。")));
                    for (int i = 0; i < cycle.Count; i++) MarkSuspect(result, cycle[i]);
                }
                return null;
            }
            List<string> res = new List<string>();
            for (int i = 0; i < outIdx.Count; i++) res.Add(active[outIdx[i]]);
            return res;
        }

        private static void CheckVersion(PackContext ctx, InstalledMod m, ModAnalysisResult result)
        {
            if (m.SupportedVersions == null || m.SupportedVersions.Count == 0) return;
            string game = ShortVersion(ctx == null ? "" : ctx.GameVersion);
            if (string.IsNullOrEmpty(game)) return;
            bool ok = false;
            for (int i = 0; i < m.SupportedVersions.Count; i++)
            {
                if (string.Equals(ShortVersion(m.SupportedVersions[i]), game, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
            }
            if (!ok)
            {
                // 很多模组的 supportedVersions 只是没更新，不能仅凭这个就判死刑
                result.Issues.Add(NewIssue(ModIssueLevel.Warning, "VERSION_MISMATCH", m.PackageId, m.Name, L.T("支持的版本是 ") + string.Join(", ", m.SupportedVersions.ToArray()) + L.T("，当前游戏是 ") + game + "。", L.T("确认模组是否真的兼容；如果游戏能正常加载，可忽略这条。")));
            }
        }

        private static string ShortVersion(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            Match m = Regex.Match(v, "([0-9]+\\.[0-9]+)");
            return m.Success ? m.Groups[1].Value : v.Trim();
        }

        private static void MarkSuspect(ModAnalysisResult result, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            for (int i = 0; i < result.Suspects.Count; i++) if (string.Equals(result.Suspects[i], id, StringComparison.OrdinalIgnoreCase)) return;
            result.Suspects.Add(id);
        }

        private static ModIssue NewIssue(ModIssueLevel level, string code, string pid, string name, string msg, string hint)
        {
            ModIssue i = new ModIssue();
            i.Level = level; i.Code = code; i.PackageId = pid; i.ModName = name; i.Message = msg; i.Hint = hint;
            return i;
        }

        public static string WriteSuggestedScheme(PackContext ctx, SortScheme baseScheme, ModAnalysisResult result)
        {
            if (ctx == null || baseScheme == null || result == null) return null;
            string name = L.T("智能排序_") + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return WriteScheme(ctx, baseScheme, result.SuggestedOrder, name, result);
        }

        public static string WriteSafeScheme(PackContext ctx, SortScheme baseScheme, ModAnalysisResult result)
        {
            if (ctx == null || baseScheme == null || result == null) return null;
            List<string> keep = new List<string>();
            HashSet<string> bad = new HashSet<string>(result.Suspects, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < baseScheme.Mods.Count; i++)
            {
                string id = NormalizeId(baseScheme.Mods[i]);
                if (!bad.Contains(id)) keep.Add(baseScheme.Mods[i]);
            }
            string name = L.T("安全排序_") + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            result.Issues.Add(NewIssue(ModIssueLevel.Info, "SAFE_SCHEME", "", "", L.T("安全排序已排除 ") + (baseScheme.Mods.Count - keep.Count) + L.T(" 个疑似问题模组。"), L.T("如果游戏能启动，再逐个加回来定位。")));
            return WriteScheme(ctx, baseScheme, keep, name, result);
        }

        private static string WriteScheme(PackContext ctx, SortScheme baseScheme, List<string> order, string name, ModAnalysisResult result)
        {
            try
            {
                string dir = ctx.SortDir;
                if (string.IsNullOrEmpty(dir)) dir = Path.Combine(ctx.Root, L.T("排序文件"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, SanitizeName(name) + ".xml");
                SortScheme s = new SortScheme();
                s.SourcePath = file;
                s.Name = name;
                s.Format = "XML";
                s.Version = baseScheme.Version;
                s.Expansions = baseScheme.Expansions == null ? new List<string>() : new List<string>(baseScheme.Expansions);
                s.Mods = new List<string>(order == null || order.Count == 0 ? baseScheme.Mods : order);
                s.WriteModsConfig(file);

                string report = result.BuildReport(baseScheme.SourcePath, file, null);
                File.WriteAllText(Path.ChangeExtension(file, ".txt"), report, new UTF8Encoding(true));
                Log.Info(L.T("已生成排序方案：") + file + "（" + order.Count + L.T(" 个模组）"));
                return file;
            }
            catch (Exception ex)
            {
                Log.Error(L.T("生成排序方案失败"), ex);
                return null;
            }
        }

        private static string SanitizeName(string name)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = true;
                for (int j = 0; j < bad.Length; j++) if (c == bad[j]) { ok = false; break; }
                sb.Append(ok ? c : '_');
            }
            return sb.ToString();
        }

        public static void AnalyzePlayerLog(PackContext ctx, SortScheme scheme, ModAnalysisResult result)
        {
            if (ctx == null || result == null) return;
            string logPath = ctx.PlayerLog;
            if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath)) return;
            try
            {
                List<string> active = new List<string>();
                if (scheme != null) for (int i = 0; i < scheme.Mods.Count; i++) active.Add(NormalizeId(scheme.Mods[i]));
                Dictionary<string, InstalledMod> installed = ScanInstalled(ctx, new List<ModIssue>());
                List<InstalledMod> mods = new List<InstalledMod>();
                foreach (string id in active) if (installed.ContainsKey(id)) mods.Add(installed[id]);

                string[] lines = File.ReadAllLines(logPath, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - 8000);
                int hits = 0;
                for (int i = start; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line) || line.Length > 4000) continue;
                    // 只看真正的错误；WARN/INFO 里的依赖元数据提醒不算冲突
                    if (!Regex.IsMatch(line, "\\bERROR\\b|Exception|XML error|Could not load|Failed to load|Asset pack .* could not be opened", RegexOptions.IgnoreCase)) continue;
                    if (line.IndexOf("Tried loading mod with the same packageId multiple times", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Issues.Add(NewIssue(ModIssueLevel.Info, "LOG_DUPLICATE_ID", "", "", L.T("日志里有 duplicate packageId 错误，但具体归属已由静态扫描单独列出。"), L.T("优先看静态扫描里的重复 packageId。")));
                        continue;
                    }
                    InstalledMod hit = MatchMod(line, mods);
                    if (hit == null) continue;
                    hits++;
                    result.Issues.Add(NewIssue(ModIssueLevel.Warning, "LOG_HIT", hit.PackageId, hit.Name, L.T("上次运行日志命中这个模组：") + Clip(line, 260), L.T("查看完整 Player.log 判断是它本身报错，还是被其它模组影响。")));
                    MarkSuspect(result, hit.PackageId);
                    if (hits >= 30) break;
                }
                if (hits == 0)
                    result.Issues.Add(NewIssue(ModIssueLevel.Info, "LOG_NO_MATCH", "", "", L.T("日志里有报错关键词，但没有匹配到具体模组。"), L.T("可把 Player.log 发给管理员做人工归因。")));
            }
            catch (Exception ex)
            {
                result.Issues.Add(NewIssue(ModIssueLevel.Info, "LOG_READ_FAIL", "", "", L.T("读取 Player.log 失败：") + ex.Message, L.T("不影响启动，只是这次没有日志归因。")));
            }
        }

        private static InstalledMod MatchMod(string line, List<InstalledMod> mods)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                InstalledMod m = mods[i];
                if (!string.IsNullOrEmpty(m.RootDir) && line.IndexOf(m.RootDir, StringComparison.OrdinalIgnoreCase) >= 0) return m;
                if (!string.IsNullOrEmpty(m.PackageId) && m.PackageId.Length >= 6 && line.IndexOf(m.PackageId, StringComparison.OrdinalIgnoreCase) >= 0) return m;
                if (!string.IsNullOrEmpty(m.FolderName) && m.FolderName.Length >= 6 && line.IndexOf(m.FolderName, StringComparison.OrdinalIgnoreCase) >= 0) return m;
            }
            return null;
        }

        private static string Clip(string s, int max)
        {
            if (s == null) return "";
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
