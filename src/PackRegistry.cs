using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Zogos
{
    // =====================================================================
    //  一个「导入过的整合包」
    // =====================================================================
    public class PackEntry
    {
        public string PackId = "";
        public string Root = "";
        public string Name = "";
        public string Version = "";
        public string LastScheme = "";
        public int Mods;
        public string LastUsed = "";

        public string DisplayName
        {
            get
            {
                string n = Name;
                if (string.IsNullOrEmpty(n) && !string.IsNullOrEmpty(Root))
                {
                    try { n = Path.GetFileName(Root.TrimEnd('\\', '/')); } catch { n = Root; }
                }
                return string.IsNullOrEmpty(n) ? L.T("(无名)") : n;
            }
        }

        public string VersionText { get { return string.IsNullOrEmpty(Version) ? L.T("未知版本") : Version; } }

        public bool Missing
        {
            get { return string.IsNullOrEmpty(Root) || !Directory.Exists(Root); }
        }

        public string Badge { get { return VersionText; } }
    }

    // =====================================================================
    //  整合包注册表
    //  记住所有导入过的整合包（含 1.6 / 1.7），支持一键切换。
    //  存在 settingsDir\packs.ini，一行一个包，字段用 TAB 分隔。
    // =====================================================================
    public static class PackRegistry
    {
        public const string FileName = "packs.ini";

        public static string PathOf(string settingsDir)
        {
            return Path.Combine(settingsDir, FileName);
        }

        /// <summary>字段用 TAB 分隔，所以只要把 TAB / 换行清掉就够，路径本身保持原样（可读）。</summary>
        private static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\t", " ").Replace("\r", "").Replace("\n", " ");
        }

        private static string Unesc(string s)
        {
            return s == null ? "" : s;
        }

        public static List<PackEntry> Load(string settingsDir)
        {
            List<PackEntry> list = new List<PackEntry>();
            try
            {
                string p = PathOf(settingsDir);
                if (!File.Exists(p)) return list;
                string[] lines = File.ReadAllLines(p, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i];
                    if (ln == null) continue;
                    ln = ln.Trim();
                    if (ln.Length == 0 || ln.StartsWith("#")) continue;
                    string[] f = ln.Split('\t');
                    if (f.Length < 2) continue;
                    PackEntry e = new PackEntry();
                    e.PackId = f[0];
                    e.Root = Unesc(f[1]);
                    if (f.Length > 2) e.Name = Unesc(f[2]);
                    if (f.Length > 3) e.Version = f[3];
                    if (f.Length > 4) e.LastScheme = Unesc(f[4]);
                    if (f.Length > 5) { int m; if (int.TryParse(f[5], out m)) e.Mods = m; }
                    if (f.Length > 6) e.LastUsed = f[6];
                    if (!string.IsNullOrEmpty(e.Root)) list.Add(e);
                }
            }
            catch (Exception ex) { Log.Warn(L.T("读取整合包注册表失败：") + ex.Message); }
            return list;
        }

        public static void Save(string settingsDir, List<PackEntry> list)
        {
            try
            {
                if (!Directory.Exists(settingsDir)) Directory.CreateDirectory(settingsDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(L.T("# Astra 创世神协议 —— 已导入的整合包"));
                sb.AppendLine(L.T("# 一行一个包：packId <TAB> 根目录 <TAB> 名称 <TAB> 版本 <TAB> 排序 <TAB> 模组数 <TAB> 上次使用"));
                for (int i = 0; i < list.Count; i++)
                {
                    PackEntry e = list[i];
                    sb.Append(e.PackId).Append('\t')
                      .Append(Esc(e.Root)).Append('\t')
                      .Append(Esc(e.Name)).Append('\t')
                      .Append(e.Version).Append('\t')
                      .Append(Esc(e.LastScheme)).Append('\t')
                      .Append(e.Mods).Append('\t')
                      .Append(e.LastUsed).AppendLine();
                }
                File.WriteAllText(PathOf(settingsDir), sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex) { Log.Warn(L.T("保存整合包注册表失败：") + ex.Message); }
        }

        public static PackEntry Find(List<PackEntry> list, string packId)
        {
            if (list == null || string.IsNullOrEmpty(packId)) return null;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].PackId, packId, StringComparison.OrdinalIgnoreCase)) return list[i];
            return null;
        }

        public static PackEntry FindByRoot(List<PackEntry> list, string root)
        {
            if (list == null || string.IsNullOrEmpty(root)) return null;
            string a = root.TrimEnd('\\', '/');
            for (int i = 0; i < list.Count; i++)
            {
                string b = (list[i].Root == null ? "" : list[i].Root).TrimEnd('\\', '/');
                if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return list[i];
            }
            return null;
        }

        /// <summary>把当前整合包登记进注册表（已存在则更新）。</summary>
        public static PackEntry Upsert(List<PackEntry> list, PackContext ctx, string schemePath)
        {
            if (list == null || ctx == null || string.IsNullOrEmpty(ctx.Root)) return null;
            PackEntry e = null;
            if (!string.IsNullOrEmpty(ctx.PackId)) e = Find(list, ctx.PackId);
            if (e == null) e = FindByRoot(list, ctx.Root);
            if (e == null)
            {
                e = new PackEntry();
                list.Add(e);
            }
            e.PackId = string.IsNullOrEmpty(ctx.PackId) ? PackMarker.ReadPackId(ctx.Root) : ctx.PackId;
            if (string.IsNullOrEmpty(e.PackId)) e.PackId = PackMarker.NewId();
            e.Root = ctx.Root;
            e.Name = Path.GetFileName(ctx.Root.TrimEnd('\\', '/'));
            e.Version = ctx.GameVersion;
            e.LastScheme = schemePath == null ? "" : schemePath;
            try
            {
                if (Directory.Exists(ctx.ModsDir)) e.Mods = Directory.GetDirectories(ctx.ModsDir).Length;
            }
            catch { }
            e.LastUsed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return e;
        }
    }

    // =====================================================================
    //  版本隔离：每个整合包一份配置现场
    //  切走时把游戏配置目录整份存进 profiles\<packId>\Config，
    //  切回来时再整份放回去。1.6 和 1.7 的 ModsConfig / Prefs 就不会串味。
    // =====================================================================
    public static class ProfileStore
    {
        public static string DirOf(string settingsDir, string packId)
        {
            if (string.IsNullOrEmpty(settingsDir) || string.IsNullOrEmpty(packId)) return "";
            return Path.Combine(Path.Combine(settingsDir, "profiles"), packId);
        }

        public static string ConfigDirOf(string settingsDir, string packId)
        {
            string d = DirOf(settingsDir, packId);
            return string.IsNullOrEmpty(d) ? "" : Path.Combine(d, "Config");
        }

        public static bool Has(string settingsDir, string packId)
        {
            string d = ConfigDirOf(settingsDir, packId);
            return d.Length > 0 && Directory.Exists(d);
        }

        /// <summary>保存当前现场。</summary>
        public static bool Save(PackContext ctx)
        {
            if (ctx == null || string.IsNullOrEmpty(ctx.PackId)) return false;
            if (string.IsNullOrEmpty(ctx.TargetConfigDir) || !Directory.Exists(ctx.TargetConfigDir)) return false;
            string dst = ConfigDirOf(ctx.SettingsDir, ctx.PackId);
            if (string.IsNullOrEmpty(dst)) return false;
            try
            {
                if (Directory.Exists(dst)) FileOps.ClearDir(dst, null);
                Directory.CreateDirectory(dst);
                List<string> errs = new List<string>();
                FileOps.CopyTree(ctx.TargetConfigDir, dst, errs, false);
                Log.Info(L.T("已保存整合包现场：") + ctx.PackId + " -> " + dst + "（" + errs.Count + L.T(" 个文件失败）"));
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(L.T("保存整合包现场失败：") + ex.Message);
                return false;
            }
        }

        /// <summary>恢复现场。恢复后要顺手写部署记录，否则下次启动会当成没套用过而全量覆盖。</summary>
        public static bool Restore(PackContext ctx)
        {
            if (ctx == null || string.IsNullOrEmpty(ctx.PackId)) return false;
            string src = ConfigDirOf(ctx.SettingsDir, ctx.PackId);
            if (string.IsNullOrEmpty(src) || !Directory.Exists(src)) return false;
            if (string.IsNullOrEmpty(ctx.TargetConfigDir)) return false;
            try
            {
                if (!Directory.Exists(ctx.TargetConfigDir)) Directory.CreateDirectory(ctx.TargetConfigDir);
                List<string> errs = new List<string>();
                FileOps.CopyTree(src, ctx.TargetConfigDir, errs, false);
                Log.Info(L.T("已恢复整合包现场：") + ctx.PackId + " <- " + src + "（" + errs.Count + L.T(" 个文件失败）"));
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(L.T("恢复整合包现场失败：") + ex.Message);
                return false;
            }
        }

        public static void Drop(string settingsDir, string packId)
        {
            try
            {
                string d = DirOf(settingsDir, packId);
                if (d.Length > 0 && Directory.Exists(d)) FileOps.ClearDir(d, null);
            }
            catch { }
        }
    }

    // =====================================================================
    //  一键切换
    // =====================================================================
    public static class PackSwitcher
    {
        public static string ReadGameVersion(string root)
        {
            try
            {
                string vf = Path.Combine(Path.Combine(root, "RimWorld"), "Version.txt");
                if (File.Exists(vf)) return PathHelp.ReadAllTextSmart(vf).Trim();
            }
            catch { }
            return "";
        }

        public static bool Switch(PackEntry target, Settings st, string launcherDir, out string newRoot, out string err)
        {
            newRoot = null;
            err = null;
            if (target == null) { err = L.T("没有选择整合包"); return false; }
            if (target.Missing)
            {
                err = L.T("这个整合包的文件夹找不到了：\r\n") + target.Root + L.T("\r\n\r\n可能被删了、改名了，或者搬到别的盘了。");
                return false;
            }

            // 1) 先把当前包的现场存起来
            PackContext cur = null;
            try { cur = PackContext.Detect(launcherDir, st, st.Root, st.TargetConfigDir, null); }
            catch { }
            if (cur != null && cur.Ok && !string.IsNullOrEmpty(cur.PackId))
            {
                try { ProfileStore.Save(cur); } catch { }
                // 当前包也要把版本写进令牌，否则它的令牌里始终缺 gameVersion
                string ce = null;
                PackMarker.Write(cur.Root, cur.PackId, cur.GameVersion, st.LastScheme, 0, out ce);
            }

            // 2) 版本一致性检查：别把 1.7 的排序写进 1.6 的包
            string actual = ReadGameVersion(target.Root);
            if (!string.IsNullOrEmpty(target.Version) && !string.IsNullOrEmpty(actual)
                && !string.Equals(target.Version.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                err = L.T("版本对不上，为了不把排序写错，先停一下：\r\n\r\n")
                    + L.T("注册表里记的是 ") + target.Version + "\r\n"
                    + L.T("这个目录里实际是 ") + actual + "\r\n\r\n"
                    + L.T("文件可能被替换过。请先重新导入这个整合包，或把它从列表里删掉再重新绑定。");
                return false;
            }

            // 3) 切换设置 + 刷新识别令牌（令牌里补上版本/排序/模组数）
            st.Root = target.Root;
            st.PackId = target.PackId;
            if (!string.IsNullOrEmpty(target.LastScheme)) st.LastScheme = target.LastScheme;
            st.Save();

            string mErr = null;
            PackMarker.Write(target.Root, target.PackId, target.Version, target.LastScheme, target.Mods, out mErr);
            if (mErr != null) Log.Warn(L.T("令牌写入失败：") + mErr);

            // 4) 按新目录重新检测
            PackContext ctx = null;
            try { ctx = PackContext.Detect(launcherDir, st, target.Root, st.TargetConfigDir, null); }
            catch (Exception ex) { err = L.T("重新检测整合包失败：") + ex.Message; return false; }
            if (ctx == null) { err = L.T("重新检测整合包失败"); return false; }

            // 5) 恢复这个包自己的现场（版本隔离的关键一步）
            bool restored = false;
            if (ProfileStore.Has(ctx.SettingsDir, ctx.PackId))
            {
                restored = ProfileStore.Restore(ctx);
                if (restored)
                {
                    // 告诉部署器：已经套用过了，别再整份覆盖
                    DeployState.Save(ctx);
                }
            }

            // 6) 写该包的排序方案
            List<SortScheme> all = null;
            try { all = SortSchemeLoader.Load(ctx); } catch { }
            SortScheme scheme = null;
            if (all != null && all.Count > 0)
            {
                if (!string.IsNullOrEmpty(target.LastScheme))
                {
                    for (int i = 0; i < all.Count; i++)
                        if (string.Equals(all[i].SourcePath, target.LastScheme, StringComparison.OrdinalIgnoreCase)) { scheme = all[i]; break; }
                }
                if (scheme == null)
                {
                    for (int i = 0; i < all.Count; i++) if (all[i].IsBuiltIn) { scheme = all[i]; break; }
                }
                if (scheme == null) scheme = all[0];
                try
                {
                    ConfigDeployer.Deploy(ctx, scheme, st, null);
                }
                catch (Exception ex) { Log.Warn(L.T("写排序方案失败：") + ex.Message); }
            }
            else Log.Warn(L.T("这个整合包里没有找到排序方案：") + ctx.SortDir);

            // 7) 注册表里记一笔
            List<PackEntry> list = PackRegistry.Load(ctx.SettingsDir);
            PacEntrySafeUpdate(list, ctx, scheme == null ? target.LastScheme : scheme.SourcePath);
            PackRegistry.Save(ctx.SettingsDir, list);

            newRoot = ctx.Root;
            Log.Info(L.T("已切换整合包：") + ctx.Root + L.T("（版本 ") + target.Version + L.T("，恢复现场=") + restored + "）");
            return true;
        }

        private static void PacEntrySafeUpdate(List<PackEntry> list, PackContext ctx, string schemePath)
        {
            PackEntry e = PackRegistry.Upsert(list, ctx, schemePath);
            if (e != null && string.IsNullOrEmpty(e.Version)) e.Version = ctx.GameVersion;
        }
    }
}
