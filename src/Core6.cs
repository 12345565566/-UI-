using System;
using System.IO;
using System.Text;

namespace Zogos
{
    /// <summary>
    /// 记录「这个整合包已经套用过了」。
    /// 有了它，启动器就不会每次启动都把整合包的 Config 覆盖回去，
    /// 玩家自己改的画面设置、按键、模组设置才能留下来。
    /// </summary>
    public static class DeployState
    {
        public const string FileName = ".zogos_deploy.ini";

        public static string PathOf(string configDir)
        {
            return Path.Combine(configDir, FileName);
        }

        /// <summary>整合包 Config 的轻量指纹：文件数 + 总字节 + 最新修改时间 + 文件名长度和。</summary>
        public static string PackSignature(PackContext ctx)
        {
            try
            {
                if (ctx == null || string.IsNullOrEmpty(ctx.PackConfigDir) || !Directory.Exists(ctx.PackConfigDir)) return "";
                string[] files = Directory.GetFiles(ctx.PackConfigDir, "*", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                long total = 0, maxTick = 0, nameLen = 0;
                for (int i = 0; i < files.Length; i++)
                {
                    FileInfo fi = new FileInfo(files[i]);
                    total += fi.Length;
                    if (fi.LastWriteTimeUtc.Ticks > maxTick) maxTick = fi.LastWriteTimeUtc.Ticks;
                    nameLen += fi.Name.Length;
                }
                return files.Length + "-" + total + "-" + maxTick + "-" + nameLen;
            }
            catch { return ""; }
        }

        public static bool Matches(PackContext ctx)
        {
            try
            {
                if (ctx == null || string.IsNullOrEmpty(ctx.TargetConfigDir)) return false;
                string p = PathOf(ctx.TargetConfigDir);
                if (!File.Exists(p)) return false;

                string[] ls = File.ReadAllLines(p, Encoding.UTF8);
                string packId = "", sig = "";
                for (int i = 0; i < ls.Length; i++)
                {
                    int eq = ls[i].IndexOf('=');
                    if (eq <= 0) continue;
                    string k = ls[i].Substring(0, eq).Trim();
                    string v = ls[i].Substring(eq + 1).Trim();
                    if (k == "packId") packId = v;
                    else if (k == "sig") sig = v;
                }

                string curId = ctx.PackId == null ? "" : ctx.PackId;
                string curSig = PackSignature(ctx);

                // 同一个包：两边都有编号时必须一致；任一边没有编号就只比指纹，
                // 不能因为拿不到 packId 就每次当成"没部署过"（那会导致每次重置配置）。
                if (packId.Length > 0 && curId.Length > 0
                    && !string.Equals(packId, curId, StringComparison.OrdinalIgnoreCase)) return false;

                // 指纹能比就比指纹
                if (sig.Length > 0 && curSig.Length > 0)
                    return string.Equals(sig, curSig, StringComparison.Ordinal);

                // 都算不出来：只要记录文件还在，就认为已经部署过
                return true;
            }
            catch { return false; }
        }

        public static void Save(PackContext ctx)
        {
            try
            {
                if (ctx == null || string.IsNullOrEmpty(ctx.TargetConfigDir)) return;
                if (!Directory.Exists(ctx.TargetConfigDir)) Directory.CreateDirectory(ctx.TargetConfigDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(L.T("# Astra 创世神协议 —— 配置部署记录"));
                sb.AppendLine(L.T("# 删掉这个文件，下次启动会重新完整套用一次整合包配置。"));
                sb.AppendLine("packId=" + ctx.PackId);
                sb.AppendLine("sig=" + PackSignature(ctx));
                sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                File.WriteAllText(PathOf(ctx.TargetConfigDir), sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        /// <summary>删掉记录 → 下次启动重新完整套用配置。</summary>
        public static void Reset(PackContext ctx)
        {
            try
            {
                if (ctx == null || string.IsNullOrEmpty(ctx.TargetConfigDir)) return;
                string p = PathOf(ctx.TargetConfigDir);
                if (File.Exists(p)) File.Delete(p);
                Log.Info(L.T("已清除部署记录，下次启动会重新完整套用整合包配置"));
            }
            catch (Exception ex) { Log.Warn(L.T("清除部署记录失败：") + ex.Message); }
        }
    }
}
