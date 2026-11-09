using System;

namespace Zogos
{
    /// <summary>
    /// 版本标识。用编译开关 /define:SPONSOR 区分两版：
    ///   普通版：标题「ZOGOS创世神启动器」，没有【外观】页，配色固定粉色
    ///   赞助版：标题「感恩的心❤，感谢有你！」，可以用【外观】页换主题色 / 换背景 / 传自己的图
    /// </summary>
    public static class Edition
    {
        public const string TitleNormal = "Astra创世神协议";
        public const string TitleSponsor = "感恩的心❤，感谢有你！";

#if SPONSOR
        public const bool Sponsor = true;
        public const string Name = "赞助版";
        public const string Title = TitleSponsor;
#else
        public const bool Sponsor = false;
        public const string Name = "普通版";
        public const string Title = TitleNormal;
#endif

        /// <summary>外观自定义（主题色 / 背景 / 自定义图片）是否可用。</summary>
        public static bool CanCustomLook { get { return Sponsor; } }
    }
}
