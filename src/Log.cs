using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Zogos
{
    /// <summary>轻量日志：内存环形缓冲 + 磁盘追加，供界面实时显示与诊断报告使用。</summary>
    public static class Log
    {
        public const string Version = "1.7.0";
        private static readonly object Sync = new object();
        private static readonly List<string> Buffer = new List<string>();
        private static readonly int BufMax = 4000;
        private static string _file;

        /// <summary>新日志行事件。可能由后台线程触发，订阅方需自行切回 UI 线程。</summary>
        public static event Action<string> Added;

        public static string FilePath { get { return _file; } }

        public static void Init(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _file = Path.Combine(dir, L.T("ZOGOS启动器日志.txt"));
                try
                {
                    if (File.Exists(_file) && new FileInfo(_file).Length > 2 * 1024 * 1024) File.Delete(_file);
                }
                catch { }
            }
            catch { _file = null; }

            Write(L.T("信息"), "");
            Write(L.T("信息"), L.T("=========== Astra 创世神协议 v") + Version + " ===========");
            Write(L.T("信息"), L.T("启动时间: ") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (_file != null) Write(L.T("信息"), L.T("日志文件: ") + _file);
        }

        public static void Info(string msg) { Write(L.T("信息"), msg); }
        public static void Warn(string msg) { Write(L.T("警告"), msg); }
        public static void Error(string msg) { Write(L.T("错误"), msg); }

        public static void Error(string msg, Exception ex)
        {
            string s = msg;
            if (ex != null)
            {
                s = msg + "  →  " + ex.GetType().Name + ": " + ex.Message;
                if (!string.IsNullOrEmpty(ex.StackTrace))
                {
                    s = s + Environment.NewLine + "        "
                        + ex.StackTrace.Replace(Environment.NewLine, Environment.NewLine + "        ");
                }
            }
            Write(L.T("错误"), s);
        }

        private static void Write(string level, string msg)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] [" + level + "] " + (msg == null ? "" : msg);
            lock (Sync)
            {
                Buffer.Add(line);
                if (Buffer.Count > BufMax) Buffer.RemoveRange(0, Buffer.Count - BufMax);
                if (_file != null)
                {
                    try { File.AppendAllText(_file, line + Environment.NewLine, new UTF8Encoding(true)); }
                    catch { }
                }
            }
            Action<string> h = Added;
            if (h != null)
            {
                try { h(line); }
                catch { }
            }
        }

        public static string[] Snapshot()
        {
            lock (Sync) { return Buffer.ToArray(); }
        }

        public static string TailText(int lines)
        {
            string[] all = Snapshot();
            int start = Math.Max(0, all.Length - lines);
            StringBuilder sb = new StringBuilder();
            for (int i = start; i < all.Length; i++) sb.AppendLine(all[i]);
            return sb.ToString();
        }
    }
}
