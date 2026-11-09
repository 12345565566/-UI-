using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Zogos
{
    /// <summary>
    /// 模组名中文对照：优先用 AI 翻译（一次批量、结果落盘缓存），
    /// AI 不可用时退回到本地词表做关键词提示。
    /// 缓存文件：%LOCALAPPDATA%\ZOGOS创世神启动器\modname_zh.txt
    /// </summary>
    public static class ModNameZh
    {
        private static readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;
        private const int BatchSize = 40;

        public static string CachePath()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器"));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "modname_zh.txt");
            }
            catch { return ""; }
        }

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string p = CachePath();
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) return;
                string[] lines = File.ReadAllLines(p, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i];
                    if (string.IsNullOrEmpty(ln)) continue;
                    int tab = ln.IndexOf('\t');
                    if (tab <= 0) continue;
                    string id = ln.Substring(0, tab).Trim();
                    string zh = ln.Substring(tab + 1).Trim();
                    if (id.Length > 0 && zh.Length > 0) _map[id] = zh;
                }
            }
            catch (Exception ex) { Log.Warn(L.T("读取模组中文名缓存失败：") + ex.Message); }
        }

        public static void Save()
        {
            try
            {
                string p = CachePath();
                if (string.IsNullOrEmpty(p)) return;
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, string> kv in _map)
                    sb.AppendLine(kv.Key + "\t" + kv.Value.Replace('\t', ' '));
                File.WriteAllText(p, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Warn(L.T("保存模组中文名缓存失败：") + ex.Message); }
        }

        public static string Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            Load();
            string v;
            return _map.TryGetValue(id, out v) ? v : "";
        }

        public static void Set(string id, string zh)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(zh)) return;
            _map[id] = zh.Trim();
        }

        public static int Count { get { Load(); return _map.Count; } }

        public static bool HasChinese(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++) if (s[i] >= 0x4E00 && s[i] <= 0x9FFF) return true;
            return false;
        }

        // ── 本地词表兜底（AI 不可用时至少给个方向）──
        private static readonly string[,] Words = new string[,] {
            {"storage",L.T("存储")},{"weapons",L.T("武器")},{"weapon",L.T("武器")},{"armor",L.T("护甲")},{"apparel",L.T("服装")},
            {"furniture",L.T("家具")},{"hair",L.T("发型")},{"beard",L.T("胡须")},{"face",L.T("脸部")},{"facial",L.T("面部")},
            {"animation",L.T("动画")},{"continued",L.T("续作")},{"patch",L.T("补丁")},{"framework",L.T("框架")},{"library",L.T("前置库")},
            {"race",L.T("种族")},{"races",L.T("种族")},{"faction",L.T("派系")},{"genes",L.T("基因")},{"gene",L.T("基因")},
            {"vanilla",L.T("原版")},{"expanded",L.T("扩展")},{"more",L.T("更多")},{"better",L.T("更好")},{"simple",L.T("简洁")},
            {"interface",L.T("界面")},{"ui",L.T("界面")},{"hud",L.T("界面")},{"menu",L.T("菜单")},{"icons",L.T("图标")},
            {"medical",L.T("医疗")},{"surgery",L.T("手术")},{"animal",L.T("动物")},{"animals",L.T("动物")},{"farming",L.T("农业")},
            {"power",L.T("电力")},{"defense",L.T("防御")},{"turret",L.T("炮塔")},{"research",L.T("研究")},{"crafting",L.T("制作")},
            {"texture",L.T("贴图")},{"retexture",L.T("贴图重置")},{"graphics",L.T("画面")},{"sound",L.T("音效")},{"music",L.T("音乐")},
            {"quality",L.T("品质")},{"life",L.T("生活")},{"realistic",L.T("写实")},{"trading",L.T("贸易")},{"trade",L.T("贸易")},
            {"world",L.T("世界")},{"map",L.T("地图")},{"terrain",L.T("地形")},{"water",L.T("水")},{"weather",L.T("天气")},
            {"translation",L.T("汉化")},{"chinese",L.T("中文")},{"japanese",L.T("日文")},{"russian",L.T("俄文")},
            {"hot spring",L.T("温泉")},{"spring",L.T("温泉")},{"floors",L.T("地板")},{"floor",L.T("地板")},{"soil",L.T("土壤")},
            {"wealth",L.T("财富")},{"columns",L.T("立柱")},{"column",L.T("立柱")},{"visible",L.T("可见")},{"standalone",L.T("独立")},
            {"toddler",L.T("幼儿")},{"toddlers",L.T("幼儿")},{"utility",L.T("实用")},{"stuffed",L.T("填充")},{"fridge",L.T("冰箱")},
            {"ship",L.T("飞船")},{"ships",L.T("飞船")},{"drop",L.T("空投")},{"spot",L.T("地点")},{"prisoner",L.T("囚犯")},
            {"stone",L.T("石材")},{"wood",L.T("木材")},{"steel",L.T("钢铁")},{"cloth",L.T("布料")},{"leather",L.T("皮革")},
            {"food",L.T("食物")},{"meal",L.T("餐食")},{"drug",L.T("药物")},{"drugs",L.T("药物")},{"plants",L.T("植物")},{"plant",L.T("植物")},
            {"trees",L.T("树木")},{"tree",L.T("树木")},{"fire",L.T("火焰")},{"light",L.T("灯光")},{"lamp",L.T("灯具")},
            {"door",L.T("门")},{"doors",L.T("门")},{"wall",L.T("墙")},{"walls",L.T("墙")},{"bed",L.T("床")},{"beds",L.T("床")},
            {"table",L.T("桌子")},{"chair",L.T("椅子")},{"workbench",L.T("工作台")},{"smith",L.T("锻造")},{"mining",L.T("采矿")},
            {"ore",L.T("矿石")},{"fuel",L.T("燃料")},{"battery",L.T("电池")},{"solar",L.T("太阳能")},{"nuclear",L.T("核能")},
            {"robot",L.T("机器人")},{"mech",L.T("机械体")},{"mechs",L.T("机械体")},{"android",L.T("仿生")},{"alien",L.T("外星")},
            {"insect",L.T("昆虫")},{"dragon",L.T("龙")},{"magic",L.T("魔法")},{"psychic",L.T("灵能")},{"psi",L.T("灵能")},
            {"religion",L.T("宗教")},{"ritual",L.T("仪式")},{"caravan",L.T("商队")},{"travel",L.T("旅行")},{"quest",L.T("任务")},
            {"events",L.T("事件")},{"event",L.T("事件")},{"raid",L.T("袭击")},{"shield",L.T("护盾")},{"guns",L.T("枪械")},
            {"gun",L.T("枪械")},{"rifle",L.T("步枪")},{"melee",L.T("近战")},{"combat",L.T("战斗")},{"damage",L.T("伤害")},
            {"health",L.T("健康")},{"addiction",L.T("成瘾")},{"pregnancy",L.T("怀孕")},{"children",L.T("儿童")},{"child",L.T("儿童")},
            {"baby",L.T("婴儿")},{"adult",L.T("成年")},{"fishing",L.T("钓鱼")},{"cooking",L.T("烹饪")},{"kitchen",L.T("厨房")}
        };

        /// <summary>把英文名里认识的词换成中文，拼成一个提示串。</summary>
        public static string LocalHint(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string lower = name.ToLowerInvariant();
            List<string> hits = new List<string>();
            for (int i = 0; i < Words.GetLength(0); i++)
            {
                if (lower.IndexOf(Words[i, 0], StringComparison.Ordinal) >= 0)
                {
                    string zh = Words[i, 1];
                    if (!hits.Contains(zh)) hits.Add(zh);
                    if (hits.Count >= 4) break;
                }
            }
            if (hits.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hits.Count; i++)
            {
                if (i > 0) sb.Append(" ");
                sb.Append(hits[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 批量翻译还没有中文名的模组。返回本次新翻译成功的条数。
        /// progress(已处理, 总数, 当前片段说明) 用于回显进度。
        /// </summary>
        public static int TranslateMissing(List<ModGraphNode> nodes, Settings st,
            Action<int, int, string> progress, out string err)
        {
            err = null;
            Load();
            if (nodes == null) return 0;

            List<ModGraphNode> need = new List<ModGraphNode>();
            for (int i = 0; i < nodes.Count; i++)
            {
                ModGraphNode n = nodes[i];
                if (n == null || string.IsNullOrEmpty(n.Name)) continue;
                if (HasChinese(n.Name)) continue;              // 本来就是中文名，不用翻
                string zh;
                if (_map.TryGetValue(n.Id, out zh) && !string.IsNullOrEmpty(zh)) continue;
                need.Add(n);
            }
            if (need.Count == 0) return 0;

            bool aiOk = st != null && !string.IsNullOrEmpty(st.ApiBase) && !string.IsNullOrEmpty(st.ApiModel);
            int done = 0;
            int newCount = 0;

            for (int start = 0; start < need.Count; start += BatchSize)
            {
                int count = Math.Min(BatchSize, need.Count - start);
                List<ModGraphNode> chunk = need.GetRange(start, count);

                if (progress != null) progress(done, need.Count, L.T("AI 正在翻译第 ") + (start + 1) + "~" + (start + count) + L.T(" 个"));

                bool chunkOk = false;
                if (aiOk)
                {
                    try
                    {
                        StringBuilder user = new StringBuilder();
                        user.AppendLine(L.T("把下面每个英文模组名翻译成简短中文名（不超过 20 字，保留常见专有名词译法）。"));
                        user.AppendLine(L.T("严格按此格式逐行输出，不要编号以外任何多余文字："));
                        user.AppendLine(L.T("序号|中文名"));
                        for (int i = 0; i < chunk.Count; i++)
                            user.AppendLine((i + 1) + "|" + chunk[i].Name);

                        string system = L.T("你是 RimWorld 模组名翻译器。只输出 序号|中文名 形式的逐行结果，不要解释。");
                        string e = null;
                        string res = AiClient.Chat(st.ApiBase, st.ApiKey, st.ApiModel, system, user.ToString(), 90000, out e);

                        if (!string.IsNullOrEmpty(res))
                        {
                            int got = 0;
                            string[] lines = res.Replace("\r", "\n").Split('\n');
                            for (int i = 0; i < lines.Length; i++)
                            {
                                Match m = Regex.Match(lines[i].Trim(), "^\\s*(\\d+)\\s*[|｜]\\s*(.+?)\\s*$");
                                if (!m.Success) continue;
                                int idx;
                                if (!int.TryParse(m.Groups[1].Value, out idx)) continue;
                                if (idx < 1 || idx > chunk.Count) continue;
                                string zh = m.Groups[2].Value.Trim().Trim('"', '\'', '。', '.');
                                if (zh.Length == 0 || zh.Length > 40) continue;
                                if (!HasChinese(zh)) continue;
                                Set(chunk[idx - 1].Id, zh);
                                got++;
                            }
                            chunkOk = got > 0;
                            if (chunkOk) newCount += got;
                            else if (err == null) err = e == null ? L.T("AI 没有按要求返回") : e;
                        }
                        else if (err == null) err = e == null ? L.T("AI 没有返回内容") : e;
                    }
                    catch (Exception ex)
                    {
                        if (err == null) err = ex.Message;
                    }
                }

                // AI 这一批没成，就用本地词表给个提示，至少不是纯英文
                if (!chunkOk)
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        string hint = LocalHint(chunk[i].Name);
                        if (!string.IsNullOrEmpty(hint)) { Set(chunk[i].Id, hint); newCount++; }
                    }
                }

                done += count;
                if (progress != null) progress(done, need.Count, L.T("已完成 ") + done + "/" + need.Count);
            }

            Save();
            return newCount;
        }
    }
}
