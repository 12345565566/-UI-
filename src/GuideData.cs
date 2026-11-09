using System;
using System.Collections.Generic;

namespace Zogos
{
    /// <summary>一句台词。Pic>0 时切换苏苏的贴图。</summary>
    public class GuideLine
    {
        public string Who = "";
        public string Text = "";
        public int Pic;
        public GuideLine(string who, string text, int pic) { Who = who; Text = text; Pic = pic; }
    }

    public class GuideChoice
    {
        public string Label = "";
        public string Next = "";
        public bool Disabled;
        public string Note = "";
        public string Action = "";   // community / import：点了执行动作而不是跳节点
    }

    public class GuideNode
    {
        public string Id = "";
        public string Title = "";
        public List<GuideLine> Lines = new List<GuideLine>();
        public List<GuideChoice> Choices = new List<GuideChoice>();
        public string Next = "";
    }

    /// <summary>
    /// 苏苏剧本（来自《Astra 创世神协议 · 苏苏版说明书》）。
    /// 图号对应文档里的 15 张插画，对话走到哪句就换哪张。
    /// </summary>
    public static class GuideScript
    {
        public const string StartId = "s0";
        public const string TocId = "toc";

        private static Dictionary<string, GuideNode> _nodes = new Dictionary<string, GuideNode>();

        public static GuideNode Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            GuideNode n;
            return _nodes.TryGetValue(id, out n) ? n : null;
        }

        private static GuideNode N(string id, string title)
        {
            GuideNode n = new GuideNode();
            n.Id = id; n.Title = title;
            _nodes[id] = n;
            return n;
        }
        private static void Say(GuideNode n, int pic, string text) { n.Lines.Add(new GuideLine(L.T("苏苏"), text, pic)); }
        private static void Note(GuideNode n, string text) { n.Lines.Add(new GuideLine("", text, 0)); }
        private static void Pick(GuideNode n, string label, string next)
        {
            GuideChoice c = new GuideChoice();
            c.Label = label; c.Next = next;
            n.Choices.Add(c);
        }

        private static void PickAct(GuideNode n, string label, string action)
        {
            GuideChoice c = new GuideChoice();
            c.Label = label; c.Next = ""; c.Action = action;
            n.Choices.Add(c);
        }

        static GuideScript() { Build(); }

        private static void Build()
        {
            // ---------- 首次启动（图 3：指着问你要快还是要详） ----------
            GuideNode s0 = N(StartId, L.T("开场"));
            Say(s0, 3, L.T("你来啦。"));
            Say(s0, 3, L.T("这个启动器我看完了，不难，就是文档写得像说明书。"));
            Say(s0, 3, L.T("你想快点开起来，还是听我细说？"));
            Pick(s0, L.T("【1】赶时间 —— 只告诉我怎么开就行"), "quick");
            Pick(s0, L.T("【2】我有空 —— 你慢慢讲"), TocId);

            // ---------- 呼叫苏苏：先问你想干嘛 ----------
            GuideNode hub = N("hub", L.T("苏苏在"));
            Say(hub, 3, L.T("叫我啦？"));
            Say(hub, 3, L.T("想让我做什么？"));
            Pick(hub, L.T("【体检】本地快速检查（不上传）"), "diag");
            PickAct(hub, L.T("【AI 诊断】上传日志让 AI 分析给建议"), "ai");
            Pick(hub, L.T("【目录】我想查某个功能怎么用"), TocId);
            Pick(hub, L.T("【从头讲】带我把怎么开玩一遍"), "quick");


            // ---------- 快速版（图 4：认真压桌 → 图 5：得意比 3） ----------
            GuideNode q = N("quick", L.T("快速版：三步开起来"));
            Say(q, 4, L.T("行，三步。我尽量短。"));
            Say(q, 4, L.T("① 主页那颗【导入整合包】按钮（或顶部【分享】页的【导入整合包…】），选你下的那个 zip。"));
            Say(q, 4, L.T("② 它会先给包做个体检，重点看两行：\n· Config 文件夹  有\n· 排序文件 文件夹  有"));
            Say(q, 4, L.T("缺一个这包就是坏的，别导入，重新下。"));
            Say(q, 4, L.T("解压路径要纯英文，比如 D:\\RimWorldPack。中文路径游戏加载不了模组 —— 这个坑我替你踩过了。"));
            Say(q, 4, L.T("③ 导完它会问你「设为当前整合包」，点是。"));
            Say(q, 4, L.T("然后去【排序】页挑一张卡片，回主页点最大的那颗【启动游戏】。"));
            Say(q, 4, L.T("……完了。"));
            Say(q, 5, L.T("报错了再回来找我，第三部分我写了。"));
            Note(q, L.T("给急的人：导入 → 设为当前 → 选排序 → 启动。就这四步。"));
            q.Next = "end";

            // ---------- 目录（图 6：拿讲稿 → 图 7：歪头 → 图 8：托腮等你选） ----------
            GuideNode toc = N(TocId, L.T("目录"));
            Say(toc, 6, L.T("那我先把目录报给你，你别全看，会困。"));
            Note(toc, L.T("★★★ 必看\n第一部分  装完先做这三件事\n第二部分  核心功能详解\n第三部分  出问题了怎么办"));
            Note(toc, L.T("★★ 建议看\n第四部分  把整合包分享给别人\n第五部分  设置页逐项说明"));
            Note(toc, L.T("★ 有空再看\n第六部分  外观自定义（只有赞助版有）\n第七部分  普通版和赞助版的区别"));
            Note(toc, L.T("☆ 备查\n第八部分  附录（FAQ、文件位置）"));
            Say(toc, 7, L.T("我的建议是：先看第一部分 1.2 那一节。看完就能玩了。"));
            Say(toc, 8, L.T("剩下的等真出问题再翻，不用现在啃。想看哪块？"));
            Pick(toc, L.T("【0】先让苏苏看看我现在的状态（体检）"), "diag");
            Pick(toc, L.T("【2.1】第一部分 —— 装完先做三件事"), "b1");
            Pick(toc, L.T("【2.2】第二部分 —— 核心功能都管什么"), "b2");
            Pick(toc, L.T("【2.3】第三部分 —— 出问题了怎么办（最重要）"), "b3");
            Pick(toc, L.T("【2.4】第四部分 —— 把包分享给别人"), "b4");
            Pick(toc, L.T("【2.5】第五部分 —— 设置页七个开关"), "b5");
            Pick(toc, L.T("【2.6】第六、七部分 —— 外观 / 版本区别"), "b6");
            Pick(toc, L.T("【2.7】第八部分 —— 附录和常见问题"), "b7");
            Pick(toc, L.T("【1】我还是赶时间 —— 走快速版"), "quick");
            GuideChoice ai = new GuideChoice();
            ai.Label = L.T("【AI】有什么直接问苏苏（即将上线）");
            ai.Next = ""; ai.Disabled = true; ai.Note = L.T("还在路上，先别急");
            toc.Choices.Add(ai);

            // ---------- 分支 2.1（图 9：前倾撑桌 → 图 10：导入那节） ----------
            GuideNode b1 = N("b1", L.T("第一部分：装完先做三件事"));
            Say(b1, 9, L.T("先说它到底干嘛。整合包 ＝ 游戏本体 ＋ 一堆模组 ＋ 一份排序表。"));
            Say(b1, 9, L.T("排序表决定谁先加载、谁后加载。顺序错，游戏就报错、闪退，或者模组干脆不生效。"));
            Say(b1, 9, L.T("启动器把这一堆活儿包了，你只要做两件事：\n① 选一个排序\n② 点【启动游戏】"));
            Say(b1, 9, L.T("它替你做的四件事：备份你原来的配置 → 用整合包配置替换 → 写入你选的排序 → 启动主程序"));
            Say(b1, 10, L.T("1.2 导入整合包 —— 这一步做完，剩下都是小事。你手上的包可能是两种形态。"));
            Say(b1, 10, L.T("情况 A：拿到的是 zip。主页【导入整合包】或【分享】页的【导入整合包…】，选 zip。"));
            Say(b1, 10, L.T("它会弹一张体检清单，重点看这两行：\n· Config 文件夹  有 ← 必须有\n· 排序文件 文件夹  有 ← 必须有"));
            Say(b1, 10, L.T("都显示「没有」的话，这包是坏的。别导入，重新下。"));
            Say(b1, 10, L.T("解压位置建议：英文路径（比如 D:\\RimWorldPack）、别放 C 盘、空间留够（清单里的解压后大小 ×1.2）。"));
            Say(b1, 10, L.T("等解压完，问你「设为当前整合包」的时候点是。它会自己写好识别令牌，切到新包。"));
            Say(b1, 10, L.T("情况 B：拿到的是文件夹。要选的是包着 RimWorld、Config、排序文件 这三个文件夹的那一层，不是直接选 RimWorld。"));
            Say(b1, 10, L.T("然后【设置】页 →【绑定整合包文件夹…】→ 选那一层 → 确定。以后你改名、搬盘，它还认得出。"));
            Say(b1, 10, L.T("导入成功长什么样：主页右边「本地模组索引」不能是 0。是 0、或写着「419 个，本地缺失 419 个」，就是包没解压完整。"));
            Say(b1, 10, L.T("导入最常见的三个坑：\n· ① 只解压了一层，里面还是 zip\n· ② 解压到中文路径\n· ③ 解压中断（主程序 0 字节、Mods 空的）"));
            Say(b1, 10, L.T("1.3 选排序：卡片上「含 RJW」＝成人向，「本地缺失 N 个」＝这版你有 N 个没有。不知道怎么选就选「整合包默认排序」。"));
            Say(b1, 10, L.T("1.4 点启动：回主页点最大的那颗【启动游戏】。第一次多花几秒（要备份 + 写配置），之后快了。"));
            Say(b1, 10, L.T("1.5 三种情况：只有 zip → 情况 A；有文件夹没认出来 → 情况 B；已经能玩只想换玩法 → 直接去排序页换卡片。"));
            b1.Next = "end";

            // ---------- 分支 2.2（图 11） ----------
            GuideNode b2 = N("b2", L.T("第二部分：核心功能"));
            Say(b2, 11, L.T("2.1 主页：左边一张大白卡，是当前整合包的身份牌。右边浅蓝面板「整合包信息」十行，全是需要核对的东西。"));
            Say(b2, 11, L.T("提示条黄色是提醒、红色是警告 —— 红色一定要看。右上角两颗按钮：【❤ 赞助创作者】【边缘创世神社区】。"));
            Say(b2, 11, L.T("2.2 排序页：一张卡片＝一套排序方案，点一下就切过去。"));
            Say(b2, 11, L.T("2.3 点「启动游戏」之后它按顺序做：备份配置 → 用整合包配置替换 → 写入排序 → 拉起主程序。第一次慢是在备份。"));
            Say(b2, 11, L.T("2.4 启动前自检：会检查主程序在不在、是不是正常的 Windows 程序。它拦你是有原因的 —— 那种状态进去也是闪退。"));
            b2.Next = "end";

            // ---------- 分支 2.3（图 12：严肃） ----------
            GuideNode b3 = N("b3", L.T("第三部分：出问题了怎么办"));
            Say(b3, 12, L.T("3.1 先分清是哪三种失败，处理方式完全不一样："));
            Note(b3, L.T("A：弹「启动前检查没通过」\n　→ 整合包坏了 / 不完整 / 不是 Windows 版 → 回 1.2 重新完整解压\n\nB：弹「启动游戏失败」，还自动生成了诊断报告\n　→ 主程序能起但很快退出 → 把诊断报告发给社区管理员\n\nC：游戏能进去但冒红字 / 模组加载失败\n　→ 模组版本不兼容，不是启动器的问题\n　→ 看【日志】页 →【打开游戏日志】，搜 ERROR"));
            Say(b3, 12, L.T("一句口诀，记这个就够：\n· 启动器弹框 → 是启动器 / 整合包的问题\n· 游戏内红字 → 是模组的问题"));
            Say(b3, 12, L.T("3.2 日志页按钮：【刷新】【打开游戏日志】【打开崩溃日志】【打开配置目录】【生成诊断报告】。"));
            Say(b3, 12, L.T("搜「错误」两个字，这次启动所有失败的地方全出来。"));
            Say(b3, 12, L.T("3.3 诊断报告包含：系统资源（磁盘/内存）、所有路径、路径有没有中文、当前排序、模组数量、缺失清单、设置项、游戏日志最近 60 条错误。"));
            Say(b3, 12, L.T("位置：整合包目录\\ZOGOS备份\\ZOGOS诊断报告_日期时间.txt。找管理员发三样：Player.log、这份报告、一句「什么时候报错」。"));
            Say(b3, 12, L.T("3.4 备份与还原：每次完整替换配置之前自动备份，默认留最近 10 份（设置里 1~200 可改）。"));
            Say(b3, 12, L.T("还原：【设置】页 →【从备份还原…】→ 选一个备份 → 确定。还原前它会先把「当前」的再备份一份，所以还原错了也还能退回来。"));
            b3.Next = "end";

            // ---------- 分支 2.4（图 13） ----------
            GuideNode b4 = N("b4", L.T("第四部分：把包分享给别人"));
            Say(b4, 13, L.T("两种包：完整包（带本体和模组，几十 G，别人下了就能玩）和轻量包（只有 Config 和排序文件，几 MB）。"));
            Say(b4, 13, L.T("导出要按命名规范来，不然别人认不出是什么包、多少模组、什么版本。名字乱起，社区里没人敢下。"));
            b4.Next = "end";

            // ---------- 分支 2.5 ----------
            GuideNode b5 = N("b5", L.T("第五部分：设置页七个开关"));
            Say(b5, 0, L.T("上面三个路径指向整合包、游戏、配置 —— 路径错了什么都白搭。"));
            Say(b5, 0, L.T("七个开关里，我说重要的四个：\n· ① 启动前自动备份玩家配置 —— 强烈建议开\n· ② 替换前清空原有 Config —— 建议开\n· ④ 启动前检查并启动 Steam —— Steam 没开有些包起不来\n· ⑤ 路径含中文时弹出警告 —— 不建议关"));
            Say(b5, 0, L.T("还有两个舒坦的：③ 同时同步游戏目录内 Config、⑥ 启动游戏后最小化启动器。"));
            Say(b5, 0, L.T("最后一个是危险开关：⑦ 每次启动都重置配置（会覆盖画面设置）—— 默认关着，开了你改的全屏 / 按键 / 模组设置每次都会没。"));
            Say(b5, 0, L.T("下面六个按钮，日常用得最多的是【从备份还原…】和【绑定整合包文件夹…】。"));
            b5.Next = "end";

            // ---------- 分支 2.6 ----------
            GuideNode b6 = N("b6", L.T("第六、七部分：外观与版本"));
            Say(b6, 0, L.T("第六部分外观自定义只有赞助版有：主题色调、背景图片、自定义图片。普通版没有这块，不是坏了。"));
            Say(b6, 0, L.T("第七部分就一句话：两个版本装出来的名字一样，装赞助版会覆盖普通版，所以只留一个就行。"));
            b6.Next = "end";

            // ---------- 分支 2.7 ----------
            GuideNode b7 = N("b7", L.T("第八部分：附录"));
            Say(b7, 0, L.T("8.1 启动器会动哪些文件、不会动哪些（存档绝对不会动）；8.2 识别令牌；8.3 常见问题 FAQ（八成有答案）；8.4 文件位置速查；8.5 软件信息。"));
            b7.Next = "end";

            // ---------- 显存补丁（图 12：认真脸） ----------
            GuideNode vr = N("vram", L.T("1.7 显存分配补丁"));
            Say(vr, 12, L.T("1.7 换了个新引擎，但泰南没做「显存不够」的保护机制。"));
            Say(vr, 12, L.T("结果就是显存小的电脑（核显、2G/4G 那种）一进游戏就爆显存 —— 黑屏、闪退。你上次那个 0xC0000005 就是这么来的。"));
            Say(vr, 12, L.T("社区有个补丁专门治这个：Steam 创意工坊里叫「1.7 显存分配补丁」。"));
            Say(vr, 12, L.T("装法四步：\n① Steam 库 → RimWorld → 创意工坊\n② 搜「1.7 显存分配补丁」\n③ 点【订阅】\n④ 等 Steam 把它下完"));
            Say(vr, 12, L.T("★ 订阅完还要在排序里启用它，不然等于没装。这一步要改排序表，最稳的是找社区管理员帮你加进去。"));
            Say(vr, 3, L.T("★ 显存大的别装！这补丁对大显存用户是负提升，装了只会更卡。"));
            vr.Next = "end";

            // ---------- 收尾（图 14 → 图 15 埋抱枕） ----------
            GuideNode e = N("end", L.T("收尾"));
            Say(e, 14, L.T("就这样。导入 → 设为当前 → 选排序 → 启动，四步。剩下的都是出问题才用得上的。"));
            Say(e, 15, L.T("有不懂的再来问，我不嫌你烦。"));
            Say(e, 15, L.T("……快去装吧，装完就来玩。"));
            e.Next = "";
        }
    }
}
