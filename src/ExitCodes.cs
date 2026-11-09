using System;

namespace Zogos
{
    /// <summary>把 Windows 的进程退出码翻译成人话。</summary>
    public static class ExitCodes
    {
        public static string Explain(int code)
        {
            if (code == 0) return L.T("正常退出");
            uint u = unchecked((uint)code);
            switch (u)
            {
                case 0xC0000005: return "内存访问违例（游戏本体崩了）";
                case 0xC000001D: return "非法指令（CPU 指令不兼容 / 文件损坏）";
                case 0xC0000094: return "整数除零";
                case 0xC00000FD: return "栈溢出（通常是某个模组递归太深）";
                case 0xC0000374: return "堆损坏（内存被写坏了，常见于原生插件冲突）";
                case 0xC0000409: return "栈缓冲区溢出 / 安全校验失败";
                case 0xC0000135: return "缺少 DLL（没装运行库，例如 VC++ 运行库）";
                case 0xC0000139: return "DLL 入口点找不到（DLL 版本不匹配）";
                case 0xC0000142: return "DLL 初始化失败（运行库或驱动问题）";
                case 0xC000007B: return "程序位数不匹配（32 位 / 64 位装错了）";
                case 0xE0434352: return ".NET 未处理异常（托管代码抛错）";
                case 0xE06D7363: return "C++ 异常未捕获";
                case 0x80000003: return "断点异常 / 反调试触发";
            }
            if (u == 0xFFFFFFFF) return L.T("进程被强制结束（可能是被任务管理器或杀软杀掉）");
            if (u >= 0x80000000) return L.T("系统级错误（0x") + u.ToString("X8") + "）";
            return L.T("未知退出码");
        }

        /// <summary>是不是"崩溃"级别的退出码（而不是正常退出）。</summary>
        public static bool IsCrash(int code)
        {
            if (code == 0) return false;
            uint u = unchecked((uint)code);
            return u >= 0x80000000;
        }

        /// <summary>根据退出码给出排查建议（每行一条）。</summary>
        public static string Advice(int code)
        {
            uint u = unchecked((uint)code);
            if (u == 0xC0000005)
                return L.T("① 先换个排序方案再试（排除模组冲突）\n")
                     + L.T("② 检查 C 盘剩余空间，至少留 20 GB\n")
                     + L.T("③ 整合包别放在 C 盘，移到 D 盘等其它盘\n")
                     + L.T("④ 显卡驱动更新到最新（尤其是 Vulkan 版本）\n")
                     + L.T("⑤ 把整合包文件夹加入杀毒软件白名单\n")
                     + L.T("⑥ 重新完整解压一次整合包（排除文件损坏）");
            if (u == 0xC0000135 || u == 0xC0000142 || u == 0xC000007B)
                return L.T("① 安装 VC++ 运行库（vcredist x64）\n")
                     + L.T("② 确认下载的是 64 位整合包（RimWorldWin64 必须配 64 位）\n")
                     + L.T("③ 重新解压整合包");
            if (u == 0xE0434352)
                return L.T("① 通常是某个模组抛了未捕获异常，看 Player.log 里的堆栈\n")
                     + L.T("② 用二分法禁用模组定位");
            return L.T("① 先换个排序方案试试\n")
                 + L.T("② 看【日志】页的 Player.log 有没有 ERROR\n")
                 + L.T("③ 把诊断报告发给社区管理员");
        }
    }
}
