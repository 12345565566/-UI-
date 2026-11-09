using System;
using System.Diagnostics;
using System.IO;

namespace Zogos
{
    /// <summary>启动游戏主程序的公共入口（界面与命令行共用同一段代码）。</summary>
    public static class GameLauncher
    {
        /// <summary>启动前体检：主程序必须是个像样的 Windows 可执行文件。</summary>
        public static bool Validate(PackContext ctx, out string reason)
        {
            reason = null;
            if (ctx == null) { reason = L.T("没有整合包信息"); return false; }
            string exe = ctx.GameExe;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) { reason = L.T("找不到游戏主程序：\r\n") + exe; return false; }
            long len = 0;
            try { len = new FileInfo(exe).Length; } catch { }
            if (len < 65536)
            {
                reason = L.T("游戏主程序只有 ") + len + L.T(" 字节，文件不完整（正常应该是 200 KB 左右）。\r\n") + exe;
                return false;
            }
            try
            {
                using (FileStream fs = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int a = fs.ReadByte();
                    int b = fs.ReadByte();
                    if (a != 0x4D || b != 0x5A)
                    {
                        string kind = L.T("（不是 Windows 程序）");
                        if (a == 0x7F && b == 0x45) kind = L.T("（这是 Linux 程序）");
                        else if (a == 0x50 && b == 0x4B) kind = L.T("（这是个压缩包，不是程序）");
                        reason = L.T("游戏主程序不是有效的 Windows 可执行文件") + kind + "：\r\n" + exe;
                        return false;
                    }
                }
            }
            catch (Exception ex2) { reason = L.T("读不了游戏主程序：") + ex2.Message; return false; }
            return true;
        }

        /// <summary>构造游戏命令行。关键：-savedatafolder 让 Config/Saves/ModLists 落在整合包内。</summary>
        public static string BuildArguments(PackContext ctx)
        {
            string dataDir = (ctx == null) ? null : ctx.SaveDataDir;
            if (string.IsNullOrEmpty(dataDir) && ctx != null && !string.IsNullOrEmpty(ctx.TargetConfigDir))
            {
                try { dataDir = Path.GetDirectoryName(ctx.TargetConfigDir.TrimEnd('\\', '/')); } catch { }
            }
            if (string.IsNullOrEmpty(dataDir)) dataDir = ".";
            return "-savedatafolder=\"" + dataDir.Replace("\"", "\\\"") + "\"";
        }

        public static Process Start(PackContext ctx)
        {
            string reason;
            if (!Validate(ctx, out reason)) throw new InvalidDataException(reason);

            ProcessStartInfo psi = new ProcessStartInfo(ctx.GameExe);
            psi.WorkingDirectory = ctx.GameDir;
            psi.UseShellExecute = false;
            psi.Arguments = BuildArguments(ctx);
            Process p = Process.Start(psi);
            Log.Info(L.T("已启动游戏：") + ctx.GameExe + "（PID " + p.Id + L.T("，工作目录 ") + ctx.GameDir + L.T("，参数 ") + psi.Arguments + "）");
            return p;
        }

        /// <summary>等游戏退出并返回退出码；超时返回 int.MinValue。</summary>
        public static int WaitForExit(Process p, int timeoutMs, out bool exited)
        {
            exited = false;
            try { exited = p.WaitForExit(timeoutMs); }
            catch { return int.MinValue; }
            if (!exited) return int.MinValue;
            try { return p.ExitCode; }
            catch { return int.MinValue; }
        }

        /// <summary>退出码是否说明启动失败。</summary>
        public static bool LooksLikeFailure(int exitCode, bool exited)
        {
            return exited && exitCode != 0;
        }
    }
}
