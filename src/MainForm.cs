using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Zogos
{
    /// <summary>
    /// 极简版主窗体（并轨版）：分层窗口逐像素透明 + 全自绘元素
    /// （元素体系见 MinControls.cs / MinSort.cs / MinSettings.cs / MinShare.cs，主题令牌见 MinTheme.cs）。
    /// 业务方法与原赛博版 MainForm 一一对应（StartGame / ExportPack / ImportPack / BindPackFolder 等原样搬运）。
    /// 旧「外观」页（主题色 / 视频背景）已移除，主题改为顶栏深浅色一键切换（持久化在 Settings.UiDark）。
    /// 窗口契约：无边框 1010×650、squircle 圆角 12、顶栏 46px。
    /// </summary>
    public class MainForm : LayeredForm
    {
        /// <summary>主窗口内容按 UiScale 放大渲染：布局仍用逻辑坐标（1010×650），文字随矢量等比放大</summary>
        protected override float ContentScale { get { return UiScale; } }

        private PackContext _ctx;
        private Settings _st;
        private List<SortScheme> _schemes = new List<SortScheme>();
        private SortScheme _current;
        private int _page;
        private bool _busy;
        private BackgroundWorker _worker;
        private SusuWindow _susu;
        private string _lastExportZip = "";
        private const string SponsorUrl = "https://www.ifdian.net/a/07210d00"; // 原作者 ZOGOS 爱发电
        private const string UiAuthorSponsorUrl = "https://afdian.com/a/qdqUI"; // UI 作者 A1 爱发电

        // —— 元素树 ——
        ElTopBar _top;
        readonly List<El> _homeEls = new List<El>();
        readonly List<El> _rightEls = new List<El>();
        bool _collapsed;

        // 主页左栏
        ElCard _leftCard;
        ElBtn _more;
        ElLaunch _launch;
        ElInfo _info;

        // 主页右栏
        readonly ElBtn[] _quick = new ElBtn[4]; // 导入整合包 / 创意工坊 / 模组管理 / 整合包资源获取（设置走页签，导出在分享页）
        ElText _rightTitle;
        ElCard _banner;
        ElText _bannerText;
        int _bannerH; // 0=隐藏；横幅高度随文案行数走（UpdateAllUi 里定）
        readonly ElText[] _coreK = new ElText[5];
        readonly ElText[] _coreV = new ElText[5];
        readonly ElText[] _extraV = new ElText[5];
        ElFold _foldInfo, _foldLog;
        ElLogView _log;
        readonly ElBtn[] _bottom = new ElBtn[5];
        ElRail _rail;

        // 日志区（并入设置页：默认一个按钮，点击动画展开为覆盖整页的全屏日志）
        ElBtn _btnLogToggle;
        ElText _lbLogTitle;
        ElCard _logWrap;
        ElLogView _logFull;
        ElBtn _btnLogBack;
        readonly ElBtn[] _logBtn = new ElBtn[3]; // 刷新 / 打开游戏日志 / 打开崩溃日志
        bool _logOpen;
        System.Windows.Forms.Timer _logAnim;
        El[] _logOverlayEls;

        // 诊断页（AI：呼叫苏苏）
        readonly List<El> _diagEls = new List<El>();
        ElCard _diagCard;
        ElText _lbDiagTitle, _lbDiagDesc;
        ElBtn _btnSusu, _btnDiagReport, _btnDiagOpenLog;
        ElCheck _chkSusuLegacy; // 使用旧版悬浮窗（默认新版内嵌覆盖层）

        // 分享页
        readonly List<El> _shareEls = new List<El>();
        ElCard _shareCard;
        ElText _lbShareTitle, _lbShareDesc;
        ElCard _specWrap;
        ElSpecView _spec;
        ElBtn _btnWorkshop, _btnExport, _btnImport, _btnOpenOut, _btnCopySpec;

        // 排序选择页
        readonly List<El> _sortEls = new List<El>();
        ElCard _sortCard;
        ElText _sortTitleT, _sortDescT, _sortHint;
        ElSchemeList _schemeList;
        ElBtn _btnRescan, _btnOpenSortDir, _btnApply;

        // 路径设置页
        readonly List<El> _setEls = new List<El>();
        ElCard _setCard;
        readonly ElText[] _setK = new ElText[4];
        ElInput _tRoot, _tTarget, _tSortDir, _nKeep;
        readonly ElBtn[] _setBrowse = new ElBtn[3];
        readonly ElCheck[] _chk = new ElCheck[7];
        ElText _lbSetStatus;
        ElBtn _btnSave, _btnOpenBackup, _btnRestore, _btnRedetect, _btnBind, _btnReapply;
        ElText _lbScale; // 「界面缩放」标签
        readonly ElBtn[] _scaleOpts = new ElBtn[6]; // 100/115/130/150/175/200% 六个直选档位
        ElInput _focusInput; // 键盘焦点所在输入框（自绘控件，需手动管理）

        // 轻提示条（窗口内右上角滑入，自绘元素 ElToast；多条向下叠放，动画由 _toastAnim 驱动）
        readonly List<ElToast> _toasts = new List<ElToast>();
        System.Windows.Forms.Timer _toastAnim;
        const int TOAST_IN = 12, TOAST_HOLD = 333, TOAST_OUT = 14; // 15ms/帧：滑入≈180ms 停留≈5s 收回≈210ms
        const int TOAST_H = 44, TOAST_GAP = 8; // 逻辑高度 / 多条间距

        // —— 模态覆盖层（创意工坊 / 模组管理）：固定覆盖在主窗口上层，弹出时主 UI 不可交互 ——
        // 自绘元素、随 UiScale 缩放（布局全部用逻辑坐标）；打开时遮罩淡入 + 面板上滑 20px，9 帧 ease-out。
        List<El> _overlayEls;          // null = 无覆盖层
        ElDim _dim;                    // 全窗半透明遮罩（点遮罩关闭）
        Action _overlayLayout;         // 窗口尺寸变化时重排覆盖层
        ElInput[] _ovlInputs;          // 当前覆盖层的输入框 Tab 序
        System.Windows.Forms.Timer _ovlAnim;
        bool _ovlClosing;              // true = 正在播放关闭动画
        int _ovlSlide;                 // 面板整体上移量（20→0）
        const int OVL_STEPS = 9;

        // 创意工坊覆盖层（对应原 WorkshopForm：搜索 / 版本类型筛选 / 下载进整合包 / 打开页面 / 工坊同步）
        readonly List<El> _wsEls = new List<El>();
        ElCard _wsPanel;
        ElText _wsTitle, _wsDesc, _wsLbVer, _wsLbType, _wsStatus;
        ElInput _wsInput;
        ElCheck _wsTranslate;
        readonly ElBtn[] _wsVerOpts = new ElBtn[5];
        readonly ElBtn[] _wsTypeOpts = new ElBtn[2];
        ElBtn _wsBtnSearch, _wsBtnAi, _wsBtnDownload, _wsBtnSync, _wsBtnOpen, _wsBtnClose;
        ElWsList _wsList;
        int _wsVerIdx, _wsTypeIdx; // 当前选中的版本档 / 类型档
        bool _wsBusy;
        static readonly string[] WsVersions = { "", "1.7", "1.6", "1.5", "1.4" };
        static readonly string[] WsVerLabels = { "全部版本", "1.7", "1.6", "1.5", "1.4" };
        static readonly string[] WsTypeLabels = { "模组", "合集" };

        // AI 接口配置覆盖层（对应原 AiSetupForm：地址 / KEY / 模型 + 拉取模型列表）
        readonly List<El> _aiEls = new List<El>();
        ElCard _aiPanel;
        ElText _aiTitle, _aiNote, _aiTip, _aiStatus, _aiLbBase, _aiLbKey, _aiLbModel;
        ElInput _aiBase, _aiKey, _aiModel;
        ElBtn _aiBtnFetch, _aiBtnSave, _aiBtnCancel, _aiBtnClose;
        ElStrList _aiModels;
        bool _aiBusy, _aiReturnWs; // _aiReturnWs：从创意工坊里点进来的，关掉后自动回到创意工坊覆盖层

        // 整合包资源获取覆盖层（对应原 PackFeedForm：卡片墙 + 详情视图 + 提交/导入子面板）
        readonly List<El> _pfEls = new List<El>();
        ElCard _pfPanel;
        ElText _pfTitle, _pfSourceLb, _pfNotice, _pfStatus;
        ElInput _pfSource;
        ElBtn _pfBtnSaveSrc, _pfBtnRefresh, _pfBtnImport, _pfBtnSubmit, _pfBtnClose, _pfBtnView;
        ElPackGrid _pfGrid;
        // 详情视图（Visible 切换，与列表视图互斥）
        ElBtn _pfBtnBack, _pfBtnQuark, _pfBtnXunlei;
        ElCover _pfCover;
        ElText _pfDName;
        ElPara _pfDBody;
        bool _pfDetailOn;
        // 子面板（提交整合包 / 导入表格；面板内再盖一层遮罩吞点击）
        ElDim _pfSubDim;
        ElCard _pfSubPanel;
        ElText _pfSubTitle, _pfSubTip, _pfSubStatus, _pfLbName, _pfLbContact, _pfLbNote;
        ElInput _pfSubName, _pfSubContact, _pfSubNote;
        ElBtn _pfSubOk, _pfSubFile, _pfSubCancel;
        string _pfSubMode; // null=关 / "submit" / "import"
        // 数据
        List<PackItem> _pfItems = new List<PackItem>();
        PackItem _pfCurrent;
        bool _pfBusy, _pfSharedLoading;
        Image _pfSharedCover; // 所有卡片共用的默认预览图（下载一次）

        // 模组管理覆盖层（对应原 ModGraphForm：依赖网状图 + 排序列表 + 搜索 + AI 分类/检测）
        readonly List<El> _mgEls = new List<El>();
        ElCard _mgPanel;
        ElText _mgTitle, _mgHint, _mgStatus, _mgListTitle;
        ElBtn _mgBtnClassify, _mgBtnDetect, _mgBtnClose, _mgBtnSearch, _mgBtnTranslate, _mgBtnSave, _mgBtnIgnore;
        ElCheck _mgChkTrans;
        ElInput _mgSearch;
        ElModGraph _mgGraph;
        ElModList _mgList;
        ElDim _mgSubDim;      // 搜索结果下拉打开时的吞点击层（点外面收掉下拉）
        ElStrList _mgMatches; // 搜索结果下拉（暂用列表元素，选中即跳转）
        bool _mgBusy, _mgShowZh;

        /// <summary>自检用：暴露模组图数据（--shot 的详情窗预览）</summary>
        internal ModGraphData DebugGraphData { get { return _mgGraph == null ? null : _mgGraph.Data; } }

        // —— 通用确认弹窗（极简风，替代 MessageBox 的是/否询问）——
        readonly List<El> _cfEls = new List<El>();
        ElConfirm _cfPanel;
        ElBtn _cfYes, _cfNo;
        Action _cfOnYes;
        Action _cfOnClosed; // 确认框关闭后（无论选是/否/取消）要执行的回调——比如返回上一个覆盖层

        // —— 切换整合包覆盖层（替代旧独立窗 PackSwitchForm）——
        readonly List<El> _psEls = new List<El>();
        ElCard _psPanel;
        ElText _psTitle, _psHint;
        ElPackList _psList;
        ElBtn _psBtnSwitch, _psBtnRemove, _psBtnClose;
        List<PackEntry> _psItems;
        string _psSettingsDir = "";

        // —— 苏苏 AI 覆盖层（新版 AI 界面；旧版独立窗 SusuWindow 保留，诊断页勾选切换）——
        readonly List<El> _suEls = new List<El>();
        ElCard _suPanel;
        ElText _suTitle, _suHint;
        ElSusu _suStage;
        ElBtn _suBtnClose, _suNext, _suLog, _suSkip, _suMute;
        ElBtn[] _suChoiceBtns = new ElBtn[8]; // 选项按钮池（最多 8 个）
        // 子面板：记录 / AI 诊断结果（可选中复制）/ 二维码
        ElDim _suSubDim;
        ElCard _suSubPanel;
        ElText _suSubTitle;
        ElLogView _suSubText;
        ElCover _suSubImg;
        ElBtn _suSubClose;
        bool _suSubOn;
        // —— 苏苏状态机（旧版 SusuWindow 同款：节点/打字机/自动推进/配音）——
        GuideNode _suNode;
        int _suLineIdx;
        string _suFull = "", _suShown = "";
        bool _suTyping, _suChoicesShown;
        readonly List<string> _suHistory = new List<string>();
        System.Windows.Forms.Timer _suTypeTimer, _suAutoTimer;

        // 鼠标分发
        El _hoverEl, _captureEl;
        public Point MousePos { get; private set; }

        /// <summary>--debugmouse：把鼠标分发轨迹写进 _trace.log（排查鼠标事件投递问题用）</summary>
        public static bool TraceMouse;
        public static void MTrace(string msg)
        {
            if (!TraceMouse) return;
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_trace.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n");
            }
            catch { }
        }
        static string ElName(El el) { return el == null ? "null" : el.GetType().Name; }

        // —— 调试探针（--debugmouse）：周期性检测光标下的窗口归属，抓「透明覆盖窗抢事件」——
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int max);
        System.Windows.Forms.Timer _probe;
        IntPtr _probeLast = IntPtr.Zero;

        // —— 装饰框/后处理：呼吸描边 + 角标 + 扫描线（FrameFx 自己裁剪，不改任何布局坐标）——
        System.Windows.Forms.Timer _fx;

        public MainForm(PackContext ctx, Settings st)
        {
            _ctx = ctx;
            _st = st;
            T.Dark = st.UiDark; // 恢复上次的深浅色选择
            UiScale = Math.Max(100, Math.Min(200, st.UiScalePct)) / 100f; // 恢复上次的界面缩放

            Text = Edition.Title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            SetLogicalSize(1010, 650); // 逻辑尺寸；物理像素 = ×UiScale
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildHome();
            BuildSort();
            BuildSet();
            BuildShare();
            BuildDiag();
            BuildWorkshopOverlay(); // 覆盖层元素只建一次，OpenOverlay 时才显示
            BuildAiOverlay();
            BuildPackFeedOverlay();
            BuildModGraphOverlay();
            BuildConfirmOverlay();
            BuildPackSwitchOverlay();
            BuildSusuOverlay();
            _top = new ElTopBar { Host = this };
            _top.TabChanged += SwitchPage;
            _top.MinClicked += delegate { WindowState = FormWindowState.Minimized; };
            _top.CloseClicked += delegate { Close(); };
            _top.ThemeClicked += delegate { ToggleTheme(); };
            _top.CoffeeClicked += ShowCoffeeMenu; // 咖啡杯：赞助创作者 / 作者主页

            Log.Added += OnLogLine;
            Load += delegate { DoLayout(); OnLoaded(); };
            Move += delegate { try { if (_susu != null && _susu.Visible) _susu.PlaceNear(this); } catch { } };
            FormClosed += delegate
            {
                try { if (_foldAnim != null) { _foldAnim.Stop(); _foldAnim.Dispose(); _foldAnim = null; } } catch { }
                try { if (_logAnim != null) { _logAnim.Stop(); _logAnim.Dispose(); _logAnim = null; } } catch { }
                try { if (_toastAnim != null) { _toastAnim.Stop(); _toastAnim.Dispose(); _toastAnim = null; } } catch { }
                try { if (_ovlAnim != null) { _ovlAnim.Stop(); _ovlAnim.Dispose(); _ovlAnim = null; } } catch { }
                try { if (_fx != null) { _fx.Stop(); _fx.Dispose(); _fx = null; } } catch { }
            };

            SwitchPage(0);
            LayoutHome();
            if (_st.RightCollapsed) SetRightCollapsed(true, false); // 恢复上次的折叠状态（无动画，直接到位）

            if (TraceMouse) StartProbe();
            StartFrameFx();
        }

        /// <summary>装饰框呼吸相位驱动：约 11fps 只做整窗重绘，不动布局</summary>
        void StartFrameFx()
        {
            if (!FrameFx.Enabled) return;
            _fx = new System.Windows.Forms.Timer { Interval = 90 };
            _fx.Tick += delegate
            {
                if (!FrameFx.Enabled || WindowState == FormWindowState.Minimized) return;
                if (Form.ActiveForm != this) return; // 后台/失焦时不空转重绘
                FrameFx.Advance(0.07f);
                RenderFxOnly(); // 廉价刷新：只重叠特效层，不重绘全部元素（解决常驻卡顿的主要来源）
            };
            _fx.Start();
        }

        void StartProbe()
        {
            _probe = new System.Windows.Forms.Timer { Interval = 500 };
            _probe.Tick += delegate
            {
                Point cp;
                if (!GetCursorPos(out cp)) return;
                IntPtr w = WindowFromPoint(cp);
                if (w == _probeLast) return;
                _probeLast = w;
                if (w == Handle) { MTrace("光标下: 本程序窗口"); return; }
                var cls = new System.Text.StringBuilder(128);
                var ttl = new System.Text.StringBuilder(256);
                GetClassName(w, cls, 128);
                GetWindowText(w, ttl, 256);
                MTrace("光标下: 【外部窗口】class=[" + cls + "] title=[" + ttl + "]");
            };
            _probe.Start();
        }

        T AddEl<T>(List<El> list, T el) where T : El { el.Host = this; list.Add(el); return el; }

        /// <summary>五页只切元素列表（p：0=主页 1=排序选择 2=设置（含日志） 3=分享 4=诊断）</summary>
        private void SwitchPage(int p)
        {
            if (_logOpen) CloseLogInstant(); // 日志覆盖层开着的话先收掉
            if (_overlayEls != null) CloseOverlayInstant(); // 模态覆盖层（创意工坊等）同理
            if (p != 0 && _collapsed) SetRightCollapsed(false); // 折叠态切页 → 先展开右栏（窗口复原 1010）
            _page = p;
            SetFocusInput(null); // 切页时交出键盘焦点
            _top.SetSelected(Math.Min(p, 4));
            if (p == 2) RefreshLogText();      // 日志区在设置页底部
            if (p == 3) _spec.Text = PackSpec.Text(); // 分享页
            Render();
        }

        /// <summary>当前页的元素列表</summary>
        List<El> CurrentList
        {
            get
            {
                switch (_page)
                {
                    case 0: return _homeEls;
                    case 1: return _sortEls;
                    case 2: return _setEls;
                    case 3: return _shareEls;
                    default: return _diagEls;
                }
            }
        }

        /// <summary>输入框键盘焦点管理（自绘控件没有系统焦点概念）</summary>
        void SetFocusInput(ElInput inp)
        {
            if (_focusInput == inp) return;
            if (_focusInput != null) _focusInput.SetFocus(false);
            _focusInput = inp;
            if (_focusInput != null) _focusInput.SetFocus(true);
        }

        // =================================================================
        //  构建界面
        // =================================================================
        void BuildHome()
        {
            // —— 左卡片（340×572）——
            _leftCard = AddEl(_homeEls, new ElCard());

            // 「打开更多功能」只在右栏折叠后出现（展开态下功能都在右栏/顶栏，不重复占位）
            _more = AddEl(_homeEls, new ElBtn(L.T("打开更多功能  ▾")) { Visible = false });
            _more.Clicked += ShowMoreMenu;

            _launch = AddEl(_homeEls, new ElLaunch());
            _launch.LaunchClicked += delegate { StartGame(); };

            _info = AddEl(_homeEls, new ElInfo());
            _info.SortClicked += delegate { SwitchPage(1); };

            // —— 右面板 ——
            // 顶部 4 快捷按钮（「设置」与页签重复已删；「导出整合包」挪到分享页）
            string[] quick = { "导入整合包", "创意工坊", "模组管理", "整合包资源获取" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var b = new ElBtn(L.T(quick[i])) { Bold = true };
                b.Clicked += delegate
                {
                    if (idx == 0) ImportPack();
                    else if (idx == 1) OpenWorkshop();
                    else if (idx == 2) OpenModGraph();
                    else OpenPackFeed();
                };
                AddEl(_homeEls, b);
                _rightEls.Add(b);
                _quick[i] = b;
            }

            _rightTitle = AddEl(_homeEls, new ElText(L.T("子页面功能面板")) { FontOf = () => T.B(15) });
            _rightEls.Add(_rightTitle);

            // 环境检查横幅（默认隐藏，UpdateAllUi 按 ctx.Ok / NonAscii 决定显示与文案）
            _banner = AddEl(_homeEls, new ElCard { Radius = 8, FillOf = () => T.WarnBg, BorderOf = () => T.WarnLine, Visible = false });
            _bannerText = AddEl(_homeEls, new ElText("") { FontOf = () => T.F(11.5f), ColorOf = () => T.WarnText, Visible = false });
            _rightEls.Add(_banner);
            _rightEls.Add(_bannerText);

            // 核心信息 5 行（值由 UpdateAllUi 填）
            string[] coreKeys = { "整合包路径", "游戏主程序", "游戏版本", "当前排序方案", "模组数量" };
            for (int i = 0; i < 5; i++)
            {
                _coreK[i] = AddEl(_homeEls, new ElText(L.T(coreKeys[i])) { ColorOf = () => T.Text3 });
                _coreV[i] = AddEl(_homeEls, new ElText("-") { Ellipsis = true });
                _rightEls.Add(_coreK[i]);
                _rightEls.Add(_coreV[i]);
            }

            // 折叠分组 1：更多环境信息
            _foldInfo = AddEl(_homeEls, new ElFold { Title = L.T("更多环境信息"), Hint = L.T("模组索引 · 数据目录 · 备份 · 令牌"), ContentHeight = 125 });
            string[] extraKeys = { "本地模组索引", "游戏数据目录", "排序文件夹", "备份目录", "识别令牌" };
            for (int i = 0; i < 5; i++)
            {
                var k = new ElText(L.T(extraKeys[i])) { ColorOf = () => T.Text3 };
                _extraV[i] = new ElText("-") { Ellipsis = true };
                k.Host = this; _extraV[i].Host = this;
                _foldInfo.Children.Add(k);
                _foldInfo.Children.Add(_extraV[i]);
            }
            _rightEls.Add(_foldInfo);

            // 折叠分组 2：运行日志
            _foldLog = AddEl(_homeEls, new ElFold { Title = L.T("运行日志"), Hint = L.T("点击展开查看"), ContentHeight = 148 });
            var logWrap = new ElCard { Radius = 8, FillOf = () => T.LogBg, BorderOf = () => T.Line2 };
            _log = new ElLogView();
            logWrap.Host = this; _log.Host = this;
            _foldLog.Children.Add(logWrap);
            _foldLog.Children.Add(_log);
            _rightEls.Add(_foldLog);

            // 手风琴互斥 + 展开后重排
            _foldInfo.OpenChanged += delegate { if (_foldInfo.IsOpen) _foldLog.SetOpen(false); RelayoutRight(); };
            _foldLog.OpenChanged += delegate { if (_foldLog.IsOpen) _foldInfo.SetOpen(false); RelayoutRight(); };

            // 底部 5 按钮
            string[] bottom = { "游戏目录", "配置目录", "备份目录", "模组管理器", "诊断报告" };
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var b = new ElBtn(L.T(bottom[i])) { Bold = true };
                b.Clicked += delegate
                {
                    if (idx == 0) PathHelpOpen(_ctx.GameDir);
                    else if (idx == 1) PathHelpOpen(_ctx.TargetConfigDir);
                    else if (idx == 2) PathHelpOpen(_ctx.BackupDir);
                    else if (idx == 3) OpenRimSort();
                    else MakeReport(true);
                };
                AddEl(_homeEls, b);
                _rightEls.Add(b);
                _bottom[i] = b;
            }

            // —— 右栏收起柄（窗口最右侧）——
            // 折叠/展开状态持久化：点收起柄时写入 Settings，下次启动在 OnLoaded 里恢复（无动画直接到位）。
            // 注意：SwitchPage 切页时的强制展开不改这个偏好——那只是临时让路，用户回到主页仍按他上次的选择来。
            _rail = AddEl(_homeEls, new ElRail());
            _rail.Clicked += delegate
            {
                SetRightCollapsed(!_rail.Collapsed);
                _st.RightCollapsed = _rail.Collapsed;
                _st.Save();
            };
        }

        void BuildSort()
        {
            _sortCard = AddEl(_sortEls, new ElCard { Radius = 12 });

            _sortTitleT = AddEl(_sortEls, new ElText(L.T("选择排序方案")) { FontOf = () => T.B(15) });
            _sortDescT = AddEl(_sortEls, new ElText(
                L.T("每个方案就是一份模组排序（ModsConfig.xml）。启动时启动器会先备份你原来的配置，再写入整合包配置和你选的排序。"))
            { FontOf = () => T.F(11.5f), ColorOf = () => T.Text2 });
            _sortHint = AddEl(_sortEls, new ElText(L.T("还没有选择排序方案")) { FontOf = () => T.F(11.5f), ColorOf = () => T.Text3 });

            _schemeList = AddEl(_sortEls, new ElSchemeList()); // 卡片由 BuildCards 按真实方案重建

            _btnRescan = AddEl(_sortEls, new ElBtn(L.T("重新扫描")));
            _btnRescan.Clicked += delegate { ReloadSchemes(true); };
            _btnOpenSortDir = AddEl(_sortEls, new ElBtn(L.T("打开排序文件夹")));
            _btnOpenSortDir.Clicked += delegate { PathHelpOpen(_ctx.SortDir); };
            _btnApply = AddEl(_sortEls, new ElBtn(L.T("用这个方案启动")) { Primary = true });
            _btnApply.Clicked += delegate { StartGame(); };
        }

        void BuildSet()
        {
            _setCard = AddEl(_setEls, new ElCard { Radius = 12 });

            // 4 行路径/数量
            string[] keys = { "整合包根目录", "游戏配置目录（UserData）", "排序文件目录", "备份保留数量" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _setK[i] = AddEl(_setEls, new ElText(L.T(keys[i])) { FontOf = () => T.F(12), ColorOf = () => T.Text2 });
                var inp = AddEl(_setEls, new ElInput());
                if (i == 3) { inp.NumericOnly = true; inp.Min = 1; inp.Max = 200; } // 备份保留数量 1–200（与原 NumericUpDown 一致）
                if (i == 0) _tRoot = inp; else if (i == 1) _tTarget = inp; else if (i == 2) _tSortDir = inp; else _nKeep = inp;
                if (i < 3)
                {
                    var b = AddEl(_setEls, new ElBtn(L.T("浏览")));
                    b.Clicked += delegate
                    {
                        if (idx == 0) BrowseInto(_tRoot, L.T("选择整合包根目录（里面要有 RimWorld 文件夹）"));
                        else if (idx == 1) BrowseInto(_tTarget, L.T("选择游戏配置目录（建议：整合包\\UserData\\Config）"));
                        else BrowseInto(_tSortDir, L.T("选择存放排序文件的文件夹"));
                    };
                    _setBrowse[idx] = b;
                }
            }

            // 7 个复选框（勾选状态由 LoadSettingsToUi 填）
            string[] cks = {
                "启动前自动备份玩家配置（强烈建议开启）",
                "替换前清空原有的 Config 内容",
                "同时同步游戏目录内的 Config（可能覆盖你的设置，建议关）",
                "启动前检查并启动 Steam",
                "路径含中文时弹出警告",
                "启动游戏后最小化启动器",
                "每次启动都重置配置（会覆盖画面设置）",
            };
            for (int i = 0; i < 7; i++)
                _chk[i] = AddEl(_setEls, new ElCheck(L.T(cks[i]), false));

            _lbSetStatus = AddEl(_setEls, new ElText("") { FontOf = () => T.F(11.5f), ColorOf = () => T.Text3 });

            // 界面缩放：一排直选档位（点谁切谁，当前档高亮），放在「备份保留数量」行右侧空位
            _lbScale = AddEl(_setEls, new ElText(L.T("界面缩放")) { FontOf = () => T.F(12), ColorOf = () => T.Text2 });
            for (int i = 0; i < ScaleSteps.Length; i++)
            {
                int idx = i;
                _scaleOpts[i] = AddEl(_setEls, new ElBtn(ScaleSteps[idx] + "%"));
                _scaleOpts[i].Clicked += delegate { ApplyUiScale(ScaleSteps[idx], true); };
            }
            RefreshScaleOpts();

            // 底部右锚 6 按钮（顺序与原实现一致：从右往左 保存/打开备份/还原/重检/绑定/重套用）
            _btnReapply = AddEl(_setEls, new ElBtn(L.T("重新套用配置")));
            _btnReapply.Clicked += delegate
            {
                DeployState.Reset(_ctx);
                MinMsg.Show(this, L.T("已清除部署记录。\r\n\r\n下次点【启动游戏】时，会重新完整套用一次整合包配置（你的画面/按键设置会被覆盖）。"),
                    L.T("重新套用配置"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            _btnBind = AddEl(_setEls, new ElBtn(L.T("绑定整合包文件夹…")) { AccentEdge = true });
            _btnBind.Clicked += delegate { BindPackFolder(); };
            _btnRedetect = AddEl(_setEls, new ElBtn(L.T("重新检测")));
            _btnRedetect.Clicked += delegate { Redetect(); };
            _btnRestore = AddEl(_setEls, new ElBtn(L.T("从备份还原…")));
            _btnRestore.Clicked += delegate { RestoreFromBackup(); };
            _btnOpenBackup = AddEl(_setEls, new ElBtn(L.T("打开备份文件夹")));
            _btnOpenBackup.Clicked += delegate { PathHelpOpen(_ctx.BackupDir); };
            _btnSave = AddEl(_setEls, new ElBtn(L.T("保存设置")) { Primary = true });
            _btnSave.Clicked += delegate { SaveSettingsFromUi(); };

            // —— 运行日志：默认只占一个按钮，点击后动画展开覆盖整个设置页 ——
            _btnLogToggle = AddEl(_setEls, new ElBtn(L.T("运行日志　›")));
            _btnLogToggle.Clicked += delegate { SetLogOpen(true); };

            // 覆盖层（注意添加顺序 = 绘制/命中层级：底卡在最下，内容在上，最后添加的最顶层）
            _logWrap = AddEl(_setEls, new ElCard { Radius = 10, FillOf = () => T.Card, BorderOf = () => T.Line, Visible = false });
            _lbLogTitle = AddEl(_setEls, new ElText(L.T("运行日志")) { FontOf = () => T.B(15), Visible = false });
            string[] lbn = { "刷新", "打开游戏日志", "打开崩溃日志" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var b = AddEl(_setEls, new ElBtn(L.T(lbn[i])) { Visible = false });
                b.Clicked += delegate
                {
                    if (idx == 0) RefreshLogText();
                    else if (idx == 1) PathHelpOpen(DirOrFile(_ctx.LocalLowLogsDir, _ctx.PlayerLog));
                    else PathHelpOpen(_ctx.GameCrashesDir);
                };
                _logBtn[idx] = b;
            }
            _btnLogBack = AddEl(_setEls, new ElBtn(L.T("‹ 返回")) { Visible = false });
            _btnLogBack.Clicked += delegate { SetLogOpen(false); };
            _logFull = AddEl(_setEls, new ElLogView { Visible = false }); // 展开时 RefreshLogText 填 Log.TailText(2000)
            _logOverlayEls = new El[] { _logWrap, _lbLogTitle, _logBtn[0], _logBtn[1], _logBtn[2], _btnLogBack, _logFull };
        }

        // —— 诊断页（AI 功能入口：呼叫苏苏 AI 诊断 + 诊断报告）——
        void BuildDiag()
        {
            _diagCard = AddEl(_diagEls, new ElCard { Radius = 12 });
            _lbDiagTitle = AddEl(_diagEls, new ElText(L.T("诊断")) { FontOf = () => T.B(15) });
            _lbDiagDesc = AddEl(_diagEls, new ElText(
                L.T("苏苏是内置的 AI 诊断助手：帮你做模组体检、分析日志、定位启动问题并给出修复建议。\n也可以生成一份诊断报告（txt），发给社区管理员人工排查。"))
            { FontOf = () => T.F(11.5f), ColorOf = () => T.Text2 });

            _btnSusu = AddEl(_diagEls, new ElBtn(L.T("呼叫苏苏 AI 诊断")) { Primary = true });
            _btnSusu.Clicked += delegate { CallSusu(false); };
            // 勾选后回到旧版粉色悬浮窗（默认新版内嵌覆盖层）
            _chkSusuLegacy = AddEl(_diagEls, new ElCheck(L.T("使用旧版悬浮窗"), _st.SusuLegacyWindow));
            _chkSusuLegacy.Changed += delegate
            {
                _st.SusuLegacyWindow = _chkSusuLegacy.Checked;
                try { _st.Save(); } catch { }
            };
            _btnDiagReport = AddEl(_diagEls, new ElBtn(L.T("生成诊断报告")));
            _btnDiagReport.Clicked += delegate { MakeReport(true); };
            _btnDiagOpenLog = AddEl(_diagEls, new ElBtn(L.T("打开日志文件夹")));
            _btnDiagOpenLog.Clicked += delegate { PathHelpOpen(DirOrFile(_ctx.LocalLowLogsDir, _ctx.PlayerLog)); };
        }

        void BuildShare()
        {
            _shareCard = AddEl(_shareEls, new ElCard { Radius = 12 });
            _lbShareTitle = AddEl(_shareEls, new ElText(L.T("导出 / 导入整合包")) { FontOf = () => T.B(15) });
            _lbShareDesc = AddEl(_shareEls, new ElText(
                L.T("导出的 zip 里带启动器识别令牌，别人用启动器导入就能直接玩。下面是 ZOGOS 整合包格式规范。"))
            { FontOf = () => T.F(11.5f), ColorOf = () => T.Text2 });

            _specWrap = AddEl(_shareEls, new ElCard { Radius = 8, FillOf = () => T.LogBg, BorderOf = () => T.Line2 });
            _spec = AddEl(_shareEls, new ElSpecView()); // 内容：进页时填 PackSpec.Text()

            _btnWorkshop = AddEl(_shareEls, new ElBtn(L.T("创意工坊搜索…")));
            _btnWorkshop.Clicked += delegate { OpenWorkshop(); };
            _btnExport = AddEl(_shareEls, new ElBtn(L.T("导出整合包…")));
            _btnExport.Clicked += delegate { ExportPack(); };
            _btnImport = AddEl(_shareEls, new ElBtn(L.T("导入整合包…")));
            _btnImport.Clicked += delegate { ImportPack(); };
            _btnOpenOut = AddEl(_shareEls, new ElBtn(L.T("打开上次导出")));
            _btnOpenOut.Clicked += delegate
            {
                if (_lastExportZip.Length > 0 && File.Exists(_lastExportZip)) PathHelpOpen(_lastExportZip);
                else PathHelpOpen(_ctx.Root);
            };
            _btnCopySpec = AddEl(_shareEls, new ElBtn(L.T("复制格式规范")));
            _btnCopySpec.Clicked += delegate
            {
                try { Clipboard.SetText(PackSpec.Text()); SetStatus(L.T("格式规范已复制到剪贴板")); }
                catch (Exception ex) { MinMsg.Show(this, L.T("复制失败：") + ex.Message, L.T("提示"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
        }

        // =================================================================
        //  布局（与原 DoLayout 相同的核心常量：pad=16 gap=14 cw=340，顶栏 46）
        // =================================================================
        private void DoLayout() { LayoutHome(); }

        void LayoutHome()
        {
            int pad = 16, gap = 14, cw = 340, th = 46;
            int bw = Lw, bh = Lh - th;
            if (bw < 100 || bh < 100) return;

            _top.SetBounds(0, 0, bw, th);

            _leftCard.SetBounds(pad, th + pad, cw, bh - pad * 2);
            int lx = _leftCard.Bounds.X, ly = _leftCard.Bounds.Y;
            _more.SetBounds(lx + (cw - 190) / 2, ly + 16, 190, 34);
            _launch.SetBounds(lx + (cw - ElLaunch.SIZE) / 2, ly + 110, ElLaunch.SIZE, ElLaunch.SIZE); // 面 216 严格居中
            _info.SetBounds(lx + 16, ly + 366, cw - 32, 96); // 信息块加高：排序选择胶囊挪到了文字上方

            int railX = bw - pad - 22;
            _rail.SetBounds(railX, ly + (bh - pad * 2 - 128) / 2, 22, 128);

            RelayoutRight();

            LayoutSort();
            LayoutSet();
            LayoutShare();
            LayoutDiag();

            // 覆盖层开着时跟随窗口尺寸重排（折叠动画 / 界面缩放改尺寸都会走到这里）
            if (_overlayEls != null && _overlayLayout != null)
            {
                if (_dim != null) _dim.SetBounds(0, 0, Lw, Lh);
                _overlayLayout();
            }
        }

        // —— 排序选择页布局（m=22，listTop=92，btnY=h-58）——
        void LayoutSort()
        {
            int th = 46, m = 22;
            int bw = Lw, bh = Lh - th;
            if (bw < 100 || bh < 100) return;
            int cx = 16, cy0 = th + 16, cw = bw - 32, ch = bh - 32; // 单卡片页 full=(16,16,978,572)

            _sortCard.SetBounds(cx, cy0, cw, ch);
            _sortTitleT.SetBounds(cx + m, cy0 + 16, cw - 2 * m, 26);
            _sortDescT.SetBounds(cx + m, cy0 + 44, cw - 2 * m, 20);
            _sortHint.SetBounds(cx + m, cy0 + 66, cw - 2 * m, 18);

            int btnY = cy0 + ch - 22 - 36;
            int listTop = cy0 + 92;
            int listH = Math.Max(80, btnY - 12 - listTop);
            _schemeList.SetBounds(cx + m, listTop, cw - 2 * m, listH);

            _btnApply.SetBounds(cx + cw - m - 156, btnY, 156, 36);
            _btnOpenSortDir.SetBounds(cx + cw - m - 156 - 8 - 140, btnY, 140, 36);
            _btnRescan.SetBounds(cx + cw - m - 156 - 8 - 140 - 8 - 104, btnY, 104, 36);
        }

        // —— 路径设置页布局（m=22，行高 44，fx=+110，btnY=h-58）——
        void LayoutSet()
        {
            int th = 46, m = 22;
            int bw = Lw, bh = Lh - th;
            if (bw < 100 || bh < 100) return;
            int cx = 16, cy0 = th + 16, cw = bw - 32, ch = bh - 32;
            if (_setCard == null) return;
            _setCard.SetBounds(cx, cy0, cw, ch);

            int y = cy0 + 56, rowH = 44;
            int fx = cx + m + 110;
            int fw = Math.Max(120, cw - 2 * m - 110 - 88);
            ElInput[] inputs = { _tRoot, _tTarget, _tSortDir, _nKeep };
            for (int i = 0; i < 4; i++)
            {
                _setK[i].SetBounds(cx + m, y, 104, rowH - 8);
                int ry = y + 8;
                inputs[i].SetBounds(fx, ry, i == 3 ? 90 : fw, 26); // 备份保留数量只有 90 宽（与原一致）
                if (i < 3) _setBrowse[i].SetBounds(cx + cw - m - 76, ry - 1, 76, 28);
                y += rowH;
            }

            int ckTop = y + 6;
            // 界面缩放：标签 + 一排直选档位，跟在「备份保留数量」数字框右边（该行输入框只有 90 宽，右侧是空位）
            if (_lbScale != null)
            {
                int sy = cy0 + 56 + 3 * rowH + 8;
                _lbScale.SetBounds(fx + 90 + 20, sy, 76, 26);
                int sx = fx + 90 + 20 + 82;
                for (int i = 0; i < _scaleOpts.Length; i++) { _scaleOpts[i].SetBounds(sx, sy, 54, 26); sx += 54 + 6; }
            }
            int colW = Math.Max(140, (cw - 2 * m - 20) / 2);
            for (int i = 0; i < 7; i++)
            {
                int col = i % 2, row = i / 2;
                _chk[i].SetBounds(cx + m + col * (colW + 20), ckTop + row * 30, colW, 26);
            }

            // —— 运行日志：整行一个展开按钮（点击后动画覆盖整页，见 SetLogOpen）——
            int logBtnY = ckTop + 4 * 30 + 14;
            _btnLogToggle.SetBounds(cx + m, logBtnY, cw - 2 * m, 40);

            int btnY = cy0 + ch - 22 - 36;
            _lbSetStatus.SetBounds(cx + m + 110, btnY - 30, cw - 2 * m - 110, 24);

            // 日志覆盖层展开时跟随卡片尺寸（窗口折叠/展开后重排也能对上）
            if (_logOpen) LayoutLogOverlay(LogOverlayRect(), true);

            // 右锚一串（从右往左各留 8 间距，与原实现同一坐标）
            int bx = cx + cw - m;
            bx -= 112; _btnSave.SetBounds(bx, btnY, 112, 36);
            bx -= 8 + 140; _btnOpenBackup.SetBounds(bx, btnY, 140, 36);
            bx -= 8 + 132; _btnRestore.SetBounds(bx, btnY, 132, 36);
            bx -= 8 + 110; _btnRedetect.SetBounds(bx, btnY, 110, 36);
            bx -= 8 + 150; _btnBind.SetBounds(bx, btnY, 150, 36);
            bx -= 8 + 150; _btnReapply.SetBounds(bx, btnY, 150, 36);
        }

        // —— 诊断页布局 ——
        void LayoutDiag()
        {
            int th = 46, m = 22;
            int bw = Lw, bh = Lh - th;
            if (bw < 100 || bh < 100) return;
            int cx = 16, cy0 = th + 16, cw = bw - 32, ch = bh - 32;
            if (_diagCard == null) return;
            _diagCard.SetBounds(cx, cy0, cw, ch);

            _lbDiagTitle.SetBounds(cx + m, cy0 + 16, cw - 2 * m, 26);
            _lbDiagDesc.SetBounds(cx + m, cy0 + 44, cw - 2 * m, 40);

            int by = cy0 + 130;
            _btnSusu.SetBounds(cx + m, by, 220, 44);
            _chkSusuLegacy.SetBounds(cx + m + 232, by + 10, 170, 24);
            _btnDiagReport.SetBounds(cx + m, by + 60, 180, 36);
            _btnDiagOpenLog.SetBounds(cx + m + 180 + 8, by + 60, 180, 36);
        }

        // =================================================================
        //  运行日志全屏覆盖层（设置页内）：从按钮位置长大/缩回，ease-out 约 180ms
        // =================================================================
        Rectangle LogOverlayRect()
        {
            var c = _setCard.Bounds;
            return new Rectangle(c.X + 6, c.Y + 6, c.Width - 12, c.Height - 12);
        }

        void LayoutLogOverlay(Rectangle r, bool withContent)
        {
            _logWrap.SetBounds(r.X, r.Y, r.Width, r.Height);
            if (!withContent) return;
            _lbLogTitle.SetBounds(r.X + 18, r.Y + 14, 200, 26);
            int bx = r.Right - 16;
            bx -= 88; _btnLogBack.SetBounds(bx, r.Y + 12, 88, 30);
            bx -= 8 + 112; _logBtn[2].SetBounds(bx, r.Y + 12, 112, 30);
            bx -= 8 + 112; _logBtn[1].SetBounds(bx, r.Y + 12, 112, 30);
            bx -= 8 + 76; _logBtn[0].SetBounds(bx, r.Y + 12, 76, 30);
            _logFull.SetBounds(r.X + 18, r.Y + 52, r.Width - 36, r.Height - 68);
        }

        void SetLogContentVisible(bool v)
        {
            _lbLogTitle.Visible = v;
            _btnLogBack.Visible = v;
            for (int i = 0; i < _logBtn.Length; i++) _logBtn[i].Visible = v;
            _logFull.Visible = v;
        }

        /// <summary>设置页其它内容显隐（覆盖层展开时隐藏路径设置/复选框/按钮等）</summary>
        void SetSettingsContentVisible(bool v)
        {
            foreach (var el in _setEls)
            {
                if (el == _setCard || el == _logWrap) continue;
                bool overlay = false;
                for (int i = 0; i < _logOverlayEls.Length; i++) if (el == _logOverlayEls[i]) { overlay = true; break; }
                if (overlay) continue;
                el.Visible = v;
            }
        }

        void SetLogOpen(bool open)
        {
            if (_logAnim != null) { _logAnim.Stop(); _logAnim.Dispose(); _logAnim = null; }
            _logOpen = open;
            if (open) RefreshLogText();

            Rectangle full = LogOverlayRect();
            Rectangle small = _btnLogToggle.Bounds;
            Rectangle from = open ? small : full;
            Rectangle to = open ? full : small;

            if (open)
            {
                SetSettingsContentVisible(false); // 展开：先藏掉路径设置等内容，只留卡片底
                _logWrap.Visible = true;          // 内容元素等动画到位后再亮
            }
            else
            {
                SetLogContentVisible(false);      // 收起：先藏覆盖层内容，缩回按钮位置后恢复设置页
            }

            int steps = 12, i = 0;
            _logAnim = new System.Windows.Forms.Timer { Interval = 15 };
            _logAnim.Tick += delegate
            {
                i++;
                float t = Math.Min(1f, i / (float)steps);
                float e = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic
                int x = from.X + (int)((to.X - from.X) * e);
                int y = from.Y + (int)((to.Y - from.Y) * e);
                int w = from.Width + (int)((to.Width - from.Width) * e);
                int h = from.Height + (int)((to.Height - from.Height) * e);
                _logWrap.SetBounds(x, y, w, h);
                Render();
                if (i >= steps)
                {
                    _logAnim.Stop(); _logAnim.Dispose(); _logAnim = null;
                    if (_logOpen)
                    {
                        LayoutLogOverlay(full, true);
                        SetLogContentVisible(true);
                    }
                    else
                    {
                        _logWrap.Visible = false;
                        SetSettingsContentVisible(true);
                        LayoutSet();
                    }
                    Render();
                }
            };
            _logAnim.Start();
        }

        /// <summary>切页时若日志覆盖层开着，立即收掉（不播动画）</summary>
        void CloseLogInstant()
        {
            if (_logAnim != null) { _logAnim.Stop(); _logAnim.Dispose(); _logAnim = null; }
            _logOpen = false;
            SetLogContentVisible(false);
            _logWrap.Visible = false;
            SetSettingsContentVisible(true);
        }

        // —— 分享页布局（top=74，btnY=h-58，右锚链式）——
        void LayoutShare()
        {
            int th = 46, m = 22;
            int bw = Lw, bh = Lh - th;
            if (bw < 100 || bh < 100) return;
            int cx = 16, cy0 = th + 16, cw = bw - 32, ch = bh - 32;
            _shareCard.SetBounds(cx, cy0, cw, ch);

            _lbShareTitle.SetBounds(cx + m, cy0 + 16, cw - 2 * m, 26);
            _lbShareDesc.SetBounds(cx + m, cy0 + 44, cw - 2 * m, 20);

            int btnY = cy0 + ch - 22 - 36;
            int top = cy0 + 74;
            int th2 = Math.Max(100, btnY - 12 - top);
            _specWrap.SetBounds(cx + m, top, cw - 2 * m, th2);
            _spec.SetBounds(_specWrap.Bounds.X + 12, _specWrap.Bounds.Y + 10, _specWrap.Bounds.Width - 24, _specWrap.Bounds.Height - 20);

            // 右锚链式前进，每步先减宽度再减 8（与原实现同一坐标）
            int x = cx + cw - m;
            x -= 140; _btnCopySpec.SetBounds(x, btnY, 140, 36);
            x -= 8 + 160; _btnOpenOut.SetBounds(x, btnY, 160, 36);
            x -= 8 + 150; _btnImport.SetBounds(x, btnY, 150, 36);
            x -= 8 + 150; _btnExport.SetBounds(x, btnY, 150, 36);
            x -= 8 + 170; _btnWorkshop.SetBounds(x, btnY, 170, 36);
        }

        public void RelayoutRight()
        {
            int pad = 16, gap = 14, cw = 340, th = 46;
            int railX = Lw - pad - 22;
            int rx = pad + cw + gap, ry = th + pad;
            int rw = Math.Max(0, railX - gap - rx), rh = Lh - th - pad * 2;
            if (rw < 150 || rh < 200) { Render(); return; }
            int m = 6;

            // 快捷按钮：按文字宽度自适应，总宽超出时按比例收缩
            int avail = rw - m * 2;
            int nq = _quick.Length;
            var bws = new int[nq];
            int total = 0;
            for (int i = 0; i < nq; i++) { bws[i] = TextRenderer.MeasureText(_quick[i].Text, T.B(12)).Width + 22; total += bws[i]; }
            total += (nq - 1) * 8;
            float k = total > avail ? (avail - (nq - 1) * 8) / (float)(total - (nq - 1) * 8) : 1f;
            int x = rx + m;
            for (int i = 0; i < nq; i++)
            {
                int bw2 = (int)(bws[i] * k);
                _quick[i].SetBounds(x, ry + 6, bw2, 32);
                x += bw2 + 8;
            }

            _rightTitle.SetBounds(rx + m, ry + 52, rw - m * 2, 24);

            int cy = ry + 84;
            if (_bannerH > 0)
            {
                _banner.SetBounds(rx + m, cy, rw - m * 2, _bannerH);
                _bannerText.SetBounds(rx + m + 14, cy + 4, rw - m * 2 - 28, _bannerH - 8);
                cy += _bannerH + 10;
            }
            for (int i = 0; i < 5; i++)
            {
                _coreK[i].SetBounds(rx + m, cy + i * 25, 84, 25);
                _coreV[i].SetBounds(rx + m + 96, cy + i * 25, Math.Max(50, rw - m * 2 - 96), 25);
            }

            _foldInfo.SetBounds(rx + m, cy + 125 + 8, rw - m * 2, _foldInfo.TotalHeight);
            LayoutFoldRows(_foldInfo, 25);
            _foldLog.SetBounds(rx + m, _foldInfo.Bounds.Bottom + 10, rw - m * 2, _foldLog.TotalHeight);
            LayoutFoldLog(_foldLog);

            int btnY = ry + rh - 2 - 32;
            int bw3 = (rw - m * 2 - 4 * 8) / 5;
            for (int i = 0; i < 5; i++)
                _bottom[i].SetBounds(rx + m + i * (bw3 + 8), btnY, bw3, 32);

            Render();
        }

        static void LayoutFoldRows(ElFold fold, int rowH)
        {
            // 键值对行：键 84px 灰字，值其余宽度
            for (int i = 0; i + 1 < fold.Children.Count; i += 2)
            {
                int row = i / 2;
                fold.Children[i].SetBounds(fold.Bounds.X + 12, fold.Bounds.Y + ElFold.HEAD + row * rowH, 84, rowH);
                fold.Children[i + 1].SetBounds(fold.Bounds.X + 12 + 96, fold.Bounds.Y + ElFold.HEAD + row * rowH,
                    Math.Max(50, fold.Bounds.Width - 24 - 96), rowH);
            }
        }

        static void LayoutFoldLog(ElFold fold)
        {
            // 子元素：0=日志底卡，1=日志文本区
            var wrap = fold.Children[0];
            wrap.SetBounds(fold.Bounds.X + 12, fold.Bounds.Y + ElFold.HEAD, fold.Bounds.Width - 24,
                Math.Max(0, fold.Bounds.Height - ElFold.HEAD - 8));
            fold.Children[1].SetBounds(wrap.Bounds.X + 8, wrap.Bounds.Y + 6, wrap.Bounds.Width - 16, wrap.Bounds.Height - 12);
        }

        // —— 右栏折叠/展开：折叠时窗口整体缩小，只显示左卡片 + 收起柄（408 = 16+340+14+22+16）——
        // 宽度变化带 ease-out 过渡动画（约 150ms）；「打开更多功能」仅折叠态可见。
        System.Windows.Forms.Timer _foldAnim;

        void SetRightCollapsed(bool collapsed) { SetRightCollapsed(collapsed, true); }

        /// <summary>展开右栏后按 _bannerH 纠正横幅显隐（右栏整体亮出时，无警告的横幅不能跟着亮）</summary>
        void SyncBannerVisible()
        {
            if (_banner == null) return;
            _banner.Visible = _bannerText.Visible = _bannerH > 0;
        }

        void SetRightCollapsed(bool collapsed, bool animate)
        {
            if (_foldAnim != null) { _foldAnim.Stop(); _foldAnim.Dispose(); _foldAnim = null; }
            _collapsed = collapsed;
            _rail.Collapsed = collapsed;
            _more.Visible = collapsed; // 折叠后顶栏页签也放不下，更多菜单是唯一入口
            int target = collapsed ? 408 : 1010;
            if (collapsed) foreach (var el in _rightEls) el.Visible = false; // 收起：先藏，避免小窗里挤压

            if (!animate || !IsHandleCreated || Lw == target)
            {
                if (!collapsed) { foreach (var el in _rightEls) el.Visible = true; SyncBannerVisible(); }
                SetLogicalSize(target, 650); // OnSizeChanged 会触发 Render
                LayoutHome();
                Render();
                return;
            }

            int from = Lw, steps = 10, i = 0;
            _foldAnim = new System.Windows.Forms.Timer { Interval = 15 };
            _foldAnim.Tick += delegate
            {
                i++;
                float t = Math.Min(1f, i / (float)steps);
                float e = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic
                SetLogicalSize(from + (int)((target - from) * e), 650);
                LayoutHome(); // 每帧重排：收起柄跟随右边缘滑动
                if (i >= steps)
                {
                    _foldAnim.Stop(); _foldAnim.Dispose(); _foldAnim = null;
                    if (!_collapsed) { foreach (var el in _rightEls) el.Visible = true; SyncBannerVisible(); } // 展开到位后再亮出右栏
                    LayoutHome();
                    Render();
                }
            };
            _foldAnim.Start();
        }

        // —— 一键切换深色模式（顶栏太阳/月亮按钮）：全部自绘，翻转色板重绘即可 ——
        void ToggleTheme()
        {
            T.Dark = !T.Dark;
            _st.UiDark = T.Dark;
            try { _st.Save(); } catch { }
            Render();
        }

        // =================================================================
        //  界面缩放（设置页一排直选档位）：渲染层矢量放大，布局坐标不变；
        //  立即重排重绘 + 持久化，放大后窗口若出屏则拉回可见区域
        // =================================================================
        static readonly int[] ScaleSteps = { 100, 115, 130, 150, 175, 200 };

        /// <summary>当前档位高亮（Primary 样式），其余恢复普通</summary>
        void RefreshScaleOpts()
        {
            for (int i = 0; i < _scaleOpts.Length; i++)
                if (_scaleOpts[i] != null) _scaleOpts[i].Primary = (ScaleSteps[i] == _st.UiScalePct);
        }

        void ApplyUiScale(int pct, bool save)
        {
            pct = Math.Max(100, Math.Min(200, pct));
            _st.UiScalePct = pct;
            UiScale = pct / 100f;
            SetLogicalSize(_collapsed ? 408 : 1010, 650);
            LayoutHome();
            if (IsHandleCreated) // 放大后防止窗口跑出屏幕可见区
            {
                var scr = Screen.FromHandle(Handle).WorkingArea;
                if (Left + Width > scr.Right) Left = Math.Max(scr.Left, scr.Right - Width);
                if (Top + Height > scr.Bottom) Top = Math.Max(scr.Top, scr.Bottom - Height);
                if (Left < scr.Left) Left = scr.Left;
                if (Top < scr.Top) Top = scr.Top;
            }
            RefreshScaleOpts();
            Render();
            if (save)
            {
                try { _st.Save(); } catch { }
                Toast(L.T("界面缩放已切换为 ") + pct + "%");
            }
        }

        // =================================================================
        //  鼠标分发（命中 → 元素；拖动期间捕获；busy 时只放行顶栏）
        // =================================================================
        El Pick(Point p)
        {
            if (_top.HitTest(p)) return _top;
            for (int i = _toasts.Count - 1; i >= 0; i--) // 提示条始终可点（点按收回）
                if (_toasts[i].Bounds.Contains(p)) return _toasts[i];
            if (_overlayEls != null) // 模态覆盖层：只响应覆盖层元素，主 UI 一律不交互
            {
                if (_ovlAnim != null && _ovlAnim.Enabled) return _dim; // 动画期间吞掉点击
                for (int i = _overlayEls.Count - 1; i >= 0; i--)
                {
                    var el = _overlayEls[i];
                    if (!el.Visible || !el.Bounds.Contains(p)) continue;
                    var wsl = el as ElWsList;
                    if (wsl != null)
                    {
                        var row = wsl.PickChild(p);
                        if (row != null) return row;
                    }
                    if (el.HitTest(p)) return el;
                }
                return _dim; // 点在遮罩空白处 → 遮罩（点击即关闭）
            }
            if (_busy) return null; // 部署中：遮罩之下不响应（对应原 LoadingOverlay 盖住内容区）
            var list = CurrentList;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var el = list[i];
                if (!el.Visible || !el.Bounds.Contains(p)) continue;
                var fold = el as ElFold;
                if (fold != null)
                {
                    var child = fold.PickChild(p);
                    if (child != null) return child;
                }
                var slist = el as ElSchemeList;
                if (slist != null)
                {
                    var child = slist.PickChild(p);
                    if (child != null) return child;
                }
                if (el.HitTest(p)) return el;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point lp = ToLogical(e.Location); // 物理像素 → 逻辑坐标
            MousePos = lp;
            if (_captureEl != null)
            {
                if (Control.MouseButtons == MouseButtons.None)
                {
                    // 松手发生在窗外（MouseUp 丢失）——自愈，防止捕获卡死导致全界面「无法互动」
                    MTrace("capture self-heal: " + ElName(_captureEl));
                    var cap = _captureEl; _captureEl = null;
                    cap.OnMouseUp(lp);
                }
                else { _captureEl.OnMouseMove(lp); return; }
            }
            var el = Pick(lp);
            if (el != _hoverEl)
            {
                MTrace("hover " + ElName(_hoverEl) + " -> " + ElName(el) + " @ " + lp.X + "," + lp.Y);
                if (_hoverEl != null) _hoverEl.OnMouseLeave();
                _hoverEl = el;
            }
            if (el != null) el.OnMouseMove(lp);
            Cursor = el != null ? el.CursorOf(lp) : Cursors.Default;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point lp = ToLogical(e.Location);
            var el = Pick(lp);
            MTrace("down @ " + lp.X + "," + lp.Y + " -> " + ElName(el));
            SetFocusInput(el as ElInput); // 点到输入框→获得焦点；点到别处→交出焦点
            _captureEl = el;
            if (el != null) el.OnMouseDown(lp);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Point lp = ToLogical(e.Location);
            MTrace("up @ " + lp.X + "," + lp.Y + " capture=" + ElName(_captureEl));
            var el = _captureEl;
            _captureEl = null;
            if (el != null) el.OnMouseUp(lp);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverEl != null) { _hoverEl.OnMouseLeave(); _hoverEl = null; }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            var el = Pick(ToLogical(e.Location));
            if (el != null && el.OnMouseWheel(e.Delta)) Render();
        }

        // —— 键盘转发给焦点输入框（自绘输入框没有系统焦点，需手动转发）——
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (_focusInput != null) { _focusInput.OnChar(e.KeyChar); e.Handled = true; }
        }
        protected override bool ProcessDialogKey(Keys keyData)
        {
            // 日志区：Ctrl+C 复制选区、Ctrl+A 全选（输入框聚焦时 Ctrl+A 不抢，留给输入框）
            if (keyData == (Keys.Control | Keys.C) && ElLogView.CopyActiveSelection()) return true;
            if (keyData == (Keys.Control | Keys.A) && _focusInput == null && ElLogView.SelectAllActive()) return true;
            if (_overlayEls != null) // 模态覆盖层键盘：Esc 关闭 / Tab 在输入框间循环 / Enter 触发主操作
            {
                Keys k0 = keyData & Keys.KeyCode;
                if (k0 == Keys.Escape)
                {
                    if (_focusInput != null) SetFocusInput(null);
                    else if (_overlayEls == _pfEls && _pfSubMode != null) PfShowSub(null); // 先收子面板
                    else if (_overlayEls == _pfEls && _pfDetailOn) PfShowDetail(false);   // 再回列表
                    else if (_overlayEls == _mgEls && _mgMatches.Visible) MgHideMatches(); // 搜索下拉先收
                    else if (_overlayEls == _suEls && _suSubOn) SusuHideSub(); // 苏苏子面板先收
                    else CloseOverlaySmart();
                    return true;
                }
                if (k0 == Keys.Tab && _ovlInputs != null && _ovlInputs.Length > 0)
                {
                    int i = Array.IndexOf(_ovlInputs, _focusInput);
                    bool back = (keyData & Keys.Shift) != 0;
                    if (i < 0) i = back ? _ovlInputs.Length : -1; // 无焦点时正向进第一个、反向进最后一个
                    SetFocusInput(_ovlInputs[(i + (back ? _ovlInputs.Length - 1 : 1)) % _ovlInputs.Length]);
                    return true;
                }
                if (k0 == Keys.Enter)
                {
                    if (_overlayEls == _wsEls && _focusInput == _wsInput) { WsSearch(); return true; }
                    if (_overlayEls == _aiEls && _focusInput != null) { AiSave(); return true; }
                    if (_overlayEls == _pfEls && _focusInput == _pfSource) { PfSaveSource(); return true; }
                    if (_overlayEls == _pfEls && _pfSubMode == "submit" && _focusInput != null) { PfDoSubmit(); return true; }
                    if (_overlayEls == _mgEls && _focusInput == _mgSearch) { MgSearchMods(); return true; }
                }
                if (_focusInput != null && _focusInput.OnKey(k0)) return true;
                return base.ProcessDialogKey(keyData);
            }
            if (_focusInput != null)
            {
                Keys k = keyData & Keys.KeyCode;
                if (k == Keys.Tab) // Tab / Shift+Tab 在四个输入框间循环
                {
                    var order = new[] { _tRoot, _tTarget, _tSortDir, _nKeep };
                    int i = Array.IndexOf(order, _focusInput);
                    bool back = (keyData & Keys.Shift) != 0;
                    if (i < 0) i = back ? order.Length : -1; // 无焦点时正向进第一个、反向进最后一个
                    SetFocusInput(order[(i + (back ? order.Length - 1 : 1)) % order.Length]);
                    return true;
                }
                if (k == Keys.Escape) { SetFocusInput(null); return true; }
                if (_focusInput.OnKey(k)) return true;
            }
            return base.ProcessDialogKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_focusInput != null && e.KeyCode == Keys.Delete) { _focusInput.OnKey(Keys.Delete); e.Handled = true; }
        }

        // =================================================================
        //  整窗绘制（分层窗口：透明底 + squircle 底板 + 元素树 + 提示条 + 忙碌遮罩）
        // =================================================================
        protected override void PaintContent(Graphics g)
        {
            T.FillRound(g, new RectangleF(0, 0, Lw, Lh), 12, T.Window);

            var list = CurrentList;
            foreach (var el in list) if (el.Visible) el.Paint(g);
            _top.Paint(g);

            // 忙碌遮罩（替代原 LoadingOverlay——分层窗口里放不了真控件，只能自绘一层）
            if (_busy)
            {
                var r = new RectangleF(0, 46, Lw, Lh - 46);
                using (var b = new SolidBrush(Color.FromArgb(170, T.Window))) g.FillRectangle(b, r);
                string t1 = L.T("正在加载中，请稍候…");
                string t2 = L.T("正在备份并写入配置，请不要关闭窗口");
                var f1 = T.B(15);
                var f2 = T.F(12);
                var s1 = TextRenderer.MeasureText(t1, f1);
                var s2 = TextRenderer.MeasureText(t2, f2);
                float cx = Lw / 2f, cy = 46 + (Lh - 46) / 2f;
                using (var b1 = new SolidBrush(T.Text))
                    g.DrawString(t1, f1, b1, cx - s1.Width / 2f, cy - 26);
                using (var b2 = new SolidBrush(T.Text3))
                    g.DrawString(t2, f2, b2, cx - s2.Width / 2f, cy + 6);
            }

            if (_overlayEls != null) // 模态覆盖层：遮罩 + 面板（面板整体带上滑动效）
            {
                _dim.Paint(g);
                var st2 = g.Save();
                g.TranslateTransform(0, _ovlSlide);
                foreach (var el in _overlayEls) if (el.Visible) el.Paint(g);
                g.Restore(st2);
            }

            for (int i = 0; i < _toasts.Count; i++) _toasts[i].Paint(g); // 提示条压在最顶层（含忙碌遮罩之上）
        }

        /// <summary>特效层开关跟随 FrameFx.Enabled：关闭时渲染走单缓冲直通（无合成、无特效计时器）</summary>
        protected override bool FxEnabled { get { return FrameFx.Enabled; } }

        /// <summary>特效层：内侧装饰框 + 扫描线/暗角。独立在内容位图之上，呼吸刷新走 RenderFxOnly 廉价通道</summary>
        protected override void PaintFx(Graphics g)
        {
            FrameFx.Paint(g, Lw, Lh, MousePos);
        }

        // =================================================================
        //  轻提示条：窗口内右上角滑入 → 停留（顶部进度条消退，≈5s）→ 滑出。
        //  多条排队：新提示叠在上一条下面；前面的先走后，后面的平滑上移补位。
        //  位置约定：主页躲开快捷按钮行（y=106），其余页贴顶栏下（y=52）；
        //  右缘内缩 46，全程不压最右侧的折叠柄。
        // =================================================================
        int ToastBaseY { get { return _page == 0 ? 106 : 52; } }
        int ToastTargetY(ElToast t) { return ToastBaseY + _toasts.IndexOf(t) * (TOAST_H + TOAST_GAP); }
        int ToastHoldX(ElToast t) { return Lw - 12 - t.Bounds.Width; } // 右缘内缩 12（会短暂盖住折叠柄一小段，点提示条即收回）

        public void Toast(string msg) // public：日志区复制成功等场景共用
        {
            int tw = TextRenderer.MeasureText(msg ?? "", T.F(12.5f)).Width;
            int w = Math.Max(180, Math.Min(400, tw + 60));
            w = Math.Min(w, Lw - 80); // 折叠态窗口只有 408 宽，别溢出
            var t = new ElToast { Host = this, Msg = msg ?? "", Progress = 1f };
            t.Clicked += delegate { if (t.Tick < TOAST_IN + TOAST_HOLD) t.Tick = TOAST_IN + TOAST_HOLD; }; // 点按立即收回
            _toasts.Add(t);
            t.SetBounds(Lw, ToastTargetY(t), w, TOAST_H); // 起点：完全藏在窗口右缘外
            if (_toastAnim == null)
            {
                _toastAnim = new System.Windows.Forms.Timer { Interval = 15 };
                _toastAnim.Tick += delegate { ToastTick(); };
            }
            if (!_toastAnim.Enabled) _toastAnim.Start();
            Render();
        }

        void ToastTick()
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                t.Tick++;
                int w = t.Bounds.Width;
                int holdX = ToastHoldX(t);
                int x;
                if (t.Tick <= TOAST_IN)
                {
                    float k = t.Tick / (float)TOAST_IN;
                    float e = 1f - (1f - k) * (1f - k) * (1f - k); // ease-out
                    x = holdX + (int)((Lw - holdX) * (1f - e));
                }
                else if (t.Tick <= TOAST_IN + TOAST_HOLD)
                {
                    t.Progress = 1f - (t.Tick - TOAST_IN) / (float)TOAST_HOLD;
                    x = holdX;
                }
                else if (t.Tick <= TOAST_IN + TOAST_HOLD + TOAST_OUT)
                {
                    float k = (t.Tick - TOAST_IN - TOAST_HOLD) / (float)TOAST_OUT;
                    x = holdX + (int)((Lw - holdX) * k * k * k); // ease-in
                }
                else
                {
                    _toasts.RemoveAt(i); // 生命周期结束，移出列表（后面的提示下一帧起补位上移）
                    continue;
                }
                // 槽位目标 y（前面的提示先走会空出位置）→ 每次逼近 1/4，平滑上移
                int dy = ToastTargetY(t) - t.Bounds.Y;
                int y = Math.Abs(dy) <= 2 ? ToastTargetY(t) : t.Bounds.Y + dy / 4;
                t.SetBounds(x, y, w, TOAST_H);
            }
            if (_toasts.Count == 0) _toastAnim.Stop();
            Render();
        }

        // —— 更多功能菜单（菜单项与原版 ShowMoreMenu 一致；分层窗口里不能用真 ContextMenuStrip）——
        void ShowMoreMenu(object sender, EventArgs e)
        {
            var menu = new MenuPopup();
            menu.AddItem(L.T("排序选择"), delegate { SwitchPage(1); });
            menu.AddItem(L.T("设置"), delegate { SwitchPage(2); });
            menu.AddItem(L.T("分享 / 导出"), delegate { SwitchPage(3); });
            menu.AddItem(L.T("诊断（苏苏 AI）"), delegate { SwitchPage(4); });
            // 「外观（赞助版）」已删：深浅色切换在顶栏月亮/太阳按钮，旧主题色/视频背景不再提供
            menu.AddSep();
            menu.AddItem(L.T("切换整合包"), delegate { SwitchPackDialog(); });
            menu.AddItem(L.T("整合包资源获取"), delegate { OpenPackFeed(); });
            // 「赞助创作者」已并入顶栏咖啡杯菜单（原作者 / UI 作者双入口），这里不再重复
            menu.LogicalMinWidth = _more.Bounds.Width; // 菜单宽度与「打开更多功能」按钮持平
            menu.ShowAt(PointToScreen(new Point((int)(_more.Bounds.X * UiScale), (int)(_more.Bounds.Bottom * UiScale) + 6))); // 逻辑坐标 → 物理坐标
        }

        // =================================================================
        //  启动时初始化
        // =================================================================
        private void OnLoaded()
        {
            Log.Info(L.T("界面已加载。整合包根目录 = ") + _ctx.Root);
            if (_ctx.Problems.Count > 0)
            {
                for (int i = 0; i < _ctx.Problems.Count; i++) Log.Error(L.T("环境检查: ") + _ctx.Problems[i]);
            }
            for (int i = 0; i < _ctx.Notes.Count; i++) Log.Warn(L.T("环境检查: ") + _ctx.Notes[i]);

            ReloadSchemes(false);
            LoadSettingsToUi();
            UpdateAllUi();
            RefreshLogText();
            _log.SetText(Log.TailText(60)); // 主页迷你日志：补上订阅事件之前的历史行
            if (!_st.GuideSeen)
            {
                _st.GuideSeen = true;
                _st.Save();
                CallSusu(true);
            }
            SwitchPage(0);

            ModIndex.StartAsync(_ctx.GameDir);
            System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
            tm.Interval = 700;
            tm.Tick += delegate(object s, EventArgs e)
            {
                if (ModIndex.Ready)
                {
                    tm.Stop();
                    tm.Dispose();
                    EvaluateSchemes();
                    UpdateAllUi();
                    Log.Info(L.T("本地模组索引已刷新，界面已更新"));
                }
            };
            tm.Start();
        }

        private void LoadSettingsToUi()
        {
            _tRoot.Text = _ctx.Root;
            _tTarget.Text = _ctx.TargetConfigDir;
            _tSortDir.Text = _ctx.SortDir;
            _nKeep.Text = Math.Max(1, Math.Min(200, _st.KeepBackups)).ToString();
            _chk[0].Checked = _st.AutoBackup;
            _chk[1].Checked = _st.ClearConfig;
            _chk[2].Checked = _st.SyncGameConfig;
            _chk[3].Checked = _st.StartSteam;
            _chk[4].Checked = _st.WarnNonAscii;
            _chk[5].Checked = _st.MinimizeAfterLaunch;
            _chk[6].Checked = _st.ReplaceEveryTime;
            _lbSetStatus.Text = L.T("设置文件：") + _st.IniPath;
        }

        private void UpdateAllUi()
        {
            string packName = _ctx.Root;
            try { packName = new DirectoryInfo(_ctx.Root).Name; } catch { }
            _info.PackName = packName;
            _info.Meta = "RimWorld " + _ctx.GameVersion + L.T(" · ") + _schemes.Count + L.T(" 个排序方案");
            _info.Scheme = _current == null ? L.T("未选择排序方案") : _current.Name;

            // 顶部提示条：先看有没有认出整合包，其次看中文路径
            bool looksPack = Directory.Exists(_ctx.PackConfigDir)
                && (File.Exists(Path.Combine(_ctx.Root, "RimWorld\\RimWorldWin64.exe")) || File.Exists(Path.Combine(_ctx.Root, "RimWorld\\RimWorld.exe")));
            if (!_ctx.Ok)
            {
                _banner.Visible = !_collapsed; // 折叠态下右栏整体隐藏，横幅不能漏出来
                _bannerText.Visible = !_collapsed;
                _bannerH = 78; // 「没认出整合包」文案最长（标题+问题+指引约 4 行）
                if (looksPack)
                {
                    _bannerText.Text = L.T("环境检查没通过：\n")
                        + (_ctx.Problems.Count > 0 ? _ctx.Problems[0] : L.T("有项目没通过")) + "\n"
                        + L.T("详细情况见【设置】页底部的运行日志，或点【绑定整合包文件夹…】重新指定。");
                }
                else
                {
                    _bannerText.Text = L.T("没有认出整合包：\n")
                        + (_ctx.Problems.Count > 0 ? _ctx.Problems[0] : L.T("找不到整合包目录")) + "\n"
                        + L.T("请到【设置】页点【绑定整合包文件夹…】，选里面有 RimWorld、Config、排序文件 的那一层。");
                }
            }
            else if (_ctx.NonAscii)
            {
                _banner.Visible = !_collapsed;
                _bannerText.Visible = !_collapsed;
                _bannerH = 58; // 中文路径提示约 2–3 行
                _bannerText.Text = L.T("注意：游戏路径包含中文（") + _ctx.NonAsciiSample + "）。\n"
                    + L.T("RimWorld 在中文路径下可能无法加载模组。建议把整合包移动到纯英文路径，例如 D:\\RimWorldPack。");
            }
            else { _banner.Visible = false; _bannerText.Visible = false; _bannerH = 0; }

            // 信息行
            _coreV[0].Text = _ctx.Root;
            _coreV[1].Text = _ctx.GameExe + (File.Exists(_ctx.GameExe) ? "" : L.T("（缺失）"));
            _coreV[2].Text = _ctx.GameVersion;
            _coreV[3].Text = _current == null ? L.T("未选择") : _current.Name + "（" + _current.Format + "）";
            _coreV[4].Text = _current == null ? "-" : (_current.Mods.Count + L.T(" 个，本地缺失 ") + _current.Missing + L.T(" 个"));
            _extraV[0].Text = ModIndex.Ready ? (ModIndex.Count + L.T(" 个 packageId")) : L.T("正在统计…");
            _extraV[1].Text = _ctx.SaveDataDir + (_ctx.PortableData ? L.T("（便携）") : "");
            _extraV[2].Text = _ctx.SortDir;
            _extraV[3].Text = _ctx.BackupDir;
            if (_ctx.HasMarker)
                _extraV[4].Text = _ctx.PackId.Length > 0 ? (L.T("已写入 · ") + _ctx.PackId) : L.T("已写入");
            else
                _extraV[4].Text = L.T("未写入（请点【绑定整合包文件夹…】）");

            // 启动键副标题（busy / 运行状态文案优先，别覆盖）
            if (!_busy
                && _launch.SubText != L.T("游戏运行中")
                && _launch.SubText != L.T("上次启动崩溃了")
                && _launch.SubText != L.T("游戏已关闭"))
            {
                if (!_ctx.Ok) _launch.SubText = L.T("请先绑定整合包文件夹");
                else _launch.SubText = _current == null ? L.T("请先选择排序方案") : (_current.Mods.Count + L.T(" 个模组 · ") + _current.Name);
            }

            if (_current != null)
                _sortHint.Text = L.T("当前选中：") + _current.Name + "（" + _current.Describe() + "）";
            else _sortHint.Text = L.T("还没有选择排序方案");

            for (int i = 0; i < _schemeList.Cards.Count; i++)
                _schemeList.Cards[i].Selected = (_schemeList.Cards[i].Scheme == _current);

            UpdateCheckStates();
            RelayoutRight(); // 横幅高度变化会联动信息行/折叠分组的位置（内部自带 Render）
        }

        private void UpdateCheckStates()
        {
            // 没有备份目录就先禁用还原按钮
            _btnRestore.Enabled = Directory.Exists(_ctx.BackupDir) && ConfigDeployer.ListBackups(_ctx.BackupDir).Count > 0;
        }

        private void SetStatus(string text)
        {
            Log.Info(text);
            // 极简主页没有「运行状态」行：部署中的进度文案合并到启动键副标题
            if (_busy && !string.IsNullOrEmpty(text)) { _launch.SubText = text; Render(); }
        }

        // =================================================================
        //  排序方案
        // =================================================================
        private void ReloadSchemes(bool announce)
        {
            try
            {
                _schemes = SortSchemeLoader.Load(_ctx);
                if (announce) Log.Info(L.T("重新扫描排序文件完成，共 ") + _schemes.Count + L.T(" 个方案"));
            }
            catch (Exception ex) { Log.Error(L.T("扫描排序文件失败"), ex); _schemes = new List<SortScheme>(); }

            EvaluateSchemes();
            BuildCards();
            PickDefaultScheme();
            UpdateAllUi();
        }

        private void EvaluateSchemes()
        {
            if (!ModIndex.Ready) return;
            for (int i = 0; i < _schemes.Count; i++) ModIndex.Evaluate(_schemes[i]);
            // 评估会改变 Missing / ModCountText，同步刷新卡片文案与徽章
            for (int i = 0; i < _schemeList.Cards.Count; i++)
            {
                var c = _schemeList.Cards[i];
                if (c.Scheme != null) FillCard(c, c.Scheme);
            }
            Render();
        }

        private void PickDefaultScheme()
        {
            SortScheme pick = null;
            if (!string.IsNullOrEmpty(_st.LastScheme))
            {
                for (int i = 0; i < _schemes.Count; i++)
                    if (string.Equals(_schemes[i].SourcePath, _st.LastScheme, StringComparison.OrdinalIgnoreCase)) { pick = _schemes[i]; break; }
            }
            if (pick == null)
            {
                for (int i = 0; i < _schemes.Count; i++) if (_schemes[i].IsBuiltIn) { pick = _schemes[i]; break; }
            }
            if (pick == null && _schemes.Count > 0) pick = _schemes[0];
            _current = pick;
        }

        private static void FillCard(ElSchemeCard c, SortScheme s)
        {
            c.Scheme = s;
            c.Title = s.Name;
            c.Desc = s.ModCountText + " · " + s.VersionText + " · " + s.Format;
            c.Missing = s.Missing;
            c.Rjw = s.RjwCount;
        }

        private void BuildCards()
        {
            _schemeList.ClearCards();
            for (int i = 0; i < _schemes.Count; i++)
            {
                var c = new ElSchemeCard();
                FillCard(c, _schemes[i]);
                var card = c;
                card.Picked += delegate { PickScheme(card.Scheme); };
                _schemeList.AddCard(card);
            }
        }

        private void PickScheme(SortScheme s)
        {
            _current = s;
            if (s != null)
            {
                _st.LastScheme = s.SourcePath;
                _st.Save();
                Log.Info(L.T("已选择排序方案：") + s.Name + "（" + s.Mods.Count + L.T(" 个模组）"));
            }
            for (int i = 0; i < _schemeList.Cards.Count; i++)
                _schemeList.Cards[i].Selected = (_schemeList.Cards[i].Scheme == s);
            UpdateAllUi();
        }

        // =================================================================
        //  日志显示
        // =================================================================
        private void OnLogLine(string line)
        {
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    AppendMini(line);
                });
            }
            catch { }
        }

        private void AppendMini(string line)
        {
            try
            {
                if (line == null) return;
                // 错误明细里可能带换行（堆栈），拆开逐行加
                string[] ls = line.Replace("\r", "").Split('\n');
                for (int i = 0; i < ls.Length; i++)
                {
                    _log.Add(ls[i]);
                    if (_page == 2) _logFull.Add(ls[i]); // 日志区在设置页
                }
            }
            catch { }
        }

        private void RefreshLogText()
        {
            try
            {
                _logFull.SetText(Log.TailText(2000));
            }
            catch { }
        }

        // =================================================================
        //  一键启动
        // =================================================================
        private void SetBusy(bool busy)
        {
            _busy = busy;
            _launch.Enabled = !busy;
            _launch.Busy = busy;
            _btnApply.Enabled = !busy;
            if (_btnExport != null) { _btnExport.Enabled = !busy; _btnImport.Enabled = !busy; }
            if (busy) { _launch.SubText = L.T("正在处理配置，请稍候…"); }
            else if (_launch.SubText != L.T("游戏运行中") && _current != null)
                _launch.SubText = _current.Mods.Count + L.T(" 个模组 · ") + _current.Name;
            Render(); // busy=true 时 PaintContent 会画遮罩（对应原 LoadingOverlay.ShowOverlay）
        }

        private void StartGame()
        {
            if (_busy) return;

            if (_current == null)
            {
                MinMsg.Show(this, L.T("还没有选择排序方案。\n请先在【排序】页里选一个方案。"),
                    L.T("感恩的心❤，感谢有你！"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                SwitchPage(1);
                return;
            }

            if (!File.Exists(_ctx.GameExe))
            {
                MinMsg.Show(this, L.T("没有找到游戏主程序：\n") + _ctx.GameExe + L.T("\n\n请到【设置】页确认整合包根目录是否正确。"),
                    L.T("找不到游戏"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                SwitchPage(2);
                return;
            }

            if (_ctx.Problems.Count > 0)
            {
                StringBuilder sb = new StringBuilder(L.T("环境检查没通过：\n\n"));
                for (int i = 0; i < _ctx.Problems.Count; i++) sb.AppendLine("· " + _ctx.Problems[i]);
                sb.AppendLine(L.T("\n仍然要尝试启动吗？（大概率会失败）"));
                sb.AppendLine();
                sb.Append(L.T("点【是】去设置页手动绑定整合包文件夹；点【否】仍然尝试启动。"));
                if (MinMsg.Show(this, sb.ToString(), L.T("环境有问题"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    SwitchPage(2);
                    BindPackFolder();
                    return;
                }
            }

            if (_ctx.NonAscii && _st.WarnNonAscii)
            {
                string msg = L.T("检测到游戏路径里含有中文：\n\n") + _ctx.Root + "\n\n"
                    + L.T("RimWorld 在中文路径下经常无法加载模组（这是游戏本身的限制）。\n")
                    + L.T("建议把整个整合包文件夹改成英文名，例如 RimWorldPack。\n\n")
                    + L.T("仍要继续启动吗？");
                if (MinMsg.Show(this, msg, L.T("路径包含中文"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            }

            // 启动前模组体检：依赖、顺序、兼容性、版本、上次日志归因
            ModAnalysisResult pre = null;
            try
            {
                SetStatus(L.T("正在做启动前模组体检…"));
                Cursor.Current = Cursors.WaitCursor;
                Application.DoEvents();
                pre = ModAnalyzer.Analyze(_ctx, _current);
                ModAnalyzer.AnalyzePlayerLog(_ctx, _current, pre);
            }
            catch (Exception ex)
            {
                Log.Warn(L.T("启动前模组体检失败：") + ex.Message);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }

            if (pre != null) Log.Info(pre.SummaryText());

            if (pre != null && (pre.HasErrors || pre.Suspects.Count > 0))
            {
                string msg = pre.SummaryText();
                msg += L.T("\r\n\r\n是否生成新的排序方案并继续启动？\r\n")
                    + L.T("是：生成智能/安全排序（不会删除模组）\r\n")
                    + L.T("否：按当前排序继续\r\n")
                    + L.T("取消：先不启动");
                DialogResult dr = MinMsg.Show(this, msg, L.T("启动前模组体检"),
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (dr == DialogResult.Cancel) return;
                if (dr == DialogResult.Yes)
                {
                    string path = pre.Suspects.Count > 0
                        ? ModAnalyzer.WriteSafeScheme(_ctx, _current, pre)
                        : ModAnalyzer.WriteSuggestedScheme(_ctx, _current, pre);
                    if (string.IsNullOrEmpty(path))
                    {
                        MinMsg.Show(this, L.T("生成新排序失败，详见日志。"), L.T("启动前体检"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    _st.LastScheme = path;
                    _st.Save();
                    ReloadSchemes(true);
                    if (_current == null)
                    {
                        MinMsg.Show(this, L.T("新排序生成后无法加载，请到排序页手动选择。"), L.T("启动前体检"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    Log.Info(L.T("启动前体检：已切换到新排序 ") + path);
                }
            }

            SetBusy(true);
            SetStatus(L.T("正在备份并写入配置…"));

            _worker = new BackgroundWorker();
            _worker.WorkerReportsProgress = true;
            _worker.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                BackgroundWorker w = s as BackgroundWorker;
                DeployResult r = ConfigDeployer.Deploy(_ctx, _current, _st, delegate(string msg)
                {
                    if (w != null) w.ReportProgress(0, msg);
                });
                e.Result = r;
            };
            _worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs e)
            {
                SetStatus(Convert.ToString(e.UserState));
            };
            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                OnDeployDone(e);
            };
            _worker.RunWorkerAsync();
        }

        private void OnDeployDone(RunWorkerCompletedEventArgs e)
        {
            SetBusy(false);

            if (e.Error != null)
            {
                Log.Error(L.T("替换配置时出错"), e.Error);
                ShowLaunchError(L.T("替换配置时出错：") + e.Error.Message);
                return;
            }

            DeployResult r = e.Result as DeployResult;
            if (r == null)
            {
                ShowLaunchError(L.T("替换配置时出现未知问题（没有返回结果）。"));
                return;
            }

            if (r.DidBackup) Log.Info(L.T("原配置已备份到：") + r.BackupPath);
            for (int i = 0; i < r.Warnings.Count; i++) Log.Warn(r.Warnings[i]);

            if (r.Errors.Count > 0)
            {
                StringBuilder sb = new StringBuilder(L.T("启动前准备失败：\n\n"));
                for (int i = 0; i < r.Errors.Count; i++) sb.AppendLine("· " + r.Errors[i]);
                ShowLaunchError(sb.ToString());
                return;
            }

            SetStatus(L.T("配置已就绪，正在启动游戏…"));
            UpdateCheckStates();
            LaunchGame();
        }

        private void LaunchGame()
        {
            ThreadPool.QueueUserWorkItem(delegate(object o)
            {
                try { if (_st.StartSteam) SteamHelp.EnsureRunning(); }
                catch (Exception ex) { Log.Warn(L.T("Steam 检查失败：") + ex.Message); }
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke((MethodInvoker)delegate { DoStartProcess(); });
                }
                catch { }
            });
        }

        private void DoStartProcess()
        {
            try
            {
                Process p = GameLauncher.Start(_ctx);
                SetStatus(L.T("游戏已启动（PID ") + p.Id + "）");
                _launch.SubText = L.T("游戏运行中");
                Render();
                MonitorProcess(p);
                if (_st.MinimizeAfterLaunch) WindowState = FormWindowState.Minimized;
            }
            catch (InvalidDataException ex)
            {
                Log.Error(L.T("启动前检查没通过"), ex);
                MinMsg.Show(this, ex.Message + "\r\n\r\n"
                    + L.T("这通常说明：\r\n")
                    + L.T("· 整合包没解压完整（主程序损坏 / 0 字节）\r\n")
                    + L.T("· 或者这个包不是 Windows 版（Linux / 安卓版）\r\n")
                    + L.T("· 或者只复制了部分文件\r\n\r\n")
                    + L.T("建议：把整合包重新完整解压一遍，再用【设置】页的【绑定整合包文件夹…】指过去。"),
                    L.T("启动前检查没通过"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log.Error(L.T("启动游戏失败"), ex);
                ShowLaunchError(L.T("启动游戏失败：") + ex.Message);
            }
        }

        private void MonitorProcess(Process p)
        {
            ThreadPool.QueueUserWorkItem(delegate(object o)
            {
                bool exited = false;
                try { exited = p.WaitForExit(180000); }
                catch { return; }
                if (!exited) return;

                int code = 0;
                try { code = p.ExitCode; } catch { }
                Log.Info(L.T("游戏进程已退出，退出代码 = ") + code);
                if (code != 0)
                {
                    try
                    {
                        if (IsHandleCreated && !IsDisposed)
                            BeginInvoke((MethodInvoker)delegate
                            {
                                _launch.SubText = L.T("上次启动崩溃了");
                                Render();
                                string head = L.T("游戏启动后崩了。\r\n\r\n")
                                    + L.T("退出代码：") + code + "（0x" + unchecked((uint)code).ToString("X8") + "）\r\n"
                                    + L.T("含义：") + ExitCodes.Explain(code) + "\r\n\r\n"
                                    + L.T("建议按这个顺序排查：\r\n") + ExitCodes.Advice(code);
                                ShowLaunchError(head);
                            });
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        if (IsHandleCreated && !IsDisposed)
                            BeginInvoke((MethodInvoker)delegate { _launch.SubText = L.T("游戏已关闭"); Render(); });
                    }
                    catch { }
                }
            });
        }

        private string LogLocationsText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("· " + _ctx.PlayerLog);
            sb.AppendLine("· " + _ctx.LocalLowLogsDir);
            sb.AppendLine("· " + _ctx.GameLogsDir);
            sb.AppendLine("· " + _ctx.GameCrashesDir);
            if (Log.FilePath != null) sb.AppendLine("· " + Log.FilePath);
            return sb.ToString();
        }

        private void ShowLaunchError(string msg)
        {
            string report = null;
            try { report = Diag.SaveReport(_ctx, _current, _st); }
            catch (Exception ex) { Log.Warn(L.T("生成诊断报告失败：") + ex.Message); }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(msg);
            sb.AppendLine();
            sb.AppendLine(L.T("请把日志或诊断报告发给社区管理员，我们会尽快帮你定位问题。"));
            sb.AppendLine();
            sb.AppendLine(L.T("日志位置："));
            sb.Append(LogLocationsText());
            if (report != null)
            {
                sb.AppendLine();
                sb.AppendLine(L.T("已自动生成诊断报告："));
                sb.AppendLine(report);
            }
            sb.AppendLine();
            sb.Append(L.T("现在打开日志文件夹吗？"));

            ShowConfirm(L.T("启动出现问题"), sb.ToString(),
                delegate { PathHelpOpen(DirOrFile(_ctx.LocalLowLogsDir, _ctx.PlayerLog)); });
        }

        private void MakeReport(bool showResult)
        {
            try
            {
                string f = Diag.SaveReport(_ctx, _current, _st);
                if (showResult)
                {
                    string path = f; // 闭包用
                    ShowConfirm(L.T("诊断报告"),
                        L.T("诊断报告已生成：\n\n") + path + L.T("\n\n把这个 txt 文件发给社区管理员即可。\n\n现在打开它所在的文件夹吗？"),
                        delegate { PathHelpOpen(path); });
                }
            }
            catch (Exception ex)
            {
                Log.Error(L.T("生成诊断报告失败"), ex);
                Toast(L.T("生成诊断报告失败：") + ex.Message); // 失败走右上角提示条，不再弹独立对话框
            }
        }

        // =================================================================
        //  设置 / 还原
        // =================================================================
        private void SaveSettingsFromUi()
        {
            _st.Root = _tRoot.Text.Trim();
            string targetText = _tTarget.Text.Trim();
            // 便携模式的默认目录不写死绝对路径，否则整合包换个盘/换台电脑就失效
            if (_ctx != null && _ctx.PortableData && PathHelp.SamePath(targetText, _ctx.TargetConfigDir))
                _st.TargetConfigDir = "";
            else
                _st.TargetConfigDir = targetText;
            _st.SortDirOverride = _tSortDir.Text.Trim();
            int keep;
            if (!int.TryParse(_nKeep.Text.Trim(), out keep)) keep = 10;
            _st.KeepBackups = Math.Max(1, Math.Min(200, keep));
            _st.AutoBackup = _chk[0].Checked;
            _st.ClearConfig = _chk[1].Checked;
            _st.ReplaceEveryTime = _chk[6].Checked;
            _st.SyncGameConfig = _chk[2].Checked;
            _st.StartSteam = _chk[3].Checked;
            _st.WarnNonAscii = _chk[4].Checked;
            _st.MinimizeAfterLaunch = _chk[5].Checked;
            _st.UiDark = T.Dark;
            _st.Save();
            Log.Info(L.T("设置已保存：") + _st.IniPath);
            Redetect();
            _lbSetStatus.Text = L.T("已保存 · ") + DateTime.Now.ToString("HH:mm:ss");
        }

        /// <summary>一键切换整合包（含 1.6 / 1.7 版本隔离）。已并轨为内嵌覆盖层，见 PsOpen()。</summary>
        private void SwitchPackDialog()
        {
            PsOpen();
        }

        private void Redetect()
        {
            _ctx = PackContext.Detect(Application.StartupPath, _st, _st.Root, _st.TargetConfigDir, null);
            if (!string.IsNullOrEmpty(_ctx.Root))
            {
                _st.Root = _ctx.Root;
                _st.PackId = _ctx.PackId;
                _st.Save();
            }
            Log.Info(L.T("重新检测完成，整合包根目录 = ") + _ctx.Root);
            ReloadSchemes(true);
            LoadSettingsToUi();
            UpdateAllUi();
            if (_ctx.Problems.Count == 0) SetStatus(L.T("检测通过，可以启动"));
        }

        private void RestoreFromBackup()
        {
            List<string> backups = ConfigDeployer.ListBackups(_ctx.BackupDir);
            if (backups.Count == 0)
            {
                MinMsg.Show(this, L.T("还没有任何备份。\n\n备份目录：") + _ctx.BackupDir,
                    L.T("没有备份"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = L.T("选择要还原的备份文件夹（默认已指向最新的备份）");
                d.SelectedPath = backups[0];
                if (d.ShowDialog(this) != DialogResult.OK) return;

                string sel = d.SelectedPath;
                if (MinMsg.Show(this,
                    L.T("确定用这个备份覆盖当前的游戏配置吗？\n\n") + sel + L.T("\n\n（当前配置会先被自动备份一次，可以再还原回来）"),
                    L.T("确认还原"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

                DeployResult r;
                try { r = ConfigDeployer.Restore(sel, _ctx.TargetConfigDir); }
                catch (Exception ex)
                {
                    Log.Error(L.T("还原失败"), ex);
                    MinMsg.Show(this, L.T("还原失败：") + ex.Message, L.T("出错了"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(L.T("还原完成。"));
                sb.AppendLine(L.T("复制文件数：") + r.Copied);
                if (r.Errors.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(L.T("有 ") + r.Errors.Count + L.T(" 个问题："));
                    for (int i = 0; i < r.Errors.Count && i < 10; i++) sb.AppendLine("· " + r.Errors[i]);
                }
                MinMsg.Show(this, sb.ToString(), L.T("还原完成"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateAllUi();
            }
        }

        private void BrowseInto(ElInput target, string desc)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = desc;
                try
                {
                    string cur = target.Text.Trim();
                    if (cur.Length > 0 && Directory.Exists(cur)) d.SelectedPath = cur;
                }
                catch { }
                if (d.ShowDialog(this) == DialogResult.OK) { target.Text = d.SelectedPath; Render(); }
            }
        }

        private void BindPackFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = L.T("选择整合包文件夹（里面要有 RimWorld、Config、排序文件 这三个文件夹）");
                try { if (Directory.Exists(_ctx.Root)) d.SelectedPath = _ctx.Root; } catch { }
                if (d.ShowDialog(this) != DialogResult.OK) return;

                string sel = d.SelectedPath;
                bool strict; List<string> notes; string pid;
                int sc = PackDetector.Score(sel, out strict, out notes, out pid);
                if (sc <= 0)
                {
                    MinMsg.Show(this, L.T("这个文件夹读不到内容，可能路径不对。"), L.T("绑定失败"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!strict)
                {
                    string miss = "";
                    if (!File.Exists(Path.Combine(sel, "RimWorld\\RimWorldWin64.exe"))) miss += L.T("· 缺少 RimWorld\\RimWorldWin64.exe\r\n");
                    if (!File.Exists(Path.Combine(sel, "Config\\ModsConfig.xml"))) miss += L.T("· 缺少 Config\\ModsConfig.xml\r\n");
                    if (!Directory.Exists(Path.Combine(sel, L.T("排序文件")))) miss += L.T("· 缺少 排序文件 文件夹\r\n");
                    if (MinMsg.Show(this, L.T("这个文件夹好像不是整合包目录：\r\n\r\n") + miss
                        + L.T("\r\n整合包目录应该是【里面有 RimWorld、Config、排序文件 的那一层】。\r\n\r\n仍然要绑定这个文件夹吗？"),
                        L.T("好像不是整合包目录"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                }

                string err;
                if (!PackMarker.Write(sel, pid, out err))
                {
                    MinMsg.Show(this, L.T("识别令牌写入失败：") + err + L.T("\r\n\r\n可能是这个文件夹没有写入权限（例如放在 C:\\Program Files 下面）。"),
                        L.T("写入失败"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _st.Root = sel;
                _st.PackId = PackMarker.ReadPackId(sel);
                _st.Save();
                Log.Info(L.T("已手动绑定整合包：") + sel + L.T("（识别令牌 ") + _st.PackId + "）");

                Redetect();
                MinMsg.Show(this, L.T("绑定成功！\r\n\r\n整合包：") + sel + L.T("\r\n识别令牌：") + _st.PackId
                    + L.T("\r\n\r\n以后这个文件夹改名字或者搬到别处，启动器也能认出它。"),
                    L.T("感恩的心❤，感谢有你！"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // =================================================================
        //  分享 / 导入导出
        // =================================================================
        // =================================================================
        //  模态覆盖层基础设施（创意工坊 / 模组管理共用）：
        //  固定覆盖在主窗口上层——不是独立窗口，主 UI 在弹出期间不可交互（Pick 里拦截），
        //  随窗口尺寸 / 界面缩放同步（布局全用逻辑坐标，渲染层统一 ScaleTransform）。
        // =================================================================
        void OpenOverlay(List<El> els, Action layout, ElInput[] inputs)
        {
            // 折叠态窗口只有 408 宽，覆盖层排不下会挤成一团 → 先直接展开右栏（无动画，避免打开瞬间布局错乱）
            if (_collapsed) SetRightCollapsed(false, false);
            CloseOverlayInstant(); // 已开着别的覆盖层就先收掉
            _overlayEls = els;
            _overlayLayout = layout;
            _ovlInputs = inputs;
            _ovlClosing = false;
            if (_dim == null)
            {
                _dim = new ElDim { Host = this };
                _dim.Clicked += delegate { CloseOverlaySmart(); }; // 点遮罩空白处关闭
            }
            _dim.SetBounds(0, 0, Lw, Lh);
            _dim.Alpha = 0f;
            _hoverEl = null; _captureEl = null;
            SetFocusInput(null);
            layout();
            // 打开动画：遮罩淡入 + 面板上滑 20→0
            if (_ovlAnim != null) { _ovlAnim.Stop(); _ovlAnim.Dispose(); _ovlAnim = null; }
            _ovlSlide = 20;
            int i = 0;
            _ovlAnim = new System.Windows.Forms.Timer { Interval = 15 };
            _ovlAnim.Tick += delegate { OvlTick(ref i); };
            _ovlAnim.Start();
        }

        void OvlTick(ref int i)
        {
            i++;
            float t = Math.Min(1f, i / (float)OVL_STEPS);
            float e = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic
            if (_ovlClosing) e = 1f - e; // 关闭时反向（淡出 + 下滑回去）
            if (_dim != null) _dim.Alpha = e;
            _ovlSlide = (int)(20 * (1f - e));
            Render();
            if (i >= OVL_STEPS)
            {
                _ovlAnim.Stop(); _ovlAnim.Dispose(); _ovlAnim = null;
                if (_ovlClosing) CloseOverlayInstant();
            }
        }

        /// <summary>点遮罩 / Esc / 关闭按钮的统一入口：AI 配置若是从创意工坊点进来的，关掉后回到创意工坊</summary>
        void CloseOverlaySmart()
        {
            if (_overlayEls == _aiEls && _aiReturnWs)
            {
                _aiReturnWs = false;
                OpenOverlay(_wsEls, LayoutWorkshopOverlay, new[] { _wsInput });
                return;
            }
            CloseOverlay();
        }

        /// <summary>点遮罩 / Esc / 面板关闭按钮：播放淡出动画后收掉</summary>
        void CloseOverlay()
        {
            if (_overlayEls == null || _ovlClosing) return;
            _ovlClosing = true;
            SetFocusInput(null);
            if (_ovlAnim != null) { _ovlAnim.Stop(); _ovlAnim.Dispose(); _ovlAnim = null; }
            int i = 0;
            _ovlAnim = new System.Windows.Forms.Timer { Interval = 15 };
            _ovlAnim.Tick += delegate { OvlTick(ref i); };
            _ovlAnim.Start();
        }

        /// <summary>不播动画直接清场（切页 / 打开新覆盖层前调用）</summary>
        void CloseOverlayInstant()
        {
            if (_overlayEls == _suEls) SusuOnOverlayClosed(); // 苏苏：停打字机/配音/子面板
            if (_ovlAnim != null) { _ovlAnim.Stop(); _ovlAnim.Dispose(); _ovlAnim = null; }
            _overlayEls = null;
            _overlayLayout = null;
            _ovlInputs = null;
            _ovlClosing = false;
            _ovlSlide = 0;
            SetFocusInput(null);
            Render();
            // 确认弹窗被关掉（无论怎么关的）后的回调——比如返回切换整合包列表
            var cb = _cfOnClosed; _cfOnClosed = null;
            if (cb != null && !IsDisposed && !Disposing) cb();
        }

        // =================================================================
        //  通用确认弹窗（极简风覆盖层）：顶部黑色装饰条 + 标题 + 正文 + 是/否。
        //  无进度条、不自动关闭，必须手动选择；点遮罩 / Esc / 否 都视为取消。
        // =================================================================
        void BuildConfirmOverlay()
        {
            _cfPanel = AddEl(_cfEls, new ElConfirm());
            _cfNo = AddEl(_cfEls, new ElBtn(L.T("否")));
            _cfNo.Clicked += delegate { CloseOverlay(); };
            _cfYes = AddEl(_cfEls, new ElBtn(L.T("是")) { Primary = true });
            _cfYes.Clicked += delegate
            {
                var act = _cfOnYes; _cfOnYes = null;
                CloseOverlay();
                if (act != null) act();
            };
        }

        void LayoutConfirmOverlay()
        {
            int w = Math.Min(520, Lw - 120);
            int h = ElConfirm.MeasureHeight(_cfPanel.Message, w);
            int maxH = Lh - 160;
            if (h > maxH) h = maxH;
            int px = (Lw - w) / 2, py = (Lh - h) / 2;
            _cfPanel.SetBounds(px, py, w, h);
            int bw = 96, bh = 32, gap = 12;
            int by = py + h - ElConfirm.BtnRowH + (ElConfirm.BtnRowH - bh) / 2 - 4;
            _cfNo.SetBounds(px + w - ElConfirm.Pad - bw * 2 - gap, by, bw, bh);
            _cfYes.SetBounds(px + w - ElConfirm.Pad - bw, by, bw, bh);
        }

        /// <summary>弹极简风确认框：标题 + 正文，用户点「是」才执行 onYes</summary>
        void ShowConfirm(string title, string msg, Action onYes) { ShowConfirm(title, msg, onYes, null); }

        /// <summary>onClosed：确认框关闭后（是/否/取消都算）执行，用于返回上一个覆盖层</summary>
        void ShowConfirm(string title, string msg, Action onYes, Action onClosed)
        {
            _cfPanel.Title = title;
            _cfPanel.Message = msg;
            _cfOnYes = onYes;
            OpenOverlay(_cfEls, LayoutConfirmOverlay, null);
            _cfOnClosed = onClosed; // 必须在 OpenOverlay 之后赋值：OpenOverlay 开场会清场，不能误触发
        }

        // =================================================================
        //  切换整合包覆盖层（原 PackSwitchForm 的并轨版：不再是独立窗口）
        // =================================================================
        void BuildPackSwitchOverlay()
        {
            _psPanel = AddEl(_psEls, new ElCard { Radius = 12 });
            _psTitle = AddEl(_psEls, new ElText(L.T("切换整合包")) { FontOf = () => T.B(15) });
            _psHint = AddEl(_psEls, new ElText(
                L.T("切换会把两边的配置各存一份（版本隔离），1.6 和 1.7 不会互相覆盖。双击直接切换。"))
                { FontOf = () => T.F(11.5f), ColorOf = () => T.Text3 });
            _psList = AddEl(_psEls, new ElPackList { EmptyHint = L.T("列表是空的。") });
            _psList.SelChanged += delegate { PsUpdateButtons(); };
            _psList.ItemActivated += delegate { PsAccept(); };
            _psBtnRemove = AddEl(_psEls, new ElBtn(L.T("从列表移除")));
            _psBtnRemove.Clicked += delegate { PsRemove(); };
            _psBtnClose = AddEl(_psEls, new ElBtn(L.T("关闭")));
            _psBtnClose.Clicked += delegate { CloseOverlay(); };
            _psBtnSwitch = AddEl(_psEls, new ElBtn(L.T("切换到这个包")) { Primary = true });
            _psBtnSwitch.Clicked += delegate { PsAccept(); };
        }

        void LayoutPackSwitchOverlay()
        {
            int w = Math.Min(620, Lw - 80), h = Math.Min(460, Lh - 120);
            int px = (Lw - w) / 2, py = (Lh - h) / 2;
            _psPanel.SetBounds(px, py, w, h);
            int m = 20;
            int ix = px + m, iw = w - m * 2;
            _psTitle.SetBounds(ix, py + 14, 300, 24);
            _psBtnClose.SetBounds(px + w - m - 64, py + 14, 64, 30);
            _psHint.SetBounds(ix, py + 44, iw, 18);
            _psList.SetBounds(ix, py + 70, iw, h - 70 - 20 - 40 - 20 + m - m);
            int by = py + h - m - 36;
            _psBtnRemove.SetBounds(ix, by, 116, 36);
            _psBtnSwitch.SetBounds(px + w - m - 140, by, 140, 36);
        }

        /// <summary>打开切换整合包覆盖层（读注册表 → 列表 → 弹层）</summary>
        void PsOpen()
        {
            string settingsDir = (_ctx == null) ? "" : _ctx.SettingsDir;
            if (string.IsNullOrEmpty(settingsDir))
                settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), L.T("ZOGOS创世神启动器"));
            _psSettingsDir = settingsDir;

            List<PackEntry> list = PackRegistry.Load(settingsDir);
            if (_ctx != null && !string.IsNullOrEmpty(_ctx.Root))
            {
                PackEntry cur = PackRegistry.Upsert(list, _ctx, _st.LastScheme);
                if (cur != null && string.IsNullOrEmpty(cur.PackId)) cur.PackId = _st.PackId;
                PackRegistry.Save(settingsDir, list);
            }
            if (list.Count == 0)
            {
                Toast(L.T("还没有导入过任何整合包，先点【导入整合包】或在【设置】页绑定"));
                return;
            }
            _psItems = list;
            _psList.Items.Clear();
            _psList.Items.AddRange(list);
            _psList.CurrentId = _st.PackId ?? "";
            int sel = 0;
            for (int i = 0; i < list.Count; i++)
                if (_psList.IsCurrent(list[i])) { sel = i; break; }
            _psList.Sel = sel;
            PsUpdateButtons();
            OpenOverlay(_psEls, LayoutPackSwitchOverlay, null);
        }

        void PsUpdateButtons()
        {
            PackEntry e = (_psList.Sel >= 0 && _psList.Sel < _psItems.Count) ? _psItems[_psList.Sel] : null;
            _psBtnSwitch.Enabled = e != null && !e.Missing && !_psList.IsCurrent(e);
            _psBtnRemove.Enabled = e != null && !_psList.IsCurrent(e);
            Render();
        }

        void PsAccept()
        {
            PackEntry e = (_psList.Sel >= 0 && _psList.Sel < _psItems.Count) ? _psItems[_psList.Sel] : null;
            if (e == null || _psList.IsCurrent(e)) return;
            if (e.Missing) { Toast(L.T("这个包的文件夹已经不在了：") + e.Root); return; }
            CloseOverlayInstant(); // 切换可能耗时，先收掉弹层
            string err, newRoot;
            bool ok = PackSwitcher.Switch(e, _st, Application.StartupPath, out newRoot, out err);
            if (!ok) { Toast(err == null ? L.T("切换失败") : err); return; }
            Redetect();
            SetStatus(L.T("已切换到：") + e.DisplayName);
            Toast(L.T("切换完成：") + e.DisplayName + L.T("，点【启动游戏】就能玩"));
        }

        void PsRemove()
        {
            PackEntry e = (_psList.Sel >= 0 && _psList.Sel < _psItems.Count) ? _psItems[_psList.Sel] : null;
            if (e == null || _psList.IsCurrent(e)) return;
            ShowConfirm(L.T("从列表移除"),
                L.T("把「") + e.DisplayName + L.T("」从列表里移除？\n\n只移除记录，不会删掉磁盘上的文件。"),
                delegate
                {
                    _psItems.Remove(e);
                    PackRegistry.Save(_psSettingsDir, _psItems);
                },
                delegate { if (_psItems.Count > 0) PsOpen(); }); // 确认框关掉后回到列表（点是也回，点否也回）
        }

        // =================================================================
        //  苏苏 AI 覆盖层（旧版粉色悬浮窗 SusuWindow 的极简重制）：
        //  立绘 + 气泡打字机 + 内联选项按钮 + 记录/AI结果/二维码子面板，
        //  状态机逻辑与旧窗一一对应（节点脚本 / 配音 / 自动推进不变）。
        // =================================================================
        void BuildSusuOverlay()
        {
            _suPanel = AddEl(_suEls, new ElCard { Radius = 12 });
            _suTitle = AddEl(_suEls, new ElText(L.T("苏苏 · AI 诊断助手")) { FontOf = () => T.B(15) });
            _suBtnClose = AddEl(_suEls, new ElBtn(L.T("关闭")));
            _suBtnClose.Clicked += delegate { SusuCloseOverlay(); };
            _suHint = AddEl(_suEls, new ElText("") { FontOf = () => T.F(11), ColorOf = () => T.Text3, Ellipsis = true });

            _suStage = AddEl(_suEls, new ElSusu());
            _suStage.BubbleClicked += delegate { SusuAdvance(); };

            _suNext = AddEl(_suEls, new ElBtn(L.T("下一句 ▸")) { Primary = true });
            _suNext.Clicked += delegate { SusuAdvance(); };
            _suLog = AddEl(_suEls, new ElBtn(L.T("记录")));
            _suLog.Clicked += delegate { SusuShowBacklog(); };
            _suSkip = AddEl(_suEls, new ElBtn(L.T("跳过")));
            _suSkip.Clicked += delegate { SusuCloseOverlay(); };
            _suMute = AddEl(_suEls, new ElBtn(L.T("配音")));
            _suMute.Clicked += delegate { SusuToggleMute(); };

            for (int i = 0; i < _suChoiceBtns.Length; i++)
            {
                int idx = i;
                var b = AddEl(_suEls, new ElBtn("") { Visible = false });
                b.Clicked += delegate { SusuPickChoice(idx); };
                _suChoiceBtns[i] = b;
            }

            // 子面板（最后添加 = 最顶层）
            _suSubDim = AddEl(_suEls, new ElDim { Visible = false });
            _suSubDim.Clicked += delegate { SusuHideSub(); };
            _suSubPanel = AddEl(_suEls, new ElCard { Radius = 12, Visible = false });
            _suSubTitle = AddEl(_suEls, new ElText("") { FontOf = () => T.B(14), Visible = false });
            _suSubText = AddEl(_suEls, new ElLogView { Visible = false });
            _suSubImg = AddEl(_suEls, new ElCover { Visible = false });
            _suSubClose = AddEl(_suEls, new ElBtn(L.T("关闭")) { Visible = false });
            _suSubClose.Clicked += delegate { SusuHideSub(); };

            _suTypeTimer = new System.Windows.Forms.Timer { Interval = 26 };
            _suTypeTimer.Tick += delegate { SusuTickType(); };
            _suAutoTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _suAutoTimer.Tick += delegate { _suAutoTimer.Stop(); SusuAdvance(); };
        }

        void LayoutSusuOverlay()
        {
            int w = Math.Min(880, Lw - 80), h = Math.Min(560, Lh - 100);
            int px = (Lw - w) / 2, py = (Lh - h) / 2;
            _suPanel.SetBounds(px, py, w, h);
            int m = 20;
            int ix = px + m, iw = w - m * 2;
            _suTitle.SetBounds(ix, py + 14, 400, 24);
            _suBtnClose.SetBounds(px + w - m - 64, py + 14, 64, 30);

            int stageH = h - 52 - 16 - 44 - 16; // 标题区 + 按钮行之外的全给舞台
            _suStage.SetBounds(ix, py + 52, iw, stageH);

            int by = py + h - m - 36;
            _suNext.SetBounds(ix, by, 110, 36);
            _suLog.SetBounds(ix + 118, by, 76, 36);
            _suSkip.SetBounds(ix + 118 + 84, by, 76, 36);
            _suMute.SetBounds(ix + 118 + 84 + 84, by, 76, 36);
            _suHint.SetBounds(ix + 118 + 84 + 84 + 84 + 16, by + 8, iw - 370 - 16, 22);

            // 选项按钮：气泡区（右列）内纵向堆叠，整体锚定在气泡底边之上（8 个选项也不会溢出气泡/压按钮行）
            int nCh = 0;
            for (int i = 0; i < _suChoiceBtns.Length; i++) { if (!_suChoiceBtns[i].Visible) break; nCh++; }
            if (nCh > 0)
            {
                int cx = ix + _suStage.PortraitW + 12 + 16;
                int cw2 = iw - _suStage.PortraitW - 12 - 32;
                int itemH = 30, gap = 6;
                int total = nCh * itemH + (nCh - 1) * gap;
                int bubbleBottom = py + 52 + stageH;
                int cy = bubbleBottom - 12 - total;
                int minY = py + 52 + 64; // 再高就要压到气泡文本区了
                if (cy < minY) cy = minY;
                for (int i = 0; i < nCh; i++)
                {
                    _suChoiceBtns[i].SetBounds(cx, cy, Math.Min(360, cw2), itemH);
                    cy += itemH + gap;
                }
            }

            // 子面板（居中 640×420）
            int sw = 640, sh = 420;
            int sx = (Lw - sw) / 2, sy = (Lh - sh) / 2;
            _suSubDim.SetBounds(0, 0, Lw, Lh);
            _suSubPanel.SetBounds(sx, sy, sw, sh);
            _suSubTitle.SetBounds(sx + 20, sy + 14, sw - 120, 24);
            _suSubClose.SetBounds(sx + sw - 20 - 64, sy + 14, 64, 30);
            _suSubText.SetBounds(sx + 20, sy + 50, sw - 40, sh - 70);
            int imgSz = Math.Min(sw - 80, sh - 110);
            _suSubImg.SetBounds(sx + (sw - imgSz) / 2, sy + 56, imgSz, imgSz);
        }

        // —— 打开 / 关闭 ——
        void SusuOpen(bool firstTime)
        {
            _suHistory.Clear();
            SusuRefreshMuteBtn();
            OpenOverlay(_suEls, LayoutSusuOverlay, null);
            SusuStartAt(firstTime ? GuideScript.StartId : "hub");
            Log.Info(L.T("苏苏上线（新版覆盖层）：") + (firstTime ? L.T("首次启动引导") : L.T("功能菜单")));
        }

        void SusuCloseOverlay()
        {
            SusuStopAll();
            CloseOverlay();
        }

        /// <summary>覆盖层被关掉（无论哪种路径）时停掉打字机/自动推进/配音</summary>
        void SusuOnOverlayClosed()
        {
            SusuStopAll();
            _suSubOn = false;
            foreach (var b in _suChoiceBtns) b.Visible = false;
        }

        void SusuStopAll()
        {
            _suTypeTimer.Stop();
            _suAutoTimer.Stop();
            _suTyping = false;
            try { SusuVoice.Stop(); } catch { }
        }

        // —— 节点推进（逻辑照搬旧窗）——
        void SusuStartAt(string nodeId)
        {
            SusuStopAll();
            SusuGotoNode(nodeId);
        }

        void SusuGotoNode(string id)
        {
            GuideNode n = (id == "diag") ? SusuDiag.BuildNode(_ctx, _st) : GuideScript.Get(id);
            if (n == null) { SusuCloseOverlay(); return; }
            SusuSetNode(n);
        }

        void SusuSetNode(GuideNode n)
        {
            _suNode = n;
            _suLineIdx = 0;
            _suChoicesShown = false;
            foreach (var b in _suChoiceBtns) b.Visible = false;
            if (n.Lines.Count == 0) { SusuShowChoices(); return; }
            SusuShowLine();
        }

        void SusuShowLine()
        {
            if (_suNode == null || _suLineIdx >= _suNode.Lines.Count) { SusuShowChoices(); return; }
            GuideLine ln = _suNode.Lines[_suLineIdx];
            SusuVoice.Play(ln);
            // 立绘：只有当前行明确指定了图片才换；Pic=0 或加载失败时保持上一张
            //（视觉小说惯例——否则没配图的对白行会把立绘掉成占位「苏」，被当成贴图丢失）
            Image im = null;
            if (ln.Pic > 0) { try { im = AppLogo.GetByName("susu" + ln.Pic.ToString("D2")); } catch { } }
            if (im != null) _suStage.Portrait = im;
            _suStage.Who = ln.Who ?? "";
            _suFull = ln.Text ?? "";
            _suShown = "";
            _suTyping = true;
            _suAutoTimer.Stop();
            _suTypeTimer.Start();
            try { _suHistory.Add(string.IsNullOrEmpty(ln.Who) ? "· " + ln.Text : ln.Who + "：" + ln.Text); }
            catch { }
            SusuSyncStage();
        }

        void SusuSyncStage()
        {
            _suStage.Text = _suShown;
            _suStage.Typing = _suTyping;
            _suStage.Progress = (_suNode != null && _suNode.Lines.Count > 0 && !_suChoicesShown)
                ? (_suLineIdx + 1) + "/" + _suNode.Lines.Count : "";
            Render();
        }

        void SusuTickType()
        {
            if (!_suTyping) { _suTypeTimer.Stop(); return; }
            int step = 2;
            if (_suShown.Length + step >= _suFull.Length)
            {
                _suShown = _suFull; _suTyping = false; _suTypeTimer.Stop(); SusuArmAuto();
            }
            else _suShown = _suFull.Substring(0, _suShown.Length + step);
            SusuSyncStage();
        }

        /// <summary>有配音就等念完再自动推进，没配音 2 秒（与旧窗一致）</summary>
        void SusuArmAuto()
        {
            _suAutoTimer.Stop();
            int ms = 2000;
            try
            {
                int rem = SusuVoice.Muted ? 0 : SusuVoice.RemainingMs;
                if (rem > 0) ms = rem + 400;
            }
            catch { }
            if (ms < 1200) ms = 1200;
            if (ms > 60000) ms = 60000;
            _suAutoTimer.Interval = ms;
            _suAutoTimer.Start();
        }

        void SusuAdvance()
        {
            if (_suSubOn) return; // 子面板开着时不推进
            _suAutoTimer.Stop();
            if (_suTyping) { _suShown = _suFull; _suTyping = false; _suTypeTimer.Stop(); SusuArmAuto(); SusuSyncStage(); return; }
            if (_suNode == null || _suChoicesShown) return;
            _suLineIdx++;
            if (_suLineIdx >= _suNode.Lines.Count) { SusuShowChoices(); return; }
            SusuShowLine();
        }

        void SusuShowChoices()
        {
            SusuStopAll();
            if (_suNode != null && _suNode.Choices.Count > 0)
            {
                _suChoicesShown = true;
                _suStage.Who = "";
                _suStage.Progress = "";
                _suFull = _suShown = L.T("你选一个吧～");
                int n = Math.Min(_suNode.Choices.Count, _suChoiceBtns.Length);
                for (int i = 0; i < n; i++)
                {
                    _suChoiceBtns[i].Text = _suNode.Choices[i].Label;
                    _suChoiceBtns[i].Visible = true;
                }
                LayoutSusuOverlay();
                SusuSyncStage();
                return;
            }
            string next = _suNode == null ? "" : _suNode.Next;
            if (string.IsNullOrEmpty(next)) { SusuCloseOverlay(); return; }
            SusuGotoNode(next);
        }

        void SusuPickChoice(int idx)
        {
            if (!_suChoicesShown || _suNode == null || idx >= _suNode.Choices.Count) return;
            GuideChoice c = _suNode.Choices[idx];
            _suChoicesShown = false;
            foreach (var b in _suChoiceBtns) b.Visible = false;
            try { _suHistory.Add(L.T("你") + "：" + c.Label); } catch { }
            if (!string.IsNullOrEmpty(c.Action)) SusuDoAction(c.Action, c.Next);
            else if (!string.IsNullOrEmpty(c.Next)) SusuGotoNode(c.Next);
            Render();
        }

        /// <summary>选项动作（与旧窗一致：社区 / AI 诊断 / 二维码 / 导入）</summary>
        void SusuDoAction(string action, string next)
        {
            try
            {
                if (action == "community") { ShellOpen.Url(SusuDiag.CommunityUrl); Log.Info(L.T("苏苏打开了社区链接")); return; }
                if (action == "ai") { SusuRunAi(); return; }
                if (action == "qr")
                {
                    SusuShowSub(L.T("创作者交流群"), null, AppLogo.GetByName("partyqr"));
                    return;
                }
                if (action == "import") { OpenImportDialog(); return; }
            }
            catch (Exception ex) { Log.Warn(L.T("执行选项动作失败: ") + ex.Message); }
            if (!string.IsNullOrEmpty(next)) SusuGotoNode(next);
        }

        // —— 记录 / 子面板 ——
        void SusuShowBacklog()
        {
            var sb = new StringBuilder();
            foreach (var line in _suHistory) sb.AppendLine(line);
            SusuShowSub(L.T("对话记录"), sb.ToString(), null);
        }

        /// <summary>子面板：text 非空显示可选中文本（记录/AI结果），img 非空显示图片（二维码）</summary>
        void SusuShowSub(string title, string text, Image img)
        {
            _suSubOn = true;
            _suSubTitle.Text = title;
            _suSubText.Visible = text != null;
            if (text != null) _suSubText.SetText(text);
            _suSubImg.Visible = img != null;
            _suSubImg.Img = img;
            _suSubDim.Visible = true;
            _suSubPanel.Visible = true;
            _suSubTitle.Visible = true;
            _suSubClose.Visible = true;
            LayoutSusuOverlay();
            Render();
        }

        void SusuHideSub()
        {
            _suSubOn = false;
            _suSubDim.Visible = false;
            _suSubPanel.Visible = false;
            _suSubTitle.Visible = false;
            _suSubText.Visible = false;
            _suSubImg.Visible = false;
            _suSubClose.Visible = false;
            Render();
        }

        // —— 配音开关 ——
        void SusuToggleMute()
        {
            try
            {
                SusuVoice.Muted = !SusuVoice.Muted;
                SusuRefreshMuteBtn();
                if (!SusuVoice.Muted && _suNode != null && _suLineIdx < _suNode.Lines.Count)
                    SusuVoice.Play(_suNode.Lines[_suLineIdx]); // 刚取消静音，把当前这句补播
            }
            catch { }
        }

        void SusuRefreshMuteBtn()
        {
            bool muted = SusuVoice.Muted;
            _suMute.Text = muted ? L.T("已静音") : L.T("配音");
        }

        // —— AI 诊断（确认弹窗用通用 ElConfirm，关掉后自动回到苏苏）——
        void SusuRunAi()
        {
            if (string.IsNullOrEmpty(_st.ApiBase) || string.IsNullOrEmpty(_st.ApiModel))
            {
                Toast(L.T("还没配置 AI 接口，先填地址和模型"));
                OpenAiSetup(false); // 会顶掉苏苏覆盖层，配好后重新呼叫即可
                return;
            }
            string ask = L.T("会把下面这些内容上传到：") + "\n" + _st.ApiBase
                + "\n" + L.T("模型：") + _st.ApiModel + "\n\n"
                + L.T("· 启动器日志（尾部 60 行）") + "\n"
                + L.T("· RimWorld 日志里的报错（最近 40 条）") + "\n"
                + L.T("· 本机环境（路径 / 显卡 / 显存 / 模组数）") + "\n\n"
                + L.T("确定要上传并让 AI 分析吗？");
            ShowConfirm(L.T("AI 诊断"), ask,
                delegate { SusuRunAiUpload(); },
                delegate { if (_suNode != null) SusuReopen(); }); // 确认框关掉后回到苏苏
        }

        /// <summary>确认框顶掉苏苏层之后，按原状态重新打开（对话进度不丢）</summary>
        void SusuReopen()
        {
            OpenOverlay(_suEls, LayoutSusuOverlay, null);
            LayoutSusuOverlay();
            SusuSyncStage();
        }

        void SusuRunAiUpload()
        {
            _suFull = _suShown = L.T("苏苏正在翻你的日志……");
            _suStage.Who = L.T("苏苏");
            _suTyping = false;

            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                try
                {
                    List<DiagItem> items = SusuDiag.Run(_ctx, _st);
                    string user = AiClient.BuildUserPrompt(_ctx, _st, items);
                    string err;
                    string ans = AiClient.Chat(_st.ApiBase, _st.ApiKey, _st.ApiModel, AiClient.SystemPrompt(), user, 90000, out err);
                    e.Result = ans == null ? (L.T("AI 诊断失败：") + (err == null ? L.T("未知错误") : err)) : ans;
                }
                catch (Exception ex) { e.Result = L.T("AI 诊断出错：") + ex.Message; }
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                if (_overlayEls != _suEls) { Toast(L.T("AI 诊断已完成，重新呼叫苏苏查看")); return; } // 用户中途关了苏苏
                string ans = e.Error != null ? L.T("AI 诊断出错：") + e.Error.Message : Convert.ToString(e.Result);
                SusuShowSub(L.T("苏苏的 AI 诊断建议"), ans, null); // 可选中复制
                SusuSetNode(SusuDiag.BuildHelpNode());
            };
            bw.RunWorkerAsync();
        }

        // =================================================================
        //  创意工坊覆盖层（原 WorkshopForm 的并轨版：不再是独立窗口，固定叠在主窗口上层）
        // =================================================================
        void BuildWorkshopOverlay()
        {
            _wsPanel = AddEl(_wsEls, new ElCard { Radius = 12 });
            _wsTitle = AddEl(_wsEls, new ElText(L.T("创意工坊")) { FontOf = () => T.B(15) });
            _wsDesc = AddEl(_wsEls, new ElText(
                L.T("搜 RimWorld 创意工坊，一键下载进当前整合包。合集也能整包拉下来。"))
                { FontOf = () => T.F(11.5f), ColorOf = () => T.Text3 });
            _wsBtnClose = AddEl(_wsEls, new ElBtn(L.T("关闭")));
            _wsBtnClose.Clicked += delegate { CloseOverlay(); };

            _wsInput = AddEl(_wsEls, new ElInput());
            _wsTranslate = AddEl(_wsEls, new ElCheck(L.T("AI 翻译成英文再搜"), true));
            _wsBtnAi = AddEl(_wsEls, new ElBtn(L.T("AI 接口…")));
            _wsBtnAi.Clicked += delegate { OpenAiSetup(true); }; // 覆盖层内打开，关掉后自动回到这里
            _wsBtnSearch = AddEl(_wsEls, new ElBtn(L.T("搜索")) { Primary = true });
            _wsBtnSearch.Clicked += delegate { WsSearch(); };

            _wsLbVer = AddEl(_wsEls, new ElText(L.T("版本")) { FontOf = () => T.F(12), ColorOf = () => T.Text2 });
            for (int i = 0; i < _wsVerOpts.Length; i++)
            {
                int idx = i;
                var b = AddEl(_wsEls, new ElBtn(WsVerLabels[idx]));
                b.Clicked += delegate { _wsVerIdx = idx; WsRefreshOpts(); };
                _wsVerOpts[i] = b;
            }
            _wsLbType = AddEl(_wsEls, new ElText(L.T("类型")) { FontOf = () => T.F(12), ColorOf = () => T.Text2 });
            for (int i = 0; i < _wsTypeOpts.Length; i++)
            {
                int idx = i;
                var b = AddEl(_wsEls, new ElBtn(WsTypeLabels[idx]));
                b.Clicked += delegate { _wsTypeIdx = idx; WsRefreshOpts(); };
                _wsTypeOpts[i] = b;
            }
            WsRefreshOpts();

            _wsList = AddEl(_wsEls, new ElWsList { EmptyHint = L.T("输入关键词后点搜索。") });
            _wsList.ItemOpened += delegate { WsOpenPage(); }; // 双击行 = 打开工坊页面
            _wsStatus = AddEl(_wsEls, new ElText("") { FontOf = () => T.F(11.5f), ColorOf = () => T.Text3, Ellipsis = true });

            _wsBtnSync = AddEl(_wsEls, new ElBtn(L.T("工坊同步 / 按排序补齐…")));
            _wsBtnSync.Clicked += delegate // 同步窗口还是旧独立窗，后续再并轨
            {
                using (WorkshopSyncForm f = new WorkshopSyncForm(_st, _ctx))
                    f.ShowDialog(this);
            };
            _wsBtnOpen = AddEl(_wsEls, new ElBtn(L.T("打开工坊页面")));
            _wsBtnOpen.Clicked += delegate { WsOpenPage(); };
            _wsBtnDownload = AddEl(_wsEls, new ElBtn(L.T("下载选中项进整合包")) { Primary = true });
            _wsBtnDownload.Clicked += delegate { WsDownload(); };
        }

        void WsRefreshOpts()
        {
            for (int i = 0; i < _wsVerOpts.Length; i++) _wsVerOpts[i].Primary = i == _wsVerIdx;
            for (int i = 0; i < _wsTypeOpts.Length; i++) _wsTypeOpts[i].Primary = i == _wsTypeIdx;
            Render();
        }

        /// <summary>覆盖层面板布局（逻辑坐标；窗口尺寸 / 缩放变化时由 LayoutHome 重新调用）</summary>
        void LayoutWorkshopOverlay()
        {
            int px = 28, py = 50; // 上下拉大：底部伸到 Lh-8，盖住主页底部一排目录按钮
            int pw = Lw - 56, ph = Lh - 58;
            if (pw < 400 || ph < 300) return;
            _wsPanel.SetBounds(px, py, pw, ph);
            int m = 20;
            int ix = px + m, iw = pw - m * 2;

            _wsTitle.SetBounds(ix, py + 14, 300, 24);
            _wsBtnClose.SetBounds(px + pw - m - 64, py + 14, 64, 30);
            _wsDesc.SetBounds(ix, py + 44, iw - 90, 18);

            int y1 = py + 74;
            _wsInput.SetBounds(ix, y1, 340, 30);
            _wsTranslate.SetBounds(ix + 352, y1 + 2, 180, 26);
            _wsBtnAi.SetBounds(px + pw - m - 110, y1, 110, 30);
            _wsBtnSearch.SetBounds(px + pw - m - 110 - 10 - 128, y1, 128, 30);

            int y2 = py + 116;
            _wsLbVer.SetBounds(ix, y2, 40, 26);
            int ox = ix + 44;
            for (int i = 0; i < _wsVerOpts.Length; i++) { _wsVerOpts[i].SetBounds(ox, y2, 62, 26); ox += 62 + 6; }
            ox += 18;
            _wsLbType.SetBounds(ox, y2, 40, 26);
            ox += 44;
            for (int i = 0; i < _wsTypeOpts.Length; i++) { _wsTypeOpts[i].SetBounds(ox, y2, 62, 26); ox += 62 + 6; }

            int botBtnY = py + ph - m - 36;
            int ly = py + 156;
            _wsList.SetBounds(ix, ly, iw, Math.Max(60, botBtnY - 44 - ly));
            _wsStatus.SetBounds(ix, botBtnY - 32, iw, 22);

            int bx = px + pw - m;
            bx -= 190; _wsBtnDownload.SetBounds(bx, botBtnY, 190, 36);
            bx -= 8 + 150; _wsBtnOpen.SetBounds(bx, botBtnY, 150, 36);
            bx -= 8 + 170; _wsBtnSync.SetBounds(bx, botBtnY, 170, 36);
        }

        void WsSetStatus(string s) { _wsStatus.Text = s; Render(); }

        void WsSetBusy(bool busy, string text)
        {
            _wsBusy = busy;
            _wsBtnSearch.Enabled = !busy;
            _wsBtnDownload.Enabled = !busy;
            _wsBtnOpen.Enabled = !busy;
            _wsBtnSync.Enabled = !busy;
            if (!string.IsNullOrEmpty(text)) _wsStatus.Text = text;
            Render();
        }

        class WsResult
        {
            public string SearchText;
            public bool UsedAi;
            public string Error;
            public string Backend;
            public int Total;
            public List<WorkshopItem> Items;
        }

        void WsSearch()
        {
            if (_wsBusy) return;
            string q = _wsInput.Text.Trim();
            if (q.Length == 0) { WsSetStatus(L.T("先输入搜索词，例如：脸部动画、种族、存储。")); return; }
            bool translate = _wsTranslate.Checked;
            string ver = WsVersions[_wsVerIdx];
            bool coll = _wsTypeIdx == 1;
            WsSetBusy(true, L.T("正在搜索创意工坊…"));
            var worker = new BackgroundWorker();
            worker.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                WsResult r = new WsResult();
                string search = q;
                bool usedAi = false;
                string terr = null;
                if (translate) search = WorkshopClient.TranslateQuery(_st, q, out usedAi, out terr);
                r.SearchText = search;
                r.UsedAi = usedAi;
                string err = null;
                string backend = "";
                int total = 0;
                string modsDir = (_ctx == null) ? "" : _ctx.ModsDir;
                r.Items = WorkshopClient.SearchAuto(_st, search, ver, coll, modsDir, q, out err, out backend, out total);
                r.Error = err;
                r.Backend = backend;
                r.Total = total;
                e.Result = r;
            };
            worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                WsSetBusy(false, "");
                WsResult r = e.Result as WsResult;
                if (r == null || r.Items == null) { WsSetStatus(L.T("搜索失败：没有返回结果。")); return; }
                _wsList.SetItems(r.Items);
                bool isLocal = r.Backend != null && r.Backend.IndexOf(L.T("本地"), StringComparison.Ordinal) >= 0;

                string msg = L.T("搜索词：") + r.SearchText + L.T("  ·  找到 ") + r.Items.Count + L.T(" 个结果");
                if (!string.IsNullOrEmpty(r.Backend)) msg += L.T("  ·  来源：") + r.Backend;
                if (isLocal) msg = L.T("【注意】工坊连不上（官方接口缺 API Key，网页被墙），下面列的是你【本地已装】的模组，不是工坊搜索结果。")
                    + L.T("要搜真正的工坊，请先在「工坊同步 / 按排序补齐」里填 Steam API Key。");
                if (r.UsedAi) msg += L.T("  ·  已用 AI 翻译");
                if (r.Items.Count == 0)
                {
                    msg = L.T("工坊搜索不可用（缺 API Key / 网页被 Steam 屏蔽）。请用「工坊同步」里的 API Key，或「按 ID / 链接导入」。");
                    if (!string.IsNullOrEmpty(r.Error)) msg += L.T("   [原因] ") + r.Error;
                }
                else if (!string.IsNullOrEmpty(r.Error)) msg += "  ·  " + r.Error;
                WsSetStatus(msg);
                if (r.Items.Count == 0 && !WorkshopWebApi.HasKey(_st))
                {
                    DialogResult dr = MinMsg.Show(this,
                        L.T("没有配置 Steam API Key，工坊关键词搜索走不通。") + Environment.NewLine + Environment.NewLine
                        + L.T("现在去申请一个吗？大约 1 分钟。") + Environment.NewLine
                        + L.T("（申请页需要梯子打开；拿到 key 填好之后就可以把梯子关掉）"),
                        L.T("需要 Steam API Key"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (dr == DialogResult.Yes)
                        using (WorkshopSyncForm f = new WorkshopSyncForm(_st, _ctx))
                            f.ShowDialog(this);
                }
            };
            worker.RunWorkerAsync();
        }

        void WsDownload()
        {
            if (_wsBusy) return;
            WorkshopItem it = _wsList.SelectedItem;
            if (it == null) { WsSetStatus(L.T("先在列表里点一个模组或合集。")); return; }
            if (_ctx == null)
            {
                string e0;
                bool ok0 = WorkshopClient.OpenSteamDownload(it.Id, out e0);
                WsSetStatus(ok0 ? L.T("已交给 Steam 下载：") + it.Title : L.T("启动 Steam 下载失败：") + e0);
                return;
            }
            bool coll = it.ItemType == "collection" || _wsTypeIdx == 1;
            WsSetBusy(true, L.T("正在下载并复制到整合包 Mods…（最多等待 4 分钟）"));
            var dl = new BackgroundWorker();
            dl.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                string dest, err;
                bool ok = WorkshopClient.DownloadToPack(_ctx, it.Id, coll, out dest, out err);
                e.Result = new object[] { ok, dest, err };
            };
            dl.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                WsSetBusy(false, "");
                object[] arr = e.Result as object[];
                bool ok = arr != null && (bool)arr[0];
                string dest = arr != null ? Convert.ToString(arr[1]) : "";
                string err = arr != null ? Convert.ToString(arr[2]) : "";
                WsSetStatus(ok ? L.T("下载完成并已复制到：") + dest : L.T("下载失败：") + err);
                if (ok) Toast(L.T("下载完成：") + it.Title);
            };
            dl.RunWorkerAsync();
        }

        void WsOpenPage()
        {
            WorkshopItem it = _wsList.SelectedItem;
            if (it == null) { WsSetStatus(L.T("先在列表里点一个模组。")); return; }
            string err;
            if (!WorkshopClient.OpenPage(it.Id, out err)) WsSetStatus(L.T("打开页面失败：") + err);
        }

        private void OpenWorkshop()
        {
            OpenOverlay(_wsEls, LayoutWorkshopOverlay, new[] { _wsInput }); // 并轨为内嵌模态覆盖层（原 WorkshopForm 保留编译，不再使用）
        }

        // =================================================================
        //  AI 接口配置覆盖层（原 AiSetupForm 的并轨版）
        // =================================================================
        void BuildAiOverlay()
        {
            _aiPanel = AddEl(_aiEls, new ElCard { Radius = 12 });
            _aiTitle = AddEl(_aiEls, new ElText(L.T("AI 诊断 · 接口配置")) { FontOf = () => T.B(15) });
            _aiBtnClose = AddEl(_aiEls, new ElBtn(L.T("关闭")));
            _aiBtnClose.Clicked += delegate { CloseOverlaySmart(); };
            _aiNote = AddEl(_aiEls, new ElText(
                L.T("支持任何「OpenAI 兼容」接口：官方 api.openai.com、各类中转站、本地 ollama / LM Studio 都行。地址填到 /v1 这一层即可。"))
                { FontOf = () => T.F(11.5f), ColorOf = () => T.Text3 });

            _aiLbBase = AddEl(_aiEls, new ElText(L.T("API 地址")) { FontOf = () => T.F(12), ColorOf = () => T.Text2 });
            _aiBase = AddEl(_aiEls, new ElInput());
            _aiLbKey = AddEl(_aiEls, new ElText("API KEY") { FontOf = () => T.F(12), ColorOf = () => T.Text2 });
            _aiKey = AddEl(_aiEls, new ElInput { Password = true });
            _aiLbModel = AddEl(_aiEls, new ElText(L.T("模型")) { FontOf = () => T.F(12), ColorOf = () => T.Text2 });
            _aiModel = AddEl(_aiEls, new ElInput());
            _aiBtnFetch = AddEl(_aiEls, new ElBtn(L.T("获取模型列表")));
            _aiBtnFetch.Clicked += delegate { AiFetch(); };

            _aiModels = AddEl(_aiEls, new ElStrList { EmptyHint = L.T("点「获取模型列表」拉取可用模型，也可以手动填写。") });
            _aiModels.SelectionChanged += delegate
            {
                string m = _aiModels.SelectedItem;
                if (m != null) _aiModel.Text = m;
                Render();
            };
            _aiStatus = AddEl(_aiEls, new ElText(L.T("先填地址和 KEY，点【获取模型列表】验证能不能连上。"))
                { FontOf = () => T.F(11.5f), ColorOf = () => T.Text3, Ellipsis = true });
            _aiTip = AddEl(_aiEls, new ElText(
                L.T("★「呼叫苏苏 AI 诊断」会把「启动器日志 + 游戏日志里的报错 + 本机环境」发给这个接口做分析；不想上传就不配置，用本地体检。"))
                { FontOf = () => T.F(10.5f), ColorOf = () => T.Text3 });

            _aiBtnCancel = AddEl(_aiEls, new ElBtn(L.T("取消")));
            _aiBtnCancel.Clicked += delegate { CloseOverlaySmart(); };
            _aiBtnSave = AddEl(_aiEls, new ElBtn(L.T("保存")) { Primary = true });
            _aiBtnSave.Clicked += delegate { AiSave(); };
        }

        void LayoutAiOverlay()
        {
            int pw = 620, ph = 470;
            int px = (Lw - pw) / 2, py = Math.Max(52, (Lh - ph) / 2);
            _aiPanel.SetBounds(px, py, pw, ph);
            int m = 20;
            int ix = px + m, iw = pw - m * 2;

            _aiTitle.SetBounds(ix, py + 14, 320, 24);
            _aiBtnClose.SetBounds(px + pw - m - 64, py + 14, 64, 30);
            _aiNote.SetBounds(ix, py + 44, iw - 80, 34); // 文案较长会折成两行，给足两行高度避免上下被裁

            int fx = ix + 76, fw = iw - 76;
            _aiLbBase.SetBounds(ix, py + 82, 70, 30);
            _aiBase.SetBounds(fx, py + 82, fw, 30);
            _aiLbKey.SetBounds(ix, py + 124, 70, 30);
            _aiKey.SetBounds(fx, py + 124, fw, 30);
            _aiLbModel.SetBounds(ix, py + 166, 70, 30);
            _aiModel.SetBounds(fx, py + 166, fw - 134, 30);
            _aiBtnFetch.SetBounds(px + pw - m - 124, py + 166, 124, 30);

            int ly = py + 208;
            int botY = py + ph - m - 36;
            _aiModels.SetBounds(ix, ly, iw, Math.Max(60, botY - 66 - ly));
            _aiStatus.SetBounds(ix, botY - 58, iw, 20);
            _aiTip.SetBounds(ix, botY - 36, iw, 34); // 底部提示同样会折两行，给足高度防裁切

            int bx = px + pw - m;
            bx -= 112; _aiBtnSave.SetBounds(bx, botY, 112, 36);
            bx -= 8 + 90; _aiBtnCancel.SetBounds(bx, botY, 90, 36);
        }

        /// <summary>fromWs=true 表示从创意工坊覆盖层里点进来（关掉后自动回去）</summary>
        void OpenAiSetup(bool fromWs)
        {
            _aiReturnWs = fromWs;
            _aiBase.Text = string.IsNullOrEmpty(_st.ApiBase) ? "https://api.openai.com/v1" : _st.ApiBase;
            _aiKey.Text = _st.ApiKey == null ? "" : _st.ApiKey;
            _aiModel.Text = _st.ApiModel == null ? "" : _st.ApiModel;
            _aiModels.SetItems(null);
            _aiStatus.Text = L.T("先填地址和 KEY，点【获取模型列表】验证能不能连上。");
            OpenOverlay(_aiEls, LayoutAiOverlay, new[] { _aiBase, _aiKey, _aiModel });
        }

        void AiSetBusy(bool busy, string text)
        {
            _aiBusy = busy;
            _aiBtnFetch.Enabled = !busy;
            _aiBtnSave.Enabled = !busy;
            if (!string.IsNullOrEmpty(text)) _aiStatus.Text = text;
            Render();
        }

        void AiFetch()
        {
            if (_aiBusy) return;
            string apiBase = _aiBase.Text, key = _aiKey.Text.Trim();
            AiSetBusy(true, L.T("正在连接 ") + AiClient.Normalize(apiBase) + " …");
            var w = new BackgroundWorker();
            w.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                string err;
                List<string> models = AiClient.ListModels(apiBase, key, out err);
                e.Result = new object[] { models, err };
            };
            w.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                AiSetBusy(false, "");
                object[] arr = e.Result as object[];
                List<string> models = arr == null ? null : arr[0] as List<string>;
                string err = arr == null ? null : arr[1] as string;
                if (err != null || models == null || models.Count == 0)
                {
                    _aiStatus.Text = L.T("连接失败：") + (err == null ? L.T("没有可用模型") : err);
                    Render();
                    return;
                }
                _aiModels.SetItems(models);
                if (string.IsNullOrEmpty(_aiModel.Text.Trim())) _aiModel.Text = models[0];
                _aiStatus.Text = L.T("连接成功，共 ") + models.Count + L.T(" 个模型。在列表里选一个，再点【保存】。");
                Render();
            };
            w.RunWorkerAsync();
        }

        void AiSave()
        {
            if (_aiBusy) return;
            string b = AiClient.Normalize(_aiBase.Text);
            if (b.Length == 0) { _aiStatus.Text = L.T("API 地址不能为空。"); Render(); return; }
            if (string.IsNullOrEmpty(_aiModel.Text.Trim())) { _aiStatus.Text = L.T("请先点【获取模型列表】并选一个模型。"); Render(); return; }
            _st.ApiBase = b;
            _st.ApiKey = _aiKey.Text.Trim();
            _st.ApiModel = _aiModel.Text.Trim();
            _st.Save();
            Toast(L.T("AI 接口已保存：") + _st.ApiBase + " / " + _st.ApiModel);
            CloseOverlaySmart();
        }

        // =================================================================
        //  整合包资源获取覆盖层（原 PackFeedForm 的并轨版：卡片墙 + 详情 + 提交/导入）
        // =================================================================
        void BuildPackFeedOverlay()
        {
            _pfPanel = AddEl(_pfEls, new ElCard { Radius = 12 });
            _pfTitle = AddEl(_pfEls, new ElText(L.T("整合包资源获取")) { FontOf = () => T.B(15) });
            _pfBtnClose = AddEl(_pfEls, new ElBtn(L.T("关闭")));
            _pfBtnClose.Clicked += delegate { CloseOverlay(); };
            _pfBtnSubmit = AddEl(_pfEls, new ElBtn(L.T("提交我的整合包")));
            _pfBtnSubmit.Clicked += delegate { PfShowSub("submit"); };
            _pfBtnImport = AddEl(_pfEls, new ElBtn(L.T("导入表格")));
            _pfBtnImport.Clicked += delegate { PfShowSub("import"); };
            _pfBtnRefresh = AddEl(_pfEls, new ElBtn(L.T("刷新")) { Primary = true });
            _pfBtnRefresh.Clicked += delegate { PfReload(true); };
            _pfBtnView = AddEl(_pfEls, new ElBtn(L.T("紧凑列表"))); // 切换 卡片墙 ⇄ 紧凑列表（无封面，快速浏览）
            _pfBtnView.Clicked += delegate
            {
                _pfGrid.Compact = !_pfGrid.Compact;
                _pfBtnView.Text = _pfGrid.Compact ? L.T("卡片视图") : L.T("紧凑列表");
                Render();
            };

            _pfSourceLb = AddEl(_pfEls, new ElText(L.T("数据源")) { FontOf = () => T.F(12), ColorOf = () => T.Text2 });
            _pfSource = AddEl(_pfEls, new ElInput());
            _pfBtnSaveSrc = AddEl(_pfEls, new ElBtn(L.T("保存地址")));
            _pfBtnSaveSrc.Clicked += delegate { PfSaveSource(); };

            _pfGrid = AddEl(_pfEls, new ElPackGrid { EmptyHint = L.T("还没有数据。点「刷新」在线获取，或点「导入表格」。") });
            _pfGrid.CardClicked += delegate(object s, EventArgs e)
            {
                PackItem it = s as PackItem;
                if (it != null) PfOpenDetail(it);
            };

            _pfNotice = AddEl(_pfEls, new ElText(
                L.T("【学习版资源声明】所列整合包均由第三方网盘提供，仅供学习交流；请于下载后 24 小时内删除，切勿商用。喜欢请支持正版。"))
                { FontOf = () => T.F(11), ColorOf = () => T.WarnText });
            _pfStatus = AddEl(_pfEls, new ElText("") { FontOf = () => T.F(11.5f), ColorOf = () => T.Text3, Ellipsis = true });

            // —— 详情视图（默认隐藏）——
            _pfDName = AddEl(_pfEls, new ElText("") { FontOf = () => T.B(15), Visible = false, Ellipsis = true });
            _pfBtnBack = AddEl(_pfEls, new ElBtn(L.T("‹ 返回列表")) { Visible = false });
            _pfBtnBack.Clicked += delegate { PfShowDetail(false); };
            _pfCover = AddEl(_pfEls, new ElCover { Visible = false });
            _pfDBody = AddEl(_pfEls, new ElPara { Visible = false });
            _pfBtnQuark = AddEl(_pfEls, new ElBtn(L.T("夸克网盘下载")) { Primary = true, Visible = false });
            _pfBtnQuark.Clicked += delegate { PfOpenLink(_pfCurrent == null ? "" : _pfCurrent.QuarkUrl, L.T("夸克网盘")); };
            _pfBtnXunlei = AddEl(_pfEls, new ElBtn(L.T("迅雷云盘下载")) { Primary = true, Visible = false });
            _pfBtnXunlei.Clicked += delegate { PfOpenLink(_pfCurrent == null ? "" : _pfCurrent.XunleiUrl, L.T("迅雷云盘")); };

            // —— 子面板（提交 / 导入共用一套元素，默认隐藏；加在列表最后=渲染与命中都在最顶层）——
            _pfSubDim = AddEl(_pfEls, new ElDim { Visible = false });
            _pfSubDim.Clicked += delegate { PfShowSub(null); };
            _pfSubPanel = AddEl(_pfEls, new ElCard { Radius = 12, Visible = false });
            _pfSubTitle = AddEl(_pfEls, new ElText("") { FontOf = () => T.B(14), Visible = false });
            _pfSubTip = AddEl(_pfEls, new ElText("") { FontOf = () => T.F(11), ColorOf = () => T.Text3, Visible = false });
            _pfLbName = AddEl(_pfEls, new ElText(L.T("整合包名称")) { FontOf = () => T.F(12), ColorOf = () => T.Text2, Visible = false });
            _pfLbContact = AddEl(_pfEls, new ElText(L.T("联系方式")) { FontOf = () => T.F(12), ColorOf = () => T.Text2, Visible = false });
            _pfLbNote = AddEl(_pfEls, new ElText(L.T("补充说明")) { FontOf = () => T.F(12), ColorOf = () => T.Text2, Visible = false });
            _pfSubName = AddEl(_pfEls, new ElInput { Visible = false });
            _pfSubContact = AddEl(_pfEls, new ElInput { Visible = false });
            _pfSubNote = AddEl(_pfEls, new ElInput { Visible = false });
            _pfSubStatus = AddEl(_pfEls, new ElText("") { FontOf = () => T.F(11), ColorOf = () => T.WarnText, Visible = false, Ellipsis = true });
            _pfSubOk = AddEl(_pfEls, new ElBtn("") { Primary = true, Visible = false });
            _pfSubOk.Clicked += delegate
            {
                if (_pfSubMode == "submit") PfDoSubmit();
                else if (_pfSubMode == "import") PfImportClipboard();
            };
            _pfSubFile = AddEl(_pfEls, new ElBtn(L.T("打开 CSV 文件…")) { Visible = false });
            _pfSubFile.Clicked += delegate { PfImportCsvFile(); };
            _pfSubCancel = AddEl(_pfEls, new ElBtn(L.T("取消")) { Visible = false });
            _pfSubCancel.Clicked += delegate { PfShowSub(null); };
        }

        void LayoutPackFeedOverlay()
        {
            int px = 28, py = 50; // 上下拉大：底部伸到 Lh-8，盖住主页底部一排目录按钮
            int pw = Lw - 56, ph = Lh - 58;
            if (pw < 400 || ph < 300) return;
            _pfPanel.SetBounds(px, py, pw, ph);
            int m = 20;
            int ix = px + m, iw = pw - m * 2;

            _pfTitle.SetBounds(ix, py + 14, 300, 24);
            int tx = px + pw - m;
            tx -= 64; _pfBtnClose.SetBounds(tx, py + 14, 64, 30); tx -= 8;
            tx -= 76; _pfBtnRefresh.SetBounds(tx, py + 14, 76, 30); tx -= 8;
            tx -= 84; _pfBtnView.SetBounds(tx, py + 14, 84, 30); tx -= 8;
            tx -= 100; _pfBtnImport.SetBounds(tx, py + 14, 100, 30); tx -= 8;
            tx -= 140; _pfBtnSubmit.SetBounds(tx, py + 14, 140, 30);

            _pfSourceLb.SetBounds(ix, py + 52, 50, 28);
            _pfSource.SetBounds(ix + 56, py + 52, iw - 56 - 104, 28);
            _pfBtnSaveSrc.SetBounds(px + pw - m - 96, py + 52, 96, 28);

            int gy = py + 92;
            _pfGrid.SetBounds(ix, gy, iw, Math.Max(80, py + ph - m - 48 - gy));
            _pfNotice.SetBounds(ix, py + ph - m - 40, iw, 18);
            _pfStatus.SetBounds(ix, py + ph - m - 18, iw, 18);

            // 详情视图
            _pfDName.SetBounds(ix, py + 16, iw - 130, 26);
            _pfBtnBack.SetBounds(px + pw - m - 116, py + 14, 116, 30);
            _pfCover.SetBounds(ix, py + 56, 300, 210);
            _pfDBody.SetBounds(ix + 320, py + 56, iw - 320, 260);
            _pfBtnQuark.SetBounds(ix + 320, py + 330, 150, 36);
            _pfBtnXunlei.SetBounds(ix + 320 + 162, py + 330, 150, 36);

            // 子面板（居中 560×高按模式；提交 320 / 导入 210）
            int sw = 560;
            int sh = _pfSubMode == "submit" ? 330 : 220;
            int sx = (Lw - sw) / 2, sy = Math.Max(60, (Lh - sh) / 2);
            _pfSubDim.SetBounds(0, 0, Lw, Lh);
            _pfSubPanel.SetBounds(sx, sy, sw, sh);
            int six = sx + 20, siw = sw - 40;
            _pfSubTitle.SetBounds(six, sy + 14, siw, 24);
            _pfSubTip.SetBounds(six, sy + 44, siw, 40);
            if (_pfSubMode == "submit")
            {
                _pfLbName.SetBounds(six, sy + 96, 90, 28);
                _pfSubName.SetBounds(six + 96, sy + 96, siw - 96, 28);
                _pfLbContact.SetBounds(six, sy + 134, 90, 28);
                _pfSubContact.SetBounds(six + 96, sy + 134, siw - 96, 28);
                _pfLbNote.SetBounds(six, sy + 172, 90, 28);
                _pfSubNote.SetBounds(six + 96, sy + 172, siw - 96, 28);
                _pfSubStatus.SetBounds(six, sy + 210, siw, 20);
            }
            else
            {
                _pfSubStatus.SetBounds(six, sy + 92, siw, 20);
            }
            int by = sy + sh - 20 - 36;
            _pfSubCancel.SetBounds(sx + sw - 20 - 90, by, 90, 36);
            _pfSubFile.SetBounds(sx + sw - 20 - 90 - 8 - 150, by, 150, 36);
            _pfSubOk.SetBounds(sx + 20, by, 150, 36);
        }

        // —— 视图切换 ——
        void PfShowDetail(bool show)
        {
            _pfDetailOn = show;
            El[] det = { _pfDName, _pfBtnBack, _pfCover, _pfDBody, _pfBtnQuark, _pfBtnXunlei };
            foreach (var el in det) el.Visible = show;
            // 列表视图互斥
            _pfGrid.Visible = !show;
            _pfSourceLb.Visible = !show;
            _pfSource.Visible = !show;
            _pfBtnSaveSrc.Visible = !show;
            _pfBtnRefresh.Visible = !show;
            _pfBtnImport.Visible = !show;
            _pfBtnSubmit.Visible = !show;
            _pfBtnView.Visible = !show;
            _pfTitle.Visible = !show;
            if (!show) _pfCurrent = null;
            Render();
        }

        void PfShowSub(string mode)
        {
            _pfSubMode = mode;
            bool on = mode != null;
            bool submit = mode == "submit";
            El[] sub = { _pfSubDim, _pfSubPanel, _pfSubTitle, _pfSubTip, _pfSubStatus, _pfSubOk, _pfSubCancel };
            foreach (var el in sub) el.Visible = on;
            foreach (var el in new El[] { _pfLbName, _pfLbContact, _pfLbNote, _pfSubName, _pfSubContact, _pfSubNote })
                el.Visible = on && submit;
            _pfSubFile.Visible = on && !submit;
            if (on)
            {
                SetFocusInput(null);
                if (submit)
                {
                    string ctxErr = (_ctx == null || string.IsNullOrEmpty(_ctx.Root) || !Directory.Exists(_ctx.Root))
                        ? L.T("没有检测到整合包目录，无法生成提交包。") : null;
                    _pfSubTitle.Text = L.T("提交我的整合包（生成提交包）");
                    _pfSubTip.Text = ctxErr == null
                        ? L.T("会打包：Config 目录里的配置 + ModsConfig.xml（模组排序）+ 一份模组清单，不含模组文件本身。生成后请在打开的网页里把 zip 拖进去提交。")
                        : ctxErr;
                    _pfSubName.Text = (_ctx == null || string.IsNullOrEmpty(_ctx.Root))
                        ? L.T("我的整合包") : new DirectoryInfo(_ctx.Root).Name;
                    _pfSubContact.Text = "";
                    _pfSubNote.Text = "";
                    _pfSubStatus.Text = "";
                    _pfSubOk.Text = L.T("生成提交包");
                    _pfSubOk.Enabled = ctxErr == null;
                    _ovlInputs = new[] { _pfSubName, _pfSubContact, _pfSubNote };
                }
                else
                {
                    _pfSubTitle.Text = L.T("导入整合包表格");
                    _pfSubTip.Text = L.T("在在线表格里全选数据行（含表头）复制，然后点「从剪贴板导入」；或点「打开 CSV 文件…」。要求第一列是整合包名称。");
                    _pfSubStatus.Text = "";
                    _pfSubOk.Text = L.T("从剪贴板导入");
                    _pfSubOk.Enabled = true;
                    _ovlInputs = null;
                }
            }
            else
            {
                _ovlInputs = new[] { _pfSource };
            }
            LayoutPackFeedOverlay();
            Render();
        }

        // —— 打开 / 数据源 / 刷新 ——
        private void OpenPackFeed()
        {
            Log.Info(L.T("打开整合包资源获取"));
            if (string.IsNullOrEmpty(_st.PackFeedUrl)) { _st.PackFeedUrl = PackFeed.DefaultSource; try { _st.Save(); } catch { } }
            _pfSource.Text = _st.PackFeedUrl;
            PfShowDetail(false);
            PfShowSub(null);
            OpenOverlay(_pfEls, LayoutPackFeedOverlay, new[] { _pfSource });
            PfReload(true); // 每次打开都尝试在线刷新，失败回退缓存
        }

        void PfSaveSource()
        {
            _st.PackFeedUrl = _pfSource.Text == null ? "" : _pfSource.Text.Trim();
            _st.Save();
            PfSetStatus(L.T("数据源地址已保存。点「刷新」重新获取。"), false);
        }

        void PfSetBusy(bool busy, string text)
        {
            _pfBusy = busy;
            _pfBtnRefresh.Enabled = !busy;
            _pfBtnImport.Enabled = !busy;
            _pfBtnSubmit.Enabled = !busy;
            if (!string.IsNullOrEmpty(text)) _pfStatus.Text = text;
            Render();
        }

        void PfSetStatus(string s, bool warn)
        {
            _pfStatus.Text = s;
            _pfStatus.ColorOf = warn ? (Func<Color>)(() => T.WarnText) : (() => T.Text3);
            Render();
        }

        void PfReload(bool tryOnline)
        {
            if (_pfBusy) return;
            string url = _pfSource.Text == null ? "" : _pfSource.Text.Trim();
            if (string.IsNullOrEmpty(url)) { url = PackFeed.DefaultSource; _pfSource.Text = url; }
            PfSetBusy(true, tryOnline ? L.T("正在获取整合包资源表…") : L.T("正在读取缓存…"));
            var bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                List<PackItem> items = null;
                string err = null;
                string usedUrl = null;
                bool isDefault = string.IsNullOrEmpty(url) || PackFeed.IsKnownSource(url);
                if (tryOnline)
                {
                    if (isDefault) items = PackFeed.FetchPublic(out err, out usedUrl);
                    else items = PackFeed.TryFetch(url, out err);
                }
                string at;
                if (items == null || items.Count == 0)
                {
                    List<PackItem> cached = PackFeed.LoadCache(out at);
                    e.Result = new object[] { cached, err, at, true, usedUrl };
                }
                else
                {
                    PackFeed.SaveCache(items, url);
                    e.Result = new object[] { items, null, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), false, usedUrl };
                }
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                PfSetBusy(false, "");
                object[] arr = e.Result as object[];
                List<PackItem> items = arr == null ? null : arr[0] as List<PackItem>;
                string err = arr == null ? "" : Convert.ToString(arr[1]);
                string at = arr == null ? "" : Convert.ToString(arr[2]);
                bool fromCache = arr != null && Convert.ToBoolean(arr[3]);
                string usedUrl = (arr == null || arr.Length < 5) ? "" : Convert.ToString(arr[4]);

                _pfItems = items == null ? new List<PackItem>() : items;
                _pfGrid.SetItems(_pfItems);
                PfEnsureSharedCover();
                PfLoadCovers();

                if (_pfItems.Count == 0)
                    PfSetStatus(L.T("还没有数据。") + (string.IsNullOrEmpty(err) ? "" : "（" + err + "）") + L.T("  请点「导入表格」粘贴或导入 CSV。"), true);
                else if (fromCache)
                    PfSetStatus(L.T("共 ") + _pfItems.Count + L.T(" 个整合包 · 数据更新于 ") + (string.IsNullOrEmpty(at) ? L.T("未知") : at)
                        + (string.IsNullOrEmpty(err) ? "" : L.T(" · 在线获取失败：") + err), true);
                else
                    PfSetStatus(L.T("共 ") + _pfItems.Count + L.T(" 个整合包 · 已更新 ") + at
                        + (string.IsNullOrEmpty(usedUrl) ? "" : L.T(" · 来源 ") + PfSourceName(usedUrl)), false);
            };
            bw.RunWorkerAsync();
        }

        // —— 封面图（异步下载，完成后重绘；覆盖层已关也安全，缓存留着下次用）——
        void PfEnsureSharedCover()
        {
            if (_pfSharedCover != null || _pfSharedLoading) return;
            _pfSharedLoading = true;
            var bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e) { e.Result = PackFeed.DownloadDefaultCover(); };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                _pfSharedLoading = false;
                string path = e.Result as string;
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (Image tmp = Image.FromStream(fs))
                        _pfSharedCover = new Bitmap(tmp);
                    _pfGrid.SharedCover = _pfSharedCover;
                    Render();
                }
                catch { }
            };
            bw.RunWorkerAsync();
        }

        void PfLoadCovers()
        {
            foreach (var it in _pfItems)
            {
                if (it == null || !PackFeed.LooksLikeUrl(it.Cover)) continue;
                string url = it.Cover;
                var bw = new BackgroundWorker();
                bw.DoWork += delegate(object s, DoWorkEventArgs e) { e.Result = PackFeed.DownloadCover(url); };
                bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
                {
                    string path = e.Result as string;
                    if (string.IsNullOrEmpty(path)) return;
                    try
                    {
                        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (Image tmp = Image.FromStream(fs))
                        {
                            var img = new Bitmap(tmp);
                            _pfGrid.SetCover(url, img);
                            // 详情页正好开着这张卡 → 同步换封面
                            if (_pfDetailOn && _pfCurrent != null && _pfCurrent.Cover == url) _pfCover.Img = img;
                        }
                    }
                    catch { }
                };
                bw.RunWorkerAsync();
            }
        }

        // —— 详情 ——
        void PfOpenDetail(PackItem it)
        {
            _pfCurrent = it;
            _pfDName.Text = it.Name;
            _pfDBody.Text = PfDetailText(it);
            _pfBtnQuark.Enabled = it.QuarkOk;
            _pfBtnQuark.Text = it.QuarkOk ? L.T("夸克网盘下载") : L.T("夸克网盘（维护中）");
            _pfBtnXunlei.Enabled = it.XunleiOk;
            _pfBtnXunlei.Text = it.XunleiOk ? L.T("迅雷云盘下载") : L.T("迅雷云盘（维护中）");
            Image img = _pfGrid.GetCover(it.Cover);
            _pfCover.Img = img != null ? img : _pfSharedCover;
            _pfCover.PlaceholderName = it.Name;
            PfShowDetail(true);
            if (img == null && PackFeed.LooksLikeUrl(it.Cover)) PfLoadCovers(); // 补拉
        }

        static string PfShow(string s) { return string.IsNullOrEmpty(s) ? "—" : s; }

        static string PfDetailText(PackItem p)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.T("本体版本：") + PfShow(p.GameVersion));
            sb.AppendLine(L.T("MOD 数量：") + PfShow(p.ModCount));
            sb.AppendLine(L.T("副本数量：") + PfShow(p.CopyCount));
            sb.AppendLine(L.T("讲述者数：") + PfShow(p.Storytellers));
            sb.AppendLine(L.T("预计加载：") + PfShow(p.LoadTime));
            sb.AppendLine(L.T("最高 FPS：") + PfShow(p.MaxFps));
            sb.AppendLine(L.T("RJW 内容：") + PfShow(p.Rjw));
            sb.AppendLine(L.T("压缩包：") + PfShow(p.ZipSize));
            sb.AppendLine(L.T("解压大小：") + PfShow(p.UnzippedSize));
            sb.AppendLine("");
            sb.Append(L.T("简介：") + (string.IsNullOrEmpty(p.Description) ? L.T("暂无") : p.Description));
            return sb.ToString();
        }

        static string PfSourceName(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            if (url.IndexOf("ghproxy", StringComparison.OrdinalIgnoreCase) >= 0) return L.T("ghproxy 加速");
            if (url.IndexOf("statically", StringComparison.OrdinalIgnoreCase) >= 0) return L.T("statically 镜像");
            if (url.IndexOf("jsdelivr", StringComparison.OrdinalIgnoreCase) >= 0) return "jsDelivr";
            if (url.IndexOf("raw.githubusercontent", StringComparison.OrdinalIgnoreCase) >= 0) return "GitHub raw";
            return L.T("自定义");
        }

        void PfOpenLink(string url, string what)
        {
            if (!PackFeed.LooksLikeUrl(url)) { PfSetStatus(what + L.T("：这一项没有填下载链接。"), true); return; }
            try
            {
                ShellOpen.Url(url.Trim());
                PfSetStatus(L.T("已用浏览器打开") + what + L.T("链接。"), false);
            }
            catch
            {
                MinMsg.Show(this, L.T("打开浏览器失败，请手动访问：\r\n\r\n") + url, what, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // —— 提交整合包 ——
        void PfDoSubmit()
        {
            string err;
            PackSubmit.Result r = PackSubmit.Build(_ctx, _st, _pfSubName.Text.Trim(), _pfSubContact.Text.Trim(), _pfSubNote.Text.Trim(), out err);
            if (r == null)
            {
                _pfSubStatus.Text = L.T("生成失败：") + (err == null ? L.T("未知原因") : err);
                Render();
                return;
            }
            PfShowSub(null);
            try
            {
                string msg = L.T("提交包已生成：\r\n\r\n") + r.ZipPath + "\r\n\r\n"
                    + L.T("包含 ") + r.Files.Count + L.T(" 个文件，模组 ") + r.ModCount + L.T(" 个。\r\n\r\n")
                    + L.T("接下来会打开 GitHub 的提交页面，请把上面这个 zip 拖进输入框再点 Submit。\r\n\r\n")
                    + L.T("如果打不开网页，提交内容已复制到剪贴板，可以自己粘到 Issue 或群里。");
                MinMsg.Show(this, msg, L.T("提交包已生成"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + r.ZipPath + "\""); } catch { }
                try { ShellOpen.Url(r.IssueUrl); }
                catch { try { Clipboard.SetText(r.Body); } catch { } }
                PfSetStatus(L.T("提交包：") + r.ZipPath, false);
            }
            catch (Exception ex)
            {
                PfSetStatus(L.T("生成后处理出错：") + ex.Message, true);
            }
        }

        // —— 导入表格 ——
        void PfImportClipboard()
        {
            string txt;
            try { txt = Clipboard.GetText(); }
            catch { _pfSubStatus.Text = L.T("读剪贴板失败，请重试。"); Render(); return; }
            if (string.IsNullOrEmpty(txt)) { _pfSubStatus.Text = L.T("剪贴板是空的。先在表格里复制数据行。"); Render(); return; }
            PfImportApply(txt);
        }

        void PfImportCsvFile()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = L.T("选择导出的 CSV 文件");
                ofd.Filter = L.T("CSV 文件|*.csv;*.txt|所有文件|*.*");
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string txt = File.ReadAllText(ofd.FileName, Encoding.UTF8);
                    if (txt.IndexOf(L.T("整合包"), StringComparison.Ordinal) < 0 && txt.IndexOf("MOD", StringComparison.Ordinal) < 0)
                    {
                        try { txt = File.ReadAllText(ofd.FileName, Encoding.Default); } catch { }
                    }
                    PfImportApply(txt);
                }
                catch (Exception ex) { _pfSubStatus.Text = L.T("读取失败：") + ex.Message; Render(); }
            }
        }

        void PfImportApply(string txt)
        {
            List<PackItem> items = PackFeed.ParseTable(txt);
            if (items.Count == 0) { _pfSubStatus.Text = L.T("没解析出数据。检查一下是不是粘到了表格内容。"); Render(); return; }
            _pfItems = items;
            PackFeed.SaveCache(items, L.T("手动导入"));
            PfShowSub(null);
            PfShowDetail(false);
            _pfGrid.SetItems(_pfItems);
            PfEnsureSharedCover();
            PfLoadCovers();
            PfSetStatus(L.T("已导入 ") + items.Count + L.T(" 个整合包（") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "）。", false);
        }

        private void OpenModGraph()
        {
            if (_current == null)
            {
                Toast(L.T("先选一个排序方案再打开模组网状图，已为你跳到排序选择页")); // 窗口内提示条，不再弹独立对话框
                SwitchPage(1);
                return;
            }
            // 并轨为内嵌模态覆盖层（原 ModGraphForm 保留编译，不再使用）
            OpenOverlay(_mgEls, LayoutModGraphOverlay, new[] { _mgSearch });
            MgBuildGraphAsync(null, L.T("正在扫描本地模组并生成网状图…"));
        }

        // =================================================================
        //  模组管理覆盖层（原 ModGraphForm 的并轨版：依赖网状图 + 排序列表）
        // =================================================================
        void BuildModGraphOverlay()
        {
            _mgPanel = AddEl(_mgEls, new ElCard { Radius = 12 });
            _mgTitle = AddEl(_mgEls, new ElText(L.T("模组管理 / 依赖网状图")) { FontOf = () => T.B(15) });
            _mgBtnClose = AddEl(_mgEls, new ElBtn(L.T("关闭")));
            _mgBtnClose.Clicked += delegate { CloseOverlay(); };
            _mgBtnDetect = AddEl(_mgEls, new ElBtn(L.T("AI 检测并启动")));
            _mgBtnDetect.Clicked += delegate { MgDetectAndLaunch(); };
            _mgBtnClassify = AddEl(_mgEls, new ElBtn(L.T("AI 分类并生成网状图")) { Primary = true });
            _mgBtnClassify.Clicked += delegate { MgClassify(); };

            _mgHint = AddEl(_mgEls, new ElText(
                L.T("滚轮缩放 · 拖拽平移 · 左键节点详情 · 右键节点启停 · 红色节点表示前置被关闭 · 放大后显示模组缩略图"))
                { FontOf = () => T.F(11), ColorOf = () => T.Text3 });

            _mgSearch = AddEl(_mgEls, new ElInput());
            _mgBtnSearch = AddEl(_mgEls, new ElBtn(L.T("搜索模组")));
            _mgBtnSearch.Clicked += delegate { MgSearchMods(); };
            _mgChkTrans = AddEl(_mgEls, new ElCheck(L.T("中文自动转英文"), true));

            _mgGraph = AddEl(_mgEls, new ElModGraph());
            _mgGraph.NodeSelected += delegate { MgOpenDetail(); };
            _mgGraph.NodeToggled += delegate { MgFillModList(); MgShowWarnings(); };

            _mgListTitle = AddEl(_mgEls, new ElText(L.T("排序列表")) { FontOf = () => T.B(12), ColorOf = () => T.Accent });
            _mgList = AddEl(_mgEls, new ElModList());
            _mgList.RowChosen += delegate
            {
                if (_mgList.RowAtNode != null) _mgGraph.FocusNode(_mgList.RowAtNode.Id, true);
            };
            _mgList.NodeToggled += delegate
            {
                ModGraphBuilder.RecomputeWarnings(_mgGraph.Data);
                _mgGraph.Redraw2();
                MgUpdateListTitle();
                MgShowWarnings();
            };

            _mgStatus = AddEl(_mgEls, new ElText("") { FontOf = () => T.F(11.5f), ColorOf = () => T.Text3, Ellipsis = true });
            _mgBtnTranslate = AddEl(_mgEls, new ElBtn(L.T("翻译模组名")));
            _mgBtnTranslate.Clicked += delegate { MgToggleTranslate(); };
            _mgBtnIgnore = AddEl(_mgEls, new ElBtn(L.T("无视风险启动")));
            _mgBtnIgnore.Clicked += delegate
            {
                if (MgSaveScheme(false))
                {
                    CloseOverlayInstant();
                    ReloadSchemes(true);
                    StartGame();
                }
            };
            _mgBtnSave = AddEl(_mgEls, new ElBtn(L.T("信任此排序方案并保存")) { Primary = true });
            _mgBtnSave.Clicked += delegate
            {
                if (MgSaveScheme(true))
                {
                    CloseOverlayInstant();
                    ReloadSchemes(true);
                }
            };

            // 搜索结果下拉（加在最后 = 渲染与命中都压在最上层；点外面由 _mgSubDim 收掉）
            _mgSubDim = AddEl(_mgEls, new ElDim { Visible = false, Alpha = 0.12f });
            _mgSubDim.Clicked += delegate { MgHideMatches(); };
            _mgMatches = AddEl(_mgEls, new ElStrList { Visible = false });
            _mgMatches.SelectionChanged += delegate
            {
                string sel = _mgMatches.SelectedItem;
                if (sel == null) return;
                int bar = sel.IndexOf("  |  ");
                string id = bar >= 0 ? sel.Substring(bar + 5).Trim() : sel.Trim();
                MgHideMatches();
                _mgGraph.FocusNode(id, true);
                _mgList.SelectedId = id;
                MgSetStatus(L.T("已跳转到：") + sel, false);
                _mgList.Redraw2();
            };
        }

        void LayoutModGraphOverlay()
        {
            int px = 20, py = 52;
            int pw = Lw - 40, ph = Lh - 80;
            if (pw < 500 || ph < 360) return;
            _mgPanel.SetBounds(px, py, pw, ph);
            int m = 16;
            int ix = px + m, iw = pw - m * 2;

            _mgTitle.SetBounds(ix, py + 12, 300, 24);
            int tx = px + pw - m;
            tx -= 64; _mgBtnClose.SetBounds(tx, py + 10, 64, 30); tx -= 8;
            tx -= 150; _mgBtnDetect.SetBounds(tx, py + 10, 150, 30); tx -= 8;
            tx -= 190; _mgBtnClassify.SetBounds(tx, py + 10, 190, 30);

            _mgHint.SetBounds(ix, py + 44, iw, 18);

            int sy = py + 68;
            _mgSearch.SetBounds(ix, sy, 260, 28);
            _mgBtnSearch.SetBounds(ix + 268, sy, 90, 28);
            _mgChkTrans.SetBounds(ix + 366, sy + 1, 150, 26);

            int by = py + ph - m - 36;
            int top = py + 106;
            int bot = by - 40;
            int listW = 300;
            _mgGraph.SetBounds(ix, top, iw - listW - 8, Math.Max(160, bot - top));
            int lx = ix + iw - listW;
            _mgListTitle.SetBounds(lx, top, listW, 20);
            _mgList.SetBounds(lx, top + 24, listW, Math.Max(120, bot - top - 24));

            _mgStatus.SetBounds(ix, by - 28, iw, 20);
            _mgBtnTranslate.SetBounds(ix, by, 140, 36);
            int bx = px + pw - m;
            bx -= 220; _mgBtnSave.SetBounds(bx, by, 220, 36);
            bx -= 8 + 150; _mgBtnIgnore.SetBounds(bx, by, 150, 36);

            // 搜索下拉挂在搜索框下面（宽度盖过搜索框+按钮）
            _mgSubDim.SetBounds(0, 0, Lw, Lh);
            _mgMatches.SetBounds(ix, sy + 32, 358, 220);
        }

        void MgHideMatches()
        {
            _mgMatches.Visible = false;
            _mgSubDim.Visible = false;
            Render();
        }

        void MgSetStatus(string s, bool warn)
        {
            _mgStatus.Text = s;
            _mgStatus.ColorOf = warn ? (Func<Color>)(() => T.WarnText) : (() => T.Text3);
            Render();
        }

        void MgSetBusy(bool busy, string text)
        {
            _mgBusy = busy;
            _mgBtnClassify.Enabled = !busy;
            _mgBtnDetect.Enabled = !busy;
            _mgBtnSave.Enabled = !busy;
            _mgBtnIgnore.Enabled = !busy;
            _mgBtnSearch.Enabled = !busy;
            if (!string.IsNullOrEmpty(text)) _mgStatus.Text = text;
            Render();
        }

        // —— 建图 / 分类 ——
        void MgClassify()
        {
            if (_mgBusy) return;
            MgSetBusy(true, L.T("AI 正在分类模组，模组多时可能需要几十秒…"));
            var bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                string err;
                Dictionary<string, string> cats = ModClassifier.Classify(_ctx, _current, _st, out err);
                e.Result = new object[] { cats, err };
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                MgSetBusy(false, "");
                object[] arr = e.Result as object[];
                Dictionary<string, string> cats = arr == null ? null : arr[0] as Dictionary<string, string>;
                string err = arr == null ? L.T("分类失败") : Convert.ToString(arr[1]);
                MgBuildGraphAsync(cats, L.T("AI 分类完成") + (string.IsNullOrEmpty(err) ? "" : "（" + err + "）"));
            };
            bw.RunWorkerAsync();
        }

        void MgBuildGraphAsync(Dictionary<string, string> cats, string status)
        {
            MgSetBusy(true, status);
            var bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                e.Result = ModGraphBuilder.Build(_ctx, _current, cats);
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                MgSetBusy(false, "");
                ModGraphData data = e.Result as ModGraphData;
                if (data == null || data.Nodes.Count == 0)
                {
                    MgSetStatus(L.T("生成网状图失败，或者当前排序方案里没有模组。"), true);
                    return;
                }
                _mgGraph.SetData(data);
                if (_mgShowZh) MgApplyZh(true); // 重新建图后把中文对照贴回去
                MgSetStatus(status + L.T("  ·  模组 ") + data.Nodes.Count + L.T(" 个  ·  依赖边 ") + data.Edges.Count + L.T(" 条"), false);
                MgShowWarnings();
                MgFillModList();
            };
            bw.RunWorkerAsync();
        }

        void MgShowWarnings()
        {
            if (_mgGraph.Data == null) return;
            ModGraphBuilder.RecomputeWarnings(_mgGraph.Data);
            int n = _mgGraph.Data.Warnings.Count;
            if (n > 0)
                MgSetStatus(L.T("警告：") + n + L.T(" 个模组的前置被关闭。例如：") + _mgGraph.Data.Warnings[0], true);
            else
                MgSetStatus(L.T("依赖检查：当前没有发现前置被关闭的模组。"), false);
            _mgGraph.Redraw2();
        }

        // —— 排序列表 ——
        void MgFillModList()
        {
            if (_mgGraph.Data == null) return;
            List<ModGraphNode> rows = new List<ModGraphNode>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_current != null && _current.Mods != null)
            {
                for (int i = 0; i < _current.Mods.Count; i++)
                {
                    string id = _current.Mods[i] == null ? "" : _current.Mods[i].Trim();
                    if (id.Length == 0 || !seen.Add(id)) continue;
                    ModGraphNode n = _mgGraph.FindNode(id);
                    if (n != null) rows.Add(n);
                }
            }
            for (int i = 0; i < _mgGraph.Data.Nodes.Count; i++)
            {
                ModGraphNode n = _mgGraph.Data.Nodes[i];
                if (seen.Add(n.Id)) rows.Add(n);
            }
            string sel = _mgGraph.SelectedId;
            _mgList.SetRows(rows);
            _mgList.SelectedId = sel;
            MgUpdateListTitle();
        }

        void MgUpdateListTitle()
        {
            if (_mgGraph.Data == null) return;
            int on = 0, total = _mgGraph.Data.Nodes.Count;
            for (int i = 0; i < total; i++) if (_mgGraph.Data.Nodes[i].Enabled) on++;
            _mgListTitle.Text = L.T("排序列表 · 启用 ") + on + L.T(" / 关闭 ") + (total - on);
            Render();
        }

        // —— 中文名对照 ——
        void MgToggleTranslate()
        {
            if (_mgGraph.Data == null || _mgBusy) return;
            if (_mgShowZh)
            {
                _mgShowZh = false;
                MgApplyZh(false);
                _mgBtnTranslate.Text = L.T("翻译模组名");
                MgSetStatus(L.T("已切回英文原名。"), false);
                return;
            }
            ModNameZh.Load();
            List<ModGraphNode> need = new List<ModGraphNode>();
            for (int i = 0; i < _mgGraph.Data.Nodes.Count; i++)
            {
                ModGraphNode n = _mgGraph.Data.Nodes[i];
                if (n == null || string.IsNullOrEmpty(n.Name)) continue;
                if (ModNameZh.HasChinese(n.Name)) continue;
                if (!string.IsNullOrEmpty(ModNameZh.Get(n.Id))) continue;
                need.Add(n);
            }
            if (need.Count == 0)
            {
                _mgShowZh = true;
                MgApplyZh(true);
                _mgBtnTranslate.Text = L.T("隐藏中文译名");
                MgSetStatus(L.T("已显示中文对照（缓存里都有了，不用再翻）。"), false);
                return;
            }
            MgSetBusy(true, L.T("正在翻译 ") + need.Count + L.T(" 个模组名…"));
            ModGraphData data = _mgGraph.Data;
            Settings st = _st;
            var bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                string err;
                int got = ModNameZh.TranslateMissing(data.Nodes, st, null, out err);
                e.Result = new object[] { got, err };
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                MgSetBusy(false, "");
                object[] arr = e.Result as object[];
                int got = arr == null ? 0 : Convert.ToInt32(arr[0]);
                string err = arr == null ? "" : Convert.ToString(arr[1]);
                _mgShowZh = true;
                MgApplyZh(true);
                _mgBtnTranslate.Text = L.T("隐藏中文译名");
                int withZh = 0;
                for (int i = 0; i < _mgGraph.Data.Nodes.Count; i++)
                    if (!string.IsNullOrEmpty(_mgGraph.Data.Nodes[i].NameZh)) withZh++;
                if (!string.IsNullOrEmpty(err))
                    MgSetStatus(L.T("翻译完成：") + withZh + L.T(" 个有中文对照（新增 ") + got + L.T("）。部分失败：") + err, true);
                else
                    MgSetStatus(L.T("翻译完成：新增 ") + got + L.T(" 个，共 ") + withZh + L.T(" 个有中文对照。结果已缓存，下次不用再翻。"), false);
            };
            bw.RunWorkerAsync();
        }

        void MgApplyZh(bool show)
        {
            if (_mgGraph.Data == null) return;
            for (int i = 0; i < _mgGraph.Data.Nodes.Count; i++)
            {
                ModGraphNode n = _mgGraph.Data.Nodes[i];
                if (n != null) n.NameZh = show ? ModNameZh.Get(n.Id) : null;
            }
            _mgGraph.Redraw2();
            MgFillModList();
        }

        // —— 搜索 ——
        void MgSearchMods()
        {
            if (_mgGraph.Data == null) return;
            string q = _mgSearch.Text.Trim();
            if (q.Length == 0) { MgSetStatus(L.T("请输入模组关键词，例如：鼠族、武器、界面。"), true); return; }
            string search = q;
            string terr = null;
            bool usedAi = false;
            if (_mgChkTrans.Checked && MgHasChinese(q))
                search = WorkshopClient.TranslateQuery(_st, q, out usedAi, out terr);

            string[] tokens = search.Split(new char[] { ' ', ',', ';', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) tokens = new string[] { search };

            List<string> matches = new List<string>();
            for (int i = 0; i < _mgGraph.Data.Nodes.Count; i++)
            {
                ModGraphNode n = _mgGraph.Data.Nodes[i];
                string name = n.Name == null ? "" : n.Name;
                string id = n.Id == null ? "" : n.Id;
                bool hit = false;
                for (int t = 0; t < tokens.Length && !hit; t++)
                {
                    string tk = tokens[t].Trim();
                    if (tk.Length < 2) continue;
                    if (name.IndexOf(tk, StringComparison.OrdinalIgnoreCase) >= 0
                        || id.IndexOf(tk, StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
                }
                if (!hit && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
                if (!hit) continue;
                matches.Add(name + "  |  " + id);
                if (matches.Count >= 200) break;
            }
            string msg = L.T("搜索：") + q + (string.IsNullOrEmpty(search) || search == q ? "" : " → " + search)
                + L.T("  找到 ") + matches.Count + L.T(" 个");
            if (usedAi) msg += L.T("  ·  已用 AI 翻译");
            if (!string.IsNullOrEmpty(terr)) msg += "  ·  " + terr;
            MgSetStatus(msg, false);
            if (matches.Count == 0) { MgHideMatches(); return; }
            _mgMatches.SetItems(matches);
            _mgMatches.Visible = true;
            _mgSubDim.Visible = true;
            Render();
        }

        static bool MgHasChinese(string s)
        {
            for (int i = 0; i < s.Length; i++) if (s[i] >= 0x4E00 && s[i] <= 0x9FFF) return true;
            return false;
        }

        // —— 详情（ModDetailForm 已重置为极简分层窗，独立 ShowDialog 弹出）——
        void MgOpenDetail()
        {
            if (_mgGraph.Data == null) return;
            string sel = _mgGraph.SelectedId;
            if (string.IsNullOrEmpty(sel) && _mgList.RowAtNode != null) sel = _mgList.RowAtNode.Id;
            if (string.IsNullOrEmpty(sel)) return;
            ModGraphNode node = _mgGraph.FindNode(sel);
            if (node == null) return;
            using (ModDetailForm f = new ModDetailForm(node, _mgGraph.Data, _st, delegate(bool enabled)
            {
                MgShowWarnings();
                _mgGraph.Redraw2();
                MgFillModList();
            }))
            {
                f.ShowDialog(this);
            }
            MgShowWarnings();
            MgFillModList();
        }

        // —— 保存 / 启动 ——
        SortScheme MgCurrentEnabledScheme()
        {
            SortScheme s = new SortScheme();
            s.Name = (_current == null ? L.T("AI网状排序") : _current.Name + L.T(" · 网状排序"));
            s.Version = _current == null ? "" : _current.Version;
            s.Expansions = _current == null ? new List<string>() : new List<string>(_current.Expansions);
            for (int i = 0; i < _mgGraph.Data.Nodes.Count; i++)
            {
                ModGraphNode n = _mgGraph.Data.Nodes[i];
                if (n.Enabled) s.Mods.Add(n.Id);
            }
            return s;
        }

        bool MgSaveScheme(bool showResult)
        {
            try
            {
                SortScheme temp = MgCurrentEnabledScheme();
                if (temp.Mods.Count == 0) { MinMsg.Show(this, L.T("所有模组都被关闭了。"), L.T("保存失败"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
                ModAnalysisResult analysis = ModAnalyzer.Analyze(_ctx, temp);
                ModAnalyzer.AnalyzePlayerLog(_ctx, temp, analysis);
                string path = ModAnalyzer.WriteSuggestedScheme(_ctx, temp, analysis);
                if (string.IsNullOrEmpty(path)) { MinMsg.Show(this, L.T("写入排序方案失败，详见日志。"), L.T("保存失败"), MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
                _st.LastScheme = path;
                _st.Save();
                if (showResult) Toast(L.T("已保存排序方案，下次启动默认使用它"));
                return true;
            }
            catch (Exception ex)
            {
                MinMsg.Show(this, L.T("保存排序失败：") + ex.Message, L.T("保存失败"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        void MgDetectAndLaunch()
        {
            if (_mgGraph.Data == null || _mgBusy) return;
            SortScheme temp = MgCurrentEnabledScheme();
            if (temp.Mods.Count == 0) { MinMsg.Show(this, L.T("所有模组都被关闭了。"), L.T("无法启动"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            MgSetBusy(true, L.T("AI 正在检测模组依赖与风险…"));
            var bw = new BackgroundWorker();
            bw.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                ModAnalysisResult analysis = ModAnalyzer.Analyze(_ctx, temp);
                ModAnalyzer.AnalyzePlayerLog(_ctx, temp, analysis);
                string aiErr;
                string ai = ModClassifier.AiRiskCheck(_ctx, temp, analysis, _st, out aiErr);
                e.Result = new object[] { analysis, ai, aiErr };
            };
            bw.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                MgSetBusy(false, "");
                object[] arr = e.Result as object[];
                if (arr == null) { MgSetStatus(L.T("检测失败。"), true); return; }
                ModAnalysisResult analysis = arr[0] as ModAnalysisResult;
                string ai = Convert.ToString(arr[1]);
                string aiErr = Convert.ToString(arr[2]);
                bool risky = analysis == null || analysis.HasErrors || analysis.Suspects.Count > 0
                    || (ai != null && ai.StartsWith("RISK", StringComparison.OrdinalIgnoreCase));
                if (risky)
                {
                    string msg = (analysis == null ? "" : analysis.SummaryText()) + "\r\nAI：" + ai
                        + (string.IsNullOrEmpty(aiErr) ? "" : "（" + aiErr + "）")
                        + L.T("\r\n\r\n检测到风险。你可以选择【无视风险启动】，或先调整网状图中的模组。");
                    MinMsg.Show(this, msg, L.T("AI 检测发现风险"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (MgSaveScheme(false))
                {
                    CloseOverlayInstant();
                    ReloadSchemes(true);
                    StartGame();
                }
            };
            bw.RunWorkerAsync();
        }

        private void ExportPack()
        {
            if (_ctx == null || !_ctx.Ok)
            {
                MinMsg.Show(this, L.T("还没认出整合包，先到【设置】页点【绑定整合包文件夹…】再导出。"), L.T("导出整合包"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult dr = MinMsg.Show(this,
                L.T("要把游戏本体和模组一起打包吗？\r\n\r\n")
                + L.T("【是】完整包：含 RimWorld 本体 + 全部模组，体积很大（几个 GB），别人解压就能玩。\r\n\r\n")
                + L.T("【否】轻量包：只含 Config、排序文件、识别令牌，体积很小，适合发给已经有这套整合包的人。\r\n\r\n")
                + L.T("【取消】不导出。"),
                L.T("导出整合包"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (dr == DialogResult.Cancel) return;
            bool full = (dr == DialogResult.Yes);

            string outZip;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = L.T("选择导出位置");
                d.Filter = L.T("ZOGOS 整合包 (*.zip)|*.zip");
                d.FileName = PackSpec.SuggestName(_ctx, _current, full);
                try { d.InitialDirectory = Path.GetDirectoryName(_ctx.Root.TrimEnd((char)92)); } catch { }
                if (d.ShowDialog(this) != DialogResult.OK) return;
                outZip = d.FileName;
            }

            SetBusy(true);
            SetStatus(full ? L.T("正在导出完整包（可能要几分钟）…") : L.T("正在导出轻量包…"));
            _worker = new BackgroundWorker();
            _worker.WorkerReportsProgress = true;
            _worker.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                BackgroundWorker w = s as BackgroundWorker;
                ExportResult rr = PackExporter.Export(_ctx, _current, full, outZip,
                    delegate(string m) { if (w != null) w.ReportProgress(0, m); },
                    delegate(int i, int n, string name)
                    {
                        if (w != null) w.ReportProgress(0, L.T("打包 ") + i + "/" + n + "   " + name);
                    });
                e.Result = rr;
            };
            _worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs e)
            {
                string m = Convert.ToString(e.UserState);
                if (!string.IsNullOrEmpty(m)) SetStatus(m);
            };
            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false);
                ExportResult rr = e.Result as ExportResult;
                if (e.Error != null || rr == null || !rr.Success)
                {
                    string msg = e.Error != null ? e.Error.Message : (rr != null && rr.Errors.Count > 0 ? rr.Errors[0] : L.T("未知错误"));
                    Log.Error(L.T("导出失败：") + msg);
                    MinMsg.Show(this, L.T("导出失败：") + msg, L.T("导出整合包"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                _lastExportZip = rr.ZipPath;
                for (int i = 0; i < rr.Warnings.Count; i++) Log.Warn(rr.Warnings[i]);
                SetStatus(L.T("导出完成：") + Path.GetFileName(rr.ZipPath) + "（" + PackExporter.Human(rr.ZipBytes) + "）");
                if (MinMsg.Show(this, L.T("导出完成！\r\n\r\n文件：") + rr.ZipPath + L.T("\r\n大小：") + PackExporter.Human(rr.ZipBytes)
                    + L.T("\r\n文件数：") + rr.Files + L.T("\r\n\r\n现在打开它所在的文件夹吗？"), L.T("导出整合包"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    PathHelpOpen(rr.ZipPath);
            };
            _worker.RunWorkerAsync();
        }

        private void ImportPack()
        {
            string zip;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = L.T("选择 ZOGOS 整合包压缩包");
                d.Filter = L.T("ZOGOS 整合包 (*.zip)|*.zip|所有文件 (*.*)|*.*");
                try { d.InitialDirectory = Path.GetDirectoryName(_ctx.Root.TrimEnd((char)92)); } catch { }
                if (d.ShowDialog(this) != DialogResult.OK) return;
                zip = d.FileName;
            }

            SetStatus(L.T("正在检查压缩包…"));
            ZipPackInfo info;
            try { info = PackImporter.Inspect(zip); }
            catch (Exception ex)
            {
                MinMsg.Show(this, L.T("读不了这个压缩包：") + ex.Message, L.T("导入整合包"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string head = info.IsZogosPack ? L.T("检测到 ZOGOS 整合包") : L.T("注意：没找到识别令牌，可能不是本启动器导出的包");
            if (MinMsg.Show(this, head + "\r\n\r\n" + info.Describe() + L.T("\r\n继续导入吗？"), L.T("导入整合包"),
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            string dest;
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = L.T("选择解压到哪个文件夹（会在里面生成整合包文件夹）");
                try { d.SelectedPath = Path.GetDirectoryName(_ctx.Root.TrimEnd((char)92)); } catch { }
                if (d.ShowDialog(this) != DialogResult.OK) return;
                dest = d.SelectedPath;
            }

            SetBusy(true);
            SetStatus(L.T("正在解压…"));
            _worker = new BackgroundWorker();
            _worker.WorkerReportsProgress = true;
            _worker.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                BackgroundWorker w = s as BackgroundWorker;
                List<string> errs = new List<string>();
                int n = PackImporter.Extract(zip, dest, delegate(int i, int all, string name)
                {
                    if (w != null) w.ReportProgress(0, L.T("解压 ") + i + "/" + all + "   " + name);
                }, errs);
                e.Result = new object[] { n, errs };
            };
            _worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs e)
            {
                string m = Convert.ToString(e.UserState);
                if (!string.IsNullOrEmpty(m)) SetStatus(m);
            };
            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                if (IsDisposed || Disposing) return;
                SetBusy(false);
                if (e.Error != null)
                {
                    Log.Error(L.T("导入失败"), e.Error);
                    MinMsg.Show(this, L.T("导入失败：") + e.Error.Message, L.T("导入整合包"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                object[] res = e.Result as object[];
                int n = (res != null && res.Length > 0) ? (int)res[0] : 0;
                List<string> errs = (res != null && res.Length > 1) ? (res[1] as List<string>) : null;
                if (errs != null) for (int i = 0; i < errs.Count && i < 20; i++) Log.Warn(L.T("导入：") + errs[i]);
                SetStatus(L.T("解压完成，共 ") + n + L.T(" 个文件"));

                string found = PackImporter.FindPackRoot(dest);
                if (string.IsNullOrEmpty(found))
                {
                    MinMsg.Show(this, L.T("解压完成（") + n + L.T(" 个文件），但没在里面找到整合包。\r\n\r\n")
                        + L.T("如果内容没问题，可以到【设置】页点【绑定整合包文件夹…】手动指过去。\r\n\r\n解压位置：") + dest,
                        L.T("导入完成"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    PathHelpOpen(dest);
                    return;
                }
                if (MinMsg.Show(this, L.T("解压完成，共 ") + n + L.T(" 个文件。\r\n\r\n整合包位置：\r\n") + found
                    + L.T("\r\n\r\n现在把它设为当前整合包吗？（会自动写识别令牌并切换过去）"), L.T("导入完成"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    _st.Root = found;
                    _st.PackId = PackMarker.ReadPackId(found);
                    _st.Save();
                    Log.Info(L.T("导入整合包完成，已切换到：") + found);
                    Redetect();
                }
                else PathHelpOpen(found);
            };
            _worker.RunWorkerAsync();
        }

        // =================================================================
        //  外部窗口 / 工具入口
        // =================================================================
        /// <summary>叫苏苏出来。firstTime=true 从开场白开始，否则直接弹目录。
        /// 默认走新版内嵌覆盖层；诊断页勾了「使用旧版悬浮窗」则走旧的粉色独立窗。</summary>
        private void CallSusu(bool firstTime)
        {
            try
            {
                if (!_st.SusuLegacyWindow) { SusuOpen(firstTime); return; } // 新版覆盖层

                if (_susu == null || _susu.IsDisposed)
                {
                    _susu = new SusuWindow();
                    _susu.Owner = this;
                    _susu.Ctx = _ctx;
                    _susu.St = _st;
                }
                _susu.PlaceNear(this);
                _susu.Show();
                _susu.BringToFront();
                _susu.StartAt(firstTime ? GuideScript.StartId : "hub");
                Log.Info(L.T("苏苏上线：") + (firstTime ? L.T("首次启动引导") : L.T("打开功能菜单（体检/目录/从头讲/AI）")));
            }
            catch (Exception ex) { Log.Error(L.T("打开苏苏失败"), ex); }
        }

        /// <summary>给苏苏用：直接打开导入对话框。</summary>
        public void OpenImportDialog()
        {
            try { ImportPack(); } catch (Exception ex) { Log.Error(L.T("打开导入对话框失败"), ex); }
        }

        /// <summary>【整合包资源获取】：已并轨为内嵌模态覆盖层（见上方 BuildPackFeedOverlay / OpenPackFeed() 新版）。</summary>
        /// <summary>咖啡杯菜单：两位作者的赞助入口（原作者 ZOGOS / UI 作者 A1）+ 说明文字</summary>
        void ShowCoffeeMenu(object sender, EventArgs e)
        {
            var menu = new MenuPopup();
            menu.AddItem(L.T("赞助原作者 ZOGOS ☕"), delegate { OpenLink(SponsorUrl, L.T("赞助原作者")); });
            menu.AddItem(L.T("赞助 UI 作者 A1 ☕"), delegate { OpenLink(UiAuthorSponsorUrl, L.T("赞助 UI 作者")); });
            menu.AddSep();
            menu.AddLabel(L.T("觉得好用，就支持一下原作者（ZOGOS）"));
            menu.AddLabel(L.T("觉得 UI 流畅又好看，就支持下我吧（A1）"));
            menu.AddLabel(L.T("后续会带来更多好看的 UI"));
            menu.LogicalMinWidth = 210;
            var r = _top.CoffeeRect;
            menu.ShowAt(PointToScreen(new Point((int)(r.X * UiScale), (int)(r.Bottom * UiScale) + 6))); // 逻辑坐标 → 物理坐标
        }

        private void OpenLink(string url, string what)
        {
            try
            {
                Log.Info(L.T("打开赞助页面：") + url);
                ShellOpen.Url(url);
            }
            catch (Exception ex)
            {
                Log.Error(L.T("打开赞助页失败"), ex);
                MinMsg.Show(this, L.T("没能自动打开浏览器，请手动访问：\r\n\r\n") + url,
                    what, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void PathHelpOpen(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                MinMsg.Show(this, L.T("这个路径还不存在。"), L.T("提示"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            FileOps.OpenInExplorer(path);
        }

        private void OpenRimSort()
        {
            try
            {
                if (string.IsNullOrEmpty(_ctx.RimSortExe) || !File.Exists(_ctx.RimSortExe))
                {
                    MinMsg.Show(this, L.T("没有在整合包目录里找到 RimSort 模组管理器。\n\n排序功能可以直接用启动器里的【排序】页。"),
                        L.T("没找到 RimSort"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Log.Info(L.T("正在启动 RimSort：") + _ctx.RimSortExe);
                ProcessStartInfo psi = new ProcessStartInfo(_ctx.RimSortExe);
                psi.WorkingDirectory = Path.GetDirectoryName(_ctx.RimSortExe);
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log.Error(L.T("启动 RimSort 失败"), ex);
                MinMsg.Show(this, L.T("启动 RimSort 失败：") + ex.Message, L.T("出错了"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string DirOrFile(string dir, string file)
        {
            if (!string.IsNullOrEmpty(file) && File.Exists(file)) return file;
            return dir;
        }
    }
}
