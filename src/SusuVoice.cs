using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 苏苏的日语配音。
    /// 音频以「内嵌资源」的方式打进 exe（Zogos.Voice.*），
    /// 找不到内嵌资源时退回 exe 旁边的 苏苏语音\voice 文件夹（开发/预览用）。
    /// 索引按「台词原文」走（manifest.tsv：文件名 TAB base64(原文)），
    /// 所以对话顺序怎么改、分支怎么走都不会错位；找不到就静音，不影响使用。
    /// </summary>
    public static class SusuVoice
    {
        private static bool _muted;
        private static bool _muteLoaded;

        private static string MuteIniPath()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器"));
                return Path.Combine(dir, "voice.ini");
            }
            catch { return null; }
        }

        private static void EnsureMuteLoaded()
        {
            if (_muteLoaded) return;
            _muteLoaded = true;
            try
            {
                string p = MuteIniPath();
                if (p != null && File.Exists(p))
                {
                    string[] ls = File.ReadAllLines(p);
                    for (int i = 0; i < ls.Length; i++)
                    {
                        string ln = ls[i] == null ? "" : ls[i].Trim();
                        if (ln.StartsWith("Muted=")) _muted = ln.EndsWith("1");
                    }
                }
            }
            catch { }
        }

        /// <summary>配音开关（用户手动静音）。记在 voice.ini 里，下次打开还记得。</summary>
        public static bool Muted
        {
            get { EnsureMuteLoaded(); return _muted; }
            set
            {
                EnsureMuteLoaded();
                _muted = value;
                if (value) Stop();
                try
                {
                    string p = MuteIniPath();
                    if (p != null)
                    {
                        string dir = Path.GetDirectoryName(p);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllText(p, L.T("# ZOGOS 苏苏配音开关\r\nMuted=") + (value ? "1" : "0") + "\r\n", new UTF8Encoding(true));
                    }
                }
                catch { }
            }
        }

        private const string ResPrefix = "Zogos.Voice.";

        private static Dictionary<string, string> _map;
        private static bool _embedded;
        private static string _dir;
        private static SoundPlayer _player;
        private static Stream _stream;
        private static string _playing;
        private static int _lastDurationMs;
        private static DateTime _playStarted;

        /// <summary>上一句配音的总时长（毫秒），0 表示没有可播的音频。</summary>
        public static int LastDurationMs { get { return _lastDurationMs; } }

        /// <summary>上一句还剩多久播完（毫秒）。界面用它决定"什么时候切下一句"。</summary>
        public static int RemainingMs
        {
            get
            {
                if (_lastDurationMs <= 0 || _player == null) return 0;
                int elapsed = (int)(DateTime.Now - _playStarted).TotalMilliseconds;
                int rem = _lastDurationMs - elapsed;
                return rem > 0 ? rem : 0;
            }
        }

        public static bool Available
        {
            get { EnsureLoaded(); return _map != null && _map.Count > 0; }
        }

        /// <summary>配音条数（诊断用）。</summary>
        public static int Count { get { EnsureLoaded(); return _map == null ? 0 : _map.Count; } }

        /// <summary>这句台词有没有配音（诊断用）。</summary>
        public static bool Has(string text)
        {
            EnsureLoaded();
            return text != null && _map != null && _map.ContainsKey(text);
        }

        /// <summary>上一次 Play 是否真的把音频交给了解码器（诊断用）。</summary>
        public static bool LastPlayLoaded
        {
            get { return _player != null && _player.IsLoadCompleted; }
        }

        private static void EnsureLoaded()
        {
            if (_map != null) return;
            _map = new Dictionary<string, string>();

            // 1) 内嵌资源优先
            try
            {
                Stream ms = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResPrefix + "manifest.tsv");
                if (ms != null)
                {
                    using (ms)
                    {
                        using (StreamReader sr = new StreamReader(ms, Encoding.UTF8)) LoadMap(sr);
                    }
                    _embedded = true;
                    Log.Info(L.T("苏苏配音：已载入内嵌读音 ") + _map.Count + L.T(" 条"));
                    return;
                }
            }
            catch (Exception ex) { Log.Warn(L.T("读取内嵌配音清单失败：") + ex.Message); }

            // 2) 退回外部文件夹
            try { _dir = FindVoiceDir(); }
            catch (Exception ex) { Log.Warn(L.T("找配音目录失败：") + ex.Message); _dir = null; }
            if (_dir == null)
            {
                Log.Info(L.T("苏苏配音：既没有内嵌音频，也没有外部音频，静音运行"));
                return;
            }
            try
            {
                using (StreamReader sr = new StreamReader(Path.Combine(_dir, "manifest.tsv"), Encoding.UTF8)) LoadMap(sr);
                Log.Info(L.T("苏苏配音：已载入外部读音 ") + _map.Count + L.T(" 条（") + _dir + "）");
            }
            catch (Exception ex) { Log.Warn(L.T("读取配音清单失败：") + ex.Message); }
        }

        private static void LoadMap(TextReader tr)
        {
            string ln;
            while ((ln = tr.ReadLine()) != null)
            {
                if (ln.Length == 0 || ln[0] == '#') continue;
                int tab = ln.IndexOf('\t');
                if (tab <= 0) continue;
                string file = ln.Substring(0, tab);
                string b64 = ln.Substring(tab + 1);
                try
                {
                    string text = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                    if (!_map.ContainsKey(text)) _map[text] = file;
                }
                catch { }
            }
        }

        private static string FindVoiceDir()
        {
            List<string> cands = new List<string>();
            try { cands.Add(Path.Combine(Path.Combine(Application.StartupPath, L.T("苏苏语音")), "voice")); }
            catch { }
            try { cands.Add(Path.Combine(Application.StartupPath, "voice")); }
            catch { }
            try
            {
                DirectoryInfo up = Directory.GetParent(Application.StartupPath);
                if (up != null)
                {
                    cands.Add(Path.Combine(Path.Combine(up.FullName, L.T("苏苏语音")), "voice"));
                    cands.Add(Path.Combine(up.FullName, "voice"));
                }
            }
            catch { }
            for (int i = 0; i < cands.Count; i++)
            {
                try
                {
                    if (Directory.Exists(cands[i]) && File.Exists(Path.Combine(cands[i], "manifest.tsv"))) return cands[i];
                }
                catch { }
            }
            return null;
        }

        /// <summary>播放某句台词。不是苏苏说的、或者没有对应音频，就直接跳过。</summary>
        public static void Play(GuideLine ln)
        {
            if (Muted || ln == null) return;
            if (ln.Who != L.T("苏苏")) return;
            EnsureLoaded();
            if (_map == null || _map.Count == 0) return;
            string file;
            if (!_map.TryGetValue(ln.Text, out file))
            {
                Log.Warn(L.T("配音缺失（这句没有对应音频）：") + ln.Text);
                return;
            }
            PlayFile(file);
        }

        private static void PlayFile(string file)
        {
            try
            {
                byte[] data;
                if (_embedded)
                {
                    Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResPrefix + file);
                    if (s == null) return;
                    using (s) { data = ReadAll(s); }
                }
                else
                {
                    string p = Path.Combine(_dir, file);
                    if (!File.Exists(p)) return;
                    data = File.ReadAllBytes(p);
                }
                if (data == null || data.Length < 44) return;

                Stop();
                _lastDurationMs = WavDurationMs(data);
                _playStarted = DateTime.Now;
                _stream = new MemoryStream(data);
                _player = new SoundPlayer(_stream);
                _player.Play();
                _playing = file;
            }
            catch (Exception ex) { Log.Warn(L.T("播放苏苏配音失败：") + ex.Message); }
        }

        private static byte[] ReadAll(Stream s)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[81920];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                return ms.ToArray();
            }
        }

        /// <summary>从 WAV 头算时长（毫秒）。算不出来返回 0。</summary>
        private static int WavDurationMs(byte[] b)
        {
            try
            {
                if (b.Length < 44) return 0;
                if (!(b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F')) return 0;
                int pos = 12, channels = 0, rate = 0, bits = 0, dataSize = 0;
                while (pos + 8 <= b.Length)
                {
                    string id = "" + (char)b[pos] + (char)b[pos + 1] + (char)b[pos + 2] + (char)b[pos + 3];
                    int size = BitConverter.ToInt32(b, pos + 4);
                    if (size < 0) break;
                    if (id == "fmt " && pos + 8 + 16 <= b.Length)
                    {
                        channels = BitConverter.ToInt16(b, pos + 10);
                        rate = BitConverter.ToInt32(b, pos + 12);
                        bits = BitConverter.ToInt16(b, pos + 22);
                    }
                    else if (id == "data")
                    {
                        dataSize = size > 0 ? size : b.Length - (pos + 8);
                        break;
                    }
                    pos += 8 + size + (size & 1);
                }
                if (channels <= 0 || rate <= 0 || bits <= 0 || dataSize <= 0) return 0;
                int bytesPerSec = channels * rate * bits / 8;
                if (bytesPerSec <= 0) return 0;
                return (int)(dataSize * 1000L / bytesPerSec);
            }
            catch { return 0; }
        }

        public static void Stop()
        {
            try { if (_player != null) { _player.Stop(); _player.Dispose(); } }
            catch { }
            try { if (_stream != null) { _stream.Dispose(); } }
            catch { }
            _player = null;
            _stream = null;
            _playing = null;
            _lastDurationMs = 0;
        }
    }
}
