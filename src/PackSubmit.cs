using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Zogos
{
    /// <summary>
    /// 「导出整合包到仓库」：把整合包的 Config 与模组排序打成 zip，
    /// 供别人提交到仓库审核。
    ///
    /// 为什么不是直接上传：普通用户没有仓库写权限、也没有令牌。
    /// 所以走「本地生成 zip + 打开预填好的 Issue 页面」，用户把 zip 拖进 Issue 即可。
    /// </summary>
    public static class PackSubmit
    {
        public const string RepoOwner = "shitaitai";
        public const string RepoName = "zogos-packs";

        public static string SubmitDir()
        {
            string dir = Path.Combine(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器")),
                "submissions");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public class Result
        {
            public string ZipPath;
            public string Title;
            public string Body;
            public string IssueUrl;
            public int ModCount;
            public List<string> Files = new List<string>();
        }

        public static Result Build(PackContext ctx, Settings st, string packName, string contact, string note, out string err)
        {
            return Build(ctx, st, packName, contact, note, null, out err);
        }

        /// <summary>outDir 为空时用默认的 submissions 目录。</summary>
        public static Result Build(PackContext ctx, Settings st, string packName, string contact, string note, string outDir, out string err)
        {
            err = null;
            if (ctx == null) { err = L.T("没有整合包信息"); return null; }
            if (string.IsNullOrEmpty(ctx.Root) || !Directory.Exists(ctx.Root)) { err = L.T("整合包目录不存在"); return null; }

            try
            {
                if (string.IsNullOrEmpty(outDir)) outDir = SubmitDir();
                if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                if (string.IsNullOrEmpty(packName)) packName = new DirectoryInfo(ctx.Root).Name;

                string modsConfig = null;
                string[] cfgCandidates = new string[] {
                    string.IsNullOrEmpty(ctx.TargetConfigDir) ? null : Path.Combine(ctx.TargetConfigDir, "ModsConfig.xml"),
                    string.IsNullOrEmpty(ctx.PackConfigDir) ? null : Path.Combine(ctx.PackConfigDir, "ModsConfig.xml"),
                    string.IsNullOrEmpty(ctx.GameConfigDir) ? null : Path.Combine(ctx.GameConfigDir, "ModsConfig.xml"),
                    Path.Combine(Path.Combine(PathHelp.LocalLowRimWorldDir(), "Config"), "ModsConfig.xml")
                };
                for (int i = 0; i < cfgCandidates.Length; i++)
                {
                    if (!string.IsNullOrEmpty(cfgCandidates[i]) && File.Exists(cfgCandidates[i])) { modsConfig = cfgCandidates[i]; break; }
                }

                List<string> modIds = new List<string>();
                if (!string.IsNullOrEmpty(modsConfig)) modIds = ReadActiveMods(modsConfig);

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeName = Sanitize(packName);
                string zipPath = Path.Combine(outDir, L.T("提交_") + safeName + "_" + stamp + ".zip");

                List<string> included = new List<string>();

                using (FileStream fs = new FileStream(zipPath, FileMode.Create))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    StringBuilder info = new StringBuilder();
                    info.AppendLine(L.T("整合包名称：") + packName);
                    info.AppendLine(L.T("游戏版本：") + (ctx.GameVersion == null ? L.T("未知") : ctx.GameVersion));
                    info.AppendLine(L.T("模组数量：") + modIds.Count);
                    info.AppendLine(L.T("打包时间：") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    info.AppendLine(L.T("整合包路径：") + ctx.Root);
                    info.AppendLine(L.T("联系方式：") + (string.IsNullOrEmpty(contact) ? L.T("（未填写）") : contact));
                    info.AppendLine("");
                    info.AppendLine(L.T("补充说明："));
                    info.AppendLine(string.IsNullOrEmpty(note) ? L.T("（未填写）") : note);
                    AddText(zip, L.T("提交信息.txt"), info.ToString());
                    included.Add(L.T("提交信息.txt"));

                    string cfgDir = null;
                    if (!string.IsNullOrEmpty(ctx.TargetConfigDir) && Directory.Exists(ctx.TargetConfigDir)) cfgDir = ctx.TargetConfigDir;
                    else if (!string.IsNullOrEmpty(ctx.PackConfigDir) && Directory.Exists(ctx.PackConfigDir)) cfgDir = ctx.PackConfigDir;

                    if (!string.IsNullOrEmpty(cfgDir))
                    {
                        string[] files = Directory.GetFiles(cfgDir, "*.xml");
                        for (int i = 0; i < files.Length; i++)
                        {
                            string nm = Path.GetFileName(files[i]);
                            if (string.Equals(nm, "Prefs.xml", StringComparison.OrdinalIgnoreCase)) continue;
                            try
                            {
                                zip.CreateEntryFromFile(files[i], "Config/" + nm, CompressionLevel.Optimal);
                                included.Add("Config/" + nm);
                            }
                            catch { }
                        }
                    }
                    if (!string.IsNullOrEmpty(modsConfig) && cfgDir == null)
                    {
                        try
                        {
                            zip.CreateEntryFromFile(modsConfig, "Config/ModsConfig.xml", CompressionLevel.Optimal);
                            included.Add("Config/ModsConfig.xml");
                        }
                        catch { }
                    }

                    StringBuilder list = new StringBuilder();
                    list.AppendLine(L.T("# 加载顺序（从上到下）"));
                    list.AppendLine(L.T("# 共 ") + modIds.Count + L.T(" 个"));
                    for (int i = 0; i < modIds.Count; i++) list.AppendLine((i + 1) + ". " + modIds[i]);
                    AddText(zip, L.T("模组清单.txt"), list.ToString());
                    included.Add(L.T("模组清单.txt"));
                }

                string title = L.T("【整合包提交】") + packName + " · " + (ctx.GameVersion == null ? "" : ctx.GameVersion) + " · " + modIds.Count + L.T(" 模组");
                StringBuilder body = new StringBuilder();
                body.AppendLine(L.T("## 整合包信息"));
                body.AppendLine("");
                body.AppendLine(L.T("| 项 | 值 |"));
                body.AppendLine("|---|---|");
                body.AppendLine(L.T("| 名称 | ") + packName + " |");
                body.AppendLine(L.T("| 游戏版本 | ") + (ctx.GameVersion == null ? L.T("未知") : ctx.GameVersion) + " |");
                body.AppendLine(L.T("| 模组数量 | ") + modIds.Count + " |");
                body.AppendLine(L.T("| 联系方式 | ") + (string.IsNullOrEmpty(contact) ? L.T("（未填写）") : contact) + " |");
                body.AppendLine(L.T("| 提交时间 | ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " |");
                body.AppendLine("");
                if (!string.IsNullOrEmpty(note))
                {
                    body.AppendLine(L.T("## 补充说明"));
                    body.AppendLine("");
                    body.AppendLine(note);
                    body.AppendLine("");
                }
                body.AppendLine(L.T("## 附件（请手动拖进来）"));
                body.AppendLine("");
                body.AppendLine(L.T("本机生成的提交包："));
                body.AppendLine("");
                body.AppendLine("【" + zipPath + "】");
                body.AppendLine("");
                body.AppendLine(L.T("把上面这个 zip 拖到本输入框里上传即可（里面是 Config 和 模组排序）。"));
                body.AppendLine("");
                body.AppendLine(L.T("> 提交内容仅包含配置与模组排序，不包含模组文件本身。"));

                Result r = new Result();
                r.ZipPath = zipPath;
                r.Title = title;
                r.Body = body.ToString();
                r.ModCount = modIds.Count;
                r.Files = included;
                r.IssueUrl = "https://github.com/" + RepoOwner + "/" + RepoName + "/issues/new?title="
                    + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(r.Body);
                return r;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return null;
            }
        }

        private static void AddText(ZipArchive zip, string entryName, string content)
        {
            ZipArchiveEntry e = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream s = e.Open())
            using (StreamWriter w = new StreamWriter(s, new UTF8Encoding(false)))
                w.Write(content);
        }

        private static List<string> ReadActiveMods(string path)
        {
            List<string> list = new List<string>();
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                XmlNode root = doc.DocumentElement;
                if (root == null) return list;
                XmlNode act = root.SelectSingleNode("activeMods");
                if (act == null) return list;
                XmlNodeList lis = act.SelectNodes("li");
                for (int i = 0; i < lis.Count; i++)
                {
                    string s = lis[i].InnerText.Trim();
                    if (s.Length > 0) list.Add(s);
                }
            }
            catch { }
            return list;
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "pack";
            StringBuilder sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < s.Length && sb.Length < 40; i++)
            {
                char c = s[i];
                bool ok = true;
                for (int k = 0; k < bad.Length; k++) if (c == bad[k]) { ok = false; break; }
                sb.Append(ok ? c : '_');
            }
            return sb.Length == 0 ? "pack" : sb.ToString();
        }
    }
}
